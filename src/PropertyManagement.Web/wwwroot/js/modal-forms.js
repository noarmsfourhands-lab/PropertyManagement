/*
 * Modal forms driven entirely by partial views returned from controller actions.
 *
 * The contract with the server is deliberately small:
 *
 *   GET  a modal url            -> HTML for the modal body (a partial view)
 *   POST the form it contains   -> 422 plus the same partial, when validation failed
 *                                  200 plus { refreshUrl, target, message }, when it succeeded
 *
 * A 422 means the modal re-renders in place with its validation messages and the user keeps
 * their input. A success closes the modal and replaces just the affected region of the page,
 * so nothing else on screen is disturbed.
 *
 * No SPA framework is involved: every piece of HTML on screen was rendered by Razor.
 */
(function () {
    'use strict';

    var MODAL_ID = 'app-modal';
    var VALIDATION_FAILED = 422;

    var modalElement = document.getElementById(MODAL_ID);
    if (!modalElement) {
        return;
    }

    var modalContent = modalElement.querySelector('.modal-content');
    var modal = new bootstrap.Modal(modalElement);

    /* Injected markup carries its own validation attributes, which jQuery Unobtrusive only
       reads when a form is first parsed. Re-parsing gives the new form client-side validation. */
    function rewireValidation(scope) {
        if (window.jQuery && window.jQuery.validator && window.jQuery.validator.unobtrusive) {
            window.jQuery(scope).find('form').each(function () {
                window.jQuery(this).removeData('validator').removeData('unobtrusiveValidation');
                window.jQuery.validator.unobtrusive.parse(this);
            });
        }
    }

    function setModalContent(html) {
        modalContent.innerHTML = html;
        rewireValidation(modalContent);

        var firstField = modalContent.querySelector('input:not([type=hidden]), select, textarea');
        if (firstField) {
            firstField.focus();
        }
    }

    function ajaxHeaders() {
        return { 'X-Requested-With': 'XMLHttpRequest' };
    }

    function showError(message) {
        setModalContent(
            '<div class="modal-header"><h5 class="modal-title">Something went wrong</h5>' +
            '<button type="button" class="btn-close" data-bs-dismiss="modal" ' +
            'aria-label="Close"></button></div>' +
            '<div class="modal-body"><p class="mb-0">' + message + '</p></div>');
        modal.show();
    }

    var latestOpen = 0;

    async function openModal(url) {
        var request = ++latestOpen;

        try {
            var response = await fetch(url, { headers: ajaxHeaders(), credentials: 'same-origin' });

            /* A second trigger was clicked while this was in flight; that one wins. */
            if (request !== latestOpen) {
                return;
            }

            if (!response.ok) {
                showError('That form could not be opened. Refresh the page and try again.');
                return;
            }

            setModalContent(await response.text());
            modal.show();
        } catch (error) {
            showError('That form could not be opened. Check your connection and try again.');
        }
    }

    /* Replaces one region of the page with freshly rendered markup from the server.

       It never throws. By the time this runs the change has already been saved, so a failure here
       is only about what is on screen, and the honest recovery is to reload and show the truth
       rather than to report a save that did not fail. */
    async function refreshRegion(targetSelector, url) {
        var target = targetSelector ? document.querySelector(targetSelector) : null;

        if (!target || !url) {
            window.location.reload();
            return;
        }

        try {
            var response = await fetch(url, { headers: ajaxHeaders(), credentials: 'same-origin' });

            if (!response.ok) {
                window.location.reload();
                return;
            }

            target.innerHTML = await response.text();
        } catch (error) {
            window.location.reload();
        }
    }

    /* A message rendered before the modal opened can be answered by what the modal just did.
       The form names which message its save invalidates, so adding a note cannot wipe a message
       about the application's own sections. */
    function clearStaleMessages(form) {
        var selector = form.getAttribute('data-clears');

        if (!selector) {
            return;
        }

        document.querySelectorAll(selector).forEach(function (element) {
            // Hidden as well as emptied: the validation summary is only rendered at all when it
            // has something to say, so emptying it alone would leave a bare alert box behind.
            element.innerHTML = '';
            element.hidden = true;
        });
    }

    function announce(message) {
        if (!message) {
            return;
        }

        var banner = document.createElement('div');
        banner.className = 'alert alert-success alert-dismissible fade show';
        banner.setAttribute('role', 'alert');
        banner.textContent = message;

        var dismiss = document.createElement('button');
        dismiss.type = 'button';
        dismiss.className = 'btn-close';
        dismiss.setAttribute('data-bs-dismiss', 'alert');
        dismiss.setAttribute('aria-label', 'Dismiss');
        banner.appendChild(dismiss);

        var host = document.querySelector('main') || document.body;
        host.parentNode.insertBefore(banner, host);

        window.setTimeout(function () { banner.remove(); }, 5000);
    }

    /* Any element with data-modal-url opens a modal, wherever it appears, including markup
       that was injected after the page first loaded. */
    document.addEventListener('click', function (event) {
        var trigger = event.target.closest('[data-modal-url]');
        if (!trigger) {
            return;
        }

        event.preventDefault();
        openModal(trigger.getAttribute('data-modal-url'));
    });

    modalElement.addEventListener('submit', async function (event) {
        var form = event.target.closest('form[data-modal-form]');
        if (!form) {
            return;
        }

        event.preventDefault();

        /* Respect client-side validation before troubling the server. */
        if (window.jQuery && window.jQuery(form).data('validator') && !window.jQuery(form).valid()) {
            return;
        }

        var submitButton = form.querySelector('[type=submit]');
        if (submitButton) {
            submitButton.disabled = true;
        }

        try {
            var response = await fetch(form.action, {
                method: form.method || 'post',
                body: new FormData(form),
                headers: ajaxHeaders(),
                credentials: 'same-origin'
            });

            if (response.status === VALIDATION_FAILED) {
                setModalContent(await response.text());
                return;
            }

            if (!response.ok) {
                showError('That change could not be saved. Refresh the page and try again.');
                return;
            }

            var result = await response.json();

            modal.hide();
            clearStaleMessages(form);
            await refreshRegion(
                result.target || form.getAttribute('data-refresh-target'),
                result.refreshUrl || form.getAttribute('data-refresh-url'));
            announce(result.message);
        } catch (error) {
            showError('That change could not be saved. Check your connection and try again.');
        } finally {
            if (submitButton) {
                submitButton.disabled = false;
            }
        }
    });

    /* Leave no stale markup behind, so the next modal never flashes the previous one. */
    modalElement.addEventListener('hidden.bs.modal', function () {
        modalContent.innerHTML = '';
    });
})();

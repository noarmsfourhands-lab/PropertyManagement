/*
    Adds a show/hide control to every password field.

    Built here rather than in the Razor views on purpose. The control does nothing at all without
    JavaScript, so rendering it in the markup would leave a dead button on the page for anyone whose
    scripts failed to load. Created from script instead, it simply never appears, and the field
    behaves exactly as it did before.

    It is a toggle button, not a checkbox and not an icon with a click handler: aria-pressed tells a
    screen reader what state the field is in, the accessible name says what pressing it will do, and
    a real <button> is reachable by keyboard and by every assistive technology without extra work.
    type="button" matters more than it looks - a bare <button> inside a form submits it.
*/
(function () {
    'use strict';

    /* Two paths in one 20x20 box so they swap without the button resizing. */
    var EYE =
        '<svg viewBox="0 0 20 20" width="18" height="18" aria-hidden="true" focusable="false">' +
        '<path fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" ' +
        'stroke-linejoin="round" d="M1.7 10S4.9 4.4 10 4.4 18.3 10 18.3 10 15.1 15.6 10 15.6 1.7 10 1.7 10Z"/>' +
        '<circle cx="10" cy="10" r="2.6" fill="none" stroke="currentColor" stroke-width="1.6"/>' +
        '</svg>';

    var EYE_OFF =
        '<svg viewBox="0 0 20 20" width="18" height="18" aria-hidden="true" focusable="false">' +
        '<path fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" ' +
        'stroke-linejoin="round" d="M1.7 10S4.9 4.4 10 4.4c1.2 0 2.3.3 3.2.8M18.3 10s-1.4 2.4-3.9 3.9' +
        'M8.2 8.2a2.6 2.6 0 0 0 3.6 3.6M3 3l14 14"/>' +
        '</svg>';

    function attach(field) {
        // Guards a second run over the same field, which would otherwise wrap it twice and leave
        // two buttons stacked on top of each other.
        if (field.dataset.revealAttached === 'true') {
            return;
        }

        field.dataset.revealAttached = 'true';

        var wrapper = document.createElement('div');
        wrapper.className = 'pw-reveal';

        field.parentNode.insertBefore(wrapper, field);
        wrapper.appendChild(field);

        var button = document.createElement('button');
        button.type = 'button';
        button.className = 'pw-reveal-toggle';
        button.innerHTML = EYE;
        button.setAttribute('aria-pressed', 'false');
        button.setAttribute('aria-label', 'Show password');
        button.title = 'Show password';

        // Named so the field's own label and this button are not confused for each other by a
        // screen reader reading the group.
        if (field.id) {
            button.setAttribute('aria-controls', field.id);
        }

        button.addEventListener('click', function () {
            var revealing = field.type === 'password';

            field.type = revealing ? 'text' : 'password';
            button.innerHTML = revealing ? EYE_OFF : EYE;
            button.setAttribute('aria-pressed', revealing ? 'true' : 'false');
            button.setAttribute('aria-label', revealing ? 'Hide password' : 'Show password');
            button.title = revealing ? 'Hide password' : 'Show password';

            // Changing the type moves the caret to the end in most browsers, which is maddening
            // halfway through typing. Put it back where it was.
            var caret = field.value.length;

            try {
                caret = field.selectionStart === null ? caret : field.selectionStart;
                field.focus();
                field.setSelectionRange(caret, caret);
            } catch {
                // setSelectionRange throws on some input types in some browsers. Focus is enough.
                field.focus();
            }
        });

        wrapper.appendChild(button);
    }

    function scan(root) {
        (root || document).querySelectorAll('input[type="password"]').forEach(attach);
    }

    document.addEventListener('DOMContentLoaded', function () {
        scan(document);
    });

    /* Exposed so a password field added to the page after load can be given one too. Nothing calls
       this today: every password field in this application is on a full page, not in a modal. The
       guard above means calling it twice over the same field is harmless. */
    window.passwordReveal = { scan: scan };
})();

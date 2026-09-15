/*
 * The browser half of the data grid view component.
 *
 * The server renders the shell: the filters, the header row and the paging controls. This fetches
 * the rows from the JSON endpoint named in data-endpoint and draws them, then refetches whenever
 * the reader sorts, filters or pages.
 *
 * It knows nothing about applications, units or statuses. A column names a property on the
 * returned row, and anything needing a decision, such as a badge's colour, arrives as a field.
 * That is what makes the component reusable rather than one list wearing a disguise.
 */
(function () {
    'use strict';

    function init(root) {
        var endpoint = root.getAttribute('data-endpoint');
        var body = root.querySelector('[data-grid-body]');
        var status = root.querySelector('[data-grid-status]');
        var pager = root.querySelector('[data-grid-pager]');
        var summary = root.querySelector('[data-grid-summary]');
        var previous = root.querySelector('[data-grid-previous]');
        var next = root.querySelector('[data-grid-next]');

        var columns = JSON.parse(root.getAttribute('data-columns'));
        var state = {
            page: 1,
            pageSize: parseInt(root.getAttribute('data-page-size'), 10) || 20,
            sort: root.getAttribute('data-sort') || '',
            descending: root.getAttribute('data-descending') === 'true'
        };

        function filterValues() {
            var values = {};
            root.querySelectorAll('[data-grid-filter]').forEach(function (control) {
                if (control.value !== '') {
                    values[control.getAttribute('data-grid-filter')] = control.value;
                }
            });
            return values;
        }

        function buildUrl() {
            var url = new URL(endpoint, window.location.origin);
            url.searchParams.set('page', state.page);
            url.searchParams.set('pageSize', state.pageSize);
            url.searchParams.set('descending', state.descending);

            if (state.sort) {
                url.searchParams.set('sort', state.sort);
            }

            var filters = filterValues();
            Object.keys(filters).forEach(function (name) {
                url.searchParams.set(name, filters[name]);
            });

            return url;
        }

        /* Keeps the address bar in step, so a filtered, sorted view can be shared or reloaded. */
        function rememberInUrl() {
            var visible = new URL(window.location.href);
            visible.search = '';

            var filters = filterValues();
            Object.keys(filters).forEach(function (name) {
                visible.searchParams.set(name, filters[name]);
            });

            if (state.page > 1) {
                visible.searchParams.set('page', state.page);
            }
            if (state.sort) {
                visible.searchParams.set('sort', state.sort);
                visible.searchParams.set('descending', state.descending);
            }

            window.history.replaceState(null, '', visible.toString());
        }

        function cell(column, row) {
            var td = document.createElement('td');

            if (column.align === 'End') {
                td.className = 'text-end';
            }

            var value = row[column.field];

            if (value === null || value === undefined) {
                value = '';
            }

            if (column.render === 'Badge') {
                var badge = document.createElement('span');
                badge.className = 'badge ' + (row[column.classField] || 'text-bg-secondary');
                badge.textContent = value;
                td.appendChild(badge);
                return td;
            }

            if (column.render === 'Link') {
                if (value) {
                    var link = document.createElement('a');
                    link.className = 'btn btn-sm btn-outline-secondary';
                    link.href = value;
                    link.textContent = column.linkText || 'Open';
                    td.appendChild(link);
                }
                return td;
            }

            /* textContent throughout: a value from the endpoint is never treated as markup. */
            td.textContent = value;
            return td;
        }

        function draw(payload) {
            body.replaceChildren();

            payload.rows.forEach(function (row) {
                var tr = document.createElement('tr');
                columns.forEach(function (column) {
                    tr.appendChild(cell(column, row));
                });
                body.appendChild(tr);
            });

            var hasRows = payload.rows.length > 0;
            root.querySelector('table').hidden = !hasRows;
            status.hidden = hasRows;

            if (!hasRows) {
                status.textContent = root.getAttribute('data-empty-message');
            }

            summary.textContent = payload.total === 0
                ? ''
                : 'Page ' + payload.page + ' of ' + Math.max(payload.pageCount, 1)
                    + ' · ' + payload.total + ' in total';

            pager.hidden = payload.pageCount <= 1;
            previous.disabled = payload.page <= 1;
            next.disabled = payload.page >= payload.pageCount;

            markSortedColumn();
        }

        function markSortedColumn() {
            root.querySelectorAll('[data-grid-sort]').forEach(function (header) {
                var isSorted = header.getAttribute('data-grid-sort') === state.sort;
                header.setAttribute('aria-sort', isSorted ? (state.descending ? 'descending' : 'ascending') : 'none');
                var arrow = header.querySelector('[data-grid-arrow]');
                if (arrow) {
                    arrow.textContent = isSorted ? (state.descending ? '↓' : '↑') : '';
                }
            });
        }

        async function load() {
            status.hidden = false;
            status.textContent = root.getAttribute('data-loading-message');

            try {
                var response = await fetch(buildUrl(), {
                    headers: { 'Accept': 'application/json' },
                    credentials: 'same-origin'
                });

                if (!response.ok) {
                    body.replaceChildren();
                    root.querySelector('table').hidden = true;
                    status.textContent = 'That list could not be loaded. Refresh the page and try again.';
                    pager.hidden = true;
                    return;
                }

                draw(await response.json());
                rememberInUrl();
            } catch (error) {
                body.replaceChildren();
                root.querySelector('table').hidden = true;
                status.textContent = 'That list could not be loaded. Check your connection and try again.';
                pager.hidden = true;
            }
        }

        root.querySelectorAll('[data-grid-sort]').forEach(function (header) {
            header.addEventListener('click', function () {
                var key = header.getAttribute('data-grid-sort');

                // Clicking the column already sorted reverses it; a new column starts descending.
                state.descending = state.sort === key ? !state.descending : true;
                state.sort = key;
                state.page = 1;
                load();
            });
        });

        root.querySelectorAll('[data-grid-filter]').forEach(function (control) {
            control.addEventListener('change', function () {
                state.page = 1;
                load();
            });
        });

        previous.addEventListener('click', function () {
            if (state.page > 1) {
                state.page -= 1;
                load();
            }
        });

        next.addEventListener('click', function () {
            state.page += 1;
            load();
        });

        var clear = root.querySelector('[data-grid-clear]');
        if (clear) {
            clear.addEventListener('click', function () {
                root.querySelectorAll('[data-grid-filter]').forEach(function (control) {
                    control.value = '';
                });
                state.page = 1;
                load();
            });
        }

        load();
    }

    document.querySelectorAll('[data-grid]').forEach(init);
})();

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

        var columns;
        try {
            columns = JSON.parse(root.getAttribute('data-columns'));
        } catch (error) {
            /* One malformed grid must not take the others on the page down with it. */
            return;
        }

        /* Whatever this grid last wrote to the address bar is where it starts, so a link it
           produced reopens on the page and in the order it was shared at. */
        var opened = new URLSearchParams(window.location.search);
        var openedPage = parseInt(opened.get('page'), 10);

        var state = {
            page: openedPage > 0 ? openedPage : 1,
            pageSize: parseInt(root.getAttribute('data-page-size'), 10) || 20,
            sort: opened.get('sort') || root.getAttribute('data-sort') || '',
            descending: opened.has('descending')
                ? opened.get('descending') === 'true'
                : root.getAttribute('data-descending') === 'true'
        };

        /* Only the newest request is allowed to draw. Clicking Next twice quickly, or sorting
           while a page is still arriving, would otherwise render whichever reply landed last. */
        var latestRequest = 0;

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
            /* Asked for a page past the end, which a shared link or a narrowed filter can both
               produce. There are rows, just not here, so go and show them. */
            if (payload.rows.length === 0 && payload.total > 0 && payload.page > 1) {
                state.page = 1;
                load();
                return;
            }

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

            /* Cleared either way: a hidden live region holding "Loading…" is still read out. */
            status.textContent = hasRows ? '' : root.getAttribute('data-empty-message');

            summary.textContent = payload.total === 0
                ? ''
                : 'Page ' + payload.page + ' of ' + Math.max(payload.pageCount, 1)
                    + ' · ' + payload.total + ' in total';

            /* The footer carries the total as well as the buttons, so it stays whenever there is
               anything to count; only the buttons go when there is nothing to page through. */
            pager.hidden = payload.total === 0;
            previous.hidden = payload.pageCount <= 1;
            next.hidden = payload.pageCount <= 1;
            previous.disabled = payload.page <= 1;
            next.disabled = payload.page >= payload.pageCount;

            /* The page can only have moved to somewhere that exists. */
            state.page = payload.page;
            state.pageCount = payload.pageCount;

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
            var request = ++latestRequest;

            status.hidden = false;
            status.textContent = root.getAttribute('data-loading-message');

            try {
                var response = await fetch(buildUrl(), {
                    headers: { 'Accept': 'application/json' },
                    credentials: 'same-origin'
                });

                /* A reply that has been overtaken is discarded rather than drawn. */
                if (request !== latestRequest) {
                    return;
                }

                if (!response.ok) {
                    body.replaceChildren();
                    root.querySelector('table').hidden = true;
                    status.textContent = 'That list could not be loaded. Refresh the page and try again.';
                    pager.hidden = true;
                    return;
                }

                var payload = await response.json();

                if (request !== latestRequest) {
                    return;
                }

                draw(payload);
                rememberInUrl();
            } catch (error) {
                if (request !== latestRequest) {
                    return;
                }

                body.replaceChildren();
                root.querySelector('table').hidden = true;
                status.textContent = 'That list could not be loaded. Check your connection and try again.';
                pager.hidden = true;
            }
        }

        root.querySelectorAll('[data-grid-sort]').forEach(function (header) {
            var control = header.querySelector('[data-grid-sort-button]') || header;

            control.addEventListener('click', function () {
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
            /* Bounded here as well as by the button's disabled state, which is only recomputed
               once a reply arrives and is therefore stale while one is in flight. */
            if (state.pageCount === undefined || state.page < state.pageCount) {
                state.page += 1;
                load();
            }
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

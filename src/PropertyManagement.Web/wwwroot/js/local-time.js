/*
 * Rewrites server-rendered instants into the reader's own locale and timezone.
 *
 * The server cannot know either of those, so it renders UTC and marks the element. Until this
 * runs, and if it never runs, the page still shows a correct instant labelled UTC rather than a
 * time that is silently wrong by the server's offset.
 */
(function () {
    "use strict";

    var STYLES = {
        moment: { dateStyle: "medium", timeStyle: "short" },
        day: { dateStyle: "medium" }
    };

    document.querySelectorAll("time[datetime][data-local]").forEach(function (element) {
        var when = new Date(element.getAttribute("datetime"));

        if (isNaN(when.getTime())) {
            return;
        }

        var options = STYLES[element.getAttribute("data-local")] || STYLES.moment;

        try {
            element.textContent = new Intl.DateTimeFormat(undefined, options).format(when);
            element.title = when.toString();
        } catch (error) {
            /* Leave the UTC fallback in place rather than replace it with something worse. */
        }
    });
})();

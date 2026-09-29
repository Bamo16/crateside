// What was clicked stays where it is while a row or the picker opens or closes (handoffs 13, 13b): a
// row's header when the row was clicked, the file list's column heads when something inside it was.
const Motion = 220;
const Settle = 80;
const Patience = 1000;

export function keepPlace(spacer) {
    const scroller = document.scrollingElement;
    const list = spacer.parentElement;
    let height = 0;
    let busy = false;
    let running = null;

    const setSpacer = (value) => {
        height = value;
        spacer.style.height = `${value}px`;
    };

    const top = (element) => element.getBoundingClientRect().top + scroller.scrollTop;
    const bottom = (element) => element.getBoundingClientRect().bottom + scroller.scrollTop;

    // Looked up by id on every frame, since a render may replace the elements.
    const finderFor = (target) => {
        const header = target.closest(".row[id]");
        const reveal = target.closest(".reveal");
        const id = header?.id ?? reveal?.previousElementSibling?.id;

        if (!id)
            return null;

        const panelOf = () => {
            const next = document.getElementById(id)?.nextElementSibling;
            return next?.classList.contains("reveal") && !next.classList.contains("closing") ? next : null;
        };

        return header
            ? { anchor: () => document.getElementById(id), panel: panelOf }
            : { anchor: () => panelOf()?.querySelector(".stem-heads, .stems"), panel: panelOf };
    };

    const onClick = (event) => {
        const finder = finderFor(event.target);
        const anchor = finder?.anchor();

        if (!anchor)
            return;

        let fixed = anchor.getBoundingClientRect().top;
        const before = finder.panel()?.offsetHeight ?? 0;
        const listBefore = list.offsetHeight;
        const clicked = performance.now();
        let changed = null;
        const run = {};

        running = run;
        busy = true;

        const step = (now) => {
            if (running !== run)
                return;

            const anchor = finder.anchor();

            if (anchor) {
                const viewport = scroller.clientHeight;
                const panel = finder.panel();
                let want = top(anchor) - fixed;

                // Only holding still grows the spacer; revealing an opened panel never scrolls past the page.
                if (want > scroller.scrollHeight - viewport)
                    setSpacer(height + want - (scroller.scrollHeight - viewport));

                if (panel && panel.offsetHeight > before) {
                    const reveal = Math.min(
                        bottom(panel) - viewport + 16,
                        top(anchor) - 8,
                        scroller.scrollHeight - viewport
                    );

                    if (reveal > want) {
                        want = reveal;
                        fixed = top(anchor) - want;
                    }
                }

                scroller.scrollTop = Math.max(0, want);
            }

            changed ??= list.offsetHeight !== listBefore ? now : null;

            const settled = changed !== null && now - changed > duration() + Settle;
            const gaveUp = changed === null && now - clicked > Patience;

            if (settled || gaveUp) {
                running = null;
                busy = false;
            } else
                requestAnimationFrame(step);
        };

        requestAnimationFrame(step);
    };

    // The user scrolling takes over at once, so a click that changed nothing never holds the page.
    const onUserScroll = () => {
        running = null;
        busy = false;
    };

    // After a close, the spacer only ever shrinks, as the user scrolls back up.
    const onScroll = () => {
        if (busy || height === 0)
            return;

        const need = Math.max(0, scroller.scrollTop + scroller.clientHeight - (scroller.scrollHeight - height));

        if (need < height)
            setSpacer(need);
    };

    list.addEventListener("click", onClick, true);
    window.addEventListener("scroll", onScroll, { passive: true });
    window.addEventListener("wheel", onUserScroll, { passive: true });
    window.addEventListener("touchmove", onUserScroll, { passive: true });
    window.addEventListener("keydown", onUserScroll);
    document.documentElement.style.overflowAnchor = "none";

    return {
        reset() {
            setSpacer(0);
        },
        dispose() {
            list.removeEventListener("click", onClick, true);
            window.removeEventListener("scroll", onScroll);
            window.removeEventListener("wheel", onUserScroll);
            window.removeEventListener("touchmove", onUserScroll);
            window.removeEventListener("keydown", onUserScroll);
            document.documentElement.style.overflowAnchor = "";
        },
    };
}

function duration() {
    return matchMedia("(prefers-reduced-motion: reduce)").matches ? 0 : Motion;
}

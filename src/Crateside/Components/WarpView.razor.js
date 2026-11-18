// A range dragged on the ruler is drawn here, snapped to the ruler's bars (or seconds) as it
// moves, and sent to the server once, when it is let go: a round trip for every bar crossed
// queued up behind the redraws (#116).
export function rulerDrag(section, server) {
    let drag = null;

    const fraction = (ruler, event) => {
        const box = ruler.getBoundingClientRect();

        return Math.min(1, Math.max(0, (event.clientX - box.left) / box.width));
    };

    // The view's unit boundaries, as fractions, from the server's render.
    const snaps = (ruler) => (ruler.dataset.snaps ?? "").split(",").filter(Boolean).map(Number);
    const below = (bounds, at) => bounds.filter((bound) => bound <= at).at(-1) ?? bounds[0];
    const above = (bounds, at) => bounds.find((bound) => bound > at) ?? bounds.at(-1);
    const nearest = (bounds, at) =>
        bounds.reduce((best, bound) => (Math.abs(bound - at) < Math.abs(best - at) ? bound : best));

    const range = (at) => {
        const { bounds, fixed, anchor } = drag;

        if (anchor) {
            return at >= anchor.start
                ? [anchor.start, above(bounds, at)]
                : [below(bounds, at), anchor.end];
        }

        let moved = nearest(bounds, at);

        // At least one unit wide: never collapse onto the edge that stays put.
        if (moved === fixed)
            moved = at >= fixed ? above(bounds, fixed) : below(bounds, fixed - 1e-9);

        return [Math.min(fixed, moved), Math.max(fixed, moved)];
    };

    const place = ([left, right]) => {
        for (const band of drag.bands) {
            band.style.left = `${left * 100}%`;
            band.style.width = `${(right - left) * 100}%`;
        }

        for (const edge of drag.edges)
            edge.style.left = `${(edge.dataset.side === "start" ? left : right) * 100}%`;

        drag.range = [left, right];
    };

    // A new range has no band yet, so one is drawn in every row the form's bands are drawn in.
    const newBands = () =>
        [...section.querySelectorAll(".ruler, .tempo-line, .off-line, .plot, .key-strip, .chord-strip")].map((row) => {
            const band = document.createElement("div");

            band.style.cssText =
                "position:absolute;top:0;bottom:0;pointer-events:none;"
                + "border-left:1px solid rgba(236,231,220,.5);border-right:1px solid rgba(236,231,220,.5);"
                + "background:rgba(236,231,220,.06)";
            row.appendChild(band);

            return band;
        });

    const down = (event) => {
        const ruler = event.target.closest(".ruler.dragging");
        const bounds = ruler ? snaps(ruler) : [];

        if (!ruler || event.button !== 0 || bounds.length < 2)
            return;

        event.preventDefault();
        ruler.setPointerCapture(event.pointerId);

        const at = fraction(ruler, event);
        const edge = event.target.closest(".edge");

        if (edge) {
            const line = edge.dataset.line;
            const bands = [...section.querySelectorAll(`.jband[data-line="${line}"]`)];
            const edges = [...section.querySelectorAll(`.edge[data-line="${line}"]`)];

            drag = {
                ruler,
                bounds,
                line: Number(line),
                fixed: Number(edge.dataset.fixed),
                bands,
                edges,
                created: [],
            };
        } else {
            const created = newBands();

            drag = {
                ruler,
                bounds,
                line: -1,
                anchor: { start: below(bounds, at), end: above(bounds, at) },
                bands: created,
                edges: [],
                created,
            };
        }

        place(range(at));
    };

    const move = (event) => {
        if (drag)
            place(range(fraction(drag.ruler, event)));
    };

    // The bands are left where the drag put them: snapped to the same bounds the server converts
    // with, so its render lands them in the same place. A new range's stand-ins go once it has.
    const up = async () => {
        if (!drag)
            return;

        const { line, range: [start, end], created } = drag;

        drag = null;
        await server.invokeMethodAsync("DropRange", line, start, end);
        created.forEach((band) => band.remove());
    };

    section.addEventListener("pointerdown", down);
    section.addEventListener("pointermove", move);
    section.addEventListener("pointerup", up);
    section.addEventListener("pointercancel", up);

    return {
        dispose() {
            section.removeEventListener("pointerdown", down);
            section.removeEventListener("pointermove", move);
            section.removeEventListener("pointerup", up);
            section.removeEventListener("pointercancel", up);
        },
    };
}

// While the edit bar is open: the keys that move the draft, and the clip-start notch as a grip that
// snaps to whole bars. Both tell the server once per gesture; the draft lives there.
export function editDrag(section, server) {
    let grab = null;

    const typing = (target) =>
        target instanceof Element && target.closest("input, select, textarea, [role=slider], .win, [contenteditable=true]");

    const key = (event) => {
        if (!section.isConnected || section.dataset.editing !== "true" || section.dataset.asking === "true")
            return;

        if (event.altKey || event.ctrlKey || event.metaKey || typing(event.target))
            return;

        if (![",", ".", "ArrowLeft", "ArrowRight"].includes(event.key))
            return;

        event.preventDefault();
        server.invokeMethodAsync("EditKey", event.key, event.shiftKey);
    };

    const snaps = (ruler) =>
        (ruler.dataset.clipSnaps ?? "")
            .split(",")
            .filter(Boolean)
            .map((pair) => pair.split(":").map(Number));

    const release = () => {
        grab.tag.remove();
        grab.ghost.remove();
        grab.lines.forEach((line) => line.remove());
        grab.notch.classList.remove("held");
    };

    const settle = () => {
        if (!grab)
            return;

        release();
        grab.notch.style.left = grab.home;
        grab = null;
    };

    const down = (event) => {
        const notch = event.target instanceof Element ? event.target.closest(".notch.grip") : null;
        const ruler = notch?.closest(".ruler");

        if (!notch || !ruler || event.button !== 0)
            return;

        const tag = document.createElement("span");
        const ghost = document.createElement("span");

        tag.className = "mono clip-tag";
        ghost.className = "notch ghost";
        ghost.style.left = notch.style.left;
        ruler.append(tag, ghost);
        notch.classList.add("held");
        notch.setPointerCapture?.(event.pointerId);
        grab = {
            notch,
            ruler,
            tag,
            ghost,
            home: notch.style.left,
            bars: snaps(ruler),
            bar: Number(notch.dataset.bar),
            lines: [...section.querySelectorAll(".lane.saving .plot")].map((plot) => {
                const line = document.createElement("div");

                line.className = "clip-line held";
                line.style.left = notch.style.left;
                plot.append(line);

                return line;
            }),
        };
        tag.style.left = notch.style.left;
        tag.textContent = `clip start · bar ${grab.bar}`;
        event.preventDefault();
    };

    const move = (event) => {
        if (!grab || grab.bars.length === 0)
            return;

        const box = grab.ruler.getBoundingClientRect();
        const at = Math.min(1, Math.max(0, (event.clientX - box.left) / box.width));
        const [fraction, bar] = grab.bars.reduce((best, pair) => (Math.abs(pair[0] - at) < Math.abs(best[0] - at) ? pair : best));
        const left = `${fraction * 100}%`;

        grab.bar = bar;
        grab.notch.style.left = left;
        grab.tag.style.left = left;
        grab.tag.textContent = `clip start · bar ${bar}`;
        grab.lines.forEach((line) => (line.style.left = left));
    };

    const up = async () => {
        if (!grab)
            return;

        const { bar, notch } = grab;
        const changed = String(bar) !== notch.dataset.bar;

        release();
        grab = null;

        // The server's render puts the notch where the draft says; until it does, it stays where it was let go.
        if (changed)
            await server.invokeMethodAsync("SetClipStartBar", bar);
    };

    const escape = (event) => {
        if (grab && event.key === "Escape") {
            event.stopPropagation();
            settle();
        }
    };

    // The page's router is static, so Blazor never offers a link click or Back to the page's
    // NavigationLock. While a draft is unsaved this asks the server instead: a click on an in-app
    // link, or Back (a sentinel history entry stands in front of it), becomes a "leave?" question.
    let guarding = false;
    let pushed = false;
    let ignorePop = false;

    const click = (event) => {
        if (!guarding || event.defaultPrevented || event.button !== 0 || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey)
            return;

        const link = event.target instanceof Element ? event.target.closest("a[href]") : null;

        if (!link || (link.target && link.target !== "_self") || link.hasAttribute("download"))
            return;

        const to = new URL(link.href, location.href);

        if (to.origin !== location.origin || (to.pathname === location.pathname && to.search === location.search))
            return;

        event.preventDefault();
        event.stopPropagation();
        server.invokeMethodAsync("AskLeave", to.pathname + to.search + to.hash);
    };

    const pop = () => {
        if (ignorePop) {
            ignorePop = false;

            return;
        }

        if (guarding && pushed) {
            pushed = false;
            server.invokeMethodAsync("AskLeave", null);
        }
    };

    const rearm = () => {
        if (guarding && !pushed) {
            history.pushState({ ...history.state, leaveGuard: true }, "", location.href);
            pushed = true;
        }
    };

    window.addEventListener("click", click, true);
    window.addEventListener("popstate", pop);
    document.addEventListener("keydown", key);
    document.addEventListener("keydown", escape, true);
    section.addEventListener("pointerdown", down);
    section.addEventListener("pointermove", move);
    section.addEventListener("pointerup", up);
    section.addEventListener("pointercancel", settle);

    return {
        guard(active) {
            guarding = active;

            if (active)
                rearm();
            else if (pushed) {
                pushed = false;

                if (history.state?.leaveGuard) {
                    ignorePop = true;
                    history.back();
                }
            }
        },
        stay: rearm,
        leaveBack() {
            guarding = false;

            // The sentinel was spent by the Back that asked; one more Back goes where it was headed.
            history.back();
        },
        dispose() {
            window.removeEventListener("click", click, true);
            window.removeEventListener("popstate", pop);
            document.removeEventListener("keydown", key);
            document.removeEventListener("keydown", escape, true);
            section.removeEventListener("pointerdown", down);
            section.removeEventListener("pointermove", move);
            section.removeEventListener("pointerup", up);
            section.removeEventListener("pointercancel", settle);
        },
    };
}

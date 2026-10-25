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
        [...section.querySelectorAll(".ruler, .tempo-line, .off-line, .plot, .key-strip")].map((row) => {
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

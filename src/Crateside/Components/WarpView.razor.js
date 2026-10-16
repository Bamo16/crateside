// A range dragged on the ruler is drawn here and sent to the server once, when it is let go:
// a round trip for every bar crossed queued up behind the redraws (#116).
export function rulerDrag(section, server) {
    let drag = null;

    const fraction = (ruler, event) => {
        const box = ruler.getBoundingClientRect();

        return Math.min(1, Math.max(0, (event.clientX - box.left) / box.width));
    };

    const draw = (at) => {
        const [left, right] = [Math.min(drag.fixed, at), Math.max(drag.fixed, at)];

        drag.ghost.style.left = `${left * 100}%`;
        drag.ghost.style.width = `${(right - left) * 100}%`;
    };

    const down = (event) => {
        const ruler = event.target.closest(".ruler.dragging");

        if (!ruler || event.button !== 0)
            return;

        event.preventDefault();
        ruler.setPointerCapture(event.pointerId);

        // On an edge handle the other edge stays put; anywhere else a new range starts here.
        const edge = event.target.closest(".edge");
        const ghost = document.createElement("div");

        ghost.style.cssText =
            "position:absolute;top:0;bottom:0;z-index:3;pointer-events:none;"
            + "border:1px solid rgba(236,231,220,.5);background:rgba(236,231,220,.1)";
        ruler.appendChild(ghost);

        drag = {
            ruler,
            ghost,
            line: edge ? Number(edge.dataset.line) : -1,
            fixed: edge ? Number(edge.dataset.fixed) : fraction(ruler, event),
        };
        draw(fraction(ruler, event));
    };

    const move = (event) => {
        if (drag)
            draw(fraction(drag.ruler, event));
    };

    const up = (event) => {
        if (!drag)
            return;

        const { ruler, ghost, line, fixed } = drag;
        const at = fraction(ruler, event);

        drag = null;
        ghost.remove();
        server.invokeMethodAsync("DropRange", line, Math.min(fixed, at), Math.max(fixed, at));
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

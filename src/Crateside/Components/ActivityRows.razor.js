// On a track's page a row is one line, and its result expands only when the line cuts the text.
const watching = new WeakMap();

export function fit(root) {
    if (!root)
        return;

    if (!watching.has(root)) {
        const observer = new ResizeObserver(() => mark(root));

        observer.observe(root);
        watching.set(root, observer);
    }

    mark(root);
}

export function release(root) {
    watching.get(root)?.disconnect();
    watching.delete(root);
}

function mark(root) {
    for (const button of root.querySelectorAll(".result")) {
        // An open row shows all of its text, so it can no longer be measured; it stays clickable.
        if (button.getAttribute("aria-expanded") === "true")
            continue;

        const text = button.querySelector(".text");
        const clipped = Boolean(text) && text.scrollWidth > text.clientWidth + 1;

        button.toggleAttribute("data-clipped", clipped);
        button.tabIndex = clipped ? 0 : -1;
    }
}

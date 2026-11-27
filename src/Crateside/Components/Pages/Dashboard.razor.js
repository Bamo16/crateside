// The sticky index marks the section or panel in view, and scrolls to one without leaving the page.
// Targets are looked up on every scroll, since folding a section removes its panels.
export function watch(index) {
    const links = () => [...index.querySelectorAll("a[data-panel]")];

    const mark = () => {
        const all = links();
        const targets = all.map((link) => document.getElementById(link.dataset.panel));
        const current = targets.findLast((target) => target && target.getBoundingClientRect().top <= 120)
            ?? targets.find((target) => target);

        all.forEach((link, i) => link.classList.toggle("in-view", targets[i] === current));
    };

    const go = (event) => {
        const link = event.target.closest("a[data-panel]");
        const target = link && document.getElementById(link.dataset.panel);

        if (!target)
            return;

        event.preventDefault();
        target.scrollIntoView({ behavior: "smooth", block: "start" });
        history.replaceState(null, "", `#${target.id}`);
    };

    let pending = false;
    const onScroll = () => {
        if (pending)
            return;

        pending = true;
        requestAnimationFrame(() => {
            pending = false;
            mark();
        });
    };

    index.addEventListener("click", go);
    window.addEventListener("scroll", onScroll, { passive: true });
    mark();

    return {
        dispose() {
            index.removeEventListener("click", go);
            window.removeEventListener("scroll", onScroll);
        },
    };
}

// A cookie rather than local storage, so the server renders the page already folded.
export function remember(name, ids) {
    document.cookie = `${name}=${ids}; path=/; max-age=31536000; samesite=lax`;
}

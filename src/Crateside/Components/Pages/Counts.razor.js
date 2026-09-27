// The sticky index marks the panel in view, and scrolls to a panel without leaving the page.
export function watch(index) {
    const links = [...index.querySelectorAll("a[data-panel]")];
    const panels = links.map((link) => document.getElementById(link.dataset.panel));

    const mark = () => {
        const current = panels.findLast((panel) => panel && panel.getBoundingClientRect().top <= 120)
            ?? panels[0];

        links.forEach((link, i) => link.classList.toggle("in-view", panels[i] === current));
    };

    const go = (event) => {
        const panel = document.getElementById(event.currentTarget.dataset.panel);

        if (!panel)
            return;

        event.preventDefault();
        panel.scrollIntoView({ behavior: "smooth", block: "start" });
        history.replaceState(null, "", `#${panel.id}`);
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

    links.forEach((link) => link.addEventListener("click", go));
    window.addEventListener("scroll", onScroll, { passive: true });
    mark();

    return {
        dispose() {
            links.forEach((link) => link.removeEventListener("click", go));
            window.removeEventListener("scroll", onScroll);
        },
    };
}

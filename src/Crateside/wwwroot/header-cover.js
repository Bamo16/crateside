// The track header's cover is square at the strip's height. CSS cannot size a column from the height
// its own width decides, so this sets --cover on each header.hs to the header's height until it settles.
const sizes = new ResizeObserver(entries => {
    for (const { target } of entries)
        requestAnimationFrame(() => fit(target));
});

function fit(header) {
    // A few rounds at most: a wider cover narrows the cells, which can only make the strip taller.
    for (let round = 0; round < 6; round++) {
        const height = Math.round(header.getBoundingClientRect().height);
        const current = parseFloat(header.style.getPropertyValue("--cover")) || 0;

        if (Math.abs(height - current) < 1)
            return;

        header.style.setProperty("--cover", `${height}px`);
    }
}

function watch() {
    for (const header of document.querySelectorAll("header.hs"))
        if (!header.dataset.coverWatched) {
            header.dataset.coverWatched = "1";
            sizes.observe(header);
            fit(header);
        }
}

new MutationObserver(watch).observe(document.documentElement, { childList: true, subtree: true });
watch();

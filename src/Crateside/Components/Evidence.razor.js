let loading;

function loadLibrary() {
    loading ??= new Promise((resolve, reject) => {
        const script = document.createElement("script");
        script.src = "lib/html-to-image/html-to-image.js";
        script.onload = () => resolve(window.htmlToImage);
        script.onerror = () => reject(new Error("the image library did not load"));
        document.head.appendChild(script);
    });

    return loading;
}

async function draw(selector) {
    const library = await loadLibrary();
    const card = document.querySelector(selector);

    if (!card) throw new Error("the card is not on the page");

    await document.fonts.ready;

    return library.toBlob(card, { pixelRatio: 2, backgroundColor: "#141310" });
}

export async function copyImage(selector) {
    await navigator.clipboard.write([new ClipboardItem({ "image/png": draw(selector) })]);
}

export async function saveImage(selector, fileName) {
    const url = URL.createObjectURL(await draw(selector));
    const link = document.createElement("a");

    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 10000);
}

export function copyText(text) {
    return navigator.clipboard.writeText(text);
}

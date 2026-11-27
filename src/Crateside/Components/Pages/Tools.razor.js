// A cookie rather than local storage, so the server renders the page already folded.
export function remember(name, value) {
    document.cookie = `${name}=${value}; path=/; max-age=31536000; samesite=lax`;
}

// Puts the page's state in the address without a navigation, so a reload or a pasted link restores it.
export function replace(url) {
    history.replaceState(history.state, "", url);
}

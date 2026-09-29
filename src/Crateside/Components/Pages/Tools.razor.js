const RePitchKey = "crateside.tools.repitch";

// Whether Re-Pitch is open: the user's last choice, else open on a window 1100px or wider.
export function rePitchOpen() {
    try {
        const kept = localStorage.getItem(RePitchKey);

        if (kept !== null)
            return kept === "open";
    } catch {
        // Storage can be missing or blocked; fall back to the width.
    }

    return window.matchMedia("(min-width: 1100px)").matches;
}

export function rememberRePitch(open) {
    try {
        localStorage.setItem(RePitchKey, open ? "open" : "folded");
    } catch {
        // Nothing to do: the fold still holds for this visit.
    }
}

// Puts the page's state in the address without a navigation, so a reload or a pasted link restores it.
export function replace(url) {
    history.replaceState(history.state, "", url);
}

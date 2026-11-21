// The warp's grid: beats to seconds and back through every marker, and Live's bar numbering (ADR 0072).

import { SNAP } from "./shared.js";

export function makeGrid(source) {
    const markers = (source?.markers ?? []).filter(
        (marker, i, all) => i === 0 || (marker[0] > all[i - 1][0] && marker[1] > all[i - 1][1]),
    );

    if (markers.length < 2)
        return null;

    const seconds = markers.map((marker) => marker[0]);
    const beats = markers.map((marker) => marker[1]);
    const perBar = (source.numerator * 4) / source.denominator;
    const unit = 4 / source.denominator;

    const beatAt = (at) => {
        let i = 1;

        while (i < seconds.length - 1 && at > seconds[i])
            i++;

        return beats[i - 1] + ((at - seconds[i - 1]) * (beats[i] - beats[i - 1])) / (seconds[i] - seconds[i - 1]);
    };

    const secondsAt = (beat) => {
        let i = 1;

        while (i < beats.length - 1 && beat > beats[i])
            i++;

        return seconds[i - 1] + ((beat - beats[i - 1]) * (seconds[i] - seconds[i - 1])) / (beats[i] - beats[i - 1]);
    };

    const indexAt = (at) => Math.floor(beatAt(at) / perBar + SNAP);
    const number = (index) => (index >= 0 ? index + 1 : index);

    return {
        perBar,
        unit,
        numerator: source.numerator,
        beatAt,
        secondsAt,
        indexAt,
        number,
        startOf: (index) => secondsAt(index * perBar),
        barAt: (at) => number(indexAt(at)),
        // Live's Start field: bar.beat.sixteenth, the way the player reads it out.
        position(at) {
            const beat = beatAt(at);
            const index = Math.floor(beat / perBar + SNAP);
            const within = beat - index * perBar;
            const inBar = Math.floor(within / unit + SNAP);
            const sixteenth = Math.floor((within - inBar * unit) / 0.25 + SNAP);

            return `${number(index)}.${inBar + 1}.${sixteenth + 1}`;
        },
    };
}

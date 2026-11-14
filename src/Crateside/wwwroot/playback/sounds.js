// The click's sounds, synthesized (ADR 0094).

// Each is a short transient: a noise tick on the attack and a brief tone behind it, the body of
// Classic between 1 and 1.5 kHz as Live's is. At full level the two sum to full scale.
export const SOUNDS = {
    classic: (ctx, out, when, accent, level) => {
        tick(ctx, out, when, accent ? 0.22 : 0.18, level, 1200);

        return tone(ctx, out, when, accent ? 1500 : 1000, 0.04, "triangle", accent ? 0.95 : 0.78, level);
    },
    wood: (ctx, out, when, accent, level) => {
        tick(ctx, out, when, accent ? 0.38 : 0.3, level);

        return tone(ctx, out, when, accent ? 1500 : 1000, 0.05, "sine", accent ? 0.62 : 0.5, level, 0.7);
    },
};

function tick(ctx, out, when, loudness, level, cutoff = 4000) {
    const frames = Math.ceil(ctx.sampleRate * 0.005);
    const buffer = ctx.createBuffer(1, frames, ctx.sampleRate);
    const data = buffer.getChannelData(0);

    for (let i = 0; i < frames; i++)
        data[i] = (Math.random() * 2 - 1) * (1 - i / frames) ** 2;

    const source = ctx.createBufferSource();
    const filter = ctx.createBiquadFilter();
    const gain = ctx.createGain();

    source.buffer = buffer;
    filter.type = "highpass";
    filter.frequency.value = cutoff;
    gain.gain.value = Math.max(0.0002, loudness * level);
    source.connect(filter).connect(gain).connect(out);
    source.start(when);
}

function tone(ctx, out, when, hertz, length, type, loudness, level, drop = 1) {
    const oscillator = ctx.createOscillator();
    const gain = ctx.createGain();

    oscillator.type = type;
    oscillator.frequency.setValueAtTime(hertz, when);
    oscillator.frequency.exponentialRampToValueAtTime(hertz * drop, when + length);
    gain.gain.setValueAtTime(0.0001, when);
    gain.gain.exponentialRampToValueAtTime(Math.max(0.0002, loudness * level), when + 0.0005);
    gain.gain.exponentialRampToValueAtTime(0.0001, when + length);
    oscillator.connect(gain).connect(out);
    oscillator.start(when);
    oscillator.stop(when + length + 0.02);

    return oscillator;
}

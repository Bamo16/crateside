"""Measures the detector-only rules of ADR 0076 on two keydetect builds, side by side (ADR 0108).

    uv run --no-project python scripts/keydetect_rules_check.py OLD_FFMPEG NEW_FFMPEG [--fresh] [--limit N] [--jobs N]

Songs are the latest run of each song whose verdict is HIGH or MEDIUM, decided by the judge, with
readings from keydetect, madmom and Essentia edma, and whose source audio is on disk (the run's
InputPath, else source-index.json). The reference is the judge's verdict collapsed to major or
minor. madmom and edma are read from the ledger. keydetect comes from the ledger when the run's
evidence names the executable's sha256, and is read from the executable otherwise (as crateside
reads it: the whole track, default options, the file given directly); --fresh always reads.

Read-only: it opens the ledger with mode=ro, never writes next to the audio, and starts nothing but
the two executables you name. Ten minutes for a build whose readings are not in the ledger.
"""

import argparse
import hashlib
import json
import os
import re
import sqlite3
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

LOCAL = Path(os.environ.get("LOCALAPPDATA", Path.home() / ".local" / "share")) / "crateside"
LEDGER = LOCAL / "ledger.db"
SOURCE_INDEX = LOCAL / "source-index.json"
R_CUTOFF = 0.80

SHARP = {"Db": "C#", "Eb": "D#", "Gb": "F#", "Ab": "G#", "Bb": "A#"}
# The judge's modes, collapsed to the third they have.
MINOR_MODES = {"Aeolian", "Dorian", "Phrygian", "Locrian"}
FINAL_KEY = re.compile(r"lavfi\.keydetect\.key=(?P<key>[A-G][#b]?m?)\s*$", re.MULTILINE)
CONFIDENCE = re.compile(r"lavfi\.keydetect\.key_confidence=(?P<r>[0-9.+-]+)")


def key_of(tonic: str, minor: bool):
    return (SHARP.get(tonic, tonic), "minor" if minor else "major")


def from_text(text: str | None):
    """'F# minor' or 'Gb major' as a key."""
    if not text:
        return None
    tonic, _, mode = text.partition(" ")
    return key_of(tonic, mode.strip().lower().startswith("min"))


def from_token(token: str):
    """The filter's spelling: F#m is F-sharp minor, F# is F-sharp major."""
    return key_of(token.removesuffix("m"), token.endswith("m"))


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_source_index():
    if not SOURCE_INDEX.exists():
        return {}
    return {entry["song"].casefold(): entry["path"] for entry in json.loads(SOURCE_INDEX.read_text("utf-8"))}


def load_songs():
    ledger = sqlite3.connect(f"file:{LEDGER.as_posix()}?mode=ro", uri=True)
    rows = ledger.execute(
        """select Song, Tonic, Mode, InputPath, Evidence from Runs r
           where Id = (select max(Id) from Runs where Song = r.Song)
             and Confidence in ('High', 'Medium') and DecidedBy = 'Judge' and Tonic is not null"""
    ).fetchall()
    index = read_source_index()
    songs, total = [], 0

    for song, tonic, mode, input_path, evidence in rows:
        if not evidence:
            continue
        algorithms = json.loads(evidence).get("algorithms", {})
        kd, mm, ed = (algorithms.get(name) or {} for name in ("ffmpeg_keydetect", "madmom", "essentia_edma"))
        if not (kd.get("key") and mm.get("key") and ed.get("key")):
            continue

        total += 1
        audio = next(
            (p for p in (input_path, index.get(song.casefold())) if p and Path(p).exists()),
            None,
        )
        if audio is None:
            continue

        stored = json.loads(evidence).get("versions", {}).get("ffmpeg_sha256")
        songs.append(
            {
                "song": song,
                "verdict": key_of(tonic, mode in MINOR_MODES),
                "audio": audio,
                "mm": from_text(mm["key"]),
                "ed": from_text(ed["key"]),
                "stored_sha": stored,
                "stored_kd": (from_text(kd["key"]), kd.get("r") or 0.0),
            }
        )

    return songs, total


def read_fresh(ffmpeg: Path, audio: str):
    result = subprocess.run(
        [str(ffmpeg), "-nostdin", "-hide_banner", "-nostats", "-i", audio, "-vn", "-af", "keydetect", "-f", "null", "-"],
        capture_output=True,
        check=False,
    )
    stderr = result.stderr.decode("utf-8", "replace")
    keys = FINAL_KEY.findall(stderr)
    if not keys:
        return None
    rs = CONFIDENCE.findall(stderr)

    return from_token(keys[-1]), float(rs[-1]) if rs else 0.0


def readings_for(ffmpeg: Path, songs, fresh: bool, jobs: int):
    executable_sha = sha256(ffmpeg)
    out = [None] * len(songs)
    todo = []

    for i, song in enumerate(songs):
        if not fresh and song["stored_sha"] == executable_sha:
            out[i] = song["stored_kd"]
        else:
            todo.append(i)

    print(f"{ffmpeg.name} {executable_sha[:8]}: {len(songs) - len(todo)} from the ledger, {len(todo)} to read", file=sys.stderr)
    with ThreadPoolExecutor(jobs) as pool:
        for i, reading in zip(todo, pool.map(lambda i: read_fresh(ffmpeg, songs[i]["audio"]), todo)):
            out[i] = reading

    return out


def rules(song, reading):
    """Each rule's key where it fires, else None."""
    if reading is None:
        return {}
    kd, r = reading
    mm, ed = song["mm"], song["ed"]
    fires = {}
    if kd == mm == ed:
        fires["all three agree"] = kd
        if r >= R_CUTOFF:
            fires["all three, keydetect r >= 0.80"] = kd
    if kd in (mm, ed):
        fires["keydetect and one backer"] = kd
        if r >= R_CUTOFF:
            fires["keydetect and one backer, r >= 0.80"] = kd
    if kd == mm or kd == ed or mm == ed:
        fires["any two agree"] = kd if kd in (mm, ed) else mm
    if mm == ed != kd:
        fires["madmom and Essentia against keydetect"] = mm

    return fires


RULE_ORDER = [
    "all three agree",
    "all three, keydetect r >= 0.80",
    "keydetect and one backer",
    "keydetect and one backer, r >= 0.80",
    "any two agree",
    "madmom and Essentia against keydetect",
]


def tally(songs, readings):
    counts = {rule: [0, 0] for rule in RULE_ORDER}
    for song, reading in zip(songs, readings):
        for rule, key in rules(song, reading).items():
            counts[rule][0] += 1
            counts[rule][1] += key == song["verdict"]

    return counts


def cell(fires: int, agrees: int, songs: int) -> str:
    return f"{fires} ({fires / songs:.0%}) | {agrees} ({agrees / fires:.1%})" if fires else "0 | -"


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("old", type=Path, help="the pinned keydetect executable")
    parser.add_argument("new", type=Path, help="the build to adopt")
    parser.add_argument("--fresh", action="store_true", help="read every song from the executables")
    parser.add_argument("--limit", type=int, help="only the first N songs, to try it out")
    parser.add_argument("--jobs", type=int, default=4, help="parallel ffmpeg reads (default 4)")
    args = parser.parse_args()

    songs, total = load_songs()
    if args.limit:
        songs = songs[: args.limit]
    old = readings_for(args.old, songs, args.fresh, args.jobs)
    new = readings_for(args.new, songs, args.fresh, args.jobs)
    columns = [(args.old.name, tally(songs, old)), (args.new.name, tally(songs, new))]

    print(f"{len(songs)} songs of {total} with all three readings (the rest have no source audio on disk).\n")
    print(f"| rule | {columns[0][0]} fires | agrees | {columns[1][0]} fires | agrees |")
    print("|---|---|---|---|---|")
    for rule in RULE_ORDER:
        old_fires, old_agrees = columns[0][1][rule]
        new_fires, new_agrees = columns[1][1][rule]
        print(f"| {rule} | {cell(old_fires, old_agrees, len(songs))} | {cell(new_fires, new_agrees, len(songs))} |")

    right = lambda readings: sum(1 for s, r in zip(songs, readings) if r and r[0] == s["verdict"])
    print(f"\nkeydetect alone right: {right(old)} on {args.old.name}, {right(new)} on {args.new.name}")


if __name__ == "__main__":
    main()

"""Retakes the README's screenshots from the made-up demo library (ADR 0074).

    uv run --no-project --with playwright python scripts/screenshots.py

Builds crateside and the demo generator, writes a demo library into a scratch folder, starts
crateside on port 5099 against it with no workers, shoots docs/images/*.png in headless Edge,
and stops what it started. It never touches the app on port 5027 or the real %APPDATA%\crateside.
"""

import os
import shutil
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
from pathlib import Path
from urllib.parse import quote

from playwright.sync_api import Page, sync_playwright

ROOT = Path(__file__).resolve().parent.parent
IMAGES = ROOT / "docs" / "images"
PORT = 5099
URL = f"http://localhost:{PORT}"
VIEWPORT = {"width": 1600, "height": 900}
# A window as wide as the maintainer's, for the Tools shot.
WIDE = {"width": 1900, "height": 1000}
FIRST_TRACK = "DemoTrack01"
# The Tracks page lists every song; its screenshot stops after this many rows.
TRACK_ROWS = 17
TOOLS_SONG = "adele - rolling in the deep"


def run(*args: str) -> None:
    subprocess.run(args, cwd=ROOT, check=True)


def is_listening() -> bool:
    try:
        urllib.request.urlopen(URL, timeout=2)
    except urllib.error.HTTPError:
        return True
    except OSError:
        return False
    return True


def wait_ready(app: subprocess.Popen) -> None:
    deadline = time.monotonic() + 180

    while time.monotonic() < deadline:
        if app.poll() is not None:
            sys.exit(f"crateside exited with {app.returncode} before it was ready")

        try:
            if urllib.request.urlopen(f"{URL}/tracks", timeout=10).status == 200:
                return
        except OSError:
            time.sleep(1)

    sys.exit("crateside was not ready in three minutes")


def settle(page: Page, path: str, ready: str) -> None:
    page.goto(f"{URL}/{path}", wait_until="networkidle")
    page.wait_for_selector(ready, timeout=60_000)
    page.wait_for_timeout(2_500)
    page.evaluate("document.querySelectorAll('details').forEach(d => d.open = true)")
    page.wait_for_timeout(500)


def shoot(page: Page, name: str, clip: dict | None = None) -> None:
    page.screenshot(path=str(IMAGES / f"{name}.png"), full_page=True, clip=clip)
    print(f"  {name}.png")


def take(page: Page) -> None:
    settle(page, "", "text=Your turn")
    shoot(page, "dashboard")

    settle(page, "tracks?filter=All", "text=Rolling in the Deep")
    rows_end = page.evaluate(
        """rows => {
            const all = [...document.querySelectorAll('div.row')];
            const last = all[Math.min(rows, all.length) - 1].getBoundingClientRect();
            return last.bottom + window.scrollY + 8;
        }""",
        TRACK_ROWS,
    )
    shoot(page, "tracks", clip={"x": 0, "y": 0, "width": VIEWPORT["width"], "height": rows_end})

    settle(page, f"song/{FIRST_TRACK}", "text=Why D major")
    page.click("button.toggle:has-text('Activity')")
    page.wait_for_timeout(500)
    why = page.locator("text=Why D major").first.evaluate("e => e.getBoundingClientRect().top + window.scrollY") - 24
    height = page.evaluate("document.documentElement.scrollHeight")
    shoot(page, "song-a-top", clip={"x": 0, "y": 0, "width": VIEWPORT["width"], "height": why})
    shoot(page, "song-b-evidence", clip={"x": 0, "y": why, "width": VIEWPORT["width"], "height": height - why})

    page.set_viewport_size(WIDE)
    settle(page, f"tools?track={quote(TOOLS_SONG)}&lo=-1&hi=0", "text=Re-Pitch")
    shoot(page, "tools-camelot")


def main() -> None:
    if is_listening():
        sys.exit(f"Port {PORT} is already in use; this script only starts its own instance.")

    scratch = Path(tempfile.mkdtemp(prefix="crateside-shots-"))
    app: subprocess.Popen | None = None

    try:
        print("building crateside")
        run("dotnet", "publish", "src/Crateside/Crateside.csproj", "-c", "Release", "-o", str(scratch / "app"))

        print("writing the demo library")
        run("dotnet", "run", "--project", "demo/Crateside.Demo", "-c", "Release", "--", str(scratch / "library"))

        env = {
            **os.environ,
            "Crateside__Paths__DataRoot": str(scratch / "library"),
            "Crateside__Workers__Disabled": "true",
            "Crateside__Queue__Offline": "true",
            "ASPNETCORE_URLS": URL,
            "ASPNETCORE_ENVIRONMENT": "Production",
        }
        app = subprocess.Popen(
            [str(scratch / "app" / "Crateside.exe")],
            cwd=scratch / "app",
            env=env,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        wait_ready(app)

        print("taking the screenshots")
        IMAGES.mkdir(parents=True, exist_ok=True)

        with sync_playwright() as playwright:
            browser = playwright.chromium.launch(channel="msedge")
            page = browser.new_page(viewport=VIEWPORT, device_scale_factor=2)
            take(page)
            browser.close()
    finally:
        if app is not None and app.poll() is None:
            app.terminate()

            try:
                app.wait(timeout=20)
            except subprocess.TimeoutExpired:
                app.kill()

        shutil.rmtree(scratch, ignore_errors=True)


if __name__ == "__main__":
    main()

"""Drive headless Chrome via Playwright to load the KNI WebGL PoC and capture
FPS readings from the browser console. The bash wrapper (run-render-poc.sh)
starts the Blazor dev server, then invokes this script.

Why Playwright instead of bare `chrome --headless --virtual-time-budget=...`?
---------------------------------------------------------------------------
Chrome's `--virtual-time-budget` flag advances a virtual clock that does NOT
pause for `fetch()` API network requests. Blazor's WASM bootstrap fires ~50
parallel .wasm downloads via `fetch()`, and under virtual time these requests
are canceled with `TypeError: Failed to fetch` before the network can service
them. Real-time driving via Playwright is the only reliable way to let Blazor
complete its bootstrap so the KNI game loop can actually run.

Exit codes (matches run-render-poc.sh contract):
  0  -> PASS                 (max FPS >= 15)
  2  -> PASS-WITH-CONCERNS   (0 < max FPS < 15)
  1  -> FAIL                 (no FPS captured)
"""
import argparse
import re
import sys
from playwright.sync_api import sync_playwright

FPS_LINE = re.compile(r"\[PoC\.Render\] FPS: (\d+)")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--url", required=True, help="URL to load")
    ap.add_argument("--wait-secs", type=int, default=60,
                    help="Real-time seconds to wait after page load for FPS readings")
    ap.add_argument("--min-readings", type=int, default=3,
                    help="Stop early once this many FPS readings are captured")
    ap.add_argument("--executable", default="",
                    help="Override the Chrome/Chromium binary path")
    ap.add_argument("--pass-threshold", type=int, default=15,
                    help="FPS threshold for PASS (>=) vs PASS-WITH-CONCERNS (<)")
    args = ap.parse_args()

    fps_readings: list[int] = []
    console_lines: list[str] = []

    launch_kwargs = {
        "headless": True,
        "args": [
            "--no-sandbox",
            # Software WebGL2 (required for headless WebGL2 — without this,
            # getContext('webgl2') returns null).
            "--use-gl=angle",
            "--use-angle=swiftshader",
            # Don't throttle background timers — we want the rAF + Blazor
            # loop to run as fast as possible while we capture FPS.
            "--disable-background-timer-throttling",
            "--disable-backgrounding-occluded-windows",
            "--disable-renderer-backgrounding",
        ],
    }
    if args.executable:
        launch_kwargs["executable_path"] = args.executable

    with sync_playwright() as p:
        browser = p.chromium.launch(**launch_kwargs)
        try:
            page = browser.new_page(viewport={"width": 800, "height": 600})

            def on_console(msg) -> None:
                text = msg.text
                console_lines.append(text)
                m = FPS_LINE.search(text)
                if m:
                    fps_readings.append(int(m.group(1)))
                    print(f"[render-poc-chrome] console: {text}", flush=True)

            page.on("console", on_console)
            # Surface page-level errors (Blazor init failures, etc.) to stderr.
            page.on("pageerror", lambda err: print(f"[render-poc-chrome] pageerror: {err}", file=sys.stderr, flush=True))

            print(f"[render-poc-chrome] Navigating to {args.url}", flush=True)
            # goto's wait_until='load' returns once the HTML load event fires.
            # Blazor then continues async bootstrap; we let it run for
            # args.wait_secs real seconds while collecting console messages.
            page.goto(args.url, wait_until="load", timeout=30000)

            # Poll for FPS readings; stop early once we have enough.
            import time
            deadline = time.time() + args.wait_secs
            while time.time() < deadline:
                if len(fps_readings) >= args.min_readings:
                    break
                page.wait_for_timeout(500)

            print(f"[render-poc-chrome] Captured {len(fps_readings)} FPS reading(s): {fps_readings}",
                  flush=True)
        finally:
            browser.close()

    if not fps_readings:
        # Print a sample of console lines for debugging.
        print("[render-poc-chrome] No FPS captured. Last 20 console lines:", file=sys.stderr)
        for line in console_lines[-20:]:
            print(f"  | {line}", file=sys.stderr)
        return 1

    max_fps = max(fps_readings)
    if max_fps >= args.pass_threshold:
        print(f"[PASS] max FPS {max_fps} >= threshold {args.pass_threshold}")
        return 0
    else:
        print(f"[PASS-WITH-CONCERNS] max FPS {max_fps} < threshold {args.pass_threshold}")
        return 2


if __name__ == "__main__":
    sys.exit(main())

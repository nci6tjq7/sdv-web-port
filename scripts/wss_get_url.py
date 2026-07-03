"""
1. Open the wenshushu share page (anonymous login handled by SPA)
2. Click 下载 to trigger /ap/dl/sign
3. Extract the real download URL from the API response
4. Save URL to /home/z/my-project/scripts/_wss_url.txt for curl/wget
"""
import asyncio
import json
import re
from pathlib import Path
from playwright.async_api import async_playwright

SHARE_URL = "https://www.wenshushu.cn/f/kan9o4nlh1v"
URL_FILE = Path("/home/z/my-project/scripts/_wss_url.txt")

async def main():
    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True)
        context = await browser.new_context(
            accept_downloads=True,
            user_agent="Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
        )
        page = await context.new_page()

        captured_url = None
        captured_filename = None

        async def on_response(resp):
            nonlocal captured_url, captured_filename
            if "/ap/dl/sign" not in resp.url:
                return
            try:
                data = await resp.json()
                if data.get("code") == 0 and data.get("data", {}).get("url"):
                    captured_url = data["data"]["url"]
                    print(f"[+] Captured download URL from /ap/dl/sign")
            except Exception as e:
                print(f"[!] err reading dl/sign: {e}")

        page.on("response", on_response)

        print(f"[+] Opening {SHARE_URL}")
        try:
            await page.goto(SHARE_URL, wait_until="networkidle", timeout=30000)
        except Exception as e:
            print(f"[!] goto: {e}")
        await page.wait_for_timeout(3000)

        # click 下载 button
        try:
            btn = page.locator('button:has-text("下载")').first
            await btn.click(timeout=10000)
            print("[+] Clicked 下载")
        except Exception as e:
            print(f"[!] click err: {e}")

        # wait for the dl/sign API call
        for _ in range(20):
            await page.wait_for_timeout(500)
            if captured_url:
                break

        # cancel any in-browser download (we'll do it via curl)
        # cancel by navigating away
        if captured_url:
            print(f"[+] URL: {captured_url[:200]}...")
            URL_FILE.write_text(captured_url, encoding="utf-8")
            print(f"[+] Saved URL to {URL_FILE}")
        else:
            print("[!] Did not capture download URL")

        await context.close()
        await browser.close()

asyncio.run(main())

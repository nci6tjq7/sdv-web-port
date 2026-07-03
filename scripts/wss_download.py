"""
Download a file from wenshushu.cn share link using Playwright.
Strategy:
  1. Open the share page
  2. Capture network requests to find the actual download URL
  3. Try to click the download button and intercept the file download
"""
import asyncio
import os
import sys
import time
import re
from pathlib import Path
from playwright.async_api import async_playwright

SHARE_URL = "https://www.wenshushu.cn/f/kan9o4nlh1v"
DOWNLOAD_DIR = Path("/home/z/my-project/download")
DOWNLOAD_DIR.mkdir(parents=True, exist_ok=True)

async def main():
    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True)
        context = await browser.new_context(
            accept_downloads=True,
            user_agent="Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
        )
        page = await context.new_page()

        api_responses = []
        async def on_response(resp):
            url = resp.url
            if any(k in url for k in ["api", "download", "file", "share", "getfile", "getinfo"]):
                try:
                    body = await resp.text()
                except Exception:
                    body = "<binary>"
                api_responses.append((resp.status, url, body[:2000]))

        page.on("response", on_response)

        print(f"[+] Opening {SHARE_URL}")
        try:
            await page.goto(SHARE_URL, wait_until="networkidle", timeout=30000)
        except Exception as e:
            print(f"[!] goto timeout/err: {e}")

        # give SPA time to render
        await page.wait_for_timeout(5000)

        # dump title & visible text
        title = await page.title()
        print(f"[+] Page title: {title}")
        body_text = await page.evaluate("document.body.innerText")
        print("[+] Visible body text (first 2000 chars):")
        print(body_text[:2000])

        print("\n[+] Captured API responses:")
        for status, url, body in api_responses:
            print(f"  [{status}] {url}")
            print(f"      body: {body[:300]}")

        # Try to find a download button
        print("\n[+] Looking for download button...")
        candidates = await page.evaluate("""
            () => {
              const els = [...document.querySelectorAll('button, a, div, span')];
              return els
                .filter(e => {
                  const t = (e.innerText || '').trim();
                  return t && t.length < 20 && /下载|保存|download/i.test(t);
                })
                .slice(0, 20)
                .map(e => ({
                  tag: e.tagName,
                  text: (e.innerText || '').trim(),
                  cls: e.className,
                  id: e.id,
                }));
            }
        """)
        print(f"  found {len(candidates)} candidates:")
        for c in candidates:
            print(f"    {c}")

        # screenshot for debugging
        await page.screenshot(path=str(DOWNLOAD_DIR / "_wss_page.png"), full_page=True)
        print(f"[+] Screenshot saved to {DOWNLOAD_DIR}/_wss_page.png")

        await context.close()
        await browser.close()

asyncio.run(main())

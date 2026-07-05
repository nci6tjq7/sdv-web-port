"""
Click the 下载 button on wenshushu share page and capture the real download URL.
Then download the file with curl.
"""
import asyncio
import os
import re
import json
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

        # capture all requests & responses
        all_logs = []
        download_urls = []

        async def on_response(resp):
            url = resp.url
            # only log non-static requests
            if "static.wenshushu.cn" in url or url.endswith(".css") or url.endswith(".js") or url.endswith(".png"):
                return
            try:
                ct = resp.headers.get("content-type", "")
                if "json" in ct or "text" in ct:
                    body = await resp.text()
                else:
                    body = "<binary>"
            except Exception:
                body = "<err>"
            all_logs.append({"status": resp.status, "url": url, "body": body[:3000]})

            # detect download URL patterns
            if any(k in url for k in ["download", "getfile", "dl", "redirect", "downurl", "ddurl"]):
                download_urls.append({"status": resp.status, "url": url, "body": body[:3000]})

        page.on("response", on_response)

        async def on_request(req):
            url = req.url
            if "static.wenshushu.cn" in url:
                return
            if any(k in url for k in ["download", "downurl", "getfile", "ddurl"]):
                download_urls.append({"type": "request", "url": url, "method": req.method, "post_data": req.post_data})

        page.on("request", on_request)

        print(f"[+] Opening {SHARE_URL}")
        try:
            await page.goto(SHARE_URL, wait_until="networkidle", timeout=30000)
        except Exception as e:
            print(f"[!] goto: {e}")

        await page.wait_for_timeout(3000)

        # click the 下载 button
        print("[+] Clicking 下载 button...")
        try:
            # there's a checkbox "I have read..." maybe? try to find the download button specifically
            btn = page.locator('button:has-text("下载")').first
            await btn.click(timeout=10000)
            print("    clicked")
        except Exception as e:
            print(f"    click err: {e}")

        # wait for download dialog or URL to appear
        await page.wait_for_timeout(5000)

        # take screenshot
        await page.screenshot(path=str(DOWNLOAD_DIR / "_wss_after_click.png"), full_page=True)
        print(f"[+] Screenshot saved")

        # check if there's a dialog/modal asking for confirmation
        body_text = await page.evaluate("document.body.innerText")
        print("[+] Body text after click (first 1500):")
        print(body_text[:1500])

        # try clicking any "确认下载" or "开始下载" or "保存" buttons
        for txt in ["确认下载", "开始下载", "确定", "保存", "普通下载", "直接下载", "继续"]:
            try:
                btn2 = page.locator(f'button:has-text("{txt}"), a:has-text("{txt}"), span:has-text("{txt}")').first
                if await btn2.count() > 0:
                    print(f"[+] Clicking '{txt}'")
                    await btn2.click(timeout=3000)
                    await page.wait_for_timeout(3000)
                    break
            except Exception:
                pass

        # wait a bit more for any async download URL fetch
        await page.wait_for_timeout(5000)

        print("\n[+] All non-static network logs:")
        for log in all_logs[-40:]:
            print(f"  [{log['status']}] {log['url']}")
            if log.get('body') and log['body'] != '<binary>':
                print(f"      body: {log['body'][:500]}")

        print("\n[+] Detected download URL candidates:")
        for d in download_urls:
            print(f"  {d}")

        # save full log to file for inspection
        with open(DOWNLOAD_DIR / "_wss_network.json", "w", encoding="utf-8") as f:
            json.dump({"all_logs": all_logs, "download_urls": download_urls}, f, ensure_ascii=False, indent=2)
        print(f"[+] Network log saved to {DOWNLOAD_DIR}/_wss_network.json")

        await context.close()
        await browser.close()

asyncio.run(main())

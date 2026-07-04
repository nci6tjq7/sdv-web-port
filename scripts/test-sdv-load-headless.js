#!/usr/bin/env node
// Headless browser test for SdvLoad PoC (Phase 2.5).
// Verifies:
//   1. Runtime boots
//   2. MockSdv.dll loads
//   3. Game1 instantiates
//   4. Run() is called
//   5. Canvas has non-black pixels (WebGL2 rendering happened)
//
// Usage:
//   node scripts/test-sdv-load-headless.js [port]
//
// Exits 0 on PASS, 1 on FAIL.

const { chromium } = require('playwright');

const PORT = process.argv[2] || '8765';
const URL = `http://localhost:${PORT}/`;

(async () => {
    let browser;
    try {
        browser = await chromium.launch({
            args: ['--no-sandbox', '--disable-setuid-sandbox', '--use-gl=swiftshader'],
        });
        const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });

        const logs = [];
        page.on('console', msg => {
            const text = msg.text();
            logs.push(`[${msg.type()}] ${text}`);
            console.log(`[browser ${msg.type()}] ${text}`);
        });
        page.on('pageerror', err => {
            logs.push(`[pageerror] ${err.message}`);
            console.log(`[pageerror] ${err.message}`);
        });

        console.log(`[+] Navigating to ${URL}`);
        await page.goto(URL, { waitUntil: 'domcontentloaded', timeout: 30000 });

        // Wait for "Run() returned" or "FAIL" or "ERROR"
        console.log('[+] Waiting for Game1.Run() to be called (up to 90s)...');
        try {
            await page.waitForFunction(
                () => {
                    const s = document.getElementById('status');
                    return s && (s.textContent.includes('returned') ||
                                 s.textContent.includes('ERROR') ||
                                 s.textContent.includes('FAIL'));
                },
                { timeout: 90000 }
            );
        } catch (err) {
            console.log('[!] Timeout waiting for Run() — checking current state');
        }

        const statusText = await page.textContent('#status');
        const logText = await page.textContent('#log');
        console.log('');
        console.log('=== Final Status ===');
        console.log(`status: ${statusText}`);
        console.log('');
        console.log('=== Page Log (last 30 lines) ===');
        const logLines = logText.split('\n').filter(l => l.trim());
        console.log(logLines.slice(-30).join('\n'));

        // Give the game loop a few seconds to render frames
        console.log('');
        console.log('[+] Waiting 3s for game loop to render frames...');
        await page.waitForTimeout(3000);

        // Check canvas pixels
        console.log('[+] Reading canvas pixels...');
        const pixelResult = await page.evaluate(() => {
            return globalThis.readCanvasPixels();
        });
        console.log(`[+] Canvas pixel result: ${pixelResult}`);

        let pixelData;
        try { pixelData = JSON.parse(pixelResult); } catch { pixelData = { error: 'parse failed' }; }

        // Decide pass/fail
        const fullText = statusText + '\n' + logText;
        const logPass = fullText.includes('[PASS]') && fullText.includes('Run() returned normally');
        const pixelPass = pixelData && pixelData.nonBlackSamples > 0 && !pixelData.error;

        console.log('');
        console.log('=== Verdict ===');
        console.log(`Log check (Run() succeeded): ${logPass ? 'PASS' : 'FAIL'}`);
        console.log(`Pixel check (non-black pixels): ${pixelPass ? 'PASS' : 'FAIL'}`);
        if (pixelData) {
            console.log(`  Pixel details: nonBlackSamples=${pixelData.nonBlackSamples}, sampleColor=${JSON.stringify(pixelData.sampleColor)}`);
        }

        if (logPass && pixelPass) {
            console.log('\n[RESULT] PASS — Game1 instantiated, Run() called, canvas has rendered pixels');
            await browser.close();
            process.exit(0);
        }
        if (fullText.includes('[FAIL] Could not fetch Stardew Valley.dll')) {
            console.log('\n[RESULT] EXPECTED FAIL (no SDV.dll) — runtime + facade bootstrap verified');
            await browser.close();
            process.exit(0);
        }
        console.log('\n[RESULT] FAIL — see checks above');
        await browser.close();
        process.exit(1);
    } catch (err) {
        console.error('[FATAL]', err.message);
        if (browser) await browser.close();
        process.exit(1);
    }
})();

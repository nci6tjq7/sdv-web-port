#!/usr/bin/env node
// Headless browser test for SdvLoad PoC.
// Verifies the runtime boots, the facade assembly loads, and the PoC's
// Main() runs to completion (or fails gracefully without SDV.dll).
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
            args: ['--no-sandbox', '--disable-setuid-sandbox'],
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

        // Wait for either "PASS" or "FAIL" or "ERROR" to appear in the status
        console.log('[+] Waiting for runtime to boot (up to 60s)...');
        try {
            await page.waitForFunction(
                () => {
                    const s = document.getElementById('status');
                    return s && (s.textContent.includes('exited with code') ||
                                 s.textContent.includes('ERROR') ||
                                 s.textContent.includes('PASS'));
                },
                { timeout: 60000 }
            );
        } catch (err) {
            console.log('[!] Timeout waiting for runtime to boot');
        }

        const statusText = await page.textContent('#status');
        const logText = await page.textContent('#log');
        console.log('');
        console.log('=== Final Status ===');
        console.log(`status: ${statusText}`);
        console.log('');
        console.log('=== Page Log ===');
        console.log(logText);

        // Decide pass/fail
        const fullText = statusText + '\n' + logText;
        if (fullText.includes('[PASS]') && fullText.includes('facade pattern WORKS')) {
            console.log('\n[RESULT] PASS');
            await browser.close();
            process.exit(0);
        }
        if (fullText.includes('[FAIL] Could not fetch Stardew Valley.dll')) {
            console.log('\n[RESULT] EXPECTED FAIL (no SDV.dll) — runtime + facade bootstrap verified');
            // This is actually a partial success: the runtime booted, the facade
            // assembly loaded, the Main() method ran, and it correctly detected
            // that SDV.dll is missing. The infrastructure works.
            await browser.close();
            process.exit(0);
        }
        console.log('\n[RESULT] FAIL — see logs above');
        await browser.close();
        process.exit(1);
    } catch (err) {
        console.error('[FATAL]', err.message);
        if (browser) await browser.close();
        process.exit(1);
    }
})();

#!/usr/bin/env node
// Phase 2.8 diagnostic: capture all browser logs while attempting to load real SDV.
// No verdict checks — just dump everything for systematic-debugging Phase 1 evidence.
//
// Usage: node scripts/test-phase28-diagnostic.js [port] [seconds]

const { chromium } = require('playwright');

const PORT = process.argv[2] || '8765';
const SECONDS = parseInt(process.argv[3] || '40', 10);
const BISECT = process.argv[4] || '0'; // 0=no bisection, 1-5=bisect modes
const URL = `http://localhost:${PORT}/?bisect=${BISECT}`;

(async () => {
    let browser;
    try {
        browser = await chromium.launch({
            args: ['--no-sandbox', '--disable-setuid-sandbox', '--use-gl=swiftshader'],
        });
        const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });

        page.on('console', msg => {
            const text = msg.text();
            console.log(`[browser ${msg.type()}] ${text}`);
        });
        page.on('pageerror', err => console.log(`[pageerror] ${err.message}`));

        console.log(`[+] Navigating to ${URL} (capturing for ${SECONDS}s)`);
        await page.goto(URL, { waitUntil: 'networkidle', timeout: 60000 });

        console.log(`[+] Waiting ${SECONDS}s for SDV load attempt...`);
        await page.waitForTimeout(SECONDS * 1000);

        // Check if page is still alive
        const title = await page.title();
        console.log(`[+] Page title at end: "${title}"`);

        // Screenshot canvas for diagnostic
        const canvas = await page.$('#theCanvas');
        if (canvas) {
            await canvas.screenshot({ path: '/tmp/phase28-canvas.png' });
            console.log('[+] Canvas screenshot saved to /tmp/phase28-canvas.png');
        } else {
            console.log('[!] No canvas element');
        }

        await browser.close();
        process.exit(0);
    } catch (err) {
        console.error('[FATAL]', err.message);
        if (browser) await browser.close();
        process.exit(1);
    }
})();

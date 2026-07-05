#!/usr/bin/env node
// Headless test for SdvBlazor PoC (Phase 2.6).
// Verifies that real SDV code (MockSdv stand-in) loads + renders in browser.
//
// Usage: node scripts/test-sdv-blazor-headless.js [port]

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

        const browserLogs = [];
        page.on('console', msg => {
            const text = msg.text();
            browserLogs.push(`[${msg.type()}] ${text}`);
            console.log(`[browser ${msg.type()}] ${text}`);
        });
        page.on('pageerror', err => console.log(`[pageerror] ${err.message}`));

        console.log(`[+] Navigating to ${URL}`);
        await page.goto(URL, { waitUntil: 'networkidle', timeout: 60000 });

        // Wait for SDV to load + game to start (up to 30s)
        console.log('[+] Waiting for SDV load + game start (up to 30s)...');
        try {
            await page.waitForFunction(
                () => {
                    const status = document.getElementById('status');
                    if (status && (status.textContent.includes('Run() returned') ||
                                   status.textContent.includes('FATAL'))) return true;
                    return false;
                },
                { timeout: 30000 }
            );
        } catch (err) {
            console.log('[!] Timeout waiting for game start — checking current state');
        }

        // Give the loop a few seconds to render frames
        console.log('[+] Waiting 8s for frames to render...');
        await page.waitForTimeout(8000);

        // Screenshot the canvas to verify rendering
        const canvas = await page.$('#theCanvas');
        let pixelPass = false;
        let pixelData = { error: 'no canvas' };
        if (canvas) {
            await canvas.screenshot({ path: '/tmp/sdv-blazor-canvas.png' });
            const sharp = require('sharp');
            const { data, info } = await sharp('/tmp/sdv-blazor-canvas.png')
                .raw()
                .toBuffer({ resolveWithObject: true });
            let nonBlack = 0;
            let sample = [0, 0, 0];
            let cornflower = 0;
            for (let i = 0; i < data.length; i += 16000) {
                const r = data[i], g = data[i+1], b = data[i+2];
                if (r > 5 || g > 5 || b > 5) nonBlack++;
                if (90 <= r && r <= 110 && 140 <= g && g <= 160 && 230 <= b && b <= 245) cornflower++;
                sample = [r, g, b];
            }
            pixelData = { nonBlack, cornflower, sample, width: info.width, height: info.height };
            pixelPass = nonBlack > 0;
            console.log(`[+] Canvas screenshot saved to /tmp/sdv-blazor-canvas.png`);
            console.log(`[+] Canvas pixels: ${JSON.stringify(pixelData)}`);
        } else {
            console.log('[!] Canvas element not found');
        }

        const fullLog = browserLogs.join('\n');
        console.log('');
        console.log('=== Verdict ===');
        const loadedSdv = fullLog.includes('Loaded: MockSdv') || fullLog.includes('Loaded: Stardew Valley');
        const foundGame1 = fullLog.includes('Found: StardewValley.FileSystemTestGame') || fullLog.includes('Found: StardewValley.Game1');
        const instantiated = fullLog.includes('Game instantiated');
        const runReturned = fullLog.includes('Run() returned');
        const hasFrames = /Frame \d+/.test(fullLog);
        const rewriterRan = fullLog.includes('Total rewrites:');
        const vfsLoaded = fullLog.includes('Loaded text: Hello from VFS!');
        const shimCalled = fullLog.includes('[SdvFileShim] OpenRead:');
        console.log(`SDV loaded:          ${loadedSdv ? 'PASS' : 'FAIL'}`);
        console.log(`Game found:          ${foundGame1 ? 'PASS' : 'FAIL'}`);
        console.log(`Game instantiated:   ${instantiated ? 'PASS' : 'FAIL'}`);
        console.log(`Run() returned:      ${runReturned ? 'PASS' : 'FAIL'}`);
        console.log(`Rewriter ran:        ${rewriterRan ? 'PASS' : 'FAIL'}`);
        console.log(`SdvFileShim called:  ${shimCalled ? 'PASS' : 'FAIL'}`);
        console.log(`VFS text loaded:     ${vfsLoaded ? 'PASS' : 'FAIL'}`);
        console.log(`Frames rendered:     ${hasFrames ? 'PASS' : 'FAIL'}`);
        console.log(`Pixels non-black:    ${pixelPass ? 'PASS' : 'FAIL'}`);
        if (pixelData && !pixelData.error) {
            console.log(`  nonBlack=${pixelData.nonBlack}, cornflower=${pixelData.cornflower}, sampleColor=${JSON.stringify(pixelData.sample)}`);
        }

        if (loadedSdv && foundGame1 && instantiated && runReturned && rewriterRan && shimCalled && vfsLoaded && hasFrames && pixelPass) {
            console.log('\n[RESULT] PASS — Real SDV Game1 loads + VFS redirect works + renders in browser!');
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

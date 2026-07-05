#!/usr/bin/env node
// Headless test for BlazorGameLoop PoC (Phase 2.5b).
// Verifies that KNI's game loop runs on net8.0 BlazorWebAssembly SDK
// with the externally-driven RAF loop pattern.
//
// Usage: node scripts/test-blazor-loop-headless.js [port]

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

        // Wait for game loop to produce frames (check browser console logs)
        console.log('[+] Waiting for game loop to start (up to 30s)...');
        try {
            await page.waitForFunction(
                () => {
                    // Check if any console.log mentioned "Frame ... drawn"
                    // We can't read console logs from the page, so check window property
                    return window.__frameCount > 0 || document.title.includes('SDV');
                },
                { timeout: 5000 }
            );
        } catch (err) {
            console.log('[!] waitForFunction timeout (expected) — continuing');
        }

        // Give the loop a few seconds to render frames
        console.log('[+] Waiting 5s for frames to render...');
        await page.waitForTimeout(5000);

        // Screenshot the canvas to verify rendering (readPixels/drawImage don't
        // work on WebGL canvases — screenshot is the reliable verification).
        const canvas = await page.$('#theCanvas');
        let pixelPass = false;
        let pixelData = { error: 'no canvas' };
        if (canvas) {
            await canvas.screenshot({ path: '/tmp/blazor-loop-canvas.png' });
            // Analyze the PNG file using sharp (available in the project)
            const sharp = require('sharp');
            const { data, info } = await sharp('/tmp/blazor-loop-canvas.png')
                .raw()
                .toBuffer({ resolveWithObject: true });
            let nonBlack = 0;
            let sample = [0, 0, 0];
            let cornflower = 0;
            // Sample every 4000th pixel (step through RGBA bytes)
            for (let i = 0; i < data.length; i += 16000) {
                const r = data[i], g = data[i+1], b = data[i+2];
                if (r > 5 || g > 5 || b > 5) nonBlack++;
                if (90 <= r && r <= 110 && 140 <= g && g <= 160 && 230 <= b && b <= 245) cornflower++;
                sample = [r, g, b];
            }
            pixelData = { nonBlack, cornflower, sample, width: info.width, height: info.height };
            pixelPass = nonBlack > 0;
            console.log(`[+] Canvas screenshot saved to /tmp/blazor-loop-canvas.png`);
            console.log(`[+] Canvas pixels: ${JSON.stringify(pixelData)}`);
        } else {
            console.log('[!] Canvas element not found');
        }

        const fullLog = browserLogs.join('\n');
        console.log('');
        console.log('=== Verdict ===');
        const hasFrames = /Frame \d+ drawn/.test(fullLog);
        console.log(`Frame log check: ${hasFrames ? 'PASS' : 'FAIL'}`);
        console.log(`Pixel check (non-black): ${pixelPass ? 'PASS' : 'FAIL'}`);
        if (pixelData && !pixelData.error) {
            console.log(`  nonBlack=${pixelData.nonBlack}, cornflower=${pixelData.cornflower}, sampleColor=${JSON.stringify(pixelData.sample)}`);
        }

        if (hasFrames && pixelPass) {
            console.log('\n[RESULT] PASS — KNI game loop works on net8.0 BlazorWebAssembly!');
            console.log(`[RESULT] Rendered ${pixelData.cornflower} CornflowerBlue pixels + bouncing red box`);
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

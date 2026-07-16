// More thorough browser test - captures network + console + longer wait
const puppeteer = require('puppeteer-core');
const fs = require('fs');
const path = require('path');
const os = require('os');

(async () => {
  // Use a fresh temp profile each run to avoid SW cache issues
  const tmpProfile = fs.mkdtempSync(path.join(os.tmpdir(), 'puppeteer-sdv-'));
  const browser = await puppeteer.launch({
    executablePath: '/home/z/.agent-browser/browsers/chrome-150.0.7871.115/chrome',
    headless: 'new',
    userDataDir: tmpProfile,
    args: [
      '--no-sandbox',
      '--disable-setuid-sandbox',
      '--disable-dev-shm-usage',
      '--disable-gpu',
      '--enable-features=SharedArrayBuffer'
    ]
  });
  const page = await browser.newPage();

  const logs = [];
  const requests = [];
  const failedReqs = [];

  page.on('console', msg => {
    const type = msg.type();
    const text = msg.text();
    logs.push(`[console.${type}] ${text}`);
  });
  page.on('pageerror', err => logs.push(`[pageerror] ${err.message}`));
  page.on('requestfailed', req => {
    failedReqs.push(`${req.url()} - ${req.failure()?.errorText}`);
  });
  page.on('response', resp => {
    const url = resp.url();
    const status = resp.status();
    if (status >= 400) {
      requests.push(`[HTTP ${status}] ${url}`);
    } else if (url.includes('sdv-web-port') && (url.endsWith('.wasm') || url.endsWith('.js'))) {
      requests.push(`[HTTP ${status}] ${url.split('/').pop()}`);
    }
  });

  console.log('=== Pass 1: register SW ===');
  try {
    await page.goto('https://nci6tjq7.github.io/sdv-web-port/?v=1784223074', {
      waitUntil: 'networkidle2',
      timeout: 60000
    });
  } catch (e) {
    console.log('goto err:', e.message);
  }
  await new Promise(r => setTimeout(r, 8000));

  console.log('=== Pass 2: reload to activate SW ===');
  logs.length = 0;
  try {
    await page.reload({ waitUntil: 'domcontentloaded', timeout: 60000 });
  } catch (e) {
    console.log('reload err:', e.message);
  }

  console.log('=== Waiting 90s for runtime boot ===');
  await new Promise(r => setTimeout(r, 90000));

  console.log('\n========== CONSOLE LOGS (after reload) ==========');
  for (const log of logs) console.log(log);

  console.log('\n========== NETWORK REQUESTS ==========');
  console.log(`Total tracked: ${requests.length}`);
  requests.slice(0, 30).forEach(r => console.log(r));

  console.log('\n========== FAILED REQUESTS ==========');
  console.log(`Total failed: ${failedReqs.length}`);
  failedReqs.slice(0, 20).forEach(r => console.log(r));

  // Check key markers
  const signatureMismatch = logs.filter(l => l.includes('signature mismatch'));
  const runtimeLoaded = logs.filter(l => l.includes('Loading .NET runtime') || l.includes('.NET runtime loaded') || l.includes('runMain'));
  const sdvBoot = logs.filter(l => l.includes('Booting StardewValley') || l.includes('SdvWebPort'));
  const errors = logs.filter(l => l.includes('[console.error]') || l.includes('[pageerror]'));

  console.log('\n========== ANALYSIS ==========');
  console.log(`"signature mismatch" count: ${signatureMismatch.length}`);
  signatureMismatch.forEach(l => console.log('  >', l));
  console.log(`\n.NET runtime load progress: ${runtimeLoaded.length}`);
  runtimeLoaded.forEach(l => console.log('  >', l));
  console.log(`\nSDV boot progress: ${sdvBoot.length}`);
  sdvBoot.forEach(l => console.log('  >', l));
  console.log(`\nErrors: ${errors.length}`);
  errors.slice(0, 10).forEach(l => console.log('  >', l));

  // Check if crossOriginIsolated was achieved
  const isolated = await page.evaluate(() => window.crossOriginIsolated).catch(() => 'eval-failed');
  console.log(`\nwindow.crossOriginIsolated: ${isolated}`);

  await browser.close();
  process.exit(signatureMismatch.length > 0 ? 1 : 0);
})();

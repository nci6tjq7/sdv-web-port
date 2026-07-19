const puppeteer = require('puppeteer-core');
const fs = require('fs');
const os = require('os');
const path = require('path');

(async () => {
  const tmpProfile = fs.mkdtempSync(path.join(os.tmpdir(), 'sdv-deep-'));
  const browser = await puppeteer.launch({
    executablePath: '/home/z/.agent-browser/browsers/chrome-150.0.7871.115/chrome',
    headless: 'new',
    userDataDir: tmpProfile,
    args: ['--no-sandbox', '--disable-setuid-sandbox', '--disable-dev-shm-usage', '--disable-gpu']
  });
  const page = await browser.newPage();

  const logs = [];
  page.on('console', msg => logs.push(`[${msg.type()}] ${msg.text()}`));
  page.on('pageerror', err => logs.push(`[pageerror] ${err.message}`));
  page.on('requestfailed', req => logs.push(`[reqfail] ${req.url().split('/').pop()} - ${req.failure()?.errorText}`));

  // Intercept network requests to see what's being fetched
  await page.setRequestInterception(true);
  page.on('request', req => {
    const url = req.url();
    if (url.includes('/deps/Content/')) {
      console.log(`[NET] Content request: ${url.split('/deps/')[1]}`);
    }
    req.continue();
  });

  console.log('=== Loading page ===');
  await page.goto('https://nci6tjq7.github.io/sdv-web-port/?v=1784438365' + Date.now(), {
    waitUntil: 'domcontentloaded', timeout: 120000
  });

  console.log('=== Waiting 120s ===');
  await new Promise(r => setTimeout(r, 120000));

  // Print ALL logs
  console.log('\n=== ALL LOGS ===');
  logs.forEach(l => console.log(l));

  // Check page state
  const state = await page.evaluate(() => ({
    crossOriginIsolated: window.crossOriginIsolated,
    errorLog: document.getElementById('error-log')?.textContent?.substring(0, 1000),
    status: document.getElementById('status')?.textContent,
  })).catch(() => ({error: 'eval failed'}));
  console.log('\n=== Page State ===');
  console.log(JSON.stringify(state, null, 2));

  await browser.close();
})();

const puppeteer = require('puppeteer-core');
const fs = require('fs');
const os = require('os');
const path = require('path');

(async () => {
  const tmpProfile = fs.mkdtempSync(path.join(os.tmpdir(), 'sdv-test-'));
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

  // Collect ALL logs
  const allLogs = [];
  page.on('console', msg => {
    const entry = `[${msg.type()}] ${msg.text()}`;
    allLogs.push(entry);
    console.log(entry);
  });
  page.on('pageerror', err => {
    allLogs.push(`[pageerror] ${err.message}`);
    console.log(`[pageerror] ${err.message}`);
  });
  page.on('requestfailed', req => {
    allLogs.push(`[reqfail] ${req.url()} - ${req.failure()?.errorText}`);
    console.log(`[reqfail] ${req.url()} - ${req.failure()?.errorText}`);
  });

  // Enable CDP to get worker logs
  const client = await page.target().createCDPSession();
  await client.send('Runtime.enable');
  await client.send('Log.enable');

  console.log('=== Loading page ===');
  try {
    await page.goto('https://nci6tjq7.github.io/sdv-web-port/?v=1784431767' + Date.now(), {
      waitUntil: 'networkidle2',
      timeout: 120000
    });
  } catch (e) {
    console.log('Navigation result:', e.message);
  }

  console.log('\n=== Waiting 90s for SDV boot ===');
  await new Promise(r => setTimeout(r, 90000));

  // Also try to evaluate the page state
  const state = await page.evaluate(() => {
    return {
      crossOriginIsolated: window.crossOriginIsolated,
      title: document.title,
      errorLog: document.getElementById('error-log')?.textContent?.substring(0, 500),
      status: document.getElementById('status')?.textContent,
      loading: document.getElementById('loading')?.style?.display
    };
  }).catch(() => ({ error: 'eval failed' }));

  console.log('\n=== Page State ===');
  console.log(JSON.stringify(state, null, 2));

  console.log('\n=== ALL LOGS (' + allLogs.length + ' total) ===');
  allLogs.forEach(l => console.log(l));

  await browser.close();
  process.exit(0);
})();

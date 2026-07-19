const puppeteer = require('puppeteer-core');
const fs = require('fs');
const os = require('os');
const path = require('path');

(async () => {
  const tmpProfile = fs.mkdtempSync(path.join(os.tmpdir(), 'sdv-sw-'));
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

  // Pass 1: load and wait for SW to register + activate
  console.log('=== Pass 1: Register SW ===');
  await page.goto('https://nci6tjq7.github.io/sdv-web-port/?v=' + Date.now(), {
    waitUntil: 'networkidle2', timeout: 60000
  });
  // Wait for SW to be ready
  await page.evaluate(async () => {
    if (navigator.serviceWorker) {
      await navigator.serviceWorker.ready;
    }
  });
  console.log('SW ready');
  await new Promise(r => setTimeout(r, 3000));

  // Pass 2: reload — SW should now control the page
  console.log('=== Pass 2: Reload ===');
  await page.reload({ waitUntil: 'networkidle2', timeout: 60000 });
  await new Promise(r => setTimeout(r, 5000));

  const isolated = await page.evaluate(() => window.crossOriginIsolated);
  console.log('crossOriginIsolated:', isolated);

  if (!isolated) {
    // Try one more reload
    console.log('=== Pass 3: Reload again ===');
    await page.reload({ waitUntil: 'networkidle2', timeout: 60000 });
    await new Promise(r => setTimeout(r, 5000));
    const isolated2 = await page.evaluate(() => window.crossOriginIsolated);
    console.log('crossOriginIsolated (pass 3):', isolated2);
  }

  // Wait for SDV boot
  console.log('=== Waiting 120s for SDV boot ===');
  await new Promise(r => setTimeout(r, 120000));

  console.log('\n=== Key logs ===');
  logs.filter(l => l.includes('TitleContainer') || l.includes('Fetching') ||
       l.includes('Got') || l.includes('fetchSync') || l.includes('FATAL') ||
       l.includes('BigCraftables') || l.includes('MojoShader') ||
       l.includes('Main returned') || l.includes('crossOrigin') ||
       l.includes('ContentLoad')).forEach(l => console.log(l));

  const state = await page.evaluate(() => ({
    errorLog: document.getElementById('error-log')?.textContent?.substring(0, 500),
    status: document.getElementById('status')?.textContent,
  })).catch(() => ({error: 'eval failed'}));
  console.log('\n=== State ===');
  console.log(JSON.stringify(state, null, 2));

  await browser.close();
})();

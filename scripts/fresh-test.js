const puppeteer = require('puppeteer-core');
const fs = require('fs');
const os = require('os');
const path = require('path');

(async () => {
  const tmpProfile = fs.mkdtempSync(path.join(os.tmpdir(), 'sdv-fresh-'));
  const browser = await puppeteer.launch({
    executablePath: '/home/z/.agent-browser/browsers/chrome-150.0.7871.115/chrome',
    headless: 'new',
    userDataDir: tmpProfile,  // Fresh profile — no cached SW
    args: ['--no-sandbox', '--disable-setuid-sandbox', '--disable-dev-shm-usage', '--disable-gpu']
  });
  const page = await browser.newPage();
  const logs = [];
  page.on('console', msg => logs.push(`[${msg.type()}] ${msg.text()}`));
  page.on('pageerror', err => logs.push(`[pageerror] ${err.message}`));

  // First load — registers SW
  console.log('=== Pass 1: Register SW ===');
  await page.goto('https://nci6tjq7.github.io/sdv-web-port/?v=' + Date.now(), {
    waitUntil: 'domcontentloaded', timeout: 60000
  });
  await new Promise(r => setTimeout(r, 5000));

  // Reload — SW should be active
  console.log('=== Pass 2: SW active ===');
  await page.reload({ waitUntil: 'domcontentloaded', timeout: 60000 });
  await new Promise(r => setTimeout(r, 10000));

  // Check crossOriginIsolated
  const isolated = await page.evaluate(() => window.crossOriginIsolated);
  console.log('crossOriginIsolated:', isolated);

  // Wait for SDV to boot
  console.log('=== Waiting 90s for SDV boot ===');
  await new Promise(r => setTimeout(r, 90000));

  // Print key logs
  console.log('\n=== Key logs ===');
  logs.filter(l => l.includes('TitleContainer') || l.includes('Fetching') ||
       l.includes('Got') || l.includes('fetchSync') || l.includes('FATAL') ||
       l.includes('BigCraftables') || l.includes('MojoShader') ||
       l.includes('Main returned') || l.includes('crossOrigin')).forEach(l => console.log(l));

  const state = await page.evaluate(() => ({
    errorLog: document.getElementById('error-log')?.textContent?.substring(0, 500),
    status: document.getElementById('status')?.textContent,
  })).catch(() => ({error: 'eval failed'}));
  console.log('\n=== State ===');
  console.log(JSON.stringify(state, null, 2));

  await browser.close();
})();

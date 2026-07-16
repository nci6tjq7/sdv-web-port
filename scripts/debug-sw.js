// Debug SW: check SW state and actual response headers
const puppeteer = require('puppeteer-core');
const fs = require('fs');
const path = require('path');
const os = require('os');

(async () => {
  const tmpProfile = fs.mkdtempSync(path.join(os.tmpdir(), 'puppeteer-sdv-'));
  const browser = await puppeteer.launch({
    executablePath: '/home/z/.agent-browser/browsers/chrome-150.0.7871.115/chrome',
    headless: 'new',
    userDataDir: tmpProfile,
    args: ['--no-sandbox', '--disable-setuid-sandbox', '--disable-dev-shm-usage', '--disable-gpu']
  });
  const page = await browser.newPage();

  const logs = [];
  page.on('console', msg => logs.push(`[console.${msg.type()}] ${msg.text()}`));
  page.on('pageerror', err => logs.push(`[pageerror] ${err.message}`));

  console.log('=== Pass 1: load page (registers SW) ===');
  await page.goto('https://nci6tjq7.github.io/sdv-web-port/?v=1784233962', {
    waitUntil: 'networkidle2',
    timeout: 60000
  }).catch(e => console.log('goto err:', e.message));
  await new Promise(r => setTimeout(r, 8000));

  // Check SW state
  const swState = await page.evaluate(async () => {
    const reg = await navigator.serviceWorker.getRegistration();
    if (!reg) return { registered: false };
    const sw = reg.active || reg.waiting || reg.installing;
    return {
      registered: true,
      scope: reg.scope,
      scriptURL: reg.active?.scriptURL,
      state: sw?.state,
      controller: !!navigator.serviceWorker.controller,
      crossOriginIsolated: window.crossOriginIsolated
    };
  });
  console.log('\n=== SW State after Pass 1 ===');
  console.log(JSON.stringify(swState, null, 2));

  console.log('\n=== Pass 2: reload ===');
  await page.reload({ waitUntil: 'networkidle2', timeout: 60000 }).catch(e => console.log('reload err:', e.message));
  await new Promise(r => setTimeout(r, 5000));

  const swState2 = await page.evaluate(async () => {
    const reg = await navigator.serviceWorker.getRegistration();
    if (!reg) return { registered: false };
    return {
      registered: true,
      scope: reg.scope,
      scriptURL: reg.active?.scriptURL,
      state: reg.active?.state,
      controller: !!navigator.serviceWorker.controller,
      crossOriginIsolated: window.crossOriginIsolated
    };
  });
  console.log('\n=== SW State after Pass 2 ===');
  console.log(JSON.stringify(swState2, null, 2));

  // Fetch a subresource THROUGH the SW and inspect headers
  const headerCheck = await page.evaluate(async () => {
    try {
      const resp = await fetch('main.js', { cache: 'no-store' });
      const headers = {};
      resp.headers.forEach((v, k) => { headers[k] = v; });
      return {
        status: resp.status,
        url: resp.url,
        type: resp.type,
        headers
      };
    } catch (e) {
      return { error: e.message };
    }
  });
  console.log('\n=== Response headers for main.js (through SW) ===');
  console.log(JSON.stringify(headerCheck, null, 2));

  // Also fetch the HTML
  const htmlHeaders = await page.evaluate(async () => {
    try {
      const resp = await fetch(location.href, { cache: 'no-store' });
      const headers = {};
      resp.headers.forEach((v, k) => { headers[k] = v; });
      return { status: resp.status, headers };
    } catch (e) {
      return { error: e.message };
    }
  });
  console.log('\n=== Response headers for HTML (through SW) ===');
  console.log(JSON.stringify(htmlHeaders, null, 2));

  // Wait a bit more to see if SDV boots
  console.log('\n=== Waiting 30s more for SDV boot ===');
  await new Promise(r => setTimeout(r, 30000));

  console.log('\n=== ALL captured logs ===');
  logs.forEach(l => console.log(l));

  await browser.close();
})();

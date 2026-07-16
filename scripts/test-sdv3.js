const puppeteer = require('puppeteer-core');
const CHROME = '/home/z/.agent-browser/browsers/' + require('fs').readdirSync('/home/z/.agent-browser/browsers/')[0] + '/chrome';

(async () => {
  const browser = await puppeteer.launch({
    executablePath: CHROME,
    headless: 'new',
    args: ['--no-sandbox', '--disable-setuid-sandbox', '--disable-dev-shm-usage', '--disable-gpu']
  });
  const page = await browser.newPage();
  const client = await page.target().createCDPSession();
  await client.send('Network.setCacheDisabled', { cacheDisabled: true });
  
  const logs = [];
  page.on('console', msg => logs.push(`[console.${msg.type()}] ${msg.text()}`));
  page.on('pageerror', err => logs.push(`[pageerror] ${err.message}`));
  page.on('requestfailed', req => logs.push(`[requestfailed] ${req.url()} - ${req.failure()?.errorText}`));
  
  // Load + reload for SW
  await page.goto('https://nci6tjq7.github.io/sdv-web-port/?t=' + Date.now(), { waitUntil: 'networkidle2', timeout: 30000 });
  await new Promise(r => setTimeout(r, 3000));
  await page.reload({ waitUntil: 'networkidle2', timeout: 30000 });
  await new Promise(r => setTimeout(r, 3000));
  await page.reload({ waitUntil: 'networkidle2', timeout: 30000 });
  await new Promise(r => setTimeout(r, 15000));
  
  console.log('\n=== Console Logs (last 25) ===');
  for (const log of logs.slice(-25)) console.log(log);
  
  const env = await page.evaluate(() => ({
    title: document.title,
    status: document.getElementById('status')?.textContent || '(none)',
    errorLog: document.getElementById('error-log')?.textContent || '(none)',
    crossOriginIsolated: window.crossOriginIsolated
  }));
  console.log('\n=== Page State ===');
  console.log(JSON.stringify(env, null, 2));
  
  await browser.close();
})();

const puppeteer = require('puppeteer-core');
const CHROME = '/home/z/.agent-browser/browsers/chrome-150.0.7871.115/chrome';

(async () => {
  const browser = await puppeteer.launch({
    executablePath: CHROME,
    headless: 'new',
    args: ['--no-sandbox', '--disable-setuid-sandbox', '--disable-dev-shm-usage', '--disable-gpu', '--disable-application-cache', '--disable-cache']
  });
  const page = await browser.newPage();
  
  // Disable cache
  const client = await page.target().createCDPSession();
  await client.send('Network.setCacheDisabled', { cacheDisabled: true });
  
  const logs = [];
  page.on('console', msg => logs.push(`[console.${msg.type()}] ${msg.text()}`));
  page.on('pageerror', err => logs.push(`[pageerror] ${err.message}`));
  
  // Use cache-busting query param
  console.log('Loading page with cache disabled...');
  await page.goto('https://nci6tjq7.github.io/sdv-web-port/?t=' + Date.now(), { waitUntil: 'networkidle2', timeout: 30000 });
  
  console.log('Waiting 15s for runtime...');
  await new Promise(r => setTimeout(r, 15000));
  
  console.log('\n=== Console Logs ===');
  for (const log of logs) console.log(log);
  
  const env = await page.evaluate(() => ({
    title: document.title,
    status: document.getElementById('status')?.textContent || '(none)',
    errorLog: document.getElementById('error-log')?.textContent || '(none)'
  }));
  console.log('\n=== Page State ===');
  console.log(JSON.stringify(env, null, 2));
  
  await browser.close();
})();

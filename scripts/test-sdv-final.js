const puppeteer = require('puppeteer-core');

(async () => {
  const browser = await puppeteer.launch({
    executablePath: '/home/z/.agent-browser/browsers//home/z/.agent-browser/browsers/chrome-150.0.7871.115/chrome',
    headless: 'new',
    args: ['--no-sandbox', '--disable-setuid-sandbox', '--disable-dev-shm-usage', '--disable-gpu']
  });
  const page = await browser.newPage();
  
  const logs = [];
  page.on('console', msg => logs.push(`[console.${msg.type()}] ${msg.text()}`));
  page.on('pageerror', err => logs.push(`[pageerror] ${err.message}`));
  
  console.log('Loading page...');
  await page.goto('https://nci6tjq7.github.io/sdv-web-port/', { waitUntil: 'networkidle2', timeout: 30000 });
  
  console.log('Waiting 15s for runtime to boot...');
  await new Promise(r => setTimeout(r, 15000));
  
  console.log('\n=== Console Logs ===');
  for (const log of logs) console.log(log);
  
  const env = await page.evaluate(() => ({
    crossOriginIsolated: window.crossOriginIsolated,
    title: document.title,
    status: document.getElementById('status')?.textContent || '(none)',
    errorLog: document.getElementById('error-log')?.textContent || '(none)'
  }));
  console.log('\n=== Page State ===');
  console.log(JSON.stringify(env, null, 2));
  
  await browser.close();
})();

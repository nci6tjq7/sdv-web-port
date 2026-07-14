const puppeteer = require('puppeteer-core');

(async () => {
  const browser = await puppeteer.launch({
    executablePath: '/home/z/.agent-browser/browsers/chrome-150.0.7871.124/chrome',
    headless: 'new',
    args: ['--no-sandbox', '--disable-setuid-sandbox', '--disable-dev-shm-usage', '--disable-gpu']
  });
  const page = await browser.newPage();
  
  const logs = [];
  page.on('console', msg => logs.push(`[console.${msg.type()}] ${msg.text()}`));
  page.on('pageerror', err => logs.push(`[pageerror] ${err.message}`));
  page.on('requestfailed', req => logs.push(`[requestfailed] ${req.url()} - ${req.failure()?.errorText}`));
  
  console.log('Navigating to https://nci6tjq7.github.io/sdv-web-port/ ...');
  try {
    await page.goto('https://nci6tjq7.github.io/sdv-web-port/', { 
      waitUntil: 'networkidle2',
      timeout: 30000 
    });
  } catch(e) {
    console.log('Navigation timeout/error:', e.message);
  }
  
  await new Promise(r => setTimeout(r, 8000));
  
  console.log('\n=== Browser Console Logs ===');
  for (const log of logs) console.log(log);
  
  const title = await page.title();
  console.log('\nPage Title:', title);
  
  const env = await page.evaluate(() => ({
    hasWebAssembly: typeof WebAssembly !== 'undefined',
    hasSharedArrayBuffer: typeof SharedArrayBuffer !== 'undefined',
    crossOriginIsolated: window.crossOriginIsolated,
    swController: !!navigator.serviceWorker?.controller
  }));
  console.log('Environment:', JSON.stringify(env, null, 2));
  
  const errorLog = await page.evaluate(() => document.getElementById('error-log')?.textContent || '(none)');
  console.log('Error Log:', errorLog);
  
  await browser.close();
})();

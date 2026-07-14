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
  
  console.log('First load (register SW)...');
  await page.goto('https://nci6tjq7.github.io/sdv-web-port/', { waitUntil: 'networkidle2', timeout: 30000 });
  await new Promise(r => setTimeout(r, 3000));
  
  console.log('Reload (activate SW)...');
  await page.reload({ waitUntil: 'networkidle2', timeout: 30000 });
  await new Promise(r => setTimeout(r, 3000));
  
  console.log('Second reload (SW active)...');
  await page.reload({ waitUntil: 'networkidle2', timeout: 30000 });
  await new Promise(r => setTimeout(r, 10000));
  
  console.log('\n=== Console Logs (last 30) ===');
  for (const log of logs.slice(-30)) console.log(log);
  
  const env = await page.evaluate(() => ({
    crossOriginIsolated: window.crossOriginIsolated,
    hasSharedArrayBuffer: typeof SharedArrayBuffer !== 'undefined'
  }));
  console.log('\nEnvironment:', JSON.stringify(env));
  
  const errorLog = await page.evaluate(() => document.getElementById('error-log')?.textContent || '(none)');
  console.log('Error Log:', errorLog.substring(0, 500));
  
  const status = await page.evaluate(() => document.getElementById('status')?.textContent || '(none)');
  console.log('Status:', status);
  
  await browser.close();
})();

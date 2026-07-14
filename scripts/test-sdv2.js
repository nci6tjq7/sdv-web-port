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
  
  // First load + 2 reloads to activate SW
  await page.goto('https://nci6tjq7.github.io/sdv-web-port/', { waitUntil: 'networkidle2', timeout: 30000 });
  await new Promise(r => setTimeout(r, 2000));
  await page.reload({ waitUntil: 'networkidle2', timeout: 30000 });
  await new Promise(r => setTimeout(r, 2000));
  await page.reload({ waitUntil: 'networkidle2', timeout: 30000 });
  
  // Wait longer for .NET to boot
  console.log('Waiting 15s for .NET runtime to boot...');
  await new Promise(r => setTimeout(r, 15000));
  
  console.log('\n=== All Console Logs ===');
  for (const log of logs) console.log(log);
  
  // Check if there's a dotnet runtime error
  const dotnetState = await page.evaluate(() => {
    return {
      crossOriginIsolated: window.crossOriginIsolated,
      // Check if Mono runtime is loaded
      monoLoaded: typeof globalThis.Module !== 'undefined' || typeof globalThis.mono !== 'undefined',
      // Check for any global .NET objects
      dotnetKeys: Object.keys(globalThis).filter(k => k.toLowerCase().includes('dotnet') || k.toLowerCase().includes('mono')).slice(0, 10)
    };
  });
  console.log('\n=== .NET Runtime State ===');
  console.log(JSON.stringify(dotnetState, null, 2));
  
  await browser.close();
})();

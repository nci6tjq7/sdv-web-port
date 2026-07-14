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
  
  // First load to register SW
  await page.goto('https://nci6tjq7.github.io/sdv-web-port/', { waitUntil: 'networkidle2', timeout: 30000 });
  await new Promise(r => setTimeout(r, 3000));
  
  // Reload to activate SW
  await page.reload({ waitUntil: 'networkidle2', timeout: 30000 });
  await new Promise(r => setTimeout(r, 8000));
  
  console.log('\n=== Console after reload ===');
  for (const log of logs.slice(-15)) console.log(log);
  
  // Check the export structure
  const exportInfo = await page.evaluate(async () => {
    try {
      const { dotnet } = await import('./_framework/dotnet.js');
      const instance = await dotnet.create();
      const exports = await instance.getAssemblyExports("SdvWebPort.FnaRuntime");
      return {
        exportKeys: Object.keys(exports),
        exportType: typeof exports,
        isUndefined: exports === undefined,
        jsonStr: JSON.stringify(exports, (k,v) => typeof v === 'function' ? '[function]' : v, 2).substring(0, 2000)
      };
    } catch(e) {
      return { error: e.message, stack: e.stack };
    }
  });
  console.log('\n=== Export Info ===');
  console.log(JSON.stringify(exportInfo, null, 2));
  
  await browser.close();
})();

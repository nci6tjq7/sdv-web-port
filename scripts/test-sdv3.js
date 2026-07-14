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
  
  // Load + reloads for SW
  await page.goto('https://nci6tjq7.github.io/sdv-web-port/', { waitUntil: 'networkidle2', timeout: 30000 });
  await new Promise(r => setTimeout(r, 2000));
  await page.reload({ waitUntil: 'networkidle2', timeout: 30000 });
  await new Promise(r => setTimeout(r, 2000));
  await page.reload({ waitUntil: 'networkidle2', timeout: 30000 });
  await new Promise(r => setTimeout(r, 5000));
  
  // Now manually test the dotnet runtime API
  const result = await page.evaluate(async () => {
    try {
      const { dotnet } = await import('./_framework/dotnet.js');
      // Check what create() accepts
      const createStr = dotnet.create.toString().substring(0, 500);
      
      // Create with explicit mainAssemblyName
      const instance = await dotnet.create({
        mainAssemblyName: "SdvWebPort.FnaRuntime"
      });
      
      // Try to get exports
      const exports = await instance.getAssemblyExports("SdvWebPort.FnaRuntime");
      const exportKeys = Object.keys(exports);
      
      // Check if there's a Program type
      let programKeys = [];
      try {
        programKeys = Object.keys(exports.SdvWebPort?.FnaRuntime?.Program || {});
      } catch(e) {}
      
      return {
        createStr: createStr,
        exportKeys: exportKeys,
        programKeys: programKeys,
        hasRunMain: typeof instance.runMain === 'function',
        instanceKeys: Object.keys(instance).filter(k => typeof instance[k] === 'function')
      };
    } catch(e) {
      return { error: e.message, stack: e.stack };
    }
  });
  
  console.log('\n=== Dotnet API Check ===');
  console.log(JSON.stringify(result, null, 2));
  
  console.log('\n=== Recent Logs ===');
  for (const log of logs.slice(-10)) console.log(log);
  
  await browser.close();
})();

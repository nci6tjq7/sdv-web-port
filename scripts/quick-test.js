const puppeteer = require('puppeteer-core');
(async () => {
  const browser = await puppeteer.launch({
    executablePath: '/home/z/.agent-browser/browsers/chrome-150.0.7871.115/chrome',
    headless: 'new',
    args: ['--no-sandbox', '--disable-setuid-sandbox', '--disable-dev-shm-usage', '--disable-gpu']
  });
  const page = await browser.newPage();
  const logs = [];
  page.on('console', msg => logs.push(`[${msg.type()}] ${msg.text()}`));
  page.on('pageerror', err => logs.push(`[pageerror] ${err.message}`));
  
  console.log('Loading page...');
  await page.goto('https://nci6tjq7.github.io/sdv-web-port/?v=' + Date.now(), {
    waitUntil: 'domcontentloaded', timeout: 60000
  });
  
  console.log('Waiting 120s for SDV boot...');
  await new Promise(r => setTimeout(r, 120000));
  
  console.log('\n=== Console logs ===');
  logs.filter(l => l.includes('SdvWebPort') || l.includes('FATAL') || l.includes('HttpTitle') || 
       l.includes('fetchSync') || l.includes('Fetching') || l.includes('Content') || 
       l.includes('BigCraftables') || l.includes('MojoShader') || l.includes('Main returned') ||
       l.includes('Got') || l.includes('bytes')).forEach(l => console.log(l));
  
  await browser.close();
})();

const puppeteer = require('puppeteer');
const path = require('path');

const BASE_URL = 'http://localhost:5160';
const screenshotsDir = path.join(__dirname, 'Screenshots');

(async () => {
    console.log('Retaking screenshot #7 - Materials Inventory...');
    
    const browser = await puppeteer.launch({ 
        headless: "new",
        ignoreHTTPSErrors: true
    });
    
    const page = await browser.newPage();
    await page.setViewport({ width: 1920, height: 1080 });

    // Login as admin
    console.log('Logging in as admin...');
    await page.goto(`${BASE_URL}/Identity/Account/Login`, { waitUntil: 'networkidle2' });
    await page.type('#Input_Email', 'admin@homeserveit.local');
    await page.type('#Input_Password', 'Admin@123');
    await Promise.all([
        page.click('#login-submit'),
        page.waitForNavigation({ waitUntil: 'networkidle2' }),
    ]);

    // Take screenshot
    console.log('Navigating to Inventory page...');
    await page.goto(`${BASE_URL}/Admin/Finance/Inventory`, { waitUntil: 'networkidle2' });
    await new Promise(r => setTimeout(r, 1500));

    const filePath = path.join(screenshotsDir, '7_MaterialsInventory.png');
    await page.screenshot({ path: filePath, fullPage: true });
    console.log(`Saved: ${filePath}`);

    await browser.close();
    console.log('Done!');
})();

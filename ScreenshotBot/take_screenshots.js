const puppeteer = require('puppeteer');
const fs = require('fs');
const path = require('path');

// Ensure Screenshots directory exists
const screenshotsDir = path.join(__dirname, 'Screenshots');
if (!fs.existsSync(screenshotsDir)){
    fs.mkdirSync(screenshotsDir);
}

// NOTE: Make sure your app is running (dotnet run or dotnet watch) before running this script!
const BASE_URL = 'http://localhost:5160'; // Changed to match your launchSettings.json HTTP profile

const pagesToScreenshot = [
    { name: '1_HomePage', url: '/' },
    { name: '2_LoginPage', url: '/Identity/Account/Login' },
    { name: '3_RegisterPage', url: '/Identity/Account/Register' },
    // Add other routes here once you build them! 
    // Example: { name: '4_AdminDashboard', url: '/Admin/Dashboard' }
];

(async () => {
    console.log('Starting screenshot bot...');
    
    // Launch browser (headless means it runs invisibly in the background)
    const browser = await puppeteer.launch({ 
        headless: "new",
        ignoreHTTPSErrors: true // Important for local development with self-signed SSL
    });
    
    const page = await browser.newPage();
    
    // Set viewport to 1080p desktop size
    await page.setViewport({ width: 1920, height: 1080 });

    for (const item of pagesToScreenshot) {
        const targetUrl = `${BASE_URL}${item.url}`;
        console.log(`Taking screenshot of: ${targetUrl}`);
        
        try {
            await page.goto(targetUrl, { waitUntil: 'networkidle2' });
            
            // Give it an extra second to load animations/images
            await new Promise(r => setTimeout(r, 1000));
            
            const filePath = path.join(screenshotsDir, `${item.name}.png`);
            await page.screenshot({ path: filePath, fullPage: true });
            
            console.log(`Saved screenshot: ${filePath}`);
        } catch (error) {
            console.error(`Failed to capture ${targetUrl}:`, error.message);
        }
    }

    await browser.close();
    console.log('Finished capturing all screens! Check the /ScreenshotBot/Screenshots folder.');
})();

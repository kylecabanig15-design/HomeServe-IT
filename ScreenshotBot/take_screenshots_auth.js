const puppeteer = require('puppeteer');
const fs = require('fs');
const path = require('path');

const screenshotsDir = path.join(__dirname, 'Screenshots');
if (!fs.existsSync(screenshotsDir)){
    fs.mkdirSync(screenshotsDir);
}

const BASE_URL = 'http://localhost:5160';

(async () => {
    console.log('Starting screenshot bot (Authenticated)...');
    
    const browser = await puppeteer.launch({ 
        headless: "new",
        ignoreHTTPSErrors: true 
    });
    
    const page = await browser.newPage();
    await page.setViewport({ width: 1920, height: 1080 });

    const login = async (email, password) => {
        console.log(`Logging in as ${email}...`);
        await page.goto(`${BASE_URL}/Identity/Account/Login`, { waitUntil: 'networkidle2' });
        await page.type('#Input_Email', email);
        await page.type('#Input_Password', password);
        await Promise.all([
            page.click('#login-submit'),
            page.waitForNavigation({ waitUntil: 'networkidle2' }),
        ]);
    };

    const logout = async () => {
        const cookies = await page.cookies();
        await page.deleteCookie(...cookies);
    };

    const capture = async (name, url) => {
        const targetUrl = `${BASE_URL}${url}`;
        console.log(`Taking screenshot of: ${targetUrl}`);
        try {
            await page.goto(targetUrl, { waitUntil: 'networkidle2' });
            await new Promise(r => setTimeout(r, 1000));
            const filePath = path.join(screenshotsDir, `${name}.png`);
            await page.screenshot({ path: filePath, fullPage: true });
            console.log(`Saved screenshot: ${filePath}`);
        } catch (error) {
            console.error(`Failed to capture ${targetUrl}:`, error.message);
        }
    };

    // Public
    await capture('1_HomePage', '/');
    await capture('2_LoginPage', '/Identity/Account/Login');
    await capture('3_RegisterPage', '/Identity/Account/Register');

    // Admin
    await login('admin@homeserveit.local', 'Admin@123');
    await capture('4_AdminDashboard', '/Admin/Dashboard');
    await capture('7_MaterialsInventory', '/Admin/Finance/Inventory');
    await capture('8_QuotationAndBilling', '/Admin/Finance/Quotations');
    await capture('9_CustomerCRM', '/Admin/Crm/Customers');
    await logout();

    // Customer
    await login('kyle@homeserveit.local', 'Cust@123');
    await capture('5_ServiceBooking', '/Customer/ServiceRequests');
    await logout();

    // Technician
    await login('marco@homeserveit.local', 'Tech@123');
    await capture('6_TechnicianJobList', '/Technician/AssignedJobs');
    await logout();

    await browser.close();
    console.log('Finished capturing all screens!');
})();

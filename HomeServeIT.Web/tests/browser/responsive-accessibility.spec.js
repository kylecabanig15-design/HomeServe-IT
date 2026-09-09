const { test, expect } = require('@playwright/test');
const AxeBuilder = require('@axe-core/playwright').default;

test.use({ trace: 'off', screenshot: 'off', video: 'off' });
test.beforeEach(() => test.skip(process.env.HOMESERVE_ISOLATED_FIXTURE !== '1', 'Disposable fixture required.'));
async function login(page, role) {
  await page.goto('/Identity/Account/Login');
  await page.locator('#Input_Email').fill(role.toLowerCase() + '@test.invalid');
  await page.locator('#Input_Password').fill(process.env.HOMESERVE_FIXTURE_PASSWORD);
  await page.locator('#login-submit').click();
  await expect(page).toHaveURL(/Dashboard/);
  await page.waitForLoadState('load');
  if (role === 'Admin') await expect.poll(() => page.evaluate(() => Boolean(window.Chart?.getChart('revenueChart')))).toBe(true);
}
for (const role of ['Admin', 'Customer', 'Technician']) {
  for (const width of [390, 768, 1440]) {
    test(`${role} shell at ${width}px has contained overflow and usable navigation`, async ({ page }, testInfo) => {
      const errors = [];
      page.on('pageerror', error => errors.push(error.message));
      page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
      await page.setViewportSize({ width, height: 900 });
      await login(page, role);
      await page.screenshot({ path: `App_Data/layout-${role}-${width}-${testInfo.project.name}.png`, fullPage: true });
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
      const dashboardAudit = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
      expect(dashboardAudit.violations.filter(v => ['serious', 'critical'].includes(v.impact)).map(v => ({ id: v.id, nodes: v.nodes.map(n => n.target) }))).toEqual([]);
      if (width < 1024) {
        const menu = page.getByRole('button', { name: 'Open navigation', exact: true });
        await expect(menu).toBeVisible();
        await menu.click();
        await expect(page.locator('#app-navigation')).toBeVisible();
        await page.keyboard.press('Escape');
        await expect(menu).toBeFocused();
        await expect(page.locator('#app-navigation')).toBeHidden();
      }
      const route = role === 'Admin' ? '/Admin/Finance/Quotations' : '/' + role + '/ProfileAndSettings';
      await page.goto(route);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
      const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
      expect(results.violations.filter(v => ['serious', 'critical'].includes(v.impact)).map(v => ({ id: v.id, nodes: v.nodes.map(n => n.target) }))).toEqual([]);
      if (role !== 'Admin') {
        const edit = page.getByRole('button', { name: 'Edit', exact: true }).first();
        await edit.focus();
        await page.keyboard.press('Enter');
        const dialog = page.getByRole('dialog', { name: 'Edit Profile' });
        await expect(dialog).toBeVisible();
        expect(await dialog.evaluate(el => el.contains(document.activeElement))).toBe(true);
        const focusable = dialog.locator('input:not([type="hidden"]),button').filter({ visible: true });
        await focusable.last().focus();
        await page.keyboard.press('Tab');
        expect(await dialog.evaluate(el => el.contains(document.activeElement))).toBe(true);
        await page.keyboard.press('Escape');
        await expect(dialog).toBeHidden();
        await expect(edit).toBeFocused();
      }
      expect(errors).toEqual([]);
    });
  }
}
for (const route of ['/', '/Identity/Account/Login', '/Identity/Account/Register', '/Home/Privacy']) {
test(`Public ${route} has accessible controls and one h1`, async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 900 });
    await page.goto(route);
    await expect(page.locator('h1')).toHaveCount(1);
    const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
    expect(results.violations.filter(v => ['serious', 'critical'].includes(v.impact)).map(v => ({ route, id: v.id, nodes: v.nodes.map(n => n.target) }))).toEqual([]);
});
}

const roleRoutes = {
  Admin: ['/Admin/Dashboard', '/Admin/Operations/ServiceRequests', '/Admin/Operations/Technicians',
    '/Admin/Finance/Quotations', '/Admin/Finance/Billing', '/Admin/Finance/Inventory',
    '/Admin/Crm/Customers', '/Admin/Crm/ServiceHistory', '/Admin/Crm/Reports', '/Admin/Support',
    '/Admin/System/UserManagement', '/Admin/System/ArchivedUsers', '/Admin/System/Settings', '/Admin/Notifications'],
  Customer: ['/Customer/Dashboard', '/Customer/ServiceRequests', '/Customer/Quotations',
    '/Customer/BillsAndPayments', '/Customer/MyDevices', '/Customer/Support',
    '/Customer/ProfileAndSettings', '/Customer/Notifications'],
  Technician: ['/Technician/Dashboard', '/Technician/Schedule', '/Technician/AssignedJobs',
    '/Technician/ProfileAndSettings', '/Technician/Notifications']
};
for (const [role, routes] of Object.entries(roleRoutes)) {
  test(`${role} primary pages have one heading and no serious automated accessibility violations`, async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await login(page, role);
    const failures = [];
    for (const route of routes) {
      await page.goto(route);
      await page.waitForLoadState('load');
      const headings = await page.locator('h1').count();
      if (headings !== 1) failures.push({ route, issue: `Expected one h1; found ${headings}` });
      const audit = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
      for (const violation of audit.violations.filter(v => ['serious', 'critical'].includes(v.impact)))
        failures.push({ route, issue: violation.id, nodes: violation.nodes.map(n => n.target) });
    }
    expect(failures).toEqual([]);
  });
}

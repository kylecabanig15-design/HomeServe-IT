const { test, expect } = require('@playwright/test');

function observeRuntimeFailures(page) {
  const failures = [];
  const applicationOrigin = new URL(
    process.env.HOMESERVE_BASE_URL || 'http://127.0.0.1:5160').origin;
  page.on('pageerror', error => failures.push(`page error: ${error.message}`));
  page.on('console', message => {
    if (message.type() === 'error')
      failures.push(`console error: ${message.text()}`);
  });
  page.on('response', response => {
    if (new URL(response.url()).origin === applicationOrigin && response.status() >= 500)
      failures.push(`HTTP ${response.status()}: ${response.url()}`);
  });
  return failures;
}

test('public home page renders without runtime errors or horizontal overflow', async ({ page }) => {
  const failures = observeRuntimeFailures(page);

  await page.goto('/');
  await expect(page).toHaveTitle(/HomeServe/i);
  await expect(page.locator('body')).toBeVisible();

  const hasHorizontalOverflow = await page.evaluate(() =>
    document.documentElement.scrollWidth > document.documentElement.clientWidth + 1);
  expect(hasHorizontalOverflow).toBe(false);
  expect(failures).toEqual([]);
});

test('anonymous users are redirected to Identity login for an admin route', async ({ page }) => {
  await page.goto('/Admin/Dashboard');

  await expect(page).toHaveURL(/\/Identity\/Account\/Login/i);
  await expect(page.getByRole('button', { name: /sign in to account/i })).toBeVisible();
});

test('development administrator account reaches its role dashboard', async ({ page }) => {
  const email = process.env.HOMESERVE_ADMIN_EMAIL;
  const password = process.env.HOMESERVE_ADMIN_PASSWORD;
  test.skip(!email || !password, 'Set HOMESERVE_ADMIN_EMAIL and HOMESERVE_ADMIN_PASSWORD to run the authenticated smoke test.');
  const failures = observeRuntimeFailures(page);

  await page.goto('/Identity/Account/Login');
  await page.getByLabel(/email address/i).fill(email);
  await page.getByLabel(/^password$/i).fill(password);
  await page.getByRole('button', { name: /sign in to account/i }).click();

  await expect(page).toHaveURL(/\/Admin\/Dashboard/i);
  await expect(page.locator('body')).toBeVisible();
  expect(failures).toEqual([]);
});

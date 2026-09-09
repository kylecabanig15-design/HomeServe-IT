const { test, expect } = require('@playwright/test');

// Only the explicitly provisioned disposable fixture may be mutated by these tests.
test.beforeEach(() => {
  test.skip(process.env.HOMESERVE_ISOLATED_FIXTURE !== '1', 'Run npm run test:phases with a disposable MySQL connection.');
});
test.use({ trace: 'off', screenshot: 'off', video: 'off' });

async function login(page, name) {
  await page.goto('/Identity/Account/Login');
  await page.locator('#Input_Email').fill(name + '@test.invalid');
  await page.locator('#Input_Password').fill(process.env.HOMESERVE_FIXTURE_PASSWORD);
  await page.locator('#login-submit').click();
  await expect(page).toHaveURL(/\/(Admin|Customer|Technician)\/Dashboard/);
}
function errors(page) {
  const failures = [];
  page.on('pageerror', e => failures.push(e.message));
  page.on('console', m => { if (m.type() === 'error') failures.push(m.text()); });
  page.on('response', r => { if (r.status() >= 500) failures.push('HTTP ' + r.status() + ' ' + new URL(r.url()).pathname); });
  return failures;
}
async function token(page, route) {
  await page.goto(route);
  return page.locator('input[name="__RequestVerificationToken"]').first().inputValue();
}

test('login and registration run local validation without JavaScript errors', async ({ page }) => {
  const failures = errors(page);
  for (const route of ['/Identity/Account/Login', '/Identity/Account/Register']) {
    await page.goto(route);
    await expect.poll(() => page.evaluate(() => Boolean(window.jQuery?.validator?.unobtrusive))).toBe(true);
    expect(await page.evaluate(() => window.jQuery('form').first().valid())).toBe(false);
  }
  expect(failures).toEqual([]);
});

test('administrator settings persist, affect branding, and can close public registration', async ({ page, browser }, testInfo) => {
  const failures = errors(page);
  const anonymousContext = await browser.newContext({ baseURL: process.env.HOMESERVE_BASE_URL });
  const anonymous = await anonymousContext.newPage();
  try {
    await anonymous.goto('/Identity/Account/Register');
    const registrationToken = await anonymous.locator('input[name="__RequestVerificationToken"]').first().inputValue();
    await login(page, 'admin');
    await page.setViewportSize({ width: 390, height: 900 });
    await page.goto('/Admin/System/Settings');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    const systemName = 'HomeServe Test ' + testInfo.project.name;
    await page.getByLabel('System name').fill(systemName);
    await page.getByLabel('Support email').fill('settings@test.invalid');
    await Promise.all([
      page.waitForNavigation(),
      page.locator('form[action$="UpdateGeneral"] button[type="submit"]').click()
    ]);
    await expect(page.getByText('Settings saved successfully.')).toBeVisible();
    await expect(page).toHaveTitle(new RegExp(systemName));
    await page.reload();
    await expect(page.getByLabel('System name')).toHaveValue(systemName);

    await page.getByRole('tab', { name: 'Company profile' }).click();
    const companyName = page.getByLabel('Company or project name');
    const originalCompanyName = await companyName.inputValue();
    await companyName.fill('Unsaved value');
    await page.locator('form[action$="UpdateCompany"] button[type="reset"]').click();
    await expect(companyName).toHaveValue(originalCompanyName);
    await companyName.fill('HomeServe University Project');
    await page.getByLabel('Company or project address').fill('Davao City, Philippines');
    await Promise.all([
      page.waitForNavigation(),
      page.locator('form[action$="UpdateCompany"] button[type="submit"]').click()
    ]);
    await expect(page.getByLabel('Company or project name')).toHaveValue('HomeServe University Project');
    await anonymous.goto('/Home/Privacy');
    await expect(anonymous).toHaveTitle(new RegExp(systemName));
    await expect(anonymous.locator('footer')).toContainText('HomeServe University Project');
    await expect(anonymous.locator('a[href="mailto:settings@test.invalid"]')).toBeVisible();

    await page.getByRole('tab', { name: 'Notifications' }).click();
    await page.getByLabel('Refresh interval (seconds)').fill('45');
    await Promise.all([
      page.waitForNavigation(),
      page.locator('form[action$="UpdateNotifications"] button[type="submit"]').click()
    ]);
    await expect(page.getByLabel('Refresh interval (seconds)')).toHaveValue('45');
    await page.getByRole('tab', { name: 'Integrations' }).click();
    await expect(page.getByText('No configurable integrations')).toBeVisible();

    await page.getByRole('tab', { name: 'Security' }).click();
    const registrationToggle = page.getByLabel('Allow public customer registration');
    await registrationToggle.uncheck();
    await Promise.all([
      page.waitForNavigation(),
      page.locator('form[action$="UpdateSecurity"] button[type="submit"]').click()
    ]);
    await anonymous.goto('/Identity/Account/Register');
    await expect(anonymous.getByRole('heading', { name: 'Registration is unavailable' })).toBeVisible();
    const directPost = await anonymous.request.post('/Identity/Account/Register', { form: {
      __RequestVerificationToken: registrationToken,
      'Input.FullName': 'Blocked Person', 'Input.Email': 'blocked@test.invalid',
      'Input.PhoneNumber': '+639123456789', 'Input.Password': process.env.HOMESERVE_FIXTURE_PASSWORD,
      'Input.ConfirmPassword': process.env.HOMESERVE_FIXTURE_PASSWORD,
      'Input.StreetAddress': 'Test Street', 'Input.BarangayCity': 'Test City'
    } });
    expect(directPost.status()).toBe(200);
    expect(await directPost.text()).toContain('Registration is unavailable');
  } finally {
    if (/\/Admin\//.test(page.url())) {
      await page.goto('/Admin/System/Settings?tab=security');
      const toggle = page.getByLabel('Allow public customer registration');
      if (await toggle.isVisible() && !(await toggle.isChecked())) {
        await toggle.check();
        await Promise.all([
          page.waitForNavigation(),
          page.locator('form[action$="UpdateSecurity"] button[type="submit"]').click()
        ]);
      }
    }
    await anonymousContext.close();
  }
  expect(failures).toEqual([]);
});

for (const [role, name] of [['Customer', 'customer'], ['Technician', 'technician']]) {
  test(role + ' profile persists after reload and rejects colliding email and overposting', async ({ page }) => {
    const failures = errors(page);
    await login(page, name);
    await page.goto('/' + role + '/ProfileAndSettings');
    await page.getByRole('button', { name: 'Edit', exact: true }).first().click();
    await page.locator('#FullName').fill('Edited ' + name);
    await page.locator('#Mobile').fill('+639123456789');
    await page.locator('#Address').fill('Changed Street');
    await page.locator('#City').fill('Changed City');
    await Promise.all([
      page.waitForNavigation({ waitUntil: 'domcontentloaded' }),
      page.locator('form[action*="UpdateProfile"] button[type="submit"]').click()
    ]);
    await page.reload();
    await expect(page.locator('#FullName')).toHaveValue('Edited ' + name);
    await expect(page.locator('#Address')).toHaveValue('Changed Street');
    const csrf = await page.locator('input[name="__RequestVerificationToken"]').first().inputValue();
    const response = await page.request.post('/' + role + '/ProfileAndSettings/UpdateProfile', { form: {
      __RequestVerificationToken: csrf, FullName: 'Forbidden Change', Email: 'admin@test.invalid',
      Role: 'Administrator', IsAvailable: 'false', UserId: 'test-admin'
    } });
    expect(response.status()).toBe(200);
    expect(await response.text()).toContain('already in use');
    await page.reload();
    await expect(page.locator('#FullName')).toHaveValue('Edited ' + name);
    const allowed = await page.request.post('/' + role + '/ProfileAndSettings/UpdateProfile', { form: {
      __RequestVerificationToken: csrf, FullName: 'Edited ' + name, Email: name + '@test.invalid',
      Address: 'Changed Street', City: 'Changed City', Role: 'Administrator', UserId: 'test-admin'
    } });
    expect(allowed.ok()).toBe(true);
    const forbidden = await page.request.get('/Admin/System/UserManagement', { maxRedirects: 0 });
    expect(forbidden.status()).toBe(302);
    expect(forbidden.headers().location).toContain('AccessDenied');
    expect(failures).toEqual([]);
  });
}

test('quotation search and review support quotes, backslashes, newlines, Unicode and HTML-like text', async ({ page }) => {
  const failures = errors(page);
  await login(page, 'admin');
  await page.goto('/Admin/Finance/Quotations');
  const row = page.locator('tr[data-search]').first();
  const search = page.getByPlaceholder('Search customer, job, or quote…');
  for (const value of ["O'Brien", '"double"', '\\', 'café', '日本語', '<img']) {
    await search.fill(value);
    await expect(row).toBeVisible();
  }
  await search.fill('no matching quotation');
  await expect(row).toBeHidden();
  await search.fill('');
  await page.getByRole('button', { name: 'Review quotation' }).first().click();
  await expect(page.getByRole('heading', { name: /QT-/ })).toBeVisible();
  await expect(page.locator('textarea[name="finalBreakdown"]')).toHaveValue(/O'Brien.*"double"/s);
  await expect(page.locator('textarea[name="finalBreakdown"]')).toHaveValue(/\nUnicode/);
  await page.getByRole('button', { name: 'Close review' }).click();
  await page.getByRole('button', { name: /^Approved/ }).click();
  await expect(row).toBeHidden();
  await page.getByRole('button', { name: /^Needs review/ }).click();
  await expect(row).toBeVisible();
  expect(await page.locator('img[onerror]').count()).toBe(0);
  expect(failures).toEqual([]);
});

test('uploaded deliverable is readable only by owner, assigned technician and admin', async ({ page, browser }) => {
  await login(page, 'technician');
  const csrf = await token(page, '/Technician/AssignedJobs');
  const png = Buffer.from(await page.evaluate(() => {
    const canvas = document.createElement('canvas');
    canvas.width = canvas.height = 2;
    return canvas.toDataURL('image/png').split(',')[1];
  }), 'base64');
  const response = await page.request.post('/Technician/AssignedJobs/UploadDeliverable', { multipart: {
    __RequestVerificationToken: csrf, requestId: '1', phase: 'Diagnosis', description: 'Browser upload',
    imageFile: { name: 'proof.png', mimeType: 'image/png', buffer: png }
  } });
  expect(response.ok()).toBe(true);
  expect(await response.text()).toContain('Deliverable uploaded successfully');
  const history = await page.request.get('/Technician/AssignedJobs/GetChatHistory?requestId=1');
  // Read the owner-facing history because it includes deliverables in its response.
  const ownerContext = await browser.newContext({ baseURL: process.env.HOMESERVE_BASE_URL });
  try {
    const owner = await ownerContext.newPage();
    await login(owner, 'customer');
    const data = await (await owner.request.get('/Customer/ServiceRequests/GetJobWorkflow?requestId=1')).json();
    const url = data.deliverables.find(d => d.description === 'Browser upload' || d.Description === 'Browser upload')?.imagePath;
    expect(url).toMatch(/^\/private-files\/deliverables\//);
    expect((await owner.request.get(url)).status()).toBe(200);
    expect((await page.request.get(url)).status()).toBe(200);
    const forbiddenContext = await browser.newContext({ baseURL: process.env.HOMESERVE_BASE_URL });
    try {
      const other = await forbiddenContext.newPage();
      expect((await other.request.get(url, { maxRedirects: 0 })).status()).toBe(302);
      await login(other, 'other');
      expect((await other.request.get(url)).status()).toBe(404);
      expect((await other.request.get(url.replace('/private-files/', '/uploads/'))).status()).toBe(404);
      await otherContextLogout(other);
      await login(other, 'admin');
      expect((await other.request.get(url)).status()).toBe(200);
    } finally { await forbiddenContext.close(); }
  } finally { await ownerContext.close(); }
  expect(history.status()).toBeLessThan(500);
});

async function otherContextLogout(page) {
  const csrf = await page.locator('input[name="__RequestVerificationToken"]').first().inputValue();
  await page.request.post('/Identity/Account/Logout', { form: { __RequestVerificationToken: csrf } });
}

for (const role of ['Customer', 'Technician']) {
  test(role + ' invitation creates a usable role account with a single-use setup link', async ({ page, browser }, testInfo) => {
    await login(page, 'admin');
    const name = 'invited-' + role.toLowerCase() + '-' + testInfo.project.name;
    const email = name + '@test.invalid';
    await page.goto('/Admin/System/UserManagement');
    await page.evaluate(({ email, role }) => {
      const form = document.querySelector('form[action*="InviteUser"]');
      form.querySelector('[name="email"]').value = email;
      form.querySelector('[name="role"]').value = role;
      form.requestSubmit();
    }, { email, role });
    const link = page.getByRole('link', { name: 'Set up password', exact: true });
    await expect(link).toBeVisible();
    const setup = await link.getAttribute('href');
    const context = await browser.newContext({ baseURL: process.env.HOMESERVE_BASE_URL });
    try {
      const invited = await context.newPage();
      await invited.goto(setup);
      await invited.locator('#Input_Email').fill(email);
      await invited.locator('#Input_Password').fill(process.env.HOMESERVE_FIXTURE_PASSWORD);
      await invited.locator('#Input_ConfirmPassword').fill(process.env.HOMESERVE_FIXTURE_PASSWORD);
      await invited.locator('button[type="submit"]').click();
      await expect(invited).toHaveURL(/ResetPasswordConfirmation/);
      await login(invited, name);
      await invited.goto('/' + role + '/ProfileAndSettings');
      await expect(invited.locator('#FullName')).toHaveValue(name);
    } finally { await context.close(); }
  });
}

test('HTTP upload rejects malformed, MIME-mismatched and oversized bodies', async ({ page }) => {
  await login(page, 'technician');
  const csrf = await token(page, '/Technician/AssignedJobs');
  for (const mimeType of ['image/png', 'image/jpeg']) {
    const response = await page.request.post('/Technician/AssignedJobs/UploadDeliverable', { multipart: {
      __RequestVerificationToken: csrf, requestId: '1', phase: 'Diagnosis', description: 'Rejected upload',
      imageFile: { name: 'invalid.png', mimeType, buffer: Buffer.from('not an image') }
    } });
    const html = await response.text();
    expect(html).not.toContain('Deliverable uploaded successfully');
    expect(html).toMatch(/malformed|extension and content type/);
  }
  const oversized = await page.request.post('/Technician/AssignedJobs/UploadDeliverable', { multipart: {
    __RequestVerificationToken: csrf, requestId: '1', phase: 'Diagnosis', description: 'Rejected upload',
    imageFile: { name: 'huge.png', mimeType: 'image/png', buffer: Buffer.alloc(6 * 1024 * 1024) }
  } });
  expect([400, 413]).toContain(oversized.status());
});

test('administrator can remove a material while retaining its stock history', async ({ page }, testInfo) => {
  const failures = errors(page);
  await login(page, 'admin');
  await page.goto('/Admin/Finance/Inventory');

  const itemName = 'Archive regression ' + testInfo.project.name;
  await page.getByRole('button', { name: 'Add item', exact: true }).click();
  const addForm = page.locator('form[action$="AddInventoryItem"]');
  await addForm.locator('[name="ItemName"]').fill(itemName);
  await addForm.locator('[name="SKU"]').fill('ARCHIVE-' + testInfo.project.name);
  await addForm.locator('[name="Category"]').fill('Regression');
  await addForm.locator('[name="StockQuantity"]').fill('3');
  await addForm.locator('[name="ReorderLevel"]').fill('1');
  await addForm.locator('[name="UnitCost"]').fill('100');
  await addForm.locator('[name="UnitPrice"]').fill('125');
  await Promise.all([
    page.waitForNavigation(),
    addForm.getByRole('button', { name: 'Save item' }).click()
  ]);

  const itemRow = page.getByRole('row').filter({ hasText: itemName });
  await expect(itemRow).toBeVisible();
  await itemRow.getByTitle('Remove from inventory').click();
  await expect(page.getByRole('heading', { name: 'Remove material?' })).toBeVisible();
  await Promise.all([
    page.waitForNavigation(),
    page.getByRole('button', { name: 'Remove material' }).click()
  ]);

  await expect(page.getByRole('status')).toContainText('job and stock history was preserved');
  await expect(page.getByRole('row').filter({ hasText: itemName })).toHaveCount(0);
  await page.goto('/Admin/Finance/StockMovements');
  const archiveRow = page.getByRole('row').filter({ hasText: itemName }).filter({ hasText: 'Archived' });
  await expect(archiveRow).toBeVisible();
  expect(failures).toEqual([]);
});

test('encoded action data remains usable across administrator and customer screens', async ({ page }) => {
  const failures = errors(page);
  await login(page, 'admin');
  for (const route of ['/Admin/Operations/ServiceRequests', '/Admin/Operations/Technicians',
    '/Admin/System/UserManagement', '/Admin/System/ArchivedUsers', '/Admin/Finance/Inventory']) {
    await page.goto(route);
    await expect.poll(() => page.evaluate(() => Boolean(window.Alpine))).toBe(true);
  }
  await page.goto('/Admin/Operations/ServiceRequests');
  const request = page.locator('tr[data-request]').first();
  expect(JSON.parse(await request.getAttribute('data-request')).desc).toContain("O'Brien");
  await request.click();
  await expect.poll(() => page.evaluate(() => {
    const row = document.querySelector('tr[data-request]');
    return window.Alpine.$data(row).drawerOpen;
  })).toBe(true);
  await otherContextLogout(page);
  await login(page, 'customer');
  await page.goto('/Customer/ServiceRequests');
  await page.locator('[data-request]').first().click();
  await expect.poll(() => page.evaluate(() => window.Alpine.$data(document.querySelector('[data-request]')).drawerOpen)).toBe(true);
  expect(failures).toEqual([]);
});

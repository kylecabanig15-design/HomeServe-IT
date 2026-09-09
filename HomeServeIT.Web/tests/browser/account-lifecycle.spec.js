const { test, expect } = require('@playwright/test');

test.beforeEach(() => {
  test.skip(process.env.HOMESERVE_ISOLATED_FIXTURE !== '1', 'Run npm run test:phases with a disposable MySQL connection.');
});
test.use({ trace: 'off', screenshot: 'off', video: 'off' });

async function loginAsAdmin(page) {
  await page.goto('/Identity/Account/Login');
  await page.locator('#Input_Email').fill('admin@test.invalid');
  await page.locator('#Input_Password').fill(process.env.HOMESERVE_FIXTURE_PASSWORD);
  await page.locator('#login-submit').click();
  await expect(page).toHaveURL(/\/Admin\/Dashboard/);
}

async function invite(page, email, role) {
  await page.goto('/Admin/System/UserManagement');
  await page.getByRole('button', { name: 'Invite user' }).click();
  const modal = page.getByRole('dialog', { name: 'Invite new user' });
  await modal.getByLabel('Email address').fill(email);
  await modal.getByLabel('Role').selectOption(role);
  await modal.getByRole('button', { name: 'Send invite' }).click();
  await expect(page.getByRole('link', { name: 'Set up password' })).toBeVisible();
}

async function archive(page, email) {
  await page.goto('/Admin/System/UserManagement');
  const row = page.getByRole('row').filter({ hasText: email });
  await row.getByTitle('Archive').click();
  const modal = page.getByRole('dialog', { name: 'Archive User' });
  await modal.getByRole('button', { name: 'Archive', exact: true }).click();
  await expect(page.getByText(`User ${email} archived successfully.`)).toBeVisible();
}

async function permanentlyDelete(page, email) {
  await page.goto('/Admin/System/ArchivedUsers');
  await page.getByRole('button', { name: /User archives/ }).click();
  const row = page.getByRole('row').filter({ hasText: email });
  await row.getByRole('button', { name: 'Delete', exact: true }).click();
  const modal = page.getByRole('dialog', { name: 'Delete account permanently?' });
  await modal.getByRole('button', { name: 'Delete permanently' }).click();
  await expect(page.getByRole('status')).toContainText(`User ${email} permanently deleted.`);
  await page.getByRole('button', { name: /User archives/ }).click();
  await expect(page.getByRole('row').filter({ hasText: email })).toHaveCount(0);
}

test('customer and technician account controls archive and permanently delete accounts', async ({ page }, testInfo) => {
  await loginAsAdmin(page);
  const suffix = testInfo.project.name.replaceAll(/[^a-z0-9]/gi, '-').toLowerCase();
  const technicianEmail = `delete-technician-${suffix}@test.invalid`;
  const customerEmail = `delete-customer-${suffix}@test.invalid`;
  const issue = `Account deletion relationship regression ${suffix}`;

  await invite(page, technicianEmail, 'Technician');
  await invite(page, customerEmail, 'Customer');

  // Give the customer a real service record assigned to the technician so the
  // deletion path also exercises relationship cleanup and technician unassignment.
  await page.goto('/Admin/Crm/Customers');
  const customerRow = page.getByRole('row').filter({ hasText: customerEmail });
  await customerRow.click();
  const customerUrl = new URL(page.url());
  const customerId = customerUrl.searchParams.get('id') || customerUrl.pathname.split('/').filter(Boolean).at(-1);
  expect(customerId).toBeTruthy();

  await page.goto('/Admin/Operations/Technicians');
  const technician = await page.locator('[data-technician]').evaluateAll((cards, email) =>
    cards.map(card => JSON.parse(card.dataset.technician)).find(item => item.email === email), technicianEmail);
  expect(technician).toBeTruthy();

  await page.goto('/Admin/Operations/ServiceRequests');
  const token = await page.locator('input[name="__RequestVerificationToken"]').first().inputValue();
  const create = await page.request.post('/Admin/Operations/AddServiceRequest', { form: {
    __RequestVerificationToken: token,
    customerId,
    issueDescription: issue,
    scheduledDate: '2030-01-20T10:00',
    serviceCategory: 'Other'
  } });
  expect(create.ok()).toBe(true);
  await page.goto('/Admin/Operations/ServiceRequests');
  const requestRow = page.getByRole('row').filter({ hasText: issue });
  const request = JSON.parse(await requestRow.getAttribute('data-request'));
  const assignToken = await page.locator('input[name="__RequestVerificationToken"]').first().inputValue();
  const assign = await page.request.post('/Admin/Operations/AssignTechnician', { form: {
    __RequestVerificationToken: assignToken, requestId: request.id, techId: technician.id
  } });
  expect(assign.ok()).toBe(true);

  await archive(page, technicianEmail);
  await permanentlyDelete(page, technicianEmail);
  await archive(page, customerEmail);
  await permanentlyDelete(page, customerEmail);
});

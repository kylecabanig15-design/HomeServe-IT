const { defineConfig, devices } = require('@playwright/test');

const baseURL = process.env.HOMESERVE_BASE_URL || 'http://127.0.0.1:5160';

module.exports = defineConfig({
  testDir: './tests/browser',
  fullyParallel: false,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 2 : 0,
  reporter: 'list',
  use: {
    baseURL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },
  webServer: process.env.HOMESERVE_BASE_URL
    ? undefined
    : {
        command: 'dotnet run --no-launch-profile --urls http://127.0.0.1:5160',
        url: baseURL,
        reuseExistingServer: !process.env.CI,
        timeout: 120_000,
        env: {
          ASPNETCORE_ENVIRONMENT: 'Development',
        },
      },
  projects: [
    { name: 'chromium-desktop', use: { ...devices['Desktop Chrome'] } },
    { name: 'firefox-desktop', use: { ...devices['Desktop Firefox'] } },
    { name: 'webkit-desktop', use: { ...devices['Desktop Safari'] } },
    { name: 'webkit-mobile', use: { ...devices['iPhone 13'] } },
  ],
});

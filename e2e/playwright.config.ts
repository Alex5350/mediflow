import { defineConfig } from '@playwright/test';

// One command: ../scripts/e2e.sh boots SQL + all four services on an isolated
// database, then runs this config. `npx playwright test --headed` works too.
export default defineConfig({
  testDir: './specs',
  timeout: 45_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: 'http://localhost:8090',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
});

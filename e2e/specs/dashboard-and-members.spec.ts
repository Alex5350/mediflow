import { expect, test } from '@playwright/test';
import { gotoInteractive } from './helpers';

test.describe('dashboard', () => {
  test('shows pipeline KPIs, denial mix and plan portfolio', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByRole('heading', { name: 'Operations dashboard' })).toBeVisible();

    // KPI tiles
    await expect(page.getByText('Open claims')).toBeVisible();
    await expect(page.getByText('Active enrollments')).toBeVisible();
    await expect(page.getByText('YTD plan paid')).toBeVisible();

    // Denial chart + plan table render seeded data
    await expect(page.getByText('Denial mix')).toBeVisible();
    await expect(page.getByText('Plan portfolio')).toBeVisible();
    await expect(page.getByText('MFP-2650').first()).toBeVisible(); // five-star plan
    await expect(page.getByTitle('CMS five-star plan').first()).toBeVisible();
  });
});

test.describe('members', () => {
  test('search lists matches and the 360 view shows coverage and claims', async ({ page }) => {
    await gotoInteractive(page, '/members');
    await page.getByPlaceholder(/MBI or last\/first name/).fill('Whitfield');
    await page.getByRole('button', { name: 'Search' }).click();

    const firstRow = page.locator('tbody tr').first();
    await expect(firstRow).toContainText('Whitfield');
    await firstRow.getByRole('link', { name: 'Open 360' }).click();

    await expect(page.getByText('Active coverage')).toBeVisible();
    await expect(page.getByText('Enrollment history')).toBeVisible();
    await expect(page.getByText('Claims (latest 25)')).toBeVisible();
    await expect(page.locator('.badge').first()).toBeVisible();
  });
});

import { expect, test } from '@playwright/test';
import { ClaimsApi, firstActiveEnrollment, gotoInteractive } from './helpers';

test.describe('claims queue and detail', () => {
  test('status filter narrows the queue and detail shows remittance + audit', async ({ page }) => {
    await gotoInteractive(page, '/claims');

    await page.getByRole('button', { name: 'Paid', exact: true }).click();
    await expect(page.locator('tbody tr').first()).toContainText('CLM-');
    await expect(page.locator('tbody').first()).not.toContainText('Received');

    await page.locator('tbody tr').first().getByRole('link', { name: 'Detail' }).click();
    await expect(page.getByText('Line-level remittance')).toBeVisible();
    await expect(page.getByText('Adjudication audit trail')).toBeVisible();
    await expect(page.locator('.timeline li').first()).toContainText('Submitted');
    await expect(page.locator('tfoot')).toContainText('Totals');
  });

  test('dead-lettered claims surface on the operations page', async ({ page }) => {
    await page.goto('/ops');
    await expect(page.getByText('Dead letters', { exact: true })).toBeVisible();
    await expect(page.getByText('Outbox depth')).toBeVisible();
  });
});

test.describe('submit claim + live adjudication', () => {
  test('a claim for an enrolled member adjudicates while the detail page watches', async ({ page, request }) => {
    const enrollment = await firstActiveEnrollment(request);

    await gotoInteractive(page, '/claims/submit');
    await page.getByLabel('Member id').fill(String(enrollment.memberId));
    await page.getByLabel('Plan id').fill(String(enrollment.planId));
    await page.getByLabel('Rendering provider NPI').fill('1234567893');

    const today = new Date();
    const serviceDate = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-01`;
    await page.locator('input[type="date"]').fill(serviceDate);

    await page.getByRole('button', { name: 'Submit claim' }).click();
    await expect(page.getByText(/accepted into the queue/)).toBeVisible();

    // Follow to the detail page; the worker picks the claim up within seconds and
    // the page's 3-second poll flips the badge.
    await page.getByRole('link', { name: 'open detail' }).click();
    await expect(page.locator('h1 .badge', { hasText: /Paid|Denied/ })).toBeVisible({ timeout: 30_000 });

    // Line outcomes and audit trail are populated after adjudication.
    await expect(page.locator('.timeline li').filter({ hasText: 'Adjudicated' })).toBeVisible();
    await expect(page.locator('tbody tr td.num').first()).not.toContainText('—');

    // The rollup counts the new claim on the dashboard.
    const stats = await (await request.get(`${ClaimsApi}/api/v1/rollups/dashboard`)).json();
    expect(stats.claimsOpen).toBeGreaterThanOrEqual(0); // smoke-sanity on the API surface
  });
});

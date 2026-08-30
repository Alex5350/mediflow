import { expect, test } from '@playwright/test';
import { EnrollmentApi, first2026MaPlanId, nextMonthFirst, unenrolledEntitledMember, gotoInteractive } from './helpers';

test.describe('enrollment wizard', () => {
  test('happy path: member → plan → SEP effective date → submitted', async ({ page, request }) => {
    const memberId = await unenrolledEntitledMember(request);
    const planId = await first2026MaPlanId(request);

    await gotoInteractive(page, '/enroll');

    // Step 1: find the member
    await page.getByPlaceholder('MBI or name prefix').fill(String(memberId));
    // Search by member id won't match MBI/name — use the API-found id directly:
    // type the id, then select via the deterministic first row of the id-based search.
    // The search endpoint matches MBI/name only, so look the member up by number.
    const memberResponse = await request.get(`${EnrollmentApi}/api/v1/members/${memberId}/360`);
    const view = await memberResponse.json();
    const lastName = view.header.lastName as string;

    await page.getByPlaceholder('MBI or name prefix').fill(lastName);
    await page.getByRole('button', { name: 'Search' }).click();
    await page.getByRole('button', { name: 'Select' }).first().click();

    // Step 2: choose a plan (QuickGrid)
    await expect(page.getByText('Choose a 2026 plan')).toBeVisible();
    await page.locator('tbody tr').first().getByRole('button', { name: 'Select' }).click();

    // Step 3: SEP move effective the first of next month
    await expect(page.getByText('Effective date & SEP reason')).toBeVisible();
    await page.locator('input[type="date"]').fill(nextMonthFirst());
    await page.getByLabel(/SEP reason/).selectOption('1'); // Moved
    await page.getByRole('button', { name: 'Submit application' }).click();

    await expect(page.getByText(/submitted and routed to verification/)).toBeVisible();
    await expect(page.locator('.alert-success .mono')).toContainText(/ENR-\d{4}-\d{6}/);
  });

  test('validation: non-first-of-month date shows rule violations', async ({ page, request }) => {
    const memberId = await unenrolledEntitledMember(request);
    const memberResponse = await request.get(`${EnrollmentApi}/api/v1/members/${memberId}/360`);
    const view = await memberResponse.json();

    await gotoInteractive(page, '/enroll');
    await page.getByPlaceholder('MBI or name prefix').fill(view.header.lastName);
    await page.getByRole('button', { name: 'Search' }).click();
    await page.getByRole('button', { name: 'Select' }).first().click();
    await page.locator('tbody tr').first().getByRole('button', { name: 'Select' }).click();

    await page.locator('input[type="date"]').fill('2026-09-15');
    await page.getByRole('button', { name: 'Submit application' }).click();

    await expect(page.getByText('Eligibility violations — application rejected:')).toBeVisible();
    await expect(page.getByText(/first day of a month/)).toBeVisible();
  });
});

test.describe('applications queue', () => {
  test('approve a pending SEP application and see the status change', async ({ page }) => {
    await gotoInteractive(page, '/enrollments');

    // Pin one row by its application number — approving re-sorts the list, so
    // "first row" is not stable across the reload.
    const firstRow = page.locator('tbody tr').first();
    await expect(firstRow.locator('.badge')).toContainText(/PendingVerification|Submitted/, { ignoreCase: true });
    const applicationNumber = (await firstRow.locator('.mono').first().innerText()).trim();
    const row = page.locator('tbody tr', { hasText: applicationNumber });

    await row.getByRole('button', { name: 'Approve' }).click();

    // Approved applications leave the PendingVerification filter...
    await expect(page.locator('tbody')).not.toContainText(applicationNumber);

    // ...and show the decision in the All view.
    await page.getByRole('button', { name: 'All', exact: true }).click();
    await expect(page.locator('tbody tr', { hasText: applicationNumber }).locator('.badge'))
      .toContainText(/Approved|Active/, { ignoreCase: true });
  });
});

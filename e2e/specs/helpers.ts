import { APIRequestContext, Page } from '@playwright/test';

// The E2E stack runs in Development mode (API-key auth off) against an isolated
// database — these helpers read just enough from the APIs to drive the UI.
export const EnrollmentApi = 'http://localhost:8080';
export const ClaimsApi = 'http://localhost:8081';

/**
 * Blazor pages prerender on the server before the interactive circuit connects;
 * clicks before the negotiate handshake are silently swallowed. Wait for it.
 */
export async function gotoInteractive(page: Page, url: string) {
  // Attach the listener before navigating: negotiate fires during goto.
  const negotiated = page.waitForResponse((r) => r.url().includes('/_blazor/negotiate'));
  await page.goto(url);
  await negotiated;
  await page.waitForTimeout(400);
}

export async function firstActiveEnrollment(request: APIRequestContext) {
  const response = await request.get(`${EnrollmentApi}/api/v1/enrollments?status=5&pageSize=1`);
  const rows = await response.json();
  if (!rows.length) throw new Error('seed guarantees active enrollments — none found');
  return rows[0] as { memberId: number; memberName: string; planId: number; planCode: string };
}

export async function unenrolledEntitledMember(request: APIRequestContext): Promise<number> {
  // The seed leaves ~12 entitled members with no enrollments; find one by
  // probing member 360 headers for missing active coverage.
  for (let id = 1; id < 200; id++) {
    const response = await request.get(`${EnrollmentApi}/api/v1/members/${id}/360`);
    if (!response.ok()) continue;
    const view = await response.json();
    if (view.header && view.header.partBEffective && !view.header.planCode) return id;
  }
  throw new Error('no unenrolled entitled member found in seed data');
}

export async function first2026MaPlanId(request: APIRequestContext): Promise<number> {
  const response = await request.get(`${EnrollmentApi}/api/v1/plans?year=2026`);
  const plans = await response.json();
  return plans.find((p: { type: number }) => p.type === 1).id as number;
}

export function nextMonthFirst(): string {
  const now = new Date();
  const d = new Date(now.getFullYear(), now.getMonth() + 1, 1);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-01`;
}

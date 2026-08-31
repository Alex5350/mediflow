# MediFlow Domain Guide

This document explains the Medicare concepts MediFlow models, written for an
engineer who is new to the domain. Every rule described here is implemented in
`src/MediFlow.Domain` and enforced in one place; the APIs, the worker, the
dashboard and the MCP server all evaluate the same code. When this document and
the code disagree, the code wins; file an issue.

## Who is who

- **Beneficiary (member).** A person covered by Medicare. In MediFlow a
  `Member` (`src/MediFlow.Domain/Members/Member.cs`) carries demographics,
  state, and Part A/Part B entitlement dates.
- **Plan sponsor / carrier.** The insurer marketing plan products (`Plan` in
  `src/MediFlow.Domain/Plans/Plan.cs`).
- **Provider.** The clinician or facility that renders a service and bills a
  claim, identified by a National Provider Identifier (NPI).
- **CMS.** The Centers for Medicare & Medicaid Services - the source of the
  identifier formats and enrollment-window rules simplified below.

## Identifiers

### Medicare Beneficiary Identifier (MBI)

Every beneficiary carries an 11-character MBI, printed on the Medicare card in
three groups (`1EG4-TE5-MK73`). The `Mbi` value object
(`src/MediFlow.Domain/Common/Mbi.cs`) enforces a simplified version of the CMS
format:

- exactly 11 alphanumeric characters after stripping dashes;
- the six easily misread letters **B, I, L, O, S, Z never appear** - the
  seeder draws from `ACDEFGHJKMNPQRTUVWXY` for the same reason;
- position 1 is never `0`;
- dashes are optional on input (`TryParse` normalizes them away) and never
  stored; `ToDisplay()` re-inserts them for presentation.

### National Provider Identifier (NPI)

Providers are identified by a 10-digit NPI whose tenth digit is a check digit.
The `Npi` value object (`src/MediFlow.Domain/Common/Npi.cs`) implements the
official check-digit algorithm: a Luhn pass over the constant prefix `80840`
concatenated with the first 9 digits. `1234567893` is the canonical valid
example used throughout the tests; transposing digits or inventing a tenth
digit fails validation, which is what claim intake relies on (see
`ClaimSubmissionRules`).

## Part A and Part B entitlement

Original Medicare has two halves:

- **Part A** (inpatient/hospital) - entitlement is recorded on the member but
  does not gate anything MediFlow enforces.
- **Part B** (outpatient/medical) - entitlement is the **prerequisite for MA
  and PDP enrollment**. A member with no `PartBEffective`, or one whose Part B
  starts after the requested coverage date, cannot enroll (`PartBNotEffective`).

The adjudication-side counterpart is enrollment in a plan product: coverage on
the service date is what makes a claim payable, not Part B itself.

## Plan products: MA vs PDP

`PlanType` (`src/MediFlow.Domain/Plans/Plan.cs`) offers two product lines:

| Type | Meaning | Enrollment effect |
|------|---------|-------------------|
| `MedicareAdvantage` (Part C) | Replaces Original Medicare coverage; carries the medical benefit design MediFlow adjudicates (deductible, coinsurance, OOP max, fee schedule). | One active MA enrollment per member. |
| `PrescriptionDrug` (Part D) | Standalone drug coverage. | A PDP **may coexist** with an MA plan - the dual-coverage block only fires on the same plan type. |

Each `Plan` is a **contract-year product**: `ContractYear` must equal the
requested effective year, rates (premium, deductible, coinsurance percent, OOP
max) belong to that year, and fee schedules are per `EffectiveYear`. Five-star
status (`IsFiveStar`) is a plan attribute with the special enrollment rights
described below.

## Enrollment windows

Enrollment eligibility is a pure function: `EnrollmentRules.Validate` in
`src/MediFlow.Domain/Enrollment/EnrollmentRules.cs`. It takes the member, the
plan, the requested effective date, an asserted SEP reason, the member's active
enrollments, and the current date/time (no hidden clock, no I/O), so the API,
the pre-check endpoint and the MCP eligibility tool all answer identically.

### Annual Enrollment Period (AEP)

October 15 through December 7 (`AepStart`/`AepEnd`). An effective date of
**January 1 of the following year** is reachable through AEP
(`IsWithinAep` returns the coverage year). Note that a late-2026 AEP
submission targets a **2027 contract-year plan**, which is why the
contract-year rule exists.

### Initial Enrollment Period (ICEP)

The ±3 months around the member's Part B start (`IcepMonthsAroundEntitlement =
3`). Any first-of-month effective date from the first day of the month three
months before Part B begins through three months after is reachable - the
classic "signed up around 65" window.

### Special Enrollment Periods (SEP)

Qualifying life events let a member enroll outside AEP. The effective date is
always the **first of the month following submission**. `SepReason`
(`EnrollmentTypes.cs`) enumerates what the system accepts:

| Value | Name | Meaning |
|-------|------|---------|
| 0 | `None` | No SEP - AEP/ICEP must cover the request. |
| 1 | `Moved` | Member moved out of the plan's service area. |
| 2 | `LostCreditableCoverage` | Other creditable coverage ended (e.g. employer plan). |
| 3 | `DualEligible` | Medicaid qualification - a continuous SEP. |
| 4 | `LowIncomeSubsidy` | Extra Help / LIS - a continuous SEP. |
| 5 | `FiveStar` | Switch into a CMS 5-star plan (see below). |

SEP reasons are asserted by the applicant and verified by staff before
approval; they are recorded on the application, not trusted silently.

### Five-star special enrollment

A plan marked `IsFiveStar` accepts a switch **any month of the year** when the
application carries `SepReason.FiveStar`. It is also the one exception to the
dual-coverage block: approving a 5-star switch cancels the prior same-type
enrollment effective the last day of the month before the new coverage starts
(`EnrollmentService.DecideAsync`).

### The full rule set

`Validate` reports **all** violations at once, not just the first:

| Violation (`EnrollmentViolation`) | Trigger |
|-----------------------------------|---------|
| `PlanNotOfferedForYear` (1) | `Plan.ContractYear != requestedEffectiveDate.Year`. |
| `PartBNotEffective` (2) | No Part B entitlement, or it begins after the effective date. |
| `AlreadyEnrolledSameType` (3) | An active enrollment of the same plan type exists, unless the request is a 5-star switch. |
| `OutsideEnrollmentWindow` (4) | Effective date unreachable through AEP, ICEP, a qualifying SEP, or a 5-star switch. |
| `EffectiveDateNotFirstOfMonth` (5) | Coverage never starts mid-month. |

### Application lifecycle

`EnrollmentStateMachine` guards the status graph
`Draft → Submitted → PendingVerification → Approved/Denied → Active →
Cancelled`, with `Denied` and `Cancelled` terminal (a re-apply is a new
application). Illegal transitions are rejected rather than excepted, so the
staff decision endpoint can return a clean "illegal transition" outcome.

## Claims

### Submission validation

`ClaimSubmissionRules` (`src/MediFlow.Domain/Claims/ClaimSubmissionRules.cs`)
runs in the API before anything is persisted; failures return a validation
problem (HTTP 400) instead of accepting unadjudicatable work:

- `NPI_INVALID` - rendering provider NPI fails the Luhn/80840 check.
- `MEMBER_REQUIRED` / `PLAN_REQUIRED` - ids must be positive.
- `SERVICE_DATE_FUTURE` - service date cannot be after receipt.
- `LINES_REQUIRED`, `LINE_n_CODE_INVALID` (4-5 alphanumeric CPT/HCPCS),
  `LINE_n_CHARGE_INVALID` (charge > 0).

### Lifecycle

`ClaimStatus` (`ClaimTypes.cs`) models the queue:

```
Received → Adjudicating → Paid
                       → Denied
                       → Pended            (manual review, e.g. provider mismatch)
                       → DeadLettered      (5 failed attempts - operator intervention)
```

Intake writes the claim plus an outbox message and audit entry in one
transaction. A worker leases a batch (`usp_LeaseNextClaims`, atomic in SQL),
runs the engine, and commits the decision, line outcomes, accumulators, audit
row and outbox completion in a single stored-procedure call
(`usp_RecordAdjudicationResult`). A crashed worker's lease lapses; five failed
attempts dead-letter the claim (see `AdjudicationWorker`).

### The adjudication pipeline

`AdjudicationEngine` (`src/MediFlow.Domain/Claims/Adjudication/AdjudicationEngine.cs`)
runs claim-level rules **in registration order** - the order is semantic, since
the first denial wins:

1. **`FilingTimelinessRule` → CO-29.** Claims must be filed within
   `Claim.FilingLimitDays` (365) of the service date.
2. **`CoverageRule` → CO-27.** The member must have an active enrollment
   spanning the service date (start ≤ service date ≤ cancellation, if any).
3. **`DuplicateClaimRule` → CO-18.** A prior (non-open) claim with the same
   rendering NPI + service date + procedure code for the same member is an
   exact duplicate - the fingerprint triple is `PriorClaimFingerprint`.

If no rule denies, each line is priced by `BenefitCalculator` in sequence
order, consuming the member's benefit accumulators line by line.

## Remittance adjustment codes (CO vs PR)

On a remittance advice, every non-paid dollar carries an adjustment code:

- **CO (Contractual Obligation)** - the provider cannot bill the member for
  this amount. The plan's decision is final: duplicate, untimely, no coverage,
  non-covered service.
- **PR (Patient Responsibility)** - the amount shifts to the member:
  deductible, coinsurance, copay.

`DenialCode` (`src/MediFlow.Domain/Claims/DenialCode.cs`) implements exactly
these, with `DenialCodeDescriptions` providing the canonical text used by the
API, dashboard and MCP `explain_denial_code` tool:

| Code | Enum member | Meaning |
|------|-------------|---------|
| CO-18 | `DuplicateClaim` | Exact duplicate claim or service. |
| CO-27 | `CoverageTerminated` | Expenses incurred after coverage terminated (or before it began / never active). |
| CO-29 | `TimelyFiling` | The time limit for filing has expired (365 days). |
| CO-96 | `NonCoveredService` | Non-covered charge - not a covered code on the plan's fee schedule. |
| PR-1 | `Deductible` | Amount applied to the member's deductible. |
| PR-2 | `Coinsurance` | Member coinsurance share. |
| PR-3 | `Copay` | Member copayment. |

Note the claim-level vs line-level distinction: CO-18/27/29 deny the whole
claim (every line inherits the code); CO-96 and the PR codes are per-line, so a
claim can be `Paid` overall while one line is CO-96 and another carries PR-2.

## Benefit accumulators and how a line is priced

Accumulators (`BenefitAccumulator`) are tracked **per member per benefit
year** - not per plan. Two numbers matter:

- **Deductible met** - dollars the member has already paid toward the plan's
  annual deductible.
- **Out-of-pocket (OOP) met** - dollars the member has already paid in
  deductible + coinsurance + copay. Once this reaches the plan's OOP max, the
  plan pays 100% of allowed amounts for the rest of the year.

`BenefitCalculator.PriceLine` applies the sequence
**fee allowance → deductible → coinsurance → OOP-max cap**:

1. **Allowed.** `allowed = min(billed, fee-schedule allowed)`. A code missing
   from the schedule or flagged non-covered prices at $0 with CO-96 - the
   provider writes off the difference between billed and allowed.
2. **Deductible.** The first `min(allowed, deductible remaining)` dollars go to
   the member (PR-1).
3. **Coinsurance.** The member pays `CoinsurancePercent` of what remains after
   the deductible (PR-2). Percentages round away from zero at half-cent
   boundaries (`Money.PercentOf`) so the engine, the SQL commit and the tests
   agree to the cent.
4. **OOP max.** If deductible + coinsurance would push the member past the
   OOP max, member share is capped at the remaining room and the plan pays the
   excess.

### Worked example (computed by the real engine)

This is `AdjudicationEngineTests.Non_covered_line_denies_with_co96_but_rest_pays`
in `tests/MediFlow.Domain.UnitTests/Claims/AdjudicationTests.cs`, verbatim.

Plan: **$200 deductible, 20% coinsurance, $1,000 OOP max**. Member's
accumulators entering the claim: **deductible fully met ($200), $970 OOP met**.
Claim has two lines; the first is an office visit:

| Step | Line 1 - CPT 99214 | Value |
|------|--------------------|-------|
| Billed | charge on the line | $200.00 |
| Fee-schedule allowed (2026) | `min(billed, allowed)` | $174.00 |
| Deductible applied | remaining deductible is $0 | $0.00 |
| Coinsurance | 20% × $174.00 | $34.80 |
| OOP room | $1,000.00 − $970.00 | $30.00 |
| **Member owes** | `min($34.80, $30.00)` - the cap wins | **$30.00** (PR-2) |
| **Plan pays** | $174.00 − $30.00 | **$144.00** |
| OOP after | cap reached | $1,000.00 |

The second line bills a concierge-membership code (`S9994`, non-covered on the
fee schedule): it prices at $0 allowed with **CO-96**, owing nothing. The claim
is `Paid` overall - paid lines still pay when another line is non-covered.

The same engine, run at OOP met = $1,000 (cap already reached), pays the full
$174.00 and leaves the member owing $0.00 (`Oop_max_caps_member_exposure`).

## What the seeded demo data guarantees

`MediFlowDataSeeder` (`src/MediFlow.Infrastructure/Persistence/MediFlowDataSeeder.cs`)
exists so the dashboard, MCP server and screenshots show realistic data with
three structural guarantees:

- **Valid-format identifiers.** Every seeded MBI is built from the CMS-safe
  consonant set with a non-zero leading digit and passes `Mbi` validation;
  every provider NPI gets a real Luhn check digit over the 80840 prefix and
  passes `Npi` validation (the same validation intake enforces).
- **Determinism.** All randomness comes from a local LCG with a fixed seed and
  Numerical Recipes constants (`1664525` / `1013904223`) - no external
  dependencies, byte-identical data on every run.
- **Internal consistency.** Paid claims are not decorated with plausible
  numbers; they are priced by the real `AdjudicationEngine`/`BenefitCalculator`
  with each member's accumulators advanced claim by claim, exactly as the
  worker commits them in production. Denial rollups, line remittances and
  year-to-date totals therefore reconcile with each other.

The scale: 160 members (≈92% Part B entitled), 12 plan products for 2026
(8 MA + 4 PDP) plus 4 for 2025, a 28-row 2026 fee schedule (and a ≈4%-lower
2025 schedule), and >400 claims across all lifecycle states, including
deliberate CO-18/27/29 denials and open queue items for the worker to drain.

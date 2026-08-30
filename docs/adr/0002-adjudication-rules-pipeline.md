# ADR 0002: Adjudication rules as an ordered DI pipeline

- Status: Accepted
- Date: 2026-08-30

## Context

Before any benefit math runs, a claim must pass claim-level checks: filing
timeliness (CO-29), coverage (CO-27), and duplicate detection (CO-18). The
order is semantic, not incidental — a claim filed a year late is a timeliness
denial regardless of coverage, and reporting the right denial code depends on
evaluating the rules in a fixed sequence. The rules also need to be unit
testable in isolation, with names available for audit entries and logs.

## Decision

`AdjudicationEngine` (src/MediFlow.Domain/Claims/Adjudication/AdjudicationEngine.cs)
composes `IEnumerable<IAdjudicationClaimRule>`, materialized once into an
array, and evaluates in order — returning on the first denial, with every line
inheriting the claim-level code. If no rule denies, the engine hands off to
`BenefitCalculator` for line pricing. Each rule is a stateless sealed class:

```csharp
public interface IAdjudicationClaimRule
{
    string Name { get; }                       // for audit entries and logs
    DenialCode? Evaluate(AdjudicationRequest request); // null = continue
}
```

Registration order in `AddMediFlowInfrastructure`
(src/MediFlow.Infrastructure/ServiceCollectionExtensions.cs) is the pipeline
order: `FilingTimelinessRule`, then `CoverageRule`, then `DuplicateClaimRule`,
then the engine. Rules are stateless because they run inside a scoped worker
loop across replicas; all per-claim input arrives in the immutable
`AdjudicationRequest` (claim, member, plan, enrollment, fee schedule,
accumulators, prior-claim fingerprints), so there is nothing to reset between
claims.

## The empty-chain bug

During development the rules were briefly registered as their concrete types
(`AddScoped<FilingTimelinessRule>()` and so on). The default container resolves
`IEnumerable<IAdjudicationClaimRule>` from *interface* registrations only, so
the engine received an empty array — and an engine with no rules never denies
anything. It fails silent, not loud: every claim prices as if it were covered.

The domain unit tests stayed green because they construct the engine directly
with explicit rule instances. What caught it was an integration test
(`ClaimsApiTests.Valid_submission_is_accepted_and_preview_is_a_dry_run`)
asserting that a claim for a member with no enrollment previews as `Denied`
with a coverage denial; with an empty chain that claim priced instead. The
registrations were corrected to the interfaces, and the engine now exposes
`Rules` so the resolved chain can be inspected at the DI boundary.

## Consequences

- Adding a rule is one class plus one registration line; its position in the
  registration list is its position in the pipeline, reviewed in one place.
- Rule order is enforced only by registration order — no ordering attribute —
  so registrations must not be alphabetized or reordered casually.
- The DI boundary (container wiring, not just class behavior) is part of the
  test surface; see ADR 0008.

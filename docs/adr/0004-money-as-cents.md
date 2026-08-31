# ADR 0004: Money as integer cents with one rounding convention

- Status: Accepted
- Date: 2026-08-30

## Context

Adjudication multiplies dollar amounts by percentages. A 20% coinsurance on a
$174.00 allowed amount is exact, but on $174.50 it is $34.90 with a half-cent
in between; a multi-line claim compounds these splits line by line, and the
deductible and out-of-pocket accumulators carry the results forward across
claims. Whatever representation is chosen, the engine, the SQL commit, and the
tests must all land on the same cent.

`float`/`double` are disqualified immediately: binary floating point cannot
represent 0.1 exactly, so cents drift as soon as percentages are applied.
`decimal` is exact but was rejected as the primary representation: it invites
mixed-mode arithmetic (decimal alongside double in display math), leaks
scale questions (how many decimal places is "an amount"?) into the schema, the
REST contracts, and every accumulator, and still needs a rounding policy at
percentage splits - it moves the problem rather than solving it.

## Decision

Every monetary amount in MediFlow is an `int` count of US cents, end to end:
`*Cents` properties in the domain (`ChargeCents`, `PlanPaidCents`,
`DeductibleMetCents`, ...), `int` columns in the schema and in the
`dbo.AdjudicationLineResultType` TVP, and cents in the REST DTOs. Decimals
appear only at the edges - fee-schedule entry and display.

Rounding happens in exactly one place, `Money`
(src/MediFlow.Domain/Common/Money.cs):

```csharp
public static int PercentOf(int cents, byte percent) =>
    (int)Math.Round(cents * percent / 100m, MidpointRounding.AwayFromZero);
```

`MidpointRounding.AwayFromZero` makes a half-cent round toward the larger
magnitude deterministically. `BenefitCalculator` uses `PercentOf` for the
coinsurance split; everything else - allowed caps, deductible remaining, OOP
remaining - is integer min/max arithmetic. `Money.FromDollars` applies the same
convention when a dollar amount must cross into the system, and `Money.Format`
renders display strings.

## Consequences

- The engine, the TVP commit, and the tests agree to the cent by construction:
  the engine's `LineDecision` cents are the integers the commit writes, with no
  serialization or scale conversion in between.
- No hidden policy: because rounding has one implementation, a disputed cent
  has one line of code to point at.
- Integer bounds are not a practical concern at claim scale (an `int` holds
  any individual Medicare claim amount with room to spare), and amounts are
  always a single currency.
- UIs and external feeds that speak dollars convert exactly once, at the edge.

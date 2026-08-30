namespace MediFlow.Infrastructure.Persistence;

using MediFlow.Domain.Accumulators;
using MediFlow.Domain.Auditing;
using MediFlow.Domain.Claims;
using MediFlow.Domain.Claims.Adjudication;
using MediFlow.Domain.Enrollment;
using MediFlow.Domain.Fees;
using MediFlow.Domain.Members;
using MediFlow.Domain.Messaging;
using MediFlow.Domain.Plans;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

/// <summary>
/// Deterministic demo data. Everything is generated from a fixed seed with a local
/// LCG (no external dependencies), and paid claims are priced by the REAL
/// <see cref="AdjudicationEngine"/>/<see cref="BenefitCalculator"/> in service-date
/// order, so seeded accumulators, line outcomes and rollups are internally
/// consistent — the demo data looks exactly like production data would.
/// </summary>
public sealed class MediFlowDataSeeder(MediFlowDbContext db)
{
    private uint _state = 0x20260829; // fixed seed → byte-identical data on every run
    private DateTime _now = DateTime.UtcNow;

    private static readonly string[] FirstNames =
    [
        "Margaret", "Harold", "Eleanor", "Arthur", "Dorothy", "Walter", "Beatrice", "Clarence",
        "Virginia", "Raymond", "Frances", "Howard", "Ruth", "Gerald", "Marjorie", "Eugene",
        "Elizabeth", "Lawrence", "Gertrude", "Ernest", "Lillian", "Clifford", "Lucille", "Leonard",
        "Edna", "Herbert", "Rose", "Stanley", "Agnes", "Chester", "Juanita", "Lester",
    ];

    private static readonly string[] LastNames =
    [
        "Whitfield", "Kowalski", "Delgado", "Ostrander", "Brennan", "Castellano", "Fairbanks", "Hollingsworth",
        "Moriarty", "Zielinski", "Ashford", "Pemberton", "Villanueva", "Grimaldi", "Thornbury", "Abernathy",
        "Cavanaugh", "Ellsworth", "Bartosz", "Marchetti", "Livingston", "Okafor", "Steinberg", "Navarrete",
        "Kirkland", "Fontaine", "Beaumont", "Rutherford", "Ibarra", "Calloway", "Novak", "Prescott",
    ];

    private static readonly string[] States = ["TX", "FL", "CA", "NY", "PA", "OH", "GA", "NC", "MI", "IL", "AZ", "WA"];
    private static readonly string[] Carriers = ["Cascade Mutual Health", "Northbridge Care Network"];

    // (CPT, description, 2026 allowed dollars, covered)
    private static readonly (string Code, string Description, decimal Allowed, bool Covered)[] FeeRows2026 =
    [
        ("99203", "Office visit, new patient, 30 min", 128, true),
        ("99204", "Office visit, new patient, 45 min", 186, true),
        ("99213", "Office visit, established patient, 15 min", 118, true),
        ("99214", "Office visit, established patient, 25 min", 174, true),
        ("99215", "Office visit, established patient, 40 min", 259, true),
        ("93000", "Electrocardiogram, complete", 17, true),
        ("93010", "ECG tracing interpretation", 9, true),
        ("70450", "CT head/brain without contrast", 322, true),
        ("70460", "CT head/brain with contrast", 395, true),
        ("71046", "Chest X-ray, 2 views", 43, true),
        ("72148", "MRI lumbar spine without contrast", 488, true),
        ("73721", "MRI lower extremity without contrast", 510, true),
        ("80053", "Comprehensive metabolic panel", 29, true),
        ("85025", "Complete blood count with differential", 27, true),
        ("84443", "Thyroid stimulating hormone assay", 33, true),
        ("85610", "Prothrombin time assay", 14, true),
        ("90471", "Immunization administration", 26, true),
        ("90686", "Influenza vaccine, quadrivalent", 21, true),
        ("36415", "Venipuncture, diagnostic", 12, true),
        ("97110", "Therapeutic exercise, each 15 min", 34, true),
        ("97140", "Manual therapy techniques, each 15 min", 36, true),
        ("20610", "Major joint injection", 74, true),
        ("45378", "Diagnostic colonoscopy", 1052, true),
        ("43239", "Upper GI endoscopy with biopsy", 865, true),
        ("J1885", "Ketorolac tromethamine injection", 3, true),
        ("G2211", "Visit complexity add-on", 19, true),
        ("S9994", "Concierge care membership fee", 600, false),
        ("V5001", "Hearing aid selection and assessment", 240, false),
    ];

    private const string MbiConsonants = "ACDEFGHJKMNPQRTUVWXY"; // CMS-safe (no B,I,L,O,S,Z)

    public async Task SeedAsync(CancellationToken ct = default)
    {
        _now = DateTime.UtcNow;

        // ============ 1) fee schedules ============
        var fees2026 = FeeRows2026.Select(f => new ProcedureFee
        {
            ProcedureCode = f.Code,
            Description = f.Description,
            AllowedCents = (int)(f.Allowed * 100),
            IsCovered = f.Covered,
            EffectiveYear = 2026,
        }).ToList();
        var fees2025 = FeeRows2026
            .Where(f => f.Covered)
            .Select(f => new ProcedureFee
            {
                ProcedureCode = f.Code,
                Description = f.Description,
                AllowedCents = (int)(f.Allowed * 96m), // prior-year schedule ≈ 4% lower
                IsCovered = true,
                EffectiveYear = 2025,
            })
            .ToList();
        db.ProcedureFees.AddRange(fees2026);
        db.ProcedureFees.AddRange(fees2025);

        // ============ 2) plans ============
        List<Plan> plans2026 =
        [
            new() { PlanCode = "MFP-2601", Name = "Advantage Essentials (HMO)", Carrier = Carriers[0], Type = PlanType.MedicareAdvantage, ContractYear = 2026, MonthlyPremiumCents = 1900, DeductibleCents = 17500, CoinsurancePercent = 20, OopMaxCents = 550000 },
            new() { PlanCode = "MFP-2602", Name = "Advantage Preferred (HMO)", Carrier = Carriers[0], Type = PlanType.MedicareAdvantage, ContractYear = 2026, MonthlyPremiumCents = 4900, DeductibleCents = 0, CoinsurancePercent = 15, OopMaxCents = 490000 },
            new() { PlanCode = "MFP-2603", Name = "Advantage Complete D-SNP (HMO)", Carrier = Carriers[0], Type = PlanType.MedicareAdvantage, ContractYear = 2026, MonthlyPremiumCents = 0, DeductibleCents = 0, CoinsurancePercent = 10, OopMaxCents = 340000 },
            new() { PlanCode = "MFP-2604", Name = "Advantage Complete (PPO)", Carrier = Carriers[1], Type = PlanType.MedicareAdvantage, ContractYear = 2026, MonthlyPremiumCents = 7900, DeductibleCents = 22500, CoinsurancePercent = 20, OopMaxCents = 670000 },
            new() { PlanCode = "MFP-2605", Name = "Advantage Savings (HMO)", Carrier = Carriers[0], Type = PlanType.MedicareAdvantage, ContractYear = 2026, MonthlyPremiumCents = 0, DeductibleCents = 31000, CoinsurancePercent = 25, OopMaxCents = 610000 },
            new() { PlanCode = "MFP-2606", Name = "Advantage Value (PPO)", Carrier = Carriers[1], Type = PlanType.MedicareAdvantage, ContractYear = 2026, MonthlyPremiumCents = 3900, DeductibleCents = 15000, CoinsurancePercent = 20, OopMaxCents = 580000 },
            new() { PlanCode = "MFP-2650", Name = "Advantage Premier Five-Star (HMO)", Carrier = Carriers[0], Type = PlanType.MedicareAdvantage, ContractYear = 2026, MonthlyPremiumCents = 5900, DeductibleCents = 0, CoinsurancePercent = 10, OopMaxCents = 420000, IsFiveStar = true },
            new() { PlanCode = "MFP-2651", Name = "Advantage Classic (HMO)", Carrier = Carriers[1], Type = PlanType.MedicareAdvantage, ContractYear = 2026, MonthlyPremiumCents = 2900, DeductibleCents = 20000, CoinsurancePercent = 20, OopMaxCents = 530000 },
            new() { PlanCode = "MDP-2601", Name = "Prescription Basic (PDP)", Carrier = Carriers[0], Type = PlanType.PrescriptionDrug, ContractYear = 2026, MonthlyPremiumCents = 3400, DeductibleCents = 59000, CoinsurancePercent = 25, OopMaxCents = 200000 },
            new() { PlanCode = "MDP-2602", Name = "Prescription Standard (PDP)", Carrier = Carriers[0], Type = PlanType.PrescriptionDrug, ContractYear = 2026, MonthlyPremiumCents = 4800, DeductibleCents = 59000, CoinsurancePercent = 25, OopMaxCents = 200000 },
            new() { PlanCode = "MDP-2603", Name = "Prescription Saver (PDP)", Carrier = Carriers[1], Type = PlanType.PrescriptionDrug, ContractYear = 2026, MonthlyPremiumCents = 2200, DeductibleCents = 59000, CoinsurancePercent = 25, OopMaxCents = 200000 },
            new() { PlanCode = "MDP-2604", Name = "Prescription Enhanced (PDP)", Carrier = Carriers[1], Type = PlanType.PrescriptionDrug, ContractYear = 2026, MonthlyPremiumCents = 7600, DeductibleCents = 0, CoinsurancePercent = 15, OopMaxCents = 96000 },
        ];
        List<Plan> plans2025 =
        [
            new() { PlanCode = "MFP-2501", Name = "Advantage Essentials (HMO) 2025", Carrier = Carriers[0], Type = PlanType.MedicareAdvantage, ContractYear = 2025, MonthlyPremiumCents = 1700, DeductibleCents = 18500, CoinsurancePercent = 20, OopMaxCents = 530000 },
            new() { PlanCode = "MFP-2502", Name = "Advantage Preferred (HMO) 2025", Carrier = Carriers[0], Type = PlanType.MedicareAdvantage, ContractYear = 2025, MonthlyPremiumCents = 4500, DeductibleCents = 0, CoinsurancePercent = 15, OopMaxCents = 470000 },
            new() { PlanCode = "MFP-2504", Name = "Advantage Complete (PPO) 2025", Carrier = Carriers[1], Type = PlanType.MedicareAdvantage, ContractYear = 2025, MonthlyPremiumCents = 7400, DeductibleCents = 23500, CoinsurancePercent = 20, OopMaxCents = 640000 },
            new() { PlanCode = "MDP-2501", Name = "Prescription Basic (PDP) 2025", Carrier = Carriers[0], Type = PlanType.PrescriptionDrug, ContractYear = 2025, MonthlyPremiumCents = 3100, DeductibleCents = 59000, CoinsurancePercent = 25, OopMaxCents = 200000 },
        ];
        db.Plans.AddRange(plans2026);
        db.Plans.AddRange(plans2025);
        var maPlans = plans2026.Where(p => p.Type == PlanType.MedicareAdvantage).ToList();
        var pdpPlans = plans2026.Where(p => p.Type == PlanType.PrescriptionDrug).ToList();

        // ============ 3) members ============
        var members = new List<Member>();
        var usedMbis = new HashSet<string>();
        for (var i = 0; i < 160; i++)
        {
            var dob = new DateOnly(1938 + Next(0, 25), Next(1, 13), Next(1, 28));
            DateOnly? partA = null, partB = null;
            if (Chance(0.92m))
            {
                var year = Chance(0.7m) ? Next(2015, 2025) : Next(2025, 2027);
                partA = new DateOnly(year, Next(1, 13), 1);
                partB = partA.Value.AddMonths(Next(0, 2));
            }

            members.Add(new Member
            {
                Mbi = NextMbi(usedMbis),
                FirstName = Pick(FirstNames),
                LastName = Pick(LastNames),
                DateOfBirth = dob,
                StateCode = Pick(States),
                PartAEffective = partA,
                PartBEffective = partB,
                CreatedAtUtc = _now.AddDays(-Next(30, 500)),
            });
        }
        db.Members.AddRange(members);
        await db.SaveChangesAsync(ct); // ids for members/plans/fees

        var entitled = members.Where(m => m.PartBEffective is not null && m.PartBEffective <= new DateOnly(2026, 1, 1)).ToList();

        // ============ 4) enrollments ============
        var enrollments = new List<EnrollmentApplication>();
        var activeByMember = new Dictionary<int, EnrollmentApplication>();
        var applicationSequence = 1;
        var unenrolledCount = 0;
        string[] deniedNotes =
        [
            "Outside enrollment window — no qualifying SEP on file.",
            "Part B entitlement begins after requested effective date.",
        ];

        foreach (var member in entitled)
        {
            if (unenrolledCount < 12 && Chance(0.08m)) { unenrolledCount++; continue; }

            var plan = Pick(maPlans);
            var active = new EnrollmentApplication
            {
                ApplicationNumber = $"ENR-2025-{applicationSequence++:D6}",
                MemberId = member.Id,
                PlanId = plan.Id,
                Status = EnrollmentStatus.Active,
                SepReason = SepReason.None,
                RequestedEffectiveDate = new DateOnly(2026, 1, 1),
                SubmittedAtUtc = new DateTime(2025, 10, 15, 9, Next(0, 60), 0, DateTimeKind.Utc).AddDays(Next(0, 53)),
                DecidedAtUtc = new DateTime(2025, 12, Next(10, 20), 14, 0, 0, DateTimeKind.Utc),
            };
            enrollments.Add(active);
            activeByMember[member.Id] = active;

            if (Chance(0.18m)) // standalone PDP alongside MA — different type, allowed
            {
                enrollments.Add(new EnrollmentApplication
                {
                    ApplicationNumber = $"ENR-2025-{applicationSequence++:D6}",
                    MemberId = member.Id,
                    PlanId = Pick(pdpPlans).Id,
                    Status = EnrollmentStatus.Active,
                    SepReason = SepReason.None,
                    RequestedEffectiveDate = new DateOnly(2026, 1, 1),
                    SubmittedAtUtc = new DateTime(2025, 11, Next(1, 30), 10, 0, 0, DateTimeKind.Utc),
                    DecidedAtUtc = new DateTime(2025, 12, 18, 11, 0, 0, DateTimeKind.Utc),
                });
            }

            if (Chance(0.16m)) // SEP flow awaiting staff decision
            {
                enrollments.Add(new EnrollmentApplication
                {
                    ApplicationNumber = $"ENR-2026-{applicationSequence++:D6}",
                    MemberId = member.Id,
                    PlanId = Pick(maPlans).Id,
                    Status = EnrollmentStatus.PendingVerification,
                    SepReason = (SepReason)Next(1, 4),
                    RequestedEffectiveDate = FirstOfNextMonth(_now),
                    SubmittedAtUtc = _now.AddDays(-Next(1, 14)),
                });
            }

            if (Chance(0.10m)) // denied application for the pipeline view
            {
                enrollments.Add(new EnrollmentApplication
                {
                    ApplicationNumber = $"ENR-2026-{applicationSequence++:D6}",
                    MemberId = member.Id,
                    PlanId = Pick(maPlans).Id,
                    Status = EnrollmentStatus.Denied,
                    SepReason = SepReason.None,
                    RequestedEffectiveDate = new DateOnly(2026, Next(2, 9), 1),
                    SubmittedAtUtc = new DateTime(2026, Next(1, 8), Next(1, 28), 13, 0, 0, DateTimeKind.Utc),
                    DecidedAtUtc = new DateTime(2026, Next(1, 8), Next(1, 28), 16, 30, 0, DateTimeKind.Utc),
                    DecisionNote = Pick(deniedNotes),
                });
            }
        }
        db.Enrollments.AddRange(enrollments);
        await db.SaveChangesAsync(ct); // enrollment ids

        // ============ 5) claims — chronological per member, priced by the real engine ============
        var engine = new AdjudicationEngine(
        [
            new FilingTimelinessRule(), new CoverageRule(), new DuplicateClaimRule(),
        ]);

        var claims = new List<Claim>();
        var audits = new List<AuditEntry>();
        var outboxRows = new List<OutboxMessage>();
        var accumulatorRows = new List<BenefitAccumulator>();
        var npiPool = Enumerable.Range(0, 40).Select(_ => NextNpi()).ToList();
        var feeByCode = fees2026.ToDictionary(f => f.ProcedureCode);

        foreach (var member in entitled.Where(m => activeByMember.ContainsKey(m.Id)))
        {
            var enrollment = activeByMember[member.Id];
            var plan = maPlans.Single(p => p.Id == enrollment.PlanId);
            var running = new BenefitAccumulator { MemberId = member.Id, BenefitYear = 2026 };
            var fingerprints = new List<PriorClaimFingerprint>();
            var paidClaims = new List<(string Npi, DateOnly Date, string Proc)>();

            var count = member.Id % 7 == 0 ? Next(8, 13) : Next(0, 8); // a few heavy utilizers
            for (var i = 0; i < count; i++)
            {
                var roll = NextDecimal();
                Claim claim;
                if (roll < 0.80m)
                {
                    claim = BuildPricedClaim(member, plan, enrollment, running, fingerprints, feeByCode, npiPool, paidClaims);
                }
                else if (roll < 0.85m)
                {
                    claim = BuildWholeDenial(DenialCode.TimelyFiling, new DateOnly(2024, Next(11, 13), Next(1, 28)));
                }
                else if (roll < 0.89m)
                {
                    claim = BuildWholeDenial(DenialCode.CoverageTerminated, new DateOnly(2025, 12, Next(18, 32)).AddDays(-Next(0, 2)));
                }
                else if (roll < 0.935m)
                {
                    claim = BuildDuplicateDenial(paidClaims);
                }
                else if (roll < 0.985m)
                {
                    claim = BuildOpenClaim(ClaimStatus.Received);
                }
                else
                {
                    claim = BuildOpenClaim(Chance(0.75m) ? ClaimStatus.Pended : ClaimStatus.DeadLettered);
                }

                claim.MemberId = member.Id;
                claim.PlanId = plan.Id;
                claim.ClaimNumber = $"PEND-{claims.Count + 1:D6}"; // unique placeholder until ids exist
                claims.Add(claim);
            }

            if (running.DeductibleMetCents > 0 || running.OopMetCents > 0)
            {
                accumulatorRows.Add(running);
            }
        }

        db.Claims.AddRange(claims);
        await db.SaveChangesAsync(ct); // claim ids

        // ============ 6) business keys + outbox + audit (ids now known) ============
        foreach (var claim in claims)
        {
            claim.ClaimNumber = Claim.NextClaimNumber(claim.Id, claim.ReceivedAtUtc.Year);

            audits.Add(new AuditEntry
            {
                EntityType = "Claim",
                EntityKey = claim.ClaimNumber,
                Action = "Submitted",
                DetailJson = JsonSerializer.Serialize(new { claim.MemberId, Lines = claim.Lines.Count }),
                Actor = "provider-portal",
                AtUtc = claim.ReceivedAtUtc,
            });

            if (claim.Status is ClaimStatus.Paid or ClaimStatus.Denied)
            {
                audits.Add(new AuditEntry
                {
                    EntityType = "Claim",
                    EntityKey = claim.ClaimNumber,
                    Action = "Adjudicated",
                    DetailJson = JsonSerializer.Serialize(new
                    {
                        Status = claim.Status.ToString(),
                        PlanPaidCents = claim.TotalPlanPaidCents,
                        DenialCode = claim.DenialCode,
                    }),
                    Actor = "worker",
                    AtUtc = claim.AdjudicatedAtUtc!.Value,
                });
            }
            else if (claim.Status == ClaimStatus.Received)
            {
                outboxRows.Add(new OutboxMessage
                {
                    Type = OutboxMessage.AdjudicateClaim,
                    PayloadJson = JsonSerializer.Serialize(new { claimId = claim.Id }),
                    CreatedAtUtc = claim.ReceivedAtUtc,
                    AvailableAtUtc = _now.AddSeconds(-Next(0, 600)),
                });
            }
            else if (claim.Status == ClaimStatus.DeadLettered)
            {
                outboxRows.Add(new OutboxMessage
                {
                    Type = OutboxMessage.AdjudicateClaim,
                    PayloadJson = JsonSerializer.Serialize(new { claimId = claim.Id }),
                    CreatedAtUtc = claim.ReceivedAtUtc,
                    AvailableAtUtc = _now,
                    Attempts = 5,
                    CompletedAtUtc = _now.AddMinutes(-Next(10, 240)),
                    LastError = "Adjudication failed after 5 attempts: fee schedule lookup timeout",
                });
            }
        }

        db.AuditEntries.AddRange(audits);
        db.Outbox.AddRange(outboxRows);
        db.Accumulators.AddRange(accumulatorRows);
        await db.SaveChangesAsync(ct);

        // ---- local helpers close over the loop state above ----
        Claim BuildPricedClaim(
            Member m, Plan p, EnrollmentApplication e, BenefitAccumulator acc,
            List<PriorClaimFingerprint> priors, Dictionary<string, ProcedureFee> fees,
            List<string> npis, List<(string Npi, DateOnly Date, string Proc)> paid)
        {
            var serviceDate = RandomServiceDate();
            var received = serviceDate.ToDateTime(TimeOnly.MinValue).AddDays(Next(1, 12)).AddHours(Next(8, 18));
            if (received > _now.AddHours(-1))
            {
                received = _now.AddHours(-Next(2, 24));
            }

            var lines = BuildLines(fees, includeNonCovered: Chance(0.08m));
            var claim = NewClaim(npis, serviceDate, received, lines);
            claim.EnrollmentApplicationId = e.Id;

            // Price with the real engine against the member's running accumulators.
            var request = new AdjudicationRequest(claim, m, p, e, fees, acc, priors);
            var result = engine.Adjudicate(request);

            claim.Status = result.Status;
            claim.DenialCode = result.ClaimDenialCode;
            claim.AdjudicatedAtUtc = received.AddHours(Next(2, 10)).AddMinutes(Next(0, 59));
            claim.TotalAllowedCents = result.TotalAllowedCents;
            claim.TotalPlanPaidCents = result.TotalPlanPaidCents;
            claim.TotalMemberOwesCents = result.TotalMemberOwesCents;
            foreach (var decision in result.Lines)
            {
                var line = claim.Lines.Single(l => l.Sequence == decision.Sequence);
                line.AllowedCents = decision.AllowedCents;
                line.PlanPaidCents = decision.PlanPaidCents;
                line.MemberOwesCents = decision.MemberOwesCents;
                line.DenialCode = decision.DenialCode;
            }

            // Advance running state + duplicate fingerprints exactly like production.
            acc.DeductibleMetCents = result.NewDeductibleMetCents;
            acc.OopMetCents = result.NewOopMetCents;
            if (result.Status == ClaimStatus.Paid)
            {
                foreach (var line in claim.Lines)
                {
                    priors.Add(new PriorClaimFingerprint(claim.RenderingProviderNpi, serviceDate, line.ProcedureCode));
                }
                paid.Add((claim.RenderingProviderNpi, serviceDate, claim.Lines[0].ProcedureCode));
            }

            return claim;
        }

        Claim BuildWholeDenial(DenialCode code, DateOnly serviceDate)
        {
            var received = serviceDate.ToDateTime(TimeOnly.MinValue).AddDays(Next(20, 200));
            if (received > _now)
            {
                received = _now.AddDays(-Next(5, 30));
            }

            var claim = NewClaim(npiPool, serviceDate, received, BuildLines(feeByCode, includeNonCovered: false));
            claim.Status = ClaimStatus.Denied;
            claim.DenialCode = code;
            claim.AdjudicatedAtUtc = received.AddHours(Next(2, 9));
            claim.TotalAllowedCents = 0;
            claim.TotalPlanPaidCents = 0;
            claim.TotalMemberOwesCents = 0;
            foreach (var line in claim.Lines)
            {
                line.AllowedCents = 0;
                line.PlanPaidCents = 0;
                line.MemberOwesCents = 0;
                line.DenialCode = code;
            }
            return claim;
        }

        Claim BuildDuplicateDenial(List<(string Npi, DateOnly Date, string Proc)> paid)
        {
            if (paid.Count == 0)
            {
                return BuildWholeDenial(DenialCode.TimelyFiling, new DateOnly(2024, Next(11, 13), Next(1, 28)));
            }

            var original = Pick(paid);
            var received = original.Date.ToDateTime(TimeOnly.MinValue).AddDays(Next(15, 60)).AddHours(Next(8, 17));
            if (received > _now)
            {
                received = _now.AddDays(-Next(3, 20));
            }

            var fee = feeByCode[original.Proc];
            var claim = NewClaim([original.Npi], original.Date, received,
            [
                new ClaimLine { Sequence = 1, ProcedureCode = original.Proc, ChargeCents = VaryCharge(fee.AllowedCents) },
            ]);
            claim.Status = ClaimStatus.Denied;
            claim.DenialCode = DenialCode.DuplicateClaim;
            claim.AdjudicatedAtUtc = received.AddHours(Next(2, 8));
            claim.TotalAllowedCents = 0;
            claim.TotalPlanPaidCents = 0;
            claim.TotalMemberOwesCents = 0;
            claim.Lines[0].AllowedCents = 0;
            claim.Lines[0].PlanPaidCents = 0;
            claim.Lines[0].MemberOwesCents = 0;
            claim.Lines[0].DenialCode = DenialCode.DuplicateClaim;
            return claim;
        }

        Claim BuildOpenClaim(ClaimStatus status)
        {
            var serviceDate = RandomServiceDate();
            var received = _now.AddHours(-Next(1, 72));
            var claim = NewClaim(npiPool, serviceDate, received, BuildLines(feeByCode, includeNonCovered: false));
            claim.Status = status;
            if (status == ClaimStatus.DeadLettered)
            {
                claim.Attempts = 5;
            }
            return claim;
        }

        Claim NewClaim(List<string> npis, DateOnly serviceDate, DateTime receivedUtc, List<ClaimLine> lines) => new()
        {
            ClaimNumber = "PENDING", // overwritten with CLM-… once the identity id exists
            Type = ClaimType.Professional,
            RenderingProviderNpi = Pick(npis),
            ServiceDate = serviceDate,
            TotalChargeCents = lines.Sum(l => l.ChargeCents),
            Status = ClaimStatus.Received,
            ReceivedAtUtc = receivedUtc,
            Lines = lines,
        };

        List<ClaimLine> BuildLines(Dictionary<string, ProcedureFee> fees, bool includeNonCovered)
        {
            var covered = fees.Values.Where(f => f.IsCovered).ToList();
            var lineCount = Next(1, includeNonCovered ? 4 : 5);
            List<ClaimLine> lines = [];
            for (var i = 0; i < lineCount; i++)
            {
                var fee = Pick(covered);
                lines.Add(new ClaimLine { Sequence = i + 1, ProcedureCode = fee.ProcedureCode, ChargeCents = VaryCharge(fee.AllowedCents) });
            }
            if (includeNonCovered)
            {
                var nonCovered = fees.Values.First(f => !f.IsCovered);
                lines.Add(new ClaimLine
                {
                    Sequence = lines.Count + 1,
                    ProcedureCode = nonCovered.ProcedureCode,
                    ChargeCents = nonCovered.AllowedCents + Next(0, 5000),
                });
            }
            return lines;
        }

        DateOnly RandomServiceDate()
        {
            var earliest = new DateOnly(2026, 1, 5);
            var latest = DateOnly.FromDateTime(_now).AddDays(-2);
            var span = Math.Max(1, latest.DayNumber - earliest.DayNumber);
            return earliest.AddDays((int)(NextUint() % (uint)span));
        }

        int VaryCharge(int allowedCents) => Math.Max(500, (int)(allowedCents * (0.9m + NextDecimal() * 0.5m)));
    }

    private static DateOnly FirstOfNextMonth(DateTime now) => new DateOnly(now.Year, now.Month, 1).AddMonths(1);

    // ---- deterministic LCG (Numerical Recipes constants) ----
    private uint NextUint()
    {
        _state = unchecked(_state * 1664525u + 1013904223u);
        return _state >> 8;
    }

    private int Next(int minInclusive, int maxExclusive) =>
        minInclusive + (int)(NextUint() % (uint)Math.Max(1, maxExclusive - minInclusive));

    private decimal NextDecimal() => NextUint() / 16777216m; // [0,1)

    private bool Chance(decimal probability) => NextDecimal() < probability;

    private T Pick<T>(IReadOnlyList<T> items) => items[(int)(NextUint() % (uint)items.Count)];

    private string NextMbi(HashSet<string> used)
    {
        var chars = new char[11];
        while (true)
        {
            chars[0] = (char)('0' + Next(1, 10));
            for (var i = 1; i < 11; i++)
            {
                chars[i] = Chance(0.55m) ? MbiConsonants[Next(0, MbiConsonants.Length)] : (char)('0' + Next(0, 10));
            }
            var candidate = new string(chars);
            if (used.Add(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>Real NPI check digit (Luhn with the 80840 prefix) so seeded providers validate.</summary>
    private string NextNpi()
    {
        Span<char> digits = stackalloc char[10];
        for (var i = 0; i < 9; i++)
        {
            digits[i] = (char)('0' + Next(0, 10));
        }

        var payload = "80840" + new string(digits[..9]);
        var sum = 0;
        var doubleNext = true;
        for (var i = payload.Length - 1; i >= 0; i--)
        {
            var d = payload[i] - '0';
            if (doubleNext)
            {
                d *= 2;
                if (d > 9)
                {
                    d -= 9;
                }
            }
            sum += d;
            doubleNext = !doubleNext;
        }
        digits[9] = (char)('0' + ((10 - (sum % 10)) % 10));
        return new string(digits);
    }
}

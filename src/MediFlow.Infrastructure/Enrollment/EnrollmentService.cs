namespace MediFlow.Infrastructure.Enrollment;

using MediFlow.Contracts.Enrollment;
using MediFlow.Domain.Auditing;
using MediFlow.Domain.Enrollment;
using Microsoft.EntityFrameworkCore;
using Persistence;
using System.Text.Json;

/// <summary>Enrollment application intake and decisions.</summary>
public interface IEnrollmentService
{
    /// <summary>Runs the eligibility rules WITHOUT saving — powers the pre-check endpoint and the MCP tool.</summary>
    Task<EnrollmentValidationDto> CheckEligibilityAsync(SubmitEnrollmentRequest request, CancellationToken ct = default);

    Task<EnrollmentSubmissionOutcome> SubmitAsync(SubmitEnrollmentRequest request, string actor, CancellationToken ct = default);
    Task<EnrollmentDecisionResult> DecideAsync(int applicationId, bool approve, string? note, string actor, CancellationToken ct = default);
    Task<EnrollmentDto?> GetByIdAsync(int applicationId, CancellationToken ct = default);
    Task<IReadOnlyList<EnrollmentDto>> ListByStatusAsync(EnrollmentStatus? status, int pageIndex, int pageSize, CancellationToken ct = default);
}

public enum EnrollmentDecisionStatus { Success, NotFound, IllegalTransition }

public sealed record EnrollmentDecisionResult(EnrollmentDecisionStatus Status, EnrollmentDto? Enrollment);

/// <summary>Either accepted (with the application id) or rejected with rule violations.</summary>
public sealed record EnrollmentSubmissionOutcome(bool Accepted, int? ApplicationId, string? ApplicationNumber, EnrollmentValidationDto? Validation);

public sealed class EnrollmentService(MediFlowDbContext db, TimeProvider clock) : IEnrollmentService
{
    public async Task<EnrollmentValidationDto> CheckEligibilityAsync(SubmitEnrollmentRequest request, CancellationToken ct = default)
    {
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var asOf = DateOnly.FromDateTime(nowUtc);

        var member = await db.Members.FirstOrDefaultAsync(m => m.Id == request.MemberId, ct);
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == request.PlanId, ct);
        if (member is null || plan is null)
        {
            return new EnrollmentValidationDto(false, [new EnrollmentViolationDto(0, "Unknown member or plan.")]);
        }

        var activeEnrollments = await db.Enrollments
            .Include(a => a.Plan)
            .Where(a => a.MemberId == request.MemberId && a.Status == EnrollmentStatus.Active)
            .ToListAsync(ct);

        var validation = EnrollmentRules.Validate(
            member, plan, request.RequestedEffectiveDate, (SepReason)request.SepReason,
            activeEnrollments, asOf, nowUtc);

        return new EnrollmentValidationDto(validation.IsValid,
            [.. validation.Violations.Select(v => new EnrollmentViolationDto((int)v.Code, v.Message))]);
    }

    public async Task<EnrollmentSubmissionOutcome> SubmitAsync(SubmitEnrollmentRequest request, string actor, CancellationToken ct = default)
    {
        var validation = await CheckEligibilityAsync(request, ct);
        if (!validation.IsValid)
        {
            return new EnrollmentSubmissionOutcome(false, null, null, validation);
        }

        var member = await db.Members.AsNoTracking().FirstAsync(m => m.Id == request.MemberId, ct);
        var plan = await db.Plans.AsNoTracking().FirstAsync(p => p.Id == request.PlanId, ct);
        var nowUtc = clock.GetUtcNow().UtcDateTime;

        // SqlServerRetryingExecutionStrategy requires user transactions to run
        // inside an explicit execution strategy (retriable unit).
        EnrollmentApplication application = null!;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            application = new EnrollmentApplication
            {
                ApplicationNumber = "PENDING",
                MemberId = member.Id,
                PlanId = plan.Id,
                Status = EnrollmentStatus.Submitted,
                SepReason = (SepReason)request.SepReason,
                RequestedEffectiveDate = request.RequestedEffectiveDate,
                SubmittedAtUtc = nowUtc,
            };
            db.Enrollments.Add(application);
            await db.SaveChangesAsync(ct);

            application.ApplicationNumber = EnrollmentApplication.NextApplicationNumber(application.Id, nowUtc.Year);
            db.AuditEntries.Add(new AuditEntry
            {
                EntityType = "EnrollmentApplication",
                EntityKey = application.ApplicationNumber,
                Action = "Submitted",
                DetailJson = JsonSerializer.Serialize(new { application.MemberId, plan.PlanCode, application.RequestedEffectiveDate }),
                Actor = actor,
                AtUtc = nowUtc,
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });

        return new EnrollmentSubmissionOutcome(true, application.Id, application.ApplicationNumber, null);
    }

    public async Task<EnrollmentDecisionResult> DecideAsync(int applicationId, bool approve, string? note, string actor, CancellationToken ct = default)
    {
        var nowUtc = clock.GetUtcNow().UtcDateTime;

        var application = await db.Enrollments
            .Include(a => a.Member)
            .Include(a => a.Plan)
            .FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        if (application is null)
        {
            return new EnrollmentDecisionResult(EnrollmentDecisionStatus.NotFound, null);
        }

        // Staff route Submitted → PendingVerification first; the decision endpoint
        // handles both to keep the demo flow one click. Illegal jumps are rejected.
        if (application.Status == EnrollmentStatus.Submitted)
        {
            EnrollmentStateMachine.TryTransition(application, EnrollmentStatus.PendingVerification);
        }

        var decided = EnrollmentStateMachine.TryTransition(
            application, approve ? EnrollmentStatus.Approved : EnrollmentStatus.Denied);
        if (!decided)
        {
            return new EnrollmentDecisionResult(EnrollmentDecisionStatus.IllegalTransition, null);
        }

        application.DecidedAtUtc = nowUtc;
        application.DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        if (approve)
        {
            // Effective date already reached → coverage is live immediately.
            if (application.RequestedEffectiveDate <= DateOnly.FromDateTime(nowUtc))
            {
                EnrollmentStateMachine.TryTransition(application, EnrollmentStatus.Active);
            }

            // A 5-star switch replaces any same-type active enrollment at month end.
            if (application.SepReason == SepReason.FiveStar)
            {
                var priorSameType = await db.Enrollments
                    .Where(a => a.MemberId == application.MemberId
                        && a.Id != application.Id
                        && a.Status == EnrollmentStatus.Active
                        && a.Plan!.Type == application.Plan!.Type)
                    .ToListAsync(ct);
                foreach (var prior in priorSameType)
                {
                    EnrollmentStateMachine.TryTransition(prior, EnrollmentStatus.Cancelled);
                    prior.CancelledEffectiveDate = application.RequestedEffectiveDate.AddDays(-1);
                }
            }
        }

        db.AuditEntries.Add(new AuditEntry
        {
            EntityType = "EnrollmentApplication",
            EntityKey = application.ApplicationNumber,
            Action = approve ? "Approved" : "Denied",
            DetailJson = JsonSerializer.Serialize(new { application.Status, Note = application.DecisionNote }),
            Actor = actor,
            AtUtc = nowUtc,
        });
        await db.SaveChangesAsync(ct);

        return new EnrollmentDecisionResult(EnrollmentDecisionStatus.Success, ToDto(application));
    }

    public async Task<EnrollmentDto?> GetByIdAsync(int applicationId, CancellationToken ct = default)
    {
        var application = await db.Enrollments
            .Include(a => a.Member)
            .Include(a => a.Plan)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == applicationId, ct);
        return application is null ? null : ToDto(application);
    }

    public async Task<IReadOnlyList<EnrollmentDto>> ListByStatusAsync(EnrollmentStatus? status, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        var query = db.Enrollments.Include(a => a.Member).Include(a => a.Plan).AsNoTracking();
        if (status.HasValue)
        {
            query = query.Where(a => a.Status == status.Value);
        }

        return await query
            .OrderByDescending(a => a.SubmittedAtUtc)
            .Skip((Math.Max(1, pageIndex) - 1) * pageSize)
            .Take(Math.Clamp(pageSize, 1, 100))
            .Select(a => ToDto(a))
            .ToListAsync(ct);
    }

    private static EnrollmentDto ToDto(EnrollmentApplication a)
    {
        if (a.Member is null || a.Plan is null)
        {
            throw new InvalidOperationException("Enrollment navigation properties must be loaded before mapping.");
        }

        return new EnrollmentDto()
        {
            Id = a.Id,
            ApplicationNumber = a.ApplicationNumber,
            MemberId = a.MemberId,
            MemberName = a.Member!.DisplayName,
            Mbi = a.Member.Mbi,
            PlanId = a.PlanId,
            PlanCode = a.Plan!.PlanCode,
            PlanName = a.Plan.Name,
            Status = (int)a.Status,
            SepReason = (int)a.SepReason,
            RequestedEffectiveDate = a.RequestedEffectiveDate,
            SubmittedAtUtc = a.SubmittedAtUtc,
            DecidedAtUtc = a.DecidedAtUtc,
            DecisionNote = a.DecisionNote,
        };
    }
}

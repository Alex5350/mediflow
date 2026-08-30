namespace MediFlow.Api;

using MediFlow.Contracts.Enrollment;
using MediFlow.Domain.Enrollment;
using MediFlow.Infrastructure.Enrollment;
using Microsoft.AspNetCore.Http.HttpResults;

public static class EnrollmentModule
{
    public static IEndpointRouteBuilder MapEnrollmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/enrollments").WithTags("Enrollment");

        group.MapPost("/", async Task<Results<Accepted<EnrollmentAcceptedDto>, ValidationProblem>> (
            SubmitEnrollmentRequest request,
            IEnrollmentService service,
            CancellationToken ct) =>
        {
            var outcome = await service.SubmitAsync(request, "staff-portal", ct);
            if (!outcome.Accepted)
            {
                return TypedResults.ValidationProblem(
                    outcome.Validation!.Violations.ToDictionary(
                        v => ((EnrollmentViolation)v.Code).ToString(),
                        v => new[] { v.Message }));
            }

            return TypedResults.Accepted((string?)null, new EnrollmentAcceptedDto(outcome.ApplicationId!.Value, outcome.ApplicationNumber!));
        });

        group.MapPost("/eligibility", async Task<Ok<EnrollmentValidationDto>> (
            SubmitEnrollmentRequest request,
            IEnrollmentService service,
            CancellationToken ct) =>
        {
            var validation = await service.CheckEligibilityAsync(request, ct);
            return TypedResults.Ok(validation);
        });

        group.MapGet("/", async Task<Ok<List<EnrollmentDto>>> (
            IEnrollmentService service,
            int? status,
            int page = 1,
            int pageSize = 25,
            CancellationToken ct = default) =>
        {
            var enrollments = await service.ListByStatusAsync(
                status.HasValue ? (EnrollmentStatus)status.Value : null, page, pageSize, ct);
            return TypedResults.Ok(enrollments.ToList());
        });

        group.MapGet("/{applicationId:int}", async Task<Results<Ok<EnrollmentDto>, NotFound>> (
            int applicationId,
            IEnrollmentService service,
            CancellationToken ct) =>
        {
            var enrollment = await service.GetByIdAsync(applicationId, ct);
            return enrollment is null ? TypedResults.NotFound() : TypedResults.Ok(enrollment);
        });

        group.MapPost("/{applicationId:int}/decision", async Task<Results<Ok<EnrollmentDto>, NotFound, Conflict<string>>> (
            int applicationId,
            EnrollmentDecisionRequest request,
            IEnrollmentService service,
            CancellationToken ct) =>
        {
            var outcome = await service.DecideAsync(applicationId, request.Approve, request.Note, "staff-portal", ct);
            return outcome.Status switch
            {
                EnrollmentDecisionStatus.Success => TypedResults.Ok<EnrollmentDto>(outcome.Enrollment!),
                EnrollmentDecisionStatus.IllegalTransition =>
                    TypedResults.Conflict($"Application {applicationId} cannot transition from its current status."),
                _ => TypedResults.NotFound(),
            };
        });

        return app;
    }

    public sealed record EnrollmentAcceptedDto(int ApplicationId, string ApplicationNumber);
}

namespace MediFlow.Blazor.Services;

using MediFlow.Contracts.Claims;
using MediFlow.Contracts.Enrollment;
using MediFlow.Contracts.Members;
using MediFlow.Contracts.Plans;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;

/// <summary>Base URLs + API key for the two backing APIs.</summary>
public sealed class ApiOptions
{
    public string EnrollmentBaseUrl { get; set; } = "http://localhost:8080";
    public string ClaimsBaseUrl { get; set; } = "http://localhost:8081";
    public string Key { get; set; } = "mediflow-dev-key";
}

/// <summary>Attaches the X-Api-Key header to every outgoing API call.</summary>
public sealed class ApiKeyHandler(IOptions<ApiOptions> options) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.TryAddWithoutValidation("X-Api-Key", options.Value.Key);
        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>Members, plans and enrollment endpoints of MediFlow.Api.</summary>
public sealed class EnrollmentApiClient(HttpClient http)
{
    public async Task<PagedResult<MemberSearchResultDto>?> SearchMembersAsync(string query, int page = 1, int pageSize = 25) =>
        await http.GetFromJsonAsync<PagedResult<MemberSearchResultDto>>(
            $"/api/v1/members/search?q={Uri.EscapeDataString(query)}&page={page}&pageSize={pageSize}");

    public async Task<Member360Dto?> GetMember360Async(int memberId) =>
        await http.GetFromJsonAsync<Member360Dto>($"/api/v1/members/{memberId}/360");

    public async Task<List<PlanDto>?> GetPlansAsync(int year) =>
        await http.GetFromJsonAsync<List<PlanDto>>($"/api/v1/plans?year={year}");

    public async Task<List<PlanEnrollmentSummaryDto>?> GetPlanEnrollmentSummaryAsync(int year) =>
        await http.GetFromJsonAsync<List<PlanEnrollmentSummaryDto>>($"/api/v1/plans/enrollment-summary?year={year}");

    public async Task<List<EnrollmentDto>?> GetEnrollmentsAsync(int? status = null, int page = 1, int pageSize = 25)
    {
        var url = $"/api/v1/enrollments?page={page}&pageSize={pageSize}";
        if (status.HasValue)
        {
            url += $"&status={status}";
        }

        return await http.GetFromJsonAsync<List<EnrollmentDto>>(url);
    }

    public async Task<EnrollmentSubmissionOutcomeUi> SubmitEnrollmentAsync(SubmitEnrollmentRequest request)
    {
        var response = await http.PostAsJsonAsync("/api/v1/enrollments", request);
        if (response.IsSuccessStatusCode)
        {
            var accepted = await response.Content.ReadFromJsonAsync<AcceptedDto>();
            return new EnrollmentSubmissionOutcomeUi(true, accepted?.ApplicationNumber, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDto>();
            return new EnrollmentSubmissionOutcomeUi(false, null, problem?.Errors);
        }

        return new EnrollmentSubmissionOutcomeUi(false, null, new Dictionary<string, string[]> { ["error"] = [$"Enrollment API returned {(int)response.StatusCode}."] });
    }

    public async Task<EnrollmentDto?> DecideAsync(int applicationId, bool approve, string? note)
    {
        var response = await http.PostAsJsonAsync($"/api/v1/enrollments/{applicationId}/decision",
            new EnrollmentDecisionRequest { Approve = approve, Note = note });
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<EnrollmentDto>()
            : null;
    }
}

/// <summary>Claims and rollup endpoints of MediFlow.Claims.Api.</summary>
public sealed class ClaimsApiClient(HttpClient http)
{
    public async Task<PagedResult<ClaimQueueItemDto>?> GetQueueAsync(string? statuses, int page = 1, int pageSize = 25)
    {
        var url = $"/api/v1/claims/queue?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(statuses))
        {
            url += $"&statuses={Uri.EscapeDataString(statuses)}";
        }

        return await http.GetFromJsonAsync<PagedResult<ClaimQueueItemDto>>(url);
    }

    public async Task<ClaimDetailDto?> GetClaimAsync(int claimId) =>
        await http.GetFromJsonAsync<ClaimDetailDto>($"/api/v1/claims/{claimId}");

    public async Task<ClaimSubmissionOutcomeUi> SubmitClaimAsync(SubmitClaimRequest request)
    {
        var response = await http.PostAsJsonAsync("/api/v1/claims", request);
        if (response.IsSuccessStatusCode)
        {
            var accepted = await response.Content.ReadFromJsonAsync<ClaimAcceptedDto>();
            return new ClaimSubmissionOutcomeUi(true, accepted?.ClaimId, accepted?.ClaimNumber, null);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDto>();
            return new ClaimSubmissionOutcomeUi(false, null, null, problem?.Errors);
        }

        return new ClaimSubmissionOutcomeUi(false, null, null, new Dictionary<string, string[]> { ["error"] = [$"Claims API returned {(int)response.StatusCode}."] });
    }

    public async Task<AdjudicationPreviewDto?> PreviewAsync(int claimId) =>
        await http.GetFromJsonAsync<AdjudicationPreviewDto>($"/api/v1/claims/{claimId}/preview");

    public async Task<bool> PendAsync(int claimId, string note)
    {
        var response = await http.PostAsJsonAsync($"/api/v1/claims/{claimId}/pend", new { Note = note });
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> QueueAdjudicationAsync(int claimId)
    {
        var response = await http.PostAsync($"/api/v1/claims/{claimId}/adjudicate", content: null);
        return response.IsSuccessStatusCode;
    }

    public async Task<List<DenialRollupDto>?> GetDenialRollupAsync(int year) =>
        await http.GetFromJsonAsync<List<DenialRollupDto>>($"/api/v1/rollups/denials?year={year}");

    public async Task<DashboardStatsDto?> GetDashboardStatsAsync() =>
        await http.GetFromJsonAsync<DashboardStatsDto>("/api/v1/rollups/dashboard");
}

// --- small shared response shapes ---
public sealed record AcceptedDto(int ApplicationId, string ApplicationNumber);
public sealed record ClaimAcceptedDto(int ClaimId, string ClaimNumber);

public sealed record ValidationProblemDto()
{
    public Dictionary<string, string[]>? Errors { get; init; }
}

public sealed record EnrollmentSubmissionOutcomeUi(bool Accepted, string? ApplicationNumber, Dictionary<string, string[]>? Violations);
public sealed record ClaimSubmissionOutcomeUi(bool Accepted, int? ClaimId, string? ClaimNumber, Dictionary<string, string[]>? Violations);

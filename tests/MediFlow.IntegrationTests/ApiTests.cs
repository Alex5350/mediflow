namespace MediFlow.IntegrationTests;

using MediFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

[Collection("database")]
public sealed class EnrollmentApiTests(TestDatabaseFixture db) : IDisposable
{
    private readonly EnrollmentApiFactory _factory = new(db.ConnectionString);

    private HttpClient Client => _factory.CreateClient();

    [Fact]
    public async Task Missing_api_key_is_unauthorized()
    {
        var response = await Client.GetAsync("/api/v1/plans?year=2026");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Plans_are_served_with_the_api_key()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/plans?year=2026");
        request.Headers.TryAddWithoutValidation("X-Api-Key", EnrollmentApiFactory.ApiKey);
        var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var plans = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.True(plans!.Count >= 12);
    }

    [Fact]
    public async Task Eligibility_precheck_reports_violations_without_saving()
    {
        // Seed data guarantees members with active MA coverage — pick one.
        int memberId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MediFlowDbContext>();
            memberId = await context.Enrollments
                .AsNoTracking()
                .Where(e => e.Status == Domain.Enrollment.EnrollmentStatus.Active
                    && e.Plan!.Type == Domain.Plans.PlanType.MedicareAdvantage)
                .Select(e => e.MemberId)
                .FirstAsync();
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/enrollments/eligibility")
        {
            Content = JsonContent.Create(new
            {
                memberId,
                planId = 1,
                requestedEffectiveDate = "2026-09-15",   // not first of month → violation
                sepReason = 0,
            }),
        };
        request.Headers.TryAddWithoutValidation("X-Api-Key", EnrollmentApiFactory.ApiKey);
        var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"code\":5", body);   // EffectiveDateNotFirstOfMonth
        Assert.Contains("\"code\":4", body);   // OutsideEnrollmentWindow
    }

    [Fact]
    public async Task Submit_and_decide_happy_path()
    {
        int memberId, planId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MediFlowDbContext>();
            var member = new Domain.Members.Member
            {
                Mbi = Guid.NewGuid().ToString("N")[..11].ToUpperInvariant(),
                FirstName = "Happy",
                LastName = "Path",
                DateOfBirth = new DateOnly(1950, 3, 3),
                StateCode = "OH",
                PartAEffective = new DateOnly(2023, 6, 1),
                PartBEffective = new DateOnly(2023, 6, 1),
                CreatedAtUtc = DateTime.UtcNow,
            };
            context.Members.Add(member);
            await context.SaveChangesAsync();
            memberId = member.Id;
            planId = await context.Plans
                .AsNoTracking()
                .Where(p => p.ContractYear == 2026 && p.Type == Domain.Plans.PlanType.MedicareAdvantage)
                .Select(p => p.Id)
                .FirstAsync();
        }

        var nextMonthFirst = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(1);

        using (var submit = new HttpRequestMessage(HttpMethod.Post, "/api/v1/enrollments")
        {
            Content = JsonContent.Create(new { memberId, planId, requestedEffectiveDate = nextMonthFirst.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), sepReason = 1 }),
        })
        {
            submit.Headers.TryAddWithoutValidation("X-Api-Key", EnrollmentApiFactory.ApiKey);
            var response = await Client.SendAsync(submit);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        // Approve the newest pending application for that member.
        EnrollmentDto? application;
        using (var list = new HttpRequestMessage(HttpMethod.Get, "/api/v1/enrollments?status=1&pageSize=50"))
        {
            list.Headers.TryAddWithoutValidation("X-Api-Key", EnrollmentApiFactory.ApiKey);
            var response = await Client.SendAsync(list);
            var applications = await response.Content.ReadFromJsonAsync<List<EnrollmentDto>>();
            application = applications!.First(a => a.MemberId == memberId);
        }

        using var decide = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/enrollments/{application.Id}/decision")
        {
            Content = JsonContent.Create(new { approve = true, note = "SEP verified (integration)" }),
        };
        decide.Headers.TryAddWithoutValidation("X-Api-Key", EnrollmentApiFactory.ApiKey);
        var decideResponse = await Client.SendAsync(decide);
        Assert.Equal(HttpStatusCode.OK, decideResponse.StatusCode);
        var decided = await decideResponse.Content.ReadFromJsonAsync<EnrollmentDto>();
        // effective date is next month → Approved (3), not yet Active
        Assert.Equal(3, decided!.Status);
    }

    [Fact]
    public async Task Health_endpoints_are_anonymous()
    {
        var response = await Client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public void Dispose() => _factory.Dispose();

    private sealed record EnrollmentDto(int Id, int MemberId, int Status);
}

[Collection("database")]
public sealed class ClaimsApiTests(TestDatabaseFixture db) : IDisposable
{
    private readonly ClaimsApiFactory _factory = new(db.ConnectionString);

    private HttpClient Client => _factory.CreateClient();

    [Fact]
    public async Task Invalid_submission_returns_validation_problem()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/claims")
        {
            Content = JsonContent.Create(new
            {
                memberId = 1,
                planId = 1,
                renderingProviderNpi = "1234567890",   // bad check digit
                type = 0,
                serviceDate = "2026-01-10",
                lines = new[] { new { procedureCode = "99214", chargeCents = 15000 } },
            }),
        };
        request.Headers.TryAddWithoutValidation("X-Api-Key", ClaimsApiFactory.ApiKey);
        var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("NPI_INVALID", body);
    }

    [Fact]
    public async Task Valid_submission_is_accepted_and_preview_is_a_dry_run()
    {
        int memberId, planId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MediFlowDbContext>();
            var member = new Domain.Members.Member
            {
                Mbi = Guid.NewGuid().ToString("N")[..11].ToUpperInvariant(),
                FirstName = "Preview",
                LastName = "Dryrun",
                DateOfBirth = new DateOnly(1948, 7, 7),
                StateCode = "TX",
                PartAEffective = new DateOnly(2022, 1, 1),
                PartBEffective = new DateOnly(2022, 1, 1),
                CreatedAtUtc = DateTime.UtcNow,
            };
            context.Members.Add(member);
            await context.SaveChangesAsync();
            memberId = member.Id;
            planId = await context.Plans.AsNoTracking()
                .Where(p => p.ContractYear == 2026 && p.Type == Domain.Plans.PlanType.MedicareAdvantage)
                .Select(p => p.Id).FirstAsync();
        }

        string? claimNumber = null;
        using (var submit = new HttpRequestMessage(HttpMethod.Post, "/api/v1/claims")
        {
            Content = JsonContent.Create(new
            {
                memberId,
                planId,
                renderingProviderNpi = "1234567893",
                type = 0,
                serviceDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-7).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                lines = new[] { new { procedureCode = "99214", chargeCents = 21000 } },
            }),
        })
        {
            submit.Headers.TryAddWithoutValidation("X-Api-Key", ClaimsApiFactory.ApiKey);
            var response = await Client.SendAsync(submit);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var accepted = await response.Content.ReadFromJsonAsync<Accepted>();
            claimNumber = accepted!.claimNumber;
        }

        using var byNumber = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/claims/by-number/{claimNumber}");
        byNumber.Headers.TryAddWithoutValidation("X-Api-Key", ClaimsApiFactory.ApiKey);
        var detailResponse = await Client.SendAsync(byNumber);
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        var claimId = detail.GetProperty("id").GetInt32();

        // Preview: dry-run decision without committing.
        using var preview = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/claims/{claimId}/preview");
        preview.Headers.TryAddWithoutValidation("X-Api-Key", ClaimsApiFactory.ApiKey);
        var previewResponse = await Client.SendAsync(preview);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var previewBody = await previewResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Denied", previewBody.GetProperty("status").GetString()); // no active enrollment for this fresh member
        Assert.Contains("coverage", previewBody.GetProperty("claimDenialCode").GetString(), StringComparison.OrdinalIgnoreCase);

        // The claim itself must STILL be Received — the preview wrote nothing.
        using var check = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/claims/{claimId}");
        check.Headers.TryAddWithoutValidation("X-Api-Key", ClaimsApiFactory.ApiKey);
        var after = await (await Client.SendAsync(check)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, after.GetProperty("status").GetInt32());
    }

    public void Dispose() => _factory.Dispose();

    private sealed record Accepted(int claimId, string claimNumber);
}

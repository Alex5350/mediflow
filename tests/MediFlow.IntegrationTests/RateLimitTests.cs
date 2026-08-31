namespace MediFlow.IntegrationTests;

using System.Net;
using Xunit;

/// <summary>
/// Proves the rate limiter is actually wired: the global limiter partitions by
/// API key, so an authorized caller past the configured permit limit gets 429
/// while health traffic keeps its own anonymous bucket and stays open.
/// </summary>
[Collection("database")]
public sealed class RateLimitTests(TestDatabaseFixture db) : IDisposable
{
    private const int PermitLimit = 5;
    private readonly EnrollmentApiFactory _factory = new(db.ConnectionString, rateLimitPermitLimit: PermitLimit);

    [Fact]
    public async Task Authorized_calls_get_429_past_the_permit_limit()
    {
        using var client = _factory.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < PermitLimit + 5; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/plans?year=2026");
            request.Headers.TryAddWithoutValidation("X-Api-Key", EnrollmentApiFactory.ApiKey);
            using var response = await client.SendAsync(request);
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(PermitLimit, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(5, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }

    [Fact]
    public async Task Health_stays_reachable_past_the_authorized_limit()
    {
        using var client = _factory.CreateClient();
        for (var i = 0; i < PermitLimit + 2; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/plans?year=2026");
            request.Headers.TryAddWithoutValidation("X-Api-Key", EnrollmentApiFactory.ApiKey);
            using var response = await client.SendAsync(request);
            if (i >= PermitLimit)
            {
                Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            }
        }

        var health = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    public void Dispose() => _factory.Dispose();
}

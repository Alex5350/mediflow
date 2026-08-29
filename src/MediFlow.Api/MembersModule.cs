namespace MediFlow.Api;

using MediFlow.Contracts.Members;
using MediFlow.Infrastructure.Data;
using Microsoft.AspNetCore.Http.HttpResults;

public static class MembersModule
{
    public static IEndpointRouteBuilder MapMembersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/members").WithTags("Members");

        group.MapGet("/search", async Task<Ok<PagedResult<MemberSearchResultDto>>> (
            IReadStore readStore,
            string? q,
            int page = 1,
            int pageSize = 25,
            CancellationToken ct = default) =>
        {
            var results = await readStore.SearchMembersAsync(string.IsNullOrWhiteSpace(q) ? "" : q!, page, pageSize, ct);
            return TypedResults.Ok(results);
        });

        group.MapGet("/{memberId:int}/360", async Task<Results<Ok<Member360Dto>, NotFound>> (
            int memberId,
            IReadStore readStore,
            CancellationToken ct) =>
        {
            var view = await readStore.GetMember360Async(memberId, ct);
            return view is null ? TypedResults.NotFound() : TypedResults.Ok(view);
        });

        return app;
    }
}

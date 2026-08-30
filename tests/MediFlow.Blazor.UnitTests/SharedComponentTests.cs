namespace MediFlow.Blazor.UnitTests;

using Bunit;
using MediFlow.Blazor.Components.Shared;
using Microsoft.AspNetCore.Components;
using Xunit;

public class StatusBadgeTests : TestContext
{
    [Theory]
    [InlineData(0, "Received", "badge-received")]
    [InlineData(1, "Adjudicating", "badge-adjudicating")]
    [InlineData(2, "Paid", "badge-paid")]
    [InlineData(3, "Denied", "badge-denied")]
    [InlineData(4, "Pended", "badge-pended")]
    [InlineData(5, "DeadLettered", "badge-deadlettered")]
    public void Renders_claim_status(int status, string label, string cssClass)
    {
        var cut = RenderComponent<StatusBadge>(parameters => parameters
            .Add(p => p.Kind, "claim")
            .Add(p => p.Value, status));

        cut.MarkupMatches($"<span class=\"badge {cssClass}\">{label}</span>");
    }

    [Fact]
    public void Renders_enrollment_status()
    {
        var cut = RenderComponent<StatusBadge>(parameters => parameters
            .Add(p => p.Kind, "enrollment")
            .Add(p => p.Value, 5));

        cut.MarkupMatches("<span class=\"badge badge-active\">Active</span>");
    }
}

public class PagerTests : TestContext
{
    [Fact]
    public void Disables_prev_on_first_page()
    {
        var cut = RenderComponent<Pager>(parameters => parameters
            .Add(p => p.PageIndex, 1)
            .Add(p => p.TotalItems, 95)
            .Add(p => p.PageSize, 15)
            .Add(p => p.Go, EventCallback.Factory.Create<int>(this, () => Task.CompletedTask)));

        Assert.Contains("disabled", cut.Find("button").ToMarkup());
        cut.Markup.Contains("95 results");
    }

    [Fact]
    public void Raises_Go_with_target_page()
    {
        var navigatedTo = 0;
        var cut = RenderComponent<Pager>(parameters => parameters
            .Add(p => p.PageIndex, 2)
            .Add(p => p.TotalItems, 90)
            .Add(p => p.PageSize, 15)
            .Add(p => p.Go, EventCallback.Factory.Create<int>(this, (int page) => navigatedTo = page)));

        cut.FindAll("button").ElementAt(1).Click();   // Next
        Assert.Equal(3, navigatedTo);

        cut.FindAll("button").ElementAt(0).Click();   // Prev
        Assert.Equal(1, navigatedTo);
    }

    [Fact]
    public void Disables_next_on_last_page()
    {
        var cut = RenderComponent<Pager>(parameters => parameters
            .Add(p => p.PageIndex, 6)
            .Add(p => p.TotalItems, 90)
            .Add(p => p.PageSize, 15)
            .Add(p => p.Go, EventCallback.Factory.Create<int>(this, () => Task.CompletedTask)));

        Assert.Contains("disabled", cut.FindAll("button").ElementAt(1).ToMarkup());
    }
}

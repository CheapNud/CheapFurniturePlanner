using Bunit;
using CheapFurniturePlanner.Components.Shared;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;

namespace CheapFurniturePlanner.Tests.Components;

// UX-1 Task 1: the shared "nothing here yet" primitive - just enough to prove the text renders
// and the optional action button fires its callback, since it has no other logic.
public class EmptyStateTests : TestContext
{
    private void ConfigureServices()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Render_ShowsText()
    {
        ConfigureServices();

        var cut = Render<EmptyState>(p => p.Add(x => x.Text, "Nothing to show"));

        Assert.Contains("Nothing to show", cut.Markup);
    }

    [Fact]
    public void Render_WithoutActionText_RendersNoButton()
    {
        ConfigureServices();

        var cut = Render<EmptyState>(p => p.Add(x => x.Text, "Nothing to show"));

        Assert.Empty(cut.FindAll("button"));
    }

    [Fact]
    public void Render_WithActionText_ClickInvokesCallback()
    {
        ConfigureServices();
        var invoked = false;

        var cut = Render<EmptyState>(p => p
            .Add(x => x.Text, "Nothing to show")
            .Add(x => x.ActionText, "Add one")
            .Add(x => x.OnAction, () => invoked = true));

        cut.Find("button").Click();

        Assert.True(invoked);
    }
}

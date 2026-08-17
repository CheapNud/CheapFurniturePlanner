using Bunit;
using CheapFurniturePlanner.Components.Shared;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;

namespace CheapFurniturePlanner.Tests.Components;

// UX-2 Task 2: the header every page adopts - kicker names the module, title names the action.
public class PageHeaderTests : TestContext
{
    private void ConfigureServices()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Render_WithoutKicker_HidesKicker()
    {
        ConfigureServices();

        var cut = Render<PageHeader>(p => p.Add(x => x.Title, "Materials"));

        Assert.DoesNotContain("mud-typography-overline", cut.Markup);
        Assert.Contains("Materials", cut.Markup);
    }

    [Fact]
    public void Render_WithKickerAndSubtitle_RendersAll()
    {
        ConfigureServices();

        var cut = Render<PageHeader>(p => p
            .Add(x => x.Kicker, "MATERIALS")
            .Add(x => x.Title, "Stock overview")
            .Add(x => x.Subtitle, "Frame, fabric and spray on hand"));

        Assert.Contains("mud-typography-overline", cut.Markup);
        Assert.Contains("MATERIALS", cut.Markup);
        Assert.Contains("Stock overview", cut.Markup);
        Assert.Contains("Frame, fabric and spray on hand", cut.Markup);
    }

    [Fact]
    public void Render_WithActions_RendersActionContent()
    {
        ConfigureServices();

        var cut = Render<PageHeader>(p => p
            .Add(x => x.Title, "Materials")
            .Add(x => x.Actions, "<button>New order</button>"));

        Assert.Contains("New order", cut.Markup);
    }
}

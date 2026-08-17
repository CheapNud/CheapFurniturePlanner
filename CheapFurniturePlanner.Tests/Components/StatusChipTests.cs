using Bunit;
using CheapFurniturePlanner.Components.Shared;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Xunit;

namespace CheapFurniturePlanner.Tests.Components;

// UX-2 Task 2: the render wrapper around StatusColors.For(...) callers - humanised label,
// Text variant, Small size, the caller's Color.
public class StatusChipTests : TestContext
{
    private void ConfigureServices()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Render_HumanisesValue()
    {
        ConfigureServices();

        var cut = Render<StatusChip>(p => p
            .Add(x => x.Color, Color.Primary)
            .Add(x => x.Value, "InProgress"));

        Assert.Contains("In progress", cut.Markup);
        Assert.DoesNotContain("InProgress", cut.Markup);
    }

    [Fact]
    public void Render_UsesTextVariant()
    {
        ConfigureServices();

        var cut = Render<StatusChip>(p => p
            .Add(x => x.Color, Color.Success)
            .Add(x => x.Value, "Delivered"));

        var chip = cut.FindComponent<MudChip<string>>();
        Assert.Equal(Variant.Text, chip.Instance.Variant);
        Assert.Equal(Size.Small, chip.Instance.Size);
        Assert.Equal(Color.Success, chip.Instance.Color);
    }
}

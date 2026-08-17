using Bunit;
using CheapFurniturePlanner.Components.Shared;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Xunit;

namespace CheapFurniturePlanner.Tests.Components;

// UX-2 Task 2: proves the three-part contract every awaited button conversion relies on -
// re-entry guard (second click mid-flight no-ops), the spinner swap while running, and the
// finally-reset firing even when the awaited action throws.
public class ProgressButtonTests : TestContext
{
    private void ConfigureServices()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task Click_WhilePending_SecondClickNoOps()
    {
        ConfigureServices();
        var tcs = new TaskCompletionSource();
        var callCount = 0;
        Func<Task> onClick = () =>
        {
            callCount++;
            return tcs.Task;
        };

        var cut = Render<ProgressButton>(p => p
            .Add(x => x.OnClick, onClick)
            .AddChildContent("Save"));

        var button = cut.Find("button");
        var pendingClick = cut.InvokeAsync(() => button.Click());
        cut.WaitForState(() => callCount == 1);

        // second click lands while the first is still in flight - the _running guard, not the
        // disabled attribute, is what's under test here.
        await cut.InvokeAsync(() => button.Click());
        Assert.Equal(1, callCount);

        tcs.SetResult();
        await pendingClick;
    }

    [Fact]
    public async Task Click_WhilePending_ShowsSpinnerInsteadOfStartIcon()
    {
        ConfigureServices();
        var tcs = new TaskCompletionSource();
        Func<Task> onClick = () => tcs.Task;

        var cut = Render<ProgressButton>(p => p
            .Add(x => x.OnClick, onClick)
            .Add(x => x.StartIcon, Icons.Material.Filled.Save)
            .AddChildContent("Save"));

        Assert.Empty(cut.FindComponents<MudProgressCircular>());

        var button = cut.Find("button");
        var pendingClick = cut.InvokeAsync(() => button.Click());
        cut.WaitForState(() => cut.FindComponents<MudProgressCircular>().Count == 1);

        Assert.True(cut.Find("button").HasAttribute("disabled"));

        // Color.Inherit, not the component default (Primary) - on a filled button (e.g. white-on-blue)
        // an un-inherited spinner renders gray instead of matching the button's foreground/background.
        Assert.Contains("mud-inherit-text", cut.Find(".mud-progress-circular").ClassList);

        tcs.SetResult();
        await pendingClick;

        cut.WaitForState(() => cut.FindComponents<MudProgressCircular>().Count == 0);
        Assert.False(cut.Find("button").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Click_WhenActionThrows_ResetsInFinally_ButtonClickableAgain()
    {
        ConfigureServices();
        var callCount = 0;
        Func<Task> onClick = () =>
        {
            callCount++;
            throw new InvalidOperationException("boom");
        };

        var cut = Render<ProgressButton>(p => p
            .Add(x => x.OnClick, onClick)
            .AddChildContent("Save"));

        var button = cut.Find("button");
        await Assert.ThrowsAsync<InvalidOperationException>(() => cut.InvokeAsync(() => button.Click()));

        Assert.Equal(1, callCount);
        Assert.False(cut.Find("button").HasAttribute("disabled"));

        // clickable again - a second invocation actually runs (and throws again), proving the
        // guard reset rather than latching into a permanently-disabled state.
        await Assert.ThrowsAsync<InvalidOperationException>(() => cut.InvokeAsync(() => button.Click()));
        Assert.Equal(2, callCount);
    }
}

using Bunit;
using Bunit.TestDoubles;
using CheapAvaloniaBlazor.Services;
using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Components.Layout;
using CheapFurniturePlanner.Services;
using CheapFurniturePlanner.Tests.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Xunit;

namespace CheapFurniturePlanner.Tests.Components;

// UX-2 final-review fix 2: MainLayout's Settings-drawer switches used @bind-Checked, which
// MudSwitch<bool> (a MudBooleanInput<T>) never declares - Blazor still compiles @bind-X on a
// component that doesn't have a matching X/XChanged parameter pair as long as the component
// captures unmatched attributes (MudComponentBase does), so "Checked"/"CheckedChanged" just land
// in the catch-all UserAttributes bag instead of wiring up. The switch renders and toggles its own
// internal state in the DOM, but nothing ever flows back to the bound field - so the Settings
// dialog's "Dark Mode" switch was dead while the separate appbar toggle button (ToggleDarkMode(),
// its own click handler, unrelated to this dialog) kept working.
public class MainLayoutTests : TestContext
{
    private BunitAuthorizationContext ConfigureAuth(FakeUpdateService? updates = null)
    {
        Services.AddMudServices();
        Services.AddSingleton<ICurrentUser>(new FakeCurrentUser("office-1", Roles.Office));
        Services.AddSingleton<IUpdateService>(updates ?? new FakeUpdateService());
        JSInterop.Mode = JSRuntimeMode.Loose;
        return this.AddAuthorization();
    }

    // The appbar's own theme-toggle icon is driven by the same _isDarkMode field the Settings
    // switch is supposed to bind - it's the only externally-observable proxy for that private field,
    // so flipping the Settings switch and checking THIS icon (not the switch's own rendered state)
    // is what actually proves the bind reaches MainLayout, not just the switch's internal Value.
    [Fact]
    public async Task SettingsDialog_DarkModeSwitch_UpdatesTheAppBarToggleIcon()
    {
        var auth = ConfigureAuth();
        auth.SetAuthorized("office-1");
        auth.SetRoles(Roles.Office);

        var cut = Render<MainLayout>(p => p.Add(x => x.Body, (RenderFragment)(builder => builder.AddContent(0, "content"))));

        Assert.Equal(Icons.Material.Filled.DarkMode, ThemeToggleIcon(cut));

        var settingsButton = cut.FindComponents<MudIconButton>().Single(b => Equals(b.Instance.Icon, Icons.Material.Filled.Settings));
        await cut.InvokeAsync(() => settingsButton.Find("button").Click());

        var darkModeSwitch = cut.FindComponents<MudSwitch<bool>>().First();
        await cut.InvokeAsync(() => darkModeSwitch.Instance.ValueChanged.InvokeAsync(true));

        Assert.Equal(Icons.Material.Filled.LightMode, ThemeToggleIcon(cut));
    }

    private static string ThemeToggleIcon(IRenderedComponent<MainLayout> cut) =>
        cut.FindComponents<MudIconButton>()
            .Single(b => Equals(b.Instance.Icon, Icons.Material.Filled.DarkMode) || Equals(b.Instance.Icon, Icons.Material.Filled.LightMode))
            .Instance.Icon!;

    // The chip is absent by default (dev-run / no-update shape) and appears once IUpdateService
    // reports an update via StateChanged; clicking it hands off to ApplyAndRestart.
    [Fact]
    public async Task UpdateReady_ShowsRestartChip_ClickAppliesUpdate()
    {
        var updates = new FakeUpdateService();
        var auth = ConfigureAuth(updates);
        auth.SetAuthorized("office-1");
        auth.SetRoles(Roles.Office);

        var cut = Render<MainLayout>(p => p.Add(x => x.Body, (RenderFragment)(builder => builder.AddContent(0, "content"))));

        Assert.DoesNotContain("Update ready", cut.Markup);

        await cut.InvokeAsync(() => updates.SetUpdateReady("1.2.3"));
        cut.WaitForAssertion(() => Assert.Contains("Update ready", cut.Markup));

        var restartButton = cut.FindComponents<MudButton>().Single(b => Equals(b.Instance.StartIcon, Icons.Material.Filled.SystemUpdate));
        await cut.InvokeAsync(() => restartButton.Find("button").Click());
        Assert.True(updates.ApplyAndRestartCalled);
    }
}

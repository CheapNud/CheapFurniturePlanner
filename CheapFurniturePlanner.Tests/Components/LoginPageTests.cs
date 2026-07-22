using Bunit;
using CheapFurniturePlanner.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;

namespace CheapFurniturePlanner.Tests.Components;

// Covers the failed-login feedback path: a bare redirect back to /login looks like a silent
// refresh (the user-reported half of this bug), so SignInForm appends a ?failed= reason and this
// page is what renders it.
public class LoginPageTests : TestContext
{
    private void ConfigureServices()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Render_WithFailedCredentials_ShowsCredentialsAlert()
    {
        ConfigureServices();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/login?failed=credentials");

        var cut = RenderComponent<Login>();

        Assert.Contains("Sign in failed. Check your username and password.", cut.Markup);
    }

    [Fact]
    public void Render_WithFailedLocked_ShowsLockedAlert()
    {
        ConfigureServices();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/login?failed=locked");

        var cut = RenderComponent<Login>();

        Assert.Contains("This account has been deactivated.", cut.Markup);
    }

    [Fact]
    public void Render_WithoutFailed_ShowsNoAlert()
    {
        ConfigureServices();

        var cut = RenderComponent<Login>();

        Assert.DoesNotContain("mud-alert", cut.Markup);
    }
}

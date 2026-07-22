using CheapFurniturePlanner.Controllers;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// Reproduces "No action descriptors found": CheapAvaloniaBlazor's HostBuilder stages services
// (Program.cs's builder.Services) before the real WebApplication builder exists, so
// AddControllers() runs with no IWebHostEnvironment registered yet. MVC's part discovery reads
// ApplicationName off that environment at AddControllers() time and, finding none, caches an
// empty ApplicationPartManager - which the real builder later inherits, so no controller (and no
// SignIn action) is ever discovered. AddApplicationPart sidesteps environment-based discovery
// entirely; that's the fix asserted below and mirrored in Program.cs.
public class ControllerDiscoveryTests
{
    [Fact]
    public void AddControllers_WithoutHostEnvironment_DiscoversNoActions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();

        using var provider = services.BuildServiceProvider();
        var descriptors = provider.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items;

        Assert.DoesNotContain(descriptors, d => d is ControllerActionDescriptor cad
            && cad.ControllerName == "Account"
            && cad.ActionName == "SignIn");
    }

    [Fact]
    public void AddControllers_WithApplicationPart_DiscoversAccountControllerActions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(AccountController).Assembly);

        using var provider = services.BuildServiceProvider();
        var actionNames = provider.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Where(d => d.ControllerName == "Account")
            .Select(d => d.ActionName)
            .ToList();

        Assert.Contains("SignIn", actionNames);
        Assert.Contains("SignOut", actionNames);
    }
}

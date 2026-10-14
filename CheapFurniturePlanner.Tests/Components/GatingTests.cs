using System.Reflection;
using Bunit;
using Bunit.TestDoubles;
using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Components.Layout;
using CheapFurniturePlanner.Components.Pages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;

namespace CheapFurniturePlanner.Tests.Components;

// Task 4: role gating across NavMenu and the page inventory's [Authorize] attributes.
// NavMenu rendering uses bUnit's built-in AddAuthorization() (Bunit.TestDoubles) for the principal
// (SetAuthorized/SetRoles build the role claims) and for the <CascadingAuthenticationState> it
// auto-adds. The authorization decision itself is NOT bUnit's fake: that one only knows policy
// names handed to it via SetPolicies, which would make a policy-gated nav test prove nothing about
// the roles. The real DefaultAuthorizationService is registered on top, fed the app's own
// Policies.Configure, so these tests exercise the exact role sets production evaluates.
public class GatingTests : TestContext
{
    private BunitAuthorizationContext ConfigureAuth()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
        var auth = this.AddAuthorization();
        // Plain AddSingleton (not TryAdd) on purpose: the last registration wins over bUnit's fakes.
        Services.AddLogging();
        Services.AddAuthorizationCore(Policies.Configure);
        Services.AddSingleton<IAuthorizationPolicyProvider, DefaultAuthorizationPolicyProvider>();
        Services.AddSingleton<IAuthorizationService, DefaultAuthorizationService>();
        return auth;
    }

    [Fact]
    public void Admin_SeesUsersAndBusinessLinksAndHomeLinks()
    {
        var auth = ConfigureAuth();
        auth.SetAuthorized("boss");
        auth.SetRoles(Roles.Admin);

        var cut = Render<NavMenu>();

        Assert.Contains("href=\"/users\"", cut.Markup);
        Assert.Contains("href=\"/firms\"", cut.Markup);
        Assert.Contains("href=\"/studio\"", cut.Markup);
        Assert.Contains("href=\"/studio/prices\"", cut.Markup);
        Assert.Contains("href=\"/studio/masters\"", cut.Markup);
        Assert.Contains("href=\"/studio/articles\"", cut.Markup);
        Assert.Contains("href=\"/orders\"", cut.Markup);
        Assert.Contains("href=\"/invoices\"", cut.Markup);
        Assert.Contains("href=\"/purchasing\"", cut.Markup);
        Assert.Contains("href=\"/parties\"", cut.Markup);
        Assert.Contains("href=\"/\"", cut.Markup);
        Assert.Contains("href=\"/room-plans\"", cut.Markup);
        Assert.Contains("href=\"/furniture/catalog\"", cut.Markup);
    }

    [Fact]
    public void Office_SeesBusinessAndHomeLinksButNotUsers()
    {
        var auth = ConfigureAuth();
        auth.SetAuthorized("clerk");
        auth.SetRoles(Roles.Office);

        var cut = Render<NavMenu>();

        Assert.Contains("href=\"/studio\"", cut.Markup);
        Assert.Contains("href=\"/orders\"", cut.Markup);
        Assert.Contains("href=\"/\"", cut.Markup);
        Assert.DoesNotContain("href=\"/users\"", cut.Markup);
        Assert.DoesNotContain("href=\"/firms\"", cut.Markup);
    }

    [Fact]
    public void Mechanic_SeesHomeLinksButNeitherBusinessNorUsers()
    {
        var auth = ConfigureAuth();
        auth.SetAuthorized("wrench");
        auth.SetRoles(Roles.Mechanic);

        var cut = Render<NavMenu>();

        Assert.Contains("href=\"/\"", cut.Markup);
        Assert.Contains("href=\"/room-plans\"", cut.Markup);
        Assert.DoesNotContain("href=\"/studio\"", cut.Markup);
        Assert.DoesNotContain("href=\"/orders\"", cut.Markup);
        Assert.DoesNotContain("href=\"/users\"", cut.Markup);
        Assert.DoesNotContain("href=\"/firms\"", cut.Markup);
    }

    [Fact]
    public void AnyAuthenticated_SeesServiceLink()
    {
        var auth = ConfigureAuth();
        auth.SetAuthorized("wrench");
        auth.SetRoles(Roles.Mechanic);

        var cut = Render<NavMenu>();

        Assert.Contains("href=\"/service\"", cut.Markup);
    }

    [Fact]
    public void WarehouseRole_SeesDockLinksButNotBusinessLinks()
    {
        var auth = ConfigureAuth();
        auth.SetAuthorized("dock");
        auth.SetRoles(Roles.Warehouse);

        var cut = Render<NavMenu>();

        Assert.Contains("href=\"/receiving\"", cut.Markup);
        Assert.Contains("href=\"/finishing\"", cut.Markup);
        Assert.Contains("href=\"/trips\"", cut.Markup);
        Assert.DoesNotContain("href=\"/orders\"", cut.Markup);
        Assert.DoesNotContain("href=\"/users\"", cut.Markup);
        Assert.DoesNotContain("href=\"/firms\"", cut.Markup);
    }

    [Fact]
    public void Mechanic_DoesNotSeeDockLinks()
    {
        var auth = ConfigureAuth();
        auth.SetAuthorized("wrench");
        auth.SetRoles(Roles.Mechanic);

        var cut = Render<NavMenu>();

        Assert.DoesNotContain("href=\"/receiving\"", cut.Markup);
        Assert.DoesNotContain("href=\"/finishing\"", cut.Markup);
    }

    [Fact]
    public void DockPages_RequireTheDockPolicy()
    {
        Assert.Equal(Policies.Dock, typeof(ReceivingPage).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal(Policies.Dock, typeof(FinishingPage).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal(Policies.Dock, typeof(TripsPage).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.Equal(Policies.Dock, typeof(TripPage).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
    }

    [Fact]
    public void ServicePages_CarryExpectedAuthorization()
    {
        Assert.Equal(Policies.BackOffice, typeof(ServiceIntakePage).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        AssertAnyAuthenticated(typeof(ServiceListPage).GetCustomAttribute<AuthorizeAttribute>()!);
        AssertAnyAuthenticated(typeof(ServiceTicketPage).GetCustomAttribute<AuthorizeAttribute>()!);
    }

    private static void AssertAnyAuthenticated(AuthorizeAttribute attribute)
    {
        Assert.Null(attribute.Policy);
        Assert.Null(attribute.Roles);
    }

    [Fact]
    public void Unauthenticated_SeesOnlyTheAnonymousSafeSubset()
    {
        // AddAuthorization() defaults to SetNotAuthorized().
        ConfigureAuth();

        var cut = Render<NavMenu>();

        Assert.DoesNotContain("href=\"/\"", cut.Markup);
        Assert.DoesNotContain("href=\"/room-plans\"", cut.Markup);
        Assert.DoesNotContain("href=\"/furniture/catalog\"", cut.Markup);
        Assert.DoesNotContain("href=\"/studio\"", cut.Markup);
        Assert.DoesNotContain("href=\"/users\"", cut.Markup);
        Assert.DoesNotContain("href=\"/firms\"", cut.Markup);

        // Help/About are unattributed pages (anonymous-accessible), so the nav still shows them.
        Assert.Contains("href=\"/help\"", cut.Markup);
        Assert.Contains("href=\"/about\"", cut.Markup);
    }

    // Cheap regression net: every page in the Task 4 gating matrix keeps carrying the exact
    // [Authorize] attribute it's supposed to, regardless of how the page's own markup evolves.
    public static TheoryData<Type> BusinessPages => new()
    {
        typeof(StudioPage),
        typeof(StudioNamingPage),
        typeof(StudioElementsPage),
        typeof(StudioElementOptionsPage),
        typeof(StudioElementBomPage),
        typeof(StudioArticlesPage),
        typeof(MastersPage),
        typeof(PriceVersionsPage),
        typeof(OrdersPage),
        typeof(OrderEntryPage),
        typeof(PartiesPage),
        typeof(SellerDiscountsPage),
        typeof(InvoicesPage),
        typeof(InvoicePage),
        typeof(PurchasingPage),
        typeof(PurchaseOrderPage),
        typeof(MaterialsPage),
        typeof(MaterialOrderPage),
    };

    [Theory]
    [MemberData(nameof(BusinessPages))]
    public void BusinessPage_RequiresTheBackOfficePolicy(Type pageType)
    {
        var attribute = pageType.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(Policies.BackOffice, attribute!.Policy);
        Assert.Null(attribute.Roles);
    }

    // The bootstrap admin gate is the one sanctioned direct role attribute (see Policies.cs).
    [Fact]
    public void UsersPage_RequiresTheAdminRoleDirectly()
    {
        var attribute = typeof(UsersPage).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(Roles.Admin, attribute!.Roles);
        Assert.Null(attribute.Policy);
    }

    [Fact]
    public void FirmsPage_RequiresTheFirmAdministrationPolicy()
    {
        var attribute = typeof(FirmsPage).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(Policies.FirmAdministration, attribute!.Policy);
        Assert.Null(attribute.Roles);
    }

    // Sweep: no page other than the bootstrap gate may gate on a role string again.
    [Fact]
    public void NoPageButUsersPage_GatesOnARoleAttribute()
    {
        var offenders = typeof(UsersPage).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(UsersPage).Namespace && t != typeof(UsersPage))
            .Where(t => t.GetCustomAttribute<AuthorizeAttribute>()?.Roles is not null)
            .Select(t => t.Name)
            .ToList();

        Assert.Empty(offenders);
    }

    public static TheoryData<Type> AnyAuthenticatedPages => new()
    {
        typeof(Home),
        typeof(PlannerPage),
        typeof(FurnitureCatalog),
        typeof(RoomPlans),
    };

    [Theory]
    [MemberData(nameof(AnyAuthenticatedPages))]
    public void Page_RequiresAnyAuthenticatedUser(Type pageType)
    {
        var attribute = pageType.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(attribute);
        AssertAnyAuthenticated(attribute!);
    }

    public static TheoryData<Type> AnonymousPages => new()
    {
        typeof(Login),
        typeof(SetupPage),
    };

    [Theory]
    [MemberData(nameof(AnonymousPages))]
    public void AnonymousPage_HasNoAuthorizeAttribute(Type pageType)
    {
        Assert.Null(pageType.GetCustomAttribute<AuthorizeAttribute>());
    }
}

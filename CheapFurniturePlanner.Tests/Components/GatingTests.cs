using System.Reflection;
using Bunit;
using Bunit.TestDoubles;
using CheapFurniturePlanner.Auth;
using CheapFurniturePlanner.Components.Layout;
using CheapFurniturePlanner.Components.Pages;
using Microsoft.AspNetCore.Authorization;
using MudBlazor.Services;
using Xunit;

namespace CheapFurniturePlanner.Tests.Components;

// Task 4: role gating across NavMenu and the page inventory's [Authorize] attributes.
// NavMenu rendering uses bUnit's built-in AddAuthorization() (Bunit.TestDoubles) instead of a
// hand-rolled AuthenticationStateProvider fake - it already wires a FakeAuthenticationStateProvider
// + FakeAuthorizationService that evaluate Roles="..." the same way the real IAuthorizationService
// does, and it auto-adds <CascadingAuthenticationState> to the render tree, which is what NavMenu's
// <AuthorizeView> elements need.
public class GatingTests : TestContext
{
    private BunitAuthorizationContext ConfigureAuth()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
        return this.AddAuthorization();
    }

    [Fact]
    public void Admin_SeesUsersAndBusinessLinksAndHomeLinks()
    {
        var auth = ConfigureAuth();
        auth.SetAuthorized("boss");
        auth.SetRoles(Roles.Admin);

        var cut = Render<NavMenu>();

        Assert.Contains("href=\"/users\"", cut.Markup);
        Assert.Contains("href=\"/studio\"", cut.Markup);
        Assert.Contains("href=\"/studio/prices\"", cut.Markup);
        Assert.Contains("href=\"/studio/masters\"", cut.Markup);
        Assert.Contains("href=\"/studio/articles\"", cut.Markup);
        Assert.Contains("href=\"/orders\"", cut.Markup);
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
        Assert.Contains("href=\"/trips\"", cut.Markup);
        Assert.DoesNotContain("href=\"/orders\"", cut.Markup);
        Assert.DoesNotContain("href=\"/users\"", cut.Markup);
    }

    [Fact]
    public void Mechanic_DoesNotSeeDockLinks()
    {
        var auth = ConfigureAuth();
        auth.SetAuthorized("wrench");
        auth.SetRoles(Roles.Mechanic);

        var cut = Render<NavMenu>();

        Assert.DoesNotContain("href=\"/receiving\"", cut.Markup);
    }

    [Fact]
    public void DockPages_RequireWarehouseStaff()
    {
        Assert.Equal(Roles.WarehouseStaff, typeof(ReceivingPage).GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.Equal(Roles.WarehouseStaff, typeof(TripsPage).GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.Equal(Roles.WarehouseStaff, typeof(TripPage).GetCustomAttribute<AuthorizeAttribute>()!.Roles);
    }

    [Fact]
    public void ServicePages_CarryExpectedAuthorization()
    {
        Assert.Equal(Roles.AdminOrOffice, typeof(ServiceIntakePage).GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.Null(typeof(ServiceListPage).GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.Null(typeof(ServiceTicketPage).GetCustomAttribute<AuthorizeAttribute>()!.Roles);
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
    };

    [Theory]
    [MemberData(nameof(BusinessPages))]
    public void BusinessPage_RequiresAdminOrOffice(Type pageType)
    {
        var attribute = pageType.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(Roles.AdminOrOffice, attribute!.Roles);
    }

    [Fact]
    public void UsersPage_RequiresAdmin()
    {
        var attribute = typeof(UsersPage).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(Roles.Admin, attribute!.Roles);
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
        Assert.Null(attribute!.Roles);
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

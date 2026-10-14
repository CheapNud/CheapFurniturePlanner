using CheapFurniturePlanner.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Xunit;

namespace CheapFurniturePlanner.Tests.Services;

// The policy names on the pages are only as good as the role sets registered behind them.
// Pin each one, so a policy can't quietly widen or lose a role.
public class PoliciesTests
{
    public static TheoryData<string, string[]> ExpectedRoleSets => new()
    {
        { Policies.BackOffice, [Roles.Admin, Roles.Office] },
        { Policies.Dock, [Roles.Admin, Roles.Office, Roles.Warehouse] },
        { Policies.FirmAdministration, [Roles.Admin] },
    };

    [Theory]
    [MemberData(nameof(ExpectedRoleSets))]
    public void Policy_RequiresExactlyTheseRoles(string policyName, string[] expectedRoles)
    {
        var options = new AuthorizationOptions();
        Policies.Configure(options);

        var policy = options.GetPolicy(policyName);

        Assert.NotNull(policy);
        var requirement = Assert.IsType<RolesAuthorizationRequirement>(Assert.Single(policy!.Requirements));
        Assert.Equal(expectedRoles, requirement.AllowedRoles);
    }
}

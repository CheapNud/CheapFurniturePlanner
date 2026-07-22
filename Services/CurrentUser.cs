using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace CheapFurniturePlanner.Services;

public interface ICurrentUser
{
    Task<string?> UserIdAsync();
    Task<string> DisplayNameAsync();
    Task<bool> IsInRoleAsync(string role);
}

// Thin claims read: the audit seam other services call to stamp "who did this" without taking a
// direct dependency on Blazor's AuthenticationStateProvider. GivenName/Surname claims aren't
// populated by anything yet (default Identity claims are just Name + role), so DisplayNameAsync
// falls back to the username claim - this is forward-compatible with a richer claims principal
// factory later, not dead code today.
public sealed class CurrentUser(AuthenticationStateProvider provider) : ICurrentUser
{
    public async Task<string?> UserIdAsync()
    {
        var user = await GetUserAsync();
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }

    public async Task<string> DisplayNameAsync()
    {
        var user = await GetUserAsync();
        var displayName = $"{user.FindFirst(ClaimTypes.GivenName)?.Value} {user.FindFirst(ClaimTypes.Surname)?.Value}".Trim();
        return displayName.Length == 0 ? user.Identity?.Name ?? string.Empty : displayName;
    }

    public async Task<bool> IsInRoleAsync(string role)
    {
        var user = await GetUserAsync();
        return user.IsInRole(role);
    }

    private async Task<ClaimsPrincipal> GetUserAsync() => (await provider.GetAuthenticationStateAsync()).User;
}

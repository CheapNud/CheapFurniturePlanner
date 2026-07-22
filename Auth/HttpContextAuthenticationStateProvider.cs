using System.Security.Claims;
using CheapFurniturePlanner.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CheapFurniturePlanner.Auth;

// Bridges the ASP.NET Core Identity cookie into the Blazor circuit. Sign-in and sign-out in this
// app both go through AccountController (an MVC action, not a client-side navigation), and a
// controller Redirect() always forces a full-page reload - so HttpContext.User at the moment this
// scoped service is constructed already reflects the current auth cookie. Captured once here
// (not read lazily from GetAuthenticationStateAsync) because IHttpContextAccessor.HttpContext goes
// null once the request that spun up the circuit completes.
//
// ponytail: that one-shot read used to be the whole story, on the assumption that every role/access
// change also forces a fresh circuit. It doesn't: UserAdminService.DeactivateAsync/SetRolesAsync/
// ResetPasswordAsync are live-revocation features that mutate the DB without touching the cookie, so
// an already-open circuit would otherwise keep acting on stale claims (including Admin) until the
// user reloads. So an authenticated circuit now also gets a background PeriodicTimer
// (AuthDefaults.RevalidationInterval, 2 min) that re-checks the principal against the DB and drops
// the circuit to anonymous on mismatch - AuthorizeRouteView then bounces it to /login.
//
// _currentState is mutable (not just notified) so a direct caller of GetAuthenticationStateAsync -
// e.g. Services/CurrentUser.cs, the audit seam other services stamp "who did this" through - also
// observes the drop to anonymous, instead of only components subscribed to
// AuthenticationStateChanged seeing it. It's volatile because the revalidation timer writes it from
// a background loop while a circuit's render thread can read it concurrently.
//
// Each tick's DB round trip runs inside its own try/catch: a transient DB blip must not fault the
// loop task and silently end revalidation for the rest of the circuit's life. Failures are logged
// and the loop just waits for the next tick; only disposal (OperationCanceledException) ends it.
public sealed class HttpContextAuthenticationStateProvider : AuthenticationStateProvider, IAsyncDisposable
{
    private const string SecurityStampClaimType = "AspNet.Identity.SecurityStamp";

    private readonly IDbContextFactory<FurniturePlannerContext> _dbFactory;
    private readonly ILogger<HttpContextAuthenticationStateProvider> _logger;
    private volatile Task<AuthenticationState> _currentState;
    private readonly PeriodicTimer? _timer;
    private readonly Task? _revalidationLoop;

    public HttpContextAuthenticationStateProvider(
        IHttpContextAccessor httpContextAccessor,
        IDbContextFactory<FurniturePlannerContext> dbFactory,
        ILogger<HttpContextAuthenticationStateProvider> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        var user = httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());
        _currentState = Task.FromResult(new AuthenticationState(user));

        if (user.Identity?.IsAuthenticated == true)
        {
            _timer = new PeriodicTimer(AuthDefaults.RevalidationInterval);
            _revalidationLoop = RevalidateLoopAsync(user, _timer);
        }
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => _currentState;

    // Extracted for direct testing (AuthRevalidationTests) - swaps in the anonymous state before
    // notifying, so a direct GetAuthenticationStateAsync caller reflects the revocation right away
    // rather than only components that subscribed to the change notification.
    public void Invalidate()
    {
        _currentState = Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
        NotifyAuthenticationStateChanged(_currentState);
    }

    private async Task RevalidateLoopAsync(ClaimsPrincipal user, PeriodicTimer timer)
    {
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                if (!await ValidateAsync(user, db))
                {
                    Invalidate();
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                // Disposal in progress - let the while condition above end the loop, don't log this as a failure.
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Auth revalidation tick failed; will retry on the next interval.");
            }
        }
    }

    // Extracted for direct testing (AuthRevalidationTests) - this is the part the timer loop above
    // calls on every tick, no timer involved when called directly. A stale circuit fails validation
    // if: its user was deleted; the user is now deactivated (lockout-based, see UserAdminService);
    // its security stamp claim - rotated by ResetPasswordAsync - no longer matches the DB (when the
    // cookie carries that claim, which the default Identity claims factory always adds here); or its
    // role claims no longer match the DB (SetRolesAsync doesn't rotate the stamp, so it needs its own
    // check regardless of stamp presence).
    public static async Task<bool> ValidateAsync(ClaimsPrincipal principal, FurniturePlannerContext db)
    {
        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) { return false; }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) { return false; }
        if (user.LockoutEnabled && user.LockoutEnd == DateTimeOffset.MaxValue) { return false; }

        var stampClaim = principal.FindFirst(SecurityStampClaimType);
        if (stampClaim is not null && stampClaim.Value != user.SecurityStamp) { return false; }

        var dbRoles = await db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
            .ToListAsync();
        var principalRoles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value);
        return dbRoles.OrderBy(r => r, StringComparer.Ordinal).SequenceEqual(principalRoles.OrderBy(r => r, StringComparer.Ordinal));
    }

    public async ValueTask DisposeAsync()
    {
        _timer?.Dispose();
        if (_revalidationLoop is not null)
        {
            await _revalidationLoop;
        }
    }
}

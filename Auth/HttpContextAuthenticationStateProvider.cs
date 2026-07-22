using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;

namespace CheapFurniturePlanner.Auth;

// Bridges the ASP.NET Core Identity cookie into the Blazor circuit. Sign-in and sign-out in this
// app both go through AccountController (an MVC action, not a client-side navigation), and a
// controller Redirect() always forces a full-page reload - so HttpContext.User at the moment this
// scoped service is constructed already reflects the current auth cookie. Captured once here
// (not read lazily from GetAuthenticationStateAsync) because IHttpContextAccessor.HttpContext goes
// null once the request that spun up the circuit completes.
// ponytail: no revalidation loop (RevalidatingServerAuthenticationStateProvider) - single-user
// desktop app, short-lived circuits, and every sign-in/out already forces a fresh circuit via the
// controller redirect. Add one if this ever needs mid-session role changes without a reload.
public sealed class HttpContextAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly Task<AuthenticationState> _state;

    public HttpContextAuthenticationStateProvider(IHttpContextAccessor httpContextAccessor)
    {
        var user = httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());
        _state = Task.FromResult(new AuthenticationState(user));
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => _state;
}

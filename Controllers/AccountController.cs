using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapHelpers.Blazor.Pages.Account;
using CheapHelpers.Blazor.Services;
using CheapHelpers.Services.Email;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Text.Encodings.Web;

namespace CheapFurniturePlanner.Controllers;

// Thin subclass: CheapAccountController has no [Route] attribute by design (consumers own
// routing). This is the only piece needed to reach /Account/SignIn, /Account/SignOut, etc. -
// all sign-in logic lives in the base class via SignInManager (an MVC controller action, not
// a Blazor circuit, so SignInManager is fine here).
[Route("Account/[action]")]
public class AccountController(
    SignInManager<FurnitureUser> signInManager,
    IEmailService mailer,
    UserManager<FurnitureUser> userManager,
    UserService<FurnitureUser, FurniturePlannerContext> userService,
    UrlEncoder urlEncoder,
    AccountRouteOptions routeOptions)
    : CheapAccountController<FurnitureUser, FurniturePlannerContext>(signInManager, mailer, userManager, userService, urlEncoder, routeOptions)
{
    // The base SignIn redirects back bare on failure (and is not virtual) - this action is what
    // the planner's login form posts to: same semantics, but failures come back with a reason the
    // login page can show instead of looking like a silent refresh. [AllowAnonymous] is required
    // here (the class carries [Authorize], same as base SignIn) - without it an anonymous POST
    // never reaches PasswordSignInAsync at all, it gets redirected to /login bare, which is the
    // exact bug this action exists to fix.
    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> SignInForm(IFormCollection fc)
    {
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        var result = await signInManager.PasswordSignInAsync(fc["UserName"].ToString(), fc["Password"].ToString(), isPersistent: true, lockoutOnFailure: false);
        if (result.Succeeded) { return Redirect(routeOptions.HomeRoute); }
        return Redirect(result.IsLockedOut ? "/login?failed=locked" : "/login?failed=credentials");
    }
}

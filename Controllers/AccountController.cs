using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Models;
using CheapHelpers.Blazor.Pages.Account;
using CheapHelpers.Blazor.Services;
using CheapHelpers.Services.Email;
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
    : CheapAccountController<FurnitureUser, FurniturePlannerContext>(signInManager, mailer, userManager, userService, urlEncoder, routeOptions);

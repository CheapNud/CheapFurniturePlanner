namespace CheapFurniturePlanner.Auth;

public static class AuthDefaults
{
    public const int MinPasswordLength = 6;

    // How often an open circuit re-checks its principal against the DB (see
    // HttpContextAuthenticationStateProvider) so a deactivation/role change/password reset cuts
    // a live session instead of waiting for the user to reload.
    public static readonly TimeSpan RevalidationInterval = TimeSpan.FromMinutes(2);
}

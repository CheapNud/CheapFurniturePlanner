using CheapFurniturePlanner.Services;

namespace CheapFurniturePlanner.Tests.Services;

// Deterministic ICurrentUser for service tests (and bUnit pages): fixed id + role set.
// Task.FromResult matches the TestDbContextFactory precedent for trivially-sync fakes.
public sealed class FakeCurrentUser(string? userId, params string[] roles) : ICurrentUser
{
    public Task<string?> UserIdAsync() => Task.FromResult(userId);
    public Task<string> DisplayNameAsync() => Task.FromResult(userId ?? "anonymous");
    public Task<bool> IsInRoleAsync(string role) => Task.FromResult(roles.Contains(role));
}

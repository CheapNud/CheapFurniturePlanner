using Bunit;
using Xunit;

namespace CheapFurniturePlanner.Tests.Components;

// bunit 2.x made BunitContext the base type and its service provider now holds MudBlazor
// services that only implement IAsyncDisposable, so xunit's synchronous test-class Dispose
// blows up mid-teardown. xunit v2 only honors IAsyncLifetime for async teardown, so this base
// routes disposal through DisposeAsync. Deliberately named TestContext: the existing test
// classes keep compiling unchanged because a same-namespace type beats bunit's obsolete
// Bunit.TestContext shim during resolution.
public abstract class TestContext : BunitContext, IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync() => await DisposeAsync();
}

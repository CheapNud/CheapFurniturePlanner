using CheapAvaloniaBlazor.Services;

namespace CheapFurniturePlanner.Tests.Services;

// IUpdateService is always registered by CheapAvaloniaBlazor's DI (unconditionally, regardless
// of WithVelopackUpdates), so every bUnit render of MainLayout needs one in the container -
// this fake defaults to "no update available" (dev-run shape) and lets a test flip UpdateReady
// on demand to exercise the chip.
public sealed class FakeUpdateService : IUpdateService
{
    public bool UpdateReady { get; private set; }
    public string? PendingVersion { get; private set; }
    public bool ApplyAndRestartCalled { get; private set; }

    public event Action? StateChanged;

    public Task CheckAndDownloadAsync() => Task.CompletedTask;

    public void ApplyAndRestart() => ApplyAndRestartCalled = true;

    public void SetUpdateReady(string version)
    {
        UpdateReady = true;
        PendingVersion = version;
        StateChanged?.Invoke();
    }
}

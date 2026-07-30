using Xunit;

namespace CheapFurniturePlanner.Tests.Components;

// UX-1 Task 1: every routable page must declare a <PageTitle> - the audit found Login silently
// missing one, which is what this fact catches. The long-form check (every title suffixed
// " - Cheap Furniture Planner</PageTitle>") is a SEPARATE fact added in Task 6 once all pages
// have been converted to that long form; asserting it here would fail from day one, so it is
// deliberately split out rather than skipped.
public class UiConventionsTests
{
    [Fact]
    public void EveryRoutablePage_DeclaresAPageTitle()
    {
        var pagesDir = Path.Combine(FindRepoRoot(), "Components", "Pages");
        var failures = new List<string>();
        foreach (var pagePath in Directory.EnumerateFiles(pagesDir, "*.razor"))
        {
            var pageSource = File.ReadAllText(pagePath);
            if (!pageSource.Contains("@page")) { continue; }
            if (!pageSource.Contains("<PageTitle>")) { failures.Add($"{Path.GetFileName(pagePath)}: missing PageTitle"); }
        }
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CheapFurniturePlanner.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate repo root (CheapFurniturePlanner.sln) above the test assembly directory.");
    }
}

using Xunit;

namespace CheapFurniturePlanner.Tests.Components;

// UX-1 Task 1: every routable page must declare a <PageTitle> - the audit found Login silently
// missing one, which is what this fact catches. A SEPARATE fact (added in Task 6, once every page
// had actually converged on the short form) asserts the opposite direction: no page carries the
// long " - Cheap Furniture Planner" suffix any more. Split out rather than combined so Task 1
// could ship the missing-title check on day one, before the app-wide short-form convergence was
// even finished.
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

    [Fact]
    public void NoRoutablePage_UsesTheLongFormPageTitleSuffix()
    {
        var pagesDir = Path.Combine(FindRepoRoot(), "Components", "Pages");
        var failures = new List<string>();
        foreach (var pagePath in Directory.EnumerateFiles(pagesDir, "*.razor"))
        {
            var pageSource = File.ReadAllText(pagePath);
            if (!pageSource.Contains("@page")) { continue; }
            if (pageSource.Contains(" - Cheap Furniture Planner</PageTitle>")) { failures.Add($"{Path.GetFileName(pagePath)}: still uses the long-form PageTitle suffix"); }
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

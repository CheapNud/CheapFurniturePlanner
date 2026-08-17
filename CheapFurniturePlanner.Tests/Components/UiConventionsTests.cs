using Xunit;

namespace CheapFurniturePlanner.Tests.Components;

// UX-2 Task 7: the conventions suite flips from the UX-1-era "PageTitle only" checks to the full
// house-style canon every routed page now follows (Tasks 4-6 + the planner-pages rider swept every
// page onto it). Each rule below is a grep heuristic, not a parser - it is meant to catch the next
// regression (a new page skipping PageHeader, a raw status MudChip creeping back in), not to be a
// style linter. Where the heuristic collides with a page that was reviewed and deliberately kept
// off-canon (a judgment page, an item-card grid, a pre-existing out-of-scope shared component), the
// exception is pinned in an explicit allowlist right next to the rule, not silently special-cased.
//
// NAMING RULE (recorded here per the brief, NOT enforced retroactively - existing page names predate
// it): any NEW routed page added from this point on follows *List / *Create / *Edit / *Details
// suffixes matching the page's role (e.g. a new "FooList.razor" for a list page, "FooEdit.razor" for
// a single-record editor). Existing pages (FirmsPage, OrdersPage, PurchaseOrderPage, ...) predate the
// convention and are not renamed by this task.
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

    // Judgment pages that deliberately have no PageHeader at all - both documented inline (in the
    // page itself) as well as here. Login/Setup are pre-authentication, single-purpose screens; a
    // module kicker would name a section of the app the visitor can't reach yet (see task-6-report.md
    // "Login"/"SetupPage" notes).
    private static readonly HashSet<string> PagesWithoutPageHeader = new(StringComparer.OrdinalIgnoreCase)
    {
        "Login.razor",
        "SetupPage.razor",
    };

    [Fact]
    public void EveryRoutablePage_RendersAPageHeader_ExceptTheDocumentedJudgmentPages()
    {
        var pagesDir = Path.Combine(FindRepoRoot(), "Components", "Pages");
        var failures = new List<string>();
        foreach (var pagePath in Directory.EnumerateFiles(pagesDir, "*.razor"))
        {
            var fileName = Path.GetFileName(pagePath);
            if (PagesWithoutPageHeader.Contains(fileName)) { continue; }
            var pageSource = File.ReadAllText(pagePath);
            if (!pageSource.Contains("@page")) { continue; }
            if (!pageSource.Contains("<PageHeader")) { failures.Add($"{fileName}: missing PageHeader"); }
        }
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    // Files pre-existing item-card / stat-tile grids and out-of-scope planner-canvas components keep
    // Elevation="1"/"2" on their per-item MudCards deliberately - that's a content GRID's item card,
    // not the page's own content card (the pa-6/Elevation=0 wrapper the anatomy targets). Every one of
    // these was reviewed and left untouched across Tasks 4-6 (see their per-page notes: Home's
    // stat/dashboard cards, About/Help's feature tiles, FurnitureCatalog's stat tiles + furniture-card
    // grid, RoomPlans' grid-view room-plan cards, FurnitureConfigPanel - the planner side panel, out of
    // the pages-only sweep boundary entirely). Any Elevation="[1-9]" outside this allowlist is new and
    // must be flattened or reviewed.
    private static readonly HashSet<string> FilesWithAllowedElevatedCards = new(StringComparer.OrdinalIgnoreCase)
    {
        Path.Combine("Pages", "About.razor"),
        Path.Combine("Pages", "FurnitureCatalog.razor"),
        Path.Combine("Pages", "Help.razor"),
        Path.Combine("Pages", "Home.razor"),
        Path.Combine("Pages", "RoomPlans.razor"),
        Path.Combine("Shared", "FurnitureConfigPanel.razor"),
    };

    private static readonly System.Text.RegularExpressions.Regex ElevatedPattern =
        new(@"Elevation=""[1-9]", System.Text.RegularExpressions.RegexOptions.Compiled);

    [Fact]
    public void NoComponent_UsesAnElevatedCard_OutsideTheAllowlistedItemCardGrids()
    {
        var componentsDir = Path.Combine(FindRepoRoot(), "Components");
        var failures = new List<string>();
        foreach (var path in Directory.EnumerateFiles(componentsDir, "*.razor", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(componentsDir, path);
            if (FilesWithAllowedElevatedCards.Contains(relative)) { continue; }
            var source = File.ReadAllText(path);
            if (ElevatedPattern.IsMatch(source)) { failures.Add($"{relative}: has an Elevation=\"[1-9]\" card outside the allowlist"); }
        }
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    // A raw MudChip paired with a StatusColors.* call on the same line IS a status chip rendered
    // outside StatusChip - the one shared home for "color + humanized label" (see StatusChip.razor).
    // Widened (final-review fix 6) from a Pages-only walk to all of Components/: the original
    // Pages-only scoping let MaterialProfileDialog.razor's raw "Preferred" chip through as a
    // documented, known non-page instance (see task-7-report.md) - now converted to StatusChip and
    // caught the same as a page-level offender would be. No allowlist entries exist today: both
    // known offenders (OrderEntryPage's per-unit "seq: state" chip, MaterialProfileDialog's
    // "Preferred" chip) were converted to StatusChip rather than exempted. A plain informational
    // MudChip that never touches StatusColors (e.g. a count badge) is unaffected by this rule by
    // construction - it never matches the same-line pairing the regex looks for.
    private static readonly System.Text.RegularExpressions.Regex RawStatusChipPattern =
        new(@"MudChip.*StatusColors\.", System.Text.RegularExpressions.RegexOptions.Compiled);

    [Fact]
    public void NoComponent_RendersARawStatusMudChip_OutsideStatusChip()
    {
        var componentsDir = Path.Combine(FindRepoRoot(), "Components");
        var failures = new List<string>();
        foreach (var path in Directory.EnumerateFiles(componentsDir, "*.razor", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(componentsDir, path);
            foreach (var line in File.ReadLines(path))
            {
                if (RawStatusChipPattern.IsMatch(line))
                {
                    failures.Add($"{relative}: raw MudChip+StatusColors - use StatusChip instead");
                }
            }
        }
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    // The theme (AppTheme.cs) owns every color token and the two @font-face families - a page reaching
    // for a literal hex color or font-family string is bypassing it. Three files keep pre-existing
    // literals, all outside this phase's pages-only sweep boundary: FurnitureCatalog's placeholder tile
    // background, and the planner canvas pair (FurnitureConfigPanel/FurniturePlannerContainer -
    // documented across Tasks 4-6 as "out of the pages-only sweep boundary", the grid/measurement/
    // fabric-swatch rendering predates the house style entirely). furniture-planner.css/site.css are
    // out of scope for this rule by construction - it only walks .razor files.
    private static readonly HashSet<string> FilesWithAllowedLiteralColors = new(StringComparer.OrdinalIgnoreCase)
    {
        Path.Combine("Pages", "FurnitureCatalog.razor"),
        Path.Combine("Shared", "FurnitureConfigPanel.razor"),
        Path.Combine("Shared", "FurniturePlannerContainer.razor"),
    };

    private static readonly System.Text.RegularExpressions.Regex HexColorPattern =
        new(@"#[0-9A-Fa-f]{3,8}\b", System.Text.RegularExpressions.RegexOptions.Compiled);

    [Fact]
    public void NoComponent_UsesALiteralFontFamilyOrHexColor_OutsideTheAllowlist()
    {
        var componentsDir = Path.Combine(FindRepoRoot(), "Components");
        var failures = new List<string>();
        foreach (var path in Directory.EnumerateFiles(componentsDir, "*.razor", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(componentsDir, path);
            if (FilesWithAllowedLiteralColors.Contains(relative)) { continue; }
            var source = File.ReadAllText(path);
            if (source.Contains("font-family:")) { failures.Add($"{relative}: literal font-family:"); }
            if (HexColorPattern.IsMatch(source)) { failures.Add($"{relative}: literal hex color"); }
        }
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    // At most one Variant="Variant.Filled" button per page - the anatomy's "one filled CTA per view"
    // rule (everything else is Text/icon). PlannerPage is the one decided-and-pinned exception: its
    // two literal Variant.Filled buttons live in mutually exclusive branches of the same @if/else
    // chain (the in-page room-settings dialog's "Save Settings", and the "Room Plan Not Found"
    // error-state's "Back to Room Plans") - never rendered together, so it isn't a second competing CTA
    // in any single render. This heuristic only counts literal `Variant="Variant.Filled"` text on the
    // page itself; ProgressButton's own default-filled styling (used by every awaited primary action
    // across the sweep) lives inside ProgressButton.razor and isn't literal page markup, so it is
    // correctly invisible to this grep.
    private static readonly Dictionary<string, int> PagesWithAllowedExtraFilledButtons =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["PlannerPage.razor"] = 2,
        };

    [Fact]
    public void EveryRoutablePage_HasAtMostOneFilledButton_ExceptTheDocumentedAllowlist()
    {
        var pagesDir = Path.Combine(FindRepoRoot(), "Components", "Pages");
        var failures = new List<string>();
        foreach (var pagePath in Directory.EnumerateFiles(pagesDir, "*.razor"))
        {
            var fileName = Path.GetFileName(pagePath);
            var source = File.ReadAllText(pagePath);
            var count = System.Text.RegularExpressions.Regex.Matches(source, @"Variant\.Filled").Count;
            var allowed = PagesWithAllowedExtraFilledButtons.GetValueOrDefault(fileName, 1);
            if (count > allowed) { failures.Add($"{fileName}: {count} Variant.Filled buttons (allowed {allowed})"); }
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

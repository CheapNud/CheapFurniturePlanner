using MudBlazor;

namespace CheapFurniturePlanner.Ui;

// UX-2 Task 1: the ONLY home of design tokens (radius, drawer width, palette hairlines, opacity
// ladder, typography). Custom CSS references the --mud-palette-*/--mud-typography-* variables
// this theme emits instead of literal colors or font-family strings - AppTheme.cs and the
// @font-face block in wwwroot/css/site.css are the two places allowed to name a literal color
// or font family.
//
// Fonts are aliased rather than referenced directly: 'AppDisplay' (Sora, headings H4-H6) and
// 'AppBody' (Inter, everything else) are generic family names registered once via @font-face.
// Trying a different pair later means swapping the font files under wwwroot/fonts/ - no theme,
// CSS, or page edit required. Never more than two families.
public static class AppTheme
{
    public static readonly MudTheme Theme = new()
    {
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "12px",
            DrawerWidthLeft = "260px",
        },

        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = ["AppBody", "sans-serif"] },
            H4 = new H4Typography { FontFamily = ["AppDisplay", "sans-serif"] },
            H5 = new H5Typography { FontFamily = ["AppDisplay", "sans-serif"] },
            H6 = new H6Typography { FontFamily = ["AppDisplay", "sans-serif"] },
        },

        // Palette hues (Primary/Secondary/AppbarBackground) are unchanged - this app keeps its
        // own colors (UX-2 scope: structure/chrome, not a palette redesign). What's new here is
        // the light/dark field-vs-card contrast, the explicit hairlines, and pinning the drawer
        // to the same dark chrome in both modes.
        PaletteLight = new PaletteLight
        {
            Primary = "#1976d2",
            Secondary = "#424242",
            AppbarBackground = "#1976d2",
            Background = "#F5F6F8", // off-white field
            Surface = "#FFFFFF", // white cards
            LinesDefault = "rgba(0,0,0,0.12)",
            TableLines = "rgba(0,0,0,0.10)",
            Divider = "rgba(0,0,0,0.12)",
            // Drawer stays dark chrome in both modes - matches PaletteDark's drawer below.
            DrawerBackground = "#1e1e1e",
            DrawerText = "rgba(255,255,255,0.70)",
            DrawerIcon = "rgba(255,255,255,0.50)",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#90caf9",
            Secondary = "#f48fb1",
            AppbarBackground = "#1565c0",
            Background = "#121212", // field
            Surface = "#1e1e1e", // cards, lighter than the field
            TextPrimary = "rgba(255,255,255,0.92)",
            TextSecondary = "rgba(255,255,255,0.62)",
            LinesDefault = "rgba(255,255,255,0.12)",
            TableLines = "rgba(255,255,255,0.10)",
            Divider = "rgba(255,255,255,0.12)",
            DrawerBackground = "#1e1e1e",
            DrawerText = "rgba(255,255,255,0.70)",
            DrawerIcon = "rgba(255,255,255,0.50)",
        },
    };
}

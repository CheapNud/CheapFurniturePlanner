using CheapFurniturePlanner.Ui;
using MudBlazor;
using MudBlazor.Utilities;
using Xunit;

namespace CheapFurniturePlanner.Tests.Ui;

// UX-2 Task 1: token facts for the one place design tokens live. Pins the exact values so a
// future edit to AppTheme.cs has to change these tests deliberately, not by accident.
public class AppThemeTests
{
    [Fact]
    public void DefaultBorderRadius_Is12Px() =>
        Assert.Equal("12px", AppTheme.Theme.LayoutProperties.DefaultBorderRadius);

    [Fact]
    public void DrawerWidthLeft_Is260Px() =>
        Assert.Equal("260px", AppTheme.Theme.LayoutProperties.DrawerWidthLeft);

    [Fact]
    public void DefaultTypography_UsesAppBodyFamily() =>
        Assert.Equal("AppBody", AppTheme.Theme.Typography.Default.FontFamily?[0]);

    [Theory]
    [InlineData(0)] // H4
    [InlineData(1)] // H5
    [InlineData(2)] // H6
    public void HeadingTypography_UsesAppDisplayFamily(int index)
    {
        var heading = index switch
        {
            0 => AppTheme.Theme.Typography.H4,
            1 => AppTheme.Theme.Typography.H5,
            _ => AppTheme.Theme.Typography.H6,
        };
        Assert.Equal("AppDisplay", heading.FontFamily?[0]);
    }

    // MudColor stores alpha as a byte (0-255), so comparing against the literal it was built
    // from - rather than an exact "0.92"-style ToString() - survives that internal truncation
    // (0.92 * 255 truncates to 234, which stringifies back as 0.9176..., not 0.92) while still
    // pinning the semantic token value.
    [Fact]
    public void DarkPalette_TextOpacityLadder_MatchesSpec()
    {
        var dark = AppTheme.Theme.PaletteDark;
        Assert.Equal(new MudColor("rgba(255,255,255,0.92)"), dark.TextPrimary);
        Assert.Equal(new MudColor("rgba(255,255,255,0.62)"), dark.TextSecondary);
        Assert.Equal(new MudColor("rgba(255,255,255,0.12)"), dark.LinesDefault);
        Assert.Equal(new MudColor("rgba(255,255,255,0.10)"), dark.TableLines);
    }

    [Fact]
    public void BothPalettes_SetHairlinesExplicitly()
    {
        var light = AppTheme.Theme.PaletteLight;
        var dark = AppTheme.Theme.PaletteDark;

        Assert.Equal(new MudColor("rgba(0,0,0,0.12)"), light.LinesDefault);
        Assert.Equal(new MudColor("rgba(0,0,0,0.10)"), light.TableLines);
        Assert.Equal(new MudColor("rgba(0,0,0,0.12)"), light.Divider);

        Assert.Equal(new MudColor("rgba(255,255,255,0.12)"), dark.LinesDefault);
        Assert.Equal(new MudColor("rgba(255,255,255,0.10)"), dark.TableLines);
        Assert.Equal(new MudColor("rgba(255,255,255,0.12)"), dark.Divider);
    }

    [Fact]
    public void LightMode_HasWhiteCardsOnOffWhiteField()
    {
        var light = AppTheme.Theme.PaletteLight;
        Assert.Equal("#F5F6F8", light.Background.ToString(MudColorOutputFormats.Hex).ToUpperInvariant());
        Assert.Equal("#FFFFFF", light.Surface.ToString(MudColorOutputFormats.Hex).ToUpperInvariant());
    }

    [Fact]
    public void Drawer_IsPinnedDarkInBothModes()
    {
        var light = AppTheme.Theme.PaletteLight;
        var dark = AppTheme.Theme.PaletteDark;

        Assert.Equal(dark.DrawerBackground, light.DrawerBackground);
        Assert.Equal(dark.DrawerText, light.DrawerText);
        Assert.Equal(dark.DrawerIcon, light.DrawerIcon);
        Assert.Equal(new MudColor("#1e1e1e"), light.DrawerBackground);
    }
}

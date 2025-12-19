using FluentAssertions;
using WayfarerMobile.Core.Helpers;

namespace WayfarerMobile.Tests.Unit.Helpers;

/// <summary>
/// Unit tests for IconCatalog class.
/// Tests marker icon and color normalization (dynamic loading, no validation).
/// </summary>
public class IconCatalogTests
{
    #region Constants Tests

    [Fact]
    public void DefaultColor_IsBgBlue()
    {
        IconCatalog.DefaultColor.Should().Be("bg-blue");
    }

    [Fact]
    public void DefaultIcon_IsMarker()
    {
        IconCatalog.DefaultIcon.Should().Be("marker");
    }

    #endregion

    #region PriorityIconNames Tests

    [Fact]
    public void PriorityIconNames_ContainsMarker()
    {
        IconCatalog.PriorityIconNames.Should().Contain("marker");
    }

    [Fact]
    public void PriorityIconNames_HasReasonableCount()
    {
        // Should be a manageable subset for pickers
        IconCatalog.PriorityIconNames.Should().HaveCountGreaterThan(5);
        IconCatalog.PriorityIconNames.Should().HaveCountLessThan(30);
    }

    [Fact]
    public void PriorityIconNames_ContainsCommonIcons()
    {
        IconCatalog.PriorityIconNames.Should().Contain("star");
        IconCatalog.PriorityIconNames.Should().Contain("camera");
        IconCatalog.PriorityIconNames.Should().Contain("hotel");
    }

    #endregion

    #region CoerceColor Tests

    [Fact]
    public void CoerceColor_ValidColorWithPrefix_ReturnsAsIs()
    {
        var result = IconCatalog.CoerceColor("bg-red");
        result.Should().Be("bg-red");
    }

    [Fact]
    public void CoerceColor_ColorWithoutPrefix_AddsPrefix()
    {
        var result = IconCatalog.CoerceColor("blue");
        result.Should().Be("bg-blue");
    }

    [Fact]
    public void CoerceColor_UnknownColor_ReturnsWithPrefix()
    {
        // Dynamic: unknown colors are accepted, just normalized
        var result = IconCatalog.CoerceColor("orange");
        result.Should().Be("bg-orange");
    }

    [Fact]
    public void CoerceColor_UnknownColorWithPrefix_ReturnsAsIs()
    {
        // Dynamic: unknown colors are accepted
        var result = IconCatalog.CoerceColor("bg-orange");
        result.Should().Be("bg-orange");
    }

    [Fact]
    public void CoerceColor_NullColor_ReturnsFallback()
    {
        var result = IconCatalog.CoerceColor(null);
        result.Should().Be("bg-blue");
    }

    [Fact]
    public void CoerceColor_EmptyString_ReturnsFallback()
    {
        var result = IconCatalog.CoerceColor("");
        result.Should().Be("bg-blue");
    }

    [Fact]
    public void CoerceColor_WhitespaceString_ReturnsFallback()
    {
        var result = IconCatalog.CoerceColor("   ");
        result.Should().Be("bg-blue");
    }

    [Fact]
    public void CoerceColor_CustomFallback_ReturnsCustomFallback()
    {
        var result = IconCatalog.CoerceColor(null, "bg-red");
        result.Should().Be("bg-red");
    }

    [Theory]
    [InlineData("black", "bg-black")]
    [InlineData("blue", "bg-blue")]
    [InlineData("green", "bg-green")]
    [InlineData("purple", "bg-purple")]
    [InlineData("red", "bg-red")]
    public void CoerceColor_KnownColorsWithoutPrefix_AddPrefix(string input, string expected)
    {
        IconCatalog.CoerceColor(input).Should().Be(expected);
    }

    #endregion

    #region CoerceIcon Tests

    [Fact]
    public void CoerceIcon_ValidIcon_ReturnsIcon()
    {
        var result = IconCatalog.CoerceIcon("star");
        result.Should().Be("star");
    }

    [Fact]
    public void CoerceIcon_UnknownIcon_ReturnsIcon()
    {
        // Dynamic: unknown icons are accepted for on-demand loading
        var result = IconCatalog.CoerceIcon("my-custom-icon");
        result.Should().Be("my-custom-icon");
    }

    [Fact]
    public void CoerceIcon_NullIcon_ReturnsFallback()
    {
        var result = IconCatalog.CoerceIcon(null);
        result.Should().Be("marker");
    }

    [Fact]
    public void CoerceIcon_EmptyString_ReturnsFallback()
    {
        var result = IconCatalog.CoerceIcon("");
        result.Should().Be("marker");
    }

    [Fact]
    public void CoerceIcon_WhitespaceString_ReturnsFallback()
    {
        var result = IconCatalog.CoerceIcon("   ");
        result.Should().Be("marker");
    }

    [Fact]
    public void CoerceIcon_CustomFallback_ReturnsCustomFallback()
    {
        var result = IconCatalog.CoerceIcon(null, "star");
        result.Should().Be("star");
    }

    #endregion

    #region GetIconResourcePath Tests

    [Fact]
    public void GetIconResourcePath_ValidIconAndColor_ReturnsCorrectPath()
    {
        var result = IconCatalog.GetIconResourcePath("star", "bg-blue");
        result.Should().Be("wayfarer-map-icons/dist/png/marker/bg-blue/star.png");
    }

    [Fact]
    public void GetIconResourcePath_NullIcon_UsesFallbackIcon()
    {
        var result = IconCatalog.GetIconResourcePath(null, "bg-red");
        result.Should().Be("wayfarer-map-icons/dist/png/marker/bg-red/marker.png");
    }

    [Fact]
    public void GetIconResourcePath_NullColor_UsesFallbackColor()
    {
        var result = IconCatalog.GetIconResourcePath("star", null);
        result.Should().Be("wayfarer-map-icons/dist/png/marker/bg-blue/star.png");
    }

    [Fact]
    public void GetIconResourcePath_BothNull_UsesFallbacks()
    {
        var result = IconCatalog.GetIconResourcePath(null, null);
        result.Should().Be("wayfarer-map-icons/dist/png/marker/bg-blue/marker.png");
    }

    [Fact]
    public void GetIconResourcePath_ColorWithoutPrefix_NormalizesColor()
    {
        var result = IconCatalog.GetIconResourcePath("star", "red");
        result.Should().Be("wayfarer-map-icons/dist/png/marker/bg-red/star.png");
    }

    [Fact]
    public void GetIconResourcePath_UnknownIconAndColor_GeneratesPath()
    {
        // Dynamic: generates path for any icon/color combination
        var result = IconCatalog.GetIconResourcePath("custom-icon", "bg-orange");
        result.Should().Be("wayfarer-map-icons/dist/png/marker/bg-orange/custom-icon.png");
    }

    #endregion

    #region GetHexColor Tests

    [Fact]
    public void GetHexColor_BgBlack_ReturnsCorrectHex()
    {
        var result = IconCatalog.GetHexColor("bg-black");
        result.Should().Be("#212121");
    }

    [Fact]
    public void GetHexColor_BgBlue_ReturnsCorrectHex()
    {
        var result = IconCatalog.GetHexColor("bg-blue");
        result.Should().Be("#2196F3");
    }

    [Fact]
    public void GetHexColor_BgGreen_ReturnsCorrectHex()
    {
        var result = IconCatalog.GetHexColor("bg-green");
        result.Should().Be("#4CAF50");
    }

    [Fact]
    public void GetHexColor_BgPurple_ReturnsCorrectHex()
    {
        var result = IconCatalog.GetHexColor("bg-purple");
        result.Should().Be("#9C27B0");
    }

    [Fact]
    public void GetHexColor_BgRed_ReturnsCorrectHex()
    {
        var result = IconCatalog.GetHexColor("bg-red");
        result.Should().Be("#F44336");
    }

    [Fact]
    public void GetHexColor_UnknownColor_ReturnsFallbackHex()
    {
        // Unknown colors fall back to blue hex
        var result = IconCatalog.GetHexColor("bg-orange");
        result.Should().Be("#2196F3");
    }

    [Fact]
    public void GetHexColor_NullColor_ReturnsFallbackHex()
    {
        var result = IconCatalog.GetHexColor(null);
        result.Should().Be("#2196F3");
    }

    [Fact]
    public void GetHexColor_ColorWithoutPrefix_NormalizesAndReturnsHex()
    {
        var result = IconCatalog.GetHexColor("red");
        result.Should().Be("#F44336");
    }

    [Theory]
    [InlineData("bg-black", "#212121")]
    [InlineData("bg-blue", "#2196F3")]
    [InlineData("bg-green", "#4CAF50")]
    [InlineData("bg-purple", "#9C27B0")]
    [InlineData("bg-red", "#F44336")]
    public void GetHexColor_KnownColors_ReturnCorrectHex(string color, string expectedHex)
    {
        IconCatalog.GetHexColor(color).Should().Be(expectedHex);
    }

    #endregion
}

namespace WayfarerMobile.Tests.Unit.Helpers;

/// <summary>
/// Unit tests for IconCatalog class.
/// Tests marker icon and color validation/coercion.
/// </summary>
public class IconCatalogTests
{
    #region MarkerColors Tests

    [Fact]
    public void MarkerColors_ContainsExpectedColors()
    {
        // Assert
        IconCatalog.MarkerColors.Should().Contain("bg-black");
        IconCatalog.MarkerColors.Should().Contain("bg-blue");
        IconCatalog.MarkerColors.Should().Contain("bg-green");
        IconCatalog.MarkerColors.Should().Contain("bg-purple");
        IconCatalog.MarkerColors.Should().Contain("bg-red");
    }

    [Fact]
    public void MarkerColors_HasFiveColors()
    {
        // Assert
        IconCatalog.MarkerColors.Should().HaveCount(5);
    }

    [Fact]
    public void MarkerColors_AllStartWithBgPrefix()
    {
        // Assert
        foreach (var color in IconCatalog.MarkerColors)
        {
            color.Should().StartWith("bg-");
        }
    }

    #endregion

    #region IconNames Tests

    [Fact]
    public void IconNames_ContainsCommonIcons()
    {
        // Assert
        IconCatalog.IconNames.Should().Contain("marker");
        IconCatalog.IconNames.Should().Contain("star");
        IconCatalog.IconNames.Should().Contain("camera");
        IconCatalog.IconNames.Should().Contain("hotel");
        IconCatalog.IconNames.Should().Contain("eat");
        IconCatalog.IconNames.Should().Contain("drink");
        IconCatalog.IconNames.Should().Contain("parking");
        IconCatalog.IconNames.Should().Contain("gas");
    }

    [Fact]
    public void IconNames_HasManyIcons()
    {
        // Assert - Should have a substantial catalog
        IconCatalog.IconNames.Should().HaveCountGreaterThan(50);
    }

    #endregion

    #region PriorityIconNames Tests

    [Fact]
    public void PriorityIconNames_ContainsMarker()
    {
        // Assert
        IconCatalog.PriorityIconNames.Should().Contain("marker");
    }

    [Fact]
    public void PriorityIconNames_SubsetOfIconNames()
    {
        // Assert - All priority icons should exist in the main icon list
        foreach (var priorityIcon in IconCatalog.PriorityIconNames)
        {
            IconCatalog.IconNames.Should().Contain(priorityIcon);
        }
    }

    [Fact]
    public void PriorityIconNames_HasReasonableCount()
    {
        // Assert - Should be a manageable subset for pickers
        IconCatalog.PriorityIconNames.Should().HaveCountGreaterThan(5);
        IconCatalog.PriorityIconNames.Should().HaveCountLessThan(30);
    }

    #endregion

    #region CoerceColor Tests

    [Fact]
    public void CoerceColor_ValidColor_ReturnsColor()
    {
        // Arrange & Act
        var result = IconCatalog.CoerceColor("bg-red");

        // Assert
        result.Should().Be("bg-red");
    }

    [Fact]
    public void CoerceColor_ColorWithoutPrefix_AddsPrefix()
    {
        // Arrange & Act
        var result = IconCatalog.CoerceColor("blue");

        // Assert
        result.Should().Be("bg-blue");
    }

    [Fact]
    public void CoerceColor_InvalidColor_ReturnsFallback()
    {
        // Arrange & Act
        var result = IconCatalog.CoerceColor("bg-orange");

        // Assert
        result.Should().Be("bg-blue"); // Default fallback
    }

    [Fact]
    public void CoerceColor_InvalidColorWithoutPrefix_ReturnsFallback()
    {
        // Arrange & Act
        var result = IconCatalog.CoerceColor("orange");

        // Assert
        result.Should().Be("bg-blue");
    }

    [Fact]
    public void CoerceColor_NullColor_ReturnsFallback()
    {
        // Arrange & Act
        var result = IconCatalog.CoerceColor(null);

        // Assert
        result.Should().Be("bg-blue");
    }

    [Fact]
    public void CoerceColor_EmptyString_ReturnsFallback()
    {
        // Arrange & Act
        var result = IconCatalog.CoerceColor("");

        // Assert
        result.Should().Be("bg-blue");
    }

    [Fact]
    public void CoerceColor_WhitespaceString_ReturnsFallback()
    {
        // Arrange & Act
        var result = IconCatalog.CoerceColor("   ");

        // Assert
        result.Should().Be("bg-blue");
    }

    [Fact]
    public void CoerceColor_CustomFallback_ReturnsCustomFallback()
    {
        // Arrange & Act
        var result = IconCatalog.CoerceColor("invalid", "bg-red");

        // Assert
        result.Should().Be("bg-red");
    }

    [Fact]
    public void CoerceColor_AllValidColors_ReturnThemselves()
    {
        // Assert
        foreach (var color in IconCatalog.MarkerColors)
        {
            IconCatalog.CoerceColor(color).Should().Be(color);
        }
    }

    [Fact]
    public void CoerceColor_AllValidColorsWithoutPrefix_AddPrefix()
    {
        // Assert
        var colorNames = new[] { "black", "blue", "green", "purple", "red" };
        foreach (var name in colorNames)
        {
            IconCatalog.CoerceColor(name).Should().Be($"bg-{name}");
        }
    }

    #endregion

    #region CoerceIcon Tests

    [Fact]
    public void CoerceIcon_ValidIcon_ReturnsIcon()
    {
        // Arrange & Act
        var result = IconCatalog.CoerceIcon("star");

        // Assert
        result.Should().Be("star");
    }

    [Fact]
    public void CoerceIcon_InvalidIcon_ReturnsFallback()
    {
        // Arrange & Act
        var result = IconCatalog.CoerceIcon("nonexistent-icon");

        // Assert
        result.Should().Be("marker"); // Default fallback
    }

    [Fact]
    public void CoerceIcon_NullIcon_ReturnsFallback()
    {
        // Arrange & Act
        var result = IconCatalog.CoerceIcon(null);

        // Assert
        result.Should().Be("marker");
    }

    [Fact]
    public void CoerceIcon_EmptyString_ReturnsFallback()
    {
        // Arrange & Act
        var result = IconCatalog.CoerceIcon("");

        // Assert
        result.Should().Be("marker");
    }

    [Fact]
    public void CoerceIcon_WhitespaceString_ReturnsFallback()
    {
        // Arrange & Act
        var result = IconCatalog.CoerceIcon("   ");

        // Assert
        result.Should().Be("marker");
    }

    [Fact]
    public void CoerceIcon_CustomFallback_ReturnsCustomFallback()
    {
        // Arrange & Act
        var result = IconCatalog.CoerceIcon("invalid", "star");

        // Assert
        result.Should().Be("star");
    }

    [Fact]
    public void CoerceIcon_AllValidIcons_ReturnThemselves()
    {
        // Assert
        foreach (var icon in IconCatalog.IconNames)
        {
            IconCatalog.CoerceIcon(icon).Should().Be(icon);
        }
    }

    #endregion

    #region GetIconResourcePath Tests

    [Fact]
    public void GetIconResourcePath_ValidIconAndColor_ReturnsCorrectPath()
    {
        // Arrange & Act
        var result = IconCatalog.GetIconResourcePath("star", "bg-blue");

        // Assert
        result.Should().Be("wayfarer-map-icons/dist/png/marker/bg-blue/star.png");
    }

    [Fact]
    public void GetIconResourcePath_NullIcon_UsesFallbackIcon()
    {
        // Arrange & Act
        var result = IconCatalog.GetIconResourcePath(null, "bg-red");

        // Assert
        result.Should().Be("wayfarer-map-icons/dist/png/marker/bg-red/marker.png");
    }

    [Fact]
    public void GetIconResourcePath_NullColor_UsesFallbackColor()
    {
        // Arrange & Act
        var result = IconCatalog.GetIconResourcePath("star", null);

        // Assert
        result.Should().Be("wayfarer-map-icons/dist/png/marker/bg-blue/star.png");
    }

    [Fact]
    public void GetIconResourcePath_InvalidIconAndColor_UsesFallbacks()
    {
        // Arrange & Act
        var result = IconCatalog.GetIconResourcePath("invalid-icon", "invalid-color");

        // Assert
        result.Should().Be("wayfarer-map-icons/dist/png/marker/bg-blue/marker.png");
    }

    [Fact]
    public void GetIconResourcePath_ColorWithoutPrefix_NormalizesColor()
    {
        // Arrange & Act
        var result = IconCatalog.GetIconResourcePath("star", "red");

        // Assert
        result.Should().Be("wayfarer-map-icons/dist/png/marker/bg-red/star.png");
    }

    [Fact]
    public void GetIconResourcePath_AllColorVariants_GenerateValidPaths()
    {
        // Assert
        foreach (var color in IconCatalog.MarkerColors)
        {
            var result = IconCatalog.GetIconResourcePath("marker", color);
            result.Should().Contain(color);
            result.Should().EndWith(".png");
            result.Should().StartWith("wayfarer-map-icons/");
        }
    }

    #endregion

    #region GetHexColor Tests

    [Fact]
    public void GetHexColor_BgBlack_ReturnsCorrectHex()
    {
        // Arrange & Act
        var result = IconCatalog.GetHexColor("bg-black");

        // Assert
        result.Should().Be("#212121");
    }

    [Fact]
    public void GetHexColor_BgBlue_ReturnsCorrectHex()
    {
        // Arrange & Act
        var result = IconCatalog.GetHexColor("bg-blue");

        // Assert
        result.Should().Be("#2196F3");
    }

    [Fact]
    public void GetHexColor_BgGreen_ReturnsCorrectHex()
    {
        // Arrange & Act
        var result = IconCatalog.GetHexColor("bg-green");

        // Assert
        result.Should().Be("#4CAF50");
    }

    [Fact]
    public void GetHexColor_BgPurple_ReturnsCorrectHex()
    {
        // Arrange & Act
        var result = IconCatalog.GetHexColor("bg-purple");

        // Assert
        result.Should().Be("#9C27B0");
    }

    [Fact]
    public void GetHexColor_BgRed_ReturnsCorrectHex()
    {
        // Arrange & Act
        var result = IconCatalog.GetHexColor("bg-red");

        // Assert
        result.Should().Be("#F44336");
    }

    [Fact]
    public void GetHexColor_InvalidColor_ReturnsFallbackHex()
    {
        // Arrange & Act
        var result = IconCatalog.GetHexColor("bg-invalid");

        // Assert
        result.Should().Be("#2196F3"); // Default blue
    }

    [Fact]
    public void GetHexColor_NullColor_ReturnsFallbackHex()
    {
        // Arrange & Act
        var result = IconCatalog.GetHexColor(null);

        // Assert
        result.Should().Be("#2196F3");
    }

    [Fact]
    public void GetHexColor_AllMarkerColors_ReturnValidHex()
    {
        // Assert
        foreach (var color in IconCatalog.MarkerColors)
        {
            var hex = IconCatalog.GetHexColor(color);
            hex.Should().StartWith("#");
            hex.Should().HaveLength(7); // #RRGGBB format
        }
    }

    [Fact]
    public void GetHexColor_AllColorsUnique()
    {
        // Arrange
        var hexColors = IconCatalog.MarkerColors.Select(IconCatalog.GetHexColor).ToList();

        // Assert - All colors should map to unique hex values
        hexColors.Distinct().Should().HaveCount(hexColors.Count);
    }

    #endregion
}

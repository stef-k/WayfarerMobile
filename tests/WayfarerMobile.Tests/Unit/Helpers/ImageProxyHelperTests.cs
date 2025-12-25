using FluentAssertions;
using WayfarerMobile.Core.Helpers;
using Xunit;

namespace WayfarerMobile.Tests.Unit.Helpers;

/// <summary>
/// Tests for the ImageProxyHelper class.
/// </summary>
public class ImageProxyHelperTests
{
    private const string BackendUrl = "https://api.wayfarer.com";
    private const string BackendUrlWithApi = "https://api.wayfarer.com/api";

    #region ConvertImagesToProxyUrls Tests

    [Fact]
    public void ConvertImagesToProxyUrls_NullHtml_ReturnsEmptyString()
    {
        var result = ImageProxyHelper.ConvertImagesToProxyUrls(null, BackendUrl);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ConvertImagesToProxyUrls_EmptyHtml_ReturnsEmptyString()
    {
        var result = ImageProxyHelper.ConvertImagesToProxyUrls("", BackendUrl);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ConvertImagesToProxyUrls_WhitespaceOnlyHtml_ReturnsOriginal()
    {
        var result = ImageProxyHelper.ConvertImagesToProxyUrls("   ", BackendUrl);
        result.Should().Be("   ");
    }

    [Fact]
    public void ConvertImagesToProxyUrls_NullBackendUrl_ReturnsOriginalHtml()
    {
        var html = "<img src=\"https://example.com/image.png\">";
        var result = ImageProxyHelper.ConvertImagesToProxyUrls(html, null);
        result.Should().Be(html);
    }

    [Fact]
    public void ConvertImagesToProxyUrls_EmptyBackendUrl_ReturnsOriginalHtml()
    {
        var html = "<img src=\"https://example.com/image.png\">";
        var result = ImageProxyHelper.ConvertImagesToProxyUrls(html, "");
        result.Should().Be(html);
    }

    [Fact]
    public void ConvertImagesToProxyUrls_GoogleMapsUrl_ConvertsToProxy()
    {
        var html = "<img src=\"https://mymaps.usercontent.google.com/path/to/image.png\">";
        var result = ImageProxyHelper.ConvertImagesToProxyUrls(html, BackendUrl);

        result.Should().Contain("/Public/ProxyImage?url=");
        result.Should().Contain("https%3a%2f%2fmymaps.usercontent.google.com");
    }

    [Fact]
    public void ConvertImagesToProxyUrls_MultipleGoogleMapsUrls_ConvertsAll()
    {
        var html = @"<div>
            <img src=""https://mymaps.usercontent.google.com/image1.png"">
            <img src=""https://mymaps.usercontent.google.com/image2.png"">
        </div>";

        var result = ImageProxyHelper.ConvertImagesToProxyUrls(html, BackendUrl);

        result.Should().Contain("image1.png");
        result.Should().Contain("image2.png");
        var proxyCount = CountOccurrences(result, "/Public/ProxyImage?url=");
        proxyCount.Should().Be(2);
    }

    [Fact]
    public void ConvertImagesToProxyUrls_RelativeProxyUrl_ConvertsToAbsolute()
    {
        var html = "<img src=\"/Public/ProxyImage?url=https%3a%2f%2fexample.com%2fimage.png\">";
        var result = ImageProxyHelper.ConvertImagesToProxyUrls(html, BackendUrl);

        result.Should().StartWith("<img src=\"https://api.wayfarer.com/Public/ProxyImage?url=");
    }

    [Fact]
    public void ConvertImagesToProxyUrls_ExternalHttpUrl_ConvertsToProxy()
    {
        var html = "<img src=\"http://example.com/image.png\">";
        var result = ImageProxyHelper.ConvertImagesToProxyUrls(html, BackendUrl);

        result.Should().Contain("/Public/ProxyImage?url=");
        result.Should().Contain("http%3a%2f%2fexample.com");
    }

    [Fact]
    public void ConvertImagesToProxyUrls_ExternalHttpsUrl_ConvertsToProxy()
    {
        var html = "<img src=\"https://cdn.example.com/images/photo.jpg\">";
        var result = ImageProxyHelper.ConvertImagesToProxyUrls(html, BackendUrl);

        result.Should().Contain("/Public/ProxyImage?url=");
        result.Should().Contain("https%3a%2f%2fcdn.example.com");
    }

    [Fact]
    public void ConvertImagesToProxyUrls_AlreadyProxiedUrl_SkipsConversion()
    {
        var html = $"<img src=\"{BackendUrl}/Public/ProxyImage?url=https%3a%2f%2fexample.com%2fimage.png\">";
        var result = ImageProxyHelper.ConvertImagesToProxyUrls(html, BackendUrl);

        // Should only have one ProxyImage reference
        var proxyCount = CountOccurrences(result, "/Public/ProxyImage?url=");
        proxyCount.Should().Be(1);
    }

    [Fact]
    public void ConvertImagesToProxyUrls_BackendOwnUrl_SkipsConversion()
    {
        var html = $"<img src=\"{BackendUrl}/images/logo.png\">";
        var result = ImageProxyHelper.ConvertImagesToProxyUrls(html, BackendUrl);

        result.Should().NotContain("/Public/ProxyImage?url=");
        result.Should().Contain($"{BackendUrl}/images/logo.png");
    }

    [Fact]
    public void ConvertImagesToProxyUrls_BackendUrlWithApiSuffix_StripsCorrectly()
    {
        var html = "<img src=\"https://example.com/image.png\">";
        var result = ImageProxyHelper.ConvertImagesToProxyUrls(html, BackendUrlWithApi);

        result.Should().Contain("https://api.wayfarer.com/Public/ProxyImage?url=");
        result.Should().NotContain("/api/Public/ProxyImage");
    }

    [Fact]
    public void ConvertImagesToProxyUrls_BackendUrlWithTrailingSlash_HandlesCorrectly()
    {
        var html = "<img src=\"https://example.com/image.png\">";
        var result = ImageProxyHelper.ConvertImagesToProxyUrls(html, "https://api.wayfarer.com/");

        result.Should().Contain("https://api.wayfarer.com/Public/ProxyImage?url=");
        result.Should().NotContain("//Public/ProxyImage"); // No double slash
    }

    [Fact]
    public void ConvertImagesToProxyUrls_SpecialCharactersInUrl_UrlEncoded()
    {
        var html = "<img src=\"https://example.com/image?name=test&size=large\">";
        var result = ImageProxyHelper.ConvertImagesToProxyUrls(html, BackendUrl);

        result.Should().Contain("%26"); // & encoded
        result.Should().Contain("%3d"); // = encoded
    }

    [Fact]
    public void ConvertImagesToProxyUrls_MixedContent_ProcessesAll()
    {
        var html = @"<div>
            <img src=""https://mymaps.usercontent.google.com/google.png"">
            <img src=""/Public/ProxyImage?url=relative"">
            <img src=""https://cdn.other.com/external.png"">
        </div>";

        var result = ImageProxyHelper.ConvertImagesToProxyUrls(html, BackendUrl);

        var proxyCount = CountOccurrences(result, "/Public/ProxyImage?url=");
        proxyCount.Should().Be(3);
    }

    [Fact]
    public void ConvertImagesToProxyUrls_SingleQuotedSrc_Works()
    {
        var html = "<img src='https://example.com/image.png'>";
        var result = ImageProxyHelper.ConvertImagesToProxyUrls(html, BackendUrl);

        result.Should().Contain("/Public/ProxyImage?url=");
    }

    [Fact]
    public void ConvertImagesToProxyUrls_DoubleQuotedSrc_Works()
    {
        var html = "<img src=\"https://example.com/image.png\">";
        var result = ImageProxyHelper.ConvertImagesToProxyUrls(html, BackendUrl);

        result.Should().Contain("/Public/ProxyImage?url=");
    }

    [Fact]
    public void ConvertImagesToProxyUrls_NoImages_ReturnsOriginal()
    {
        var html = "<p>Just some text</p>";
        var result = ImageProxyHelper.ConvertImagesToProxyUrls(html, BackendUrl);

        result.Should().Be(html);
    }

    [Fact]
    public void ConvertImagesToProxyUrls_ImgWithOtherAttributes_PreservesAttributes()
    {
        var html = "<img class=\"photo\" src=\"https://example.com/image.png\" alt=\"Photo\">";
        var result = ImageProxyHelper.ConvertImagesToProxyUrls(html, BackendUrl);

        result.Should().Contain("class=\"photo\"");
        result.Should().Contain("alt=\"Photo\"");
        result.Should().Contain("/Public/ProxyImage?url=");
    }

    #endregion

    #region ConvertProxyUrlsBackToOriginal Tests

    [Fact]
    public void ConvertProxyUrlsBackToOriginal_NullHtml_ReturnsEmptyString()
    {
        var result = ImageProxyHelper.ConvertProxyUrlsBackToOriginal(null, BackendUrl);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ConvertProxyUrlsBackToOriginal_EmptyHtml_ReturnsEmptyString()
    {
        var result = ImageProxyHelper.ConvertProxyUrlsBackToOriginal("", BackendUrl);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ConvertProxyUrlsBackToOriginal_NullBackendUrl_ReturnsHtml()
    {
        var html = "<img src=\"https://example.com/image.png\">";
        var result = ImageProxyHelper.ConvertProxyUrlsBackToOriginal(html, null);
        result.Should().Be(html);
    }

    [Fact]
    public void ConvertProxyUrlsBackToOriginal_ProxiedUrl_RestoresOriginal()
    {
        var originalUrl = "https://mymaps.usercontent.google.com/image.png";
        var encodedUrl = System.Web.HttpUtility.UrlEncode(originalUrl);
        var html = $"<img src=\"{BackendUrl}/Public/ProxyImage?url={encodedUrl}\">";

        var result = ImageProxyHelper.ConvertProxyUrlsBackToOriginal(html, BackendUrl);

        result.Should().Contain($"src=\"{originalUrl}\"");
        result.Should().NotContain("/Public/ProxyImage");
    }

    [Fact]
    public void ConvertProxyUrlsBackToOriginal_MultipleProxiedUrls_RestoresAll()
    {
        var url1 = System.Web.HttpUtility.UrlEncode("https://example.com/image1.png");
        var url2 = System.Web.HttpUtility.UrlEncode("https://example.com/image2.png");
        var html = $@"<div>
            <img src=""{BackendUrl}/Public/ProxyImage?url={url1}"">
            <img src=""{BackendUrl}/Public/ProxyImage?url={url2}"">
        </div>";

        var result = ImageProxyHelper.ConvertProxyUrlsBackToOriginal(html, BackendUrl);

        result.Should().Contain("https://example.com/image1.png");
        result.Should().Contain("https://example.com/image2.png");
        result.Should().NotContain("/Public/ProxyImage");
    }

    [Fact]
    public void ConvertProxyUrlsBackToOriginal_JsonEscapedHtml_UnescapesFirst()
    {
        // JSON escaped HTML (like from API responses)
        var originalUrl = "https://example.com/image.png";
        var encodedUrl = System.Web.HttpUtility.UrlEncode(originalUrl);
        var html = $"\\u003Cimg src=\\\"{BackendUrl}/Public/ProxyImage?url={encodedUrl}\\\"\\u003E";

        var result = ImageProxyHelper.ConvertProxyUrlsBackToOriginal(html, BackendUrl);

        result.Should().Contain(originalUrl);
    }

    [Fact]
    public void ConvertProxyUrlsBackToOriginal_NonProxiedUrl_PreservesOriginal()
    {
        var html = "<img src=\"https://example.com/image.png\">";
        var result = ImageProxyHelper.ConvertProxyUrlsBackToOriginal(html, BackendUrl);

        result.Should().Be(html);
    }

    [Fact]
    public void ConvertProxyUrlsBackToOriginal_UrlEncodedSpecialChars_DecodesCorrectly()
    {
        var originalUrl = "https://example.com/image?name=test&size=large";
        var encodedUrl = System.Web.HttpUtility.UrlEncode(originalUrl);
        var html = $"<img src=\"{BackendUrl}/Public/ProxyImage?url={encodedUrl}\">";

        var result = ImageProxyHelper.ConvertProxyUrlsBackToOriginal(html, BackendUrl);

        result.Should().Contain("name=test&size=large");
    }

    [Fact]
    public void ConvertProxyUrlsBackToOriginal_RoundTrip_PreservesContent()
    {
        var originalHtml = @"<div>
            <img src=""https://mymaps.usercontent.google.com/photo.png"">
            <p>Some text</p>
        </div>";

        // Convert to proxy URLs
        var proxied = ImageProxyHelper.ConvertImagesToProxyUrls(originalHtml, BackendUrl);

        // Convert back to original
        var restored = ImageProxyHelper.ConvertProxyUrlsBackToOriginal(proxied, BackendUrl);

        restored.Should().Contain("https://mymaps.usercontent.google.com/photo.png");
        restored.Should().Contain("<p>Some text</p>");
    }

    [Fact]
    public void ConvertProxyUrlsBackToOriginal_BackendUrlWithApiSuffix_MatchesCorrectly()
    {
        var originalUrl = "https://example.com/image.png";
        var encodedUrl = System.Web.HttpUtility.UrlEncode(originalUrl);
        var html = $"<img src=\"https://api.wayfarer.com/Public/ProxyImage?url={encodedUrl}\">";

        var result = ImageProxyHelper.ConvertProxyUrlsBackToOriginal(html, BackendUrlWithApi);

        result.Should().Contain($"src=\"{originalUrl}\"");
    }

    [Fact]
    public void ConvertProxyUrlsBackToOriginal_SingleQuotedSrc_Works()
    {
        var originalUrl = "https://example.com/image.png";
        var encodedUrl = System.Web.HttpUtility.UrlEncode(originalUrl);
        var html = $"<img src='{BackendUrl}/Public/ProxyImage?url={encodedUrl}'>";

        var result = ImageProxyHelper.ConvertProxyUrlsBackToOriginal(html, BackendUrl);

        result.Should().Contain(originalUrl);
    }

    #endregion

    #region UnwrapProxyUrl Tests

    [Fact]
    public void UnwrapProxyUrl_NullUrl_ReturnsNull()
    {
        var result = ImageProxyHelper.UnwrapProxyUrl(null);
        result.Should().BeNull();
    }

    [Fact]
    public void UnwrapProxyUrl_EmptyUrl_ReturnsEmpty()
    {
        var result = ImageProxyHelper.UnwrapProxyUrl("");
        result.Should().BeEmpty();
    }

    [Fact]
    public void UnwrapProxyUrl_NonProxiedUrl_ReturnsOriginal()
    {
        var url = "https://example.com/image.png";
        var result = ImageProxyHelper.UnwrapProxyUrl(url);
        result.Should().Be(url);
    }

    [Fact]
    public void UnwrapProxyUrl_SingleProxiedUrl_UnwrapsCorrectly()
    {
        var originalUrl = "https://mymaps.usercontent.google.com/image.png";
        var encodedUrl = System.Web.HttpUtility.UrlEncode(originalUrl);
        var proxiedUrl = $"https://wayfarer.stefk.me/Public/ProxyImage?url={encodedUrl}";

        var result = ImageProxyHelper.UnwrapProxyUrl(proxiedUrl);

        result.Should().Be(originalUrl);
    }

    [Fact]
    public void UnwrapProxyUrl_DoubleProxiedUrl_UnwrapsCompletely()
    {
        var originalUrl = "https://mymaps.usercontent.google.com/image.png";
        var firstProxy = $"https://wayfarer.stefk.me/Public/ProxyImage?url={System.Web.HttpUtility.UrlEncode(originalUrl)}";
        var doubleProxy = $"https://wayfarer.stefk.me/Public/ProxyImage?url={System.Web.HttpUtility.UrlEncode(firstProxy)}";

        var result = ImageProxyHelper.UnwrapProxyUrl(doubleProxy);

        result.Should().Be(originalUrl);
    }

    [Fact]
    public void UnwrapProxyUrl_TripleProxiedUrl_UnwrapsCompletely()
    {
        var originalUrl = "https://example.com/image.png";
        var proxy1 = $"https://backend.com/Public/ProxyImage?url={System.Web.HttpUtility.UrlEncode(originalUrl)}";
        var proxy2 = $"https://backend.com/Public/ProxyImage?url={System.Web.HttpUtility.UrlEncode(proxy1)}";
        var proxy3 = $"https://backend.com/Public/ProxyImage?url={System.Web.HttpUtility.UrlEncode(proxy2)}";

        var result = ImageProxyHelper.UnwrapProxyUrl(proxy3);

        result.Should().Be(originalUrl);
    }

    [Fact]
    public void UnwrapProxyUrl_UrlWithQueryParams_PreservesParams()
    {
        var originalUrl = "https://example.com/image.png?authuser=0&fife=s16383";
        var encodedUrl = System.Web.HttpUtility.UrlEncode(originalUrl);
        var proxiedUrl = $"https://backend.com/Public/ProxyImage?url={encodedUrl}";

        var result = ImageProxyHelper.UnwrapProxyUrl(proxiedUrl);

        result.Should().Be(originalUrl);
    }

    [Fact]
    public void UnwrapProxyUrl_CaseInsensitive_Works()
    {
        var originalUrl = "https://example.com/image.png";
        var encodedUrl = System.Web.HttpUtility.UrlEncode(originalUrl);
        var proxiedUrl = $"https://backend.com/public/proxyimage?URL={encodedUrl}";

        var result = ImageProxyHelper.UnwrapProxyUrl(proxiedUrl);

        result.Should().Be(originalUrl);
    }

    #endregion

    #region Helper Methods

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.OrdinalIgnoreCase)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }

    #endregion
}

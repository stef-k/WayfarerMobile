using Microsoft.Maui.Handlers;

#if ANDROID
using Android.Webkit;
using AWebView = Android.Webkit.WebView;
#elif WINDOWS
using Microsoft.UI.Xaml.Controls;
#endif

namespace WayfarerMobile.Handlers;

/// <summary>
/// Custom WebView handler to enable external content loading (images, etc.) in WebViews.
/// Configures platform-specific settings to allow mixed content and external resources.
/// </summary>
public class CustomWebViewHandler : WebViewHandler
{
    protected override void ConnectHandler(
#if ANDROID
        AWebView platformView
#elif IOS || MACCATALYST
        WebKit.WKWebView platformView
#elif WINDOWS
        WebView2 platformView
#else
        object platformView
#endif
    )
    {
        base.ConnectHandler(platformView);

#if ANDROID
        if (platformView?.Settings != null)
        {
            // Enable JavaScript (required for our editor)
            platformView.Settings.JavaScriptEnabled = true;

            // Allow mixed content (HTTP images on HTTPS pages)
            platformView.Settings.MixedContentMode = MixedContentHandling.AlwaysAllow;

            // Enable DOM storage
            platformView.Settings.DomStorageEnabled = true;

            // Allow file access
            platformView.Settings.AllowFileAccess = true;

            // Allow content access
            platformView.Settings.AllowContentAccess = true;
        }
#endif
    }
}

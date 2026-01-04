namespace WayfarerMobile.Behaviors;

/// <summary>
/// Behavior that manages image loading lifecycle to prevent crashes when
/// the activity is destroyed while images are still loading.
///
/// Addresses Android Glide error: "You cannot start a load for a destroyed activity"
/// and ObjectDisposedException on image handler callbacks.
/// </summary>
public class LifecycleAwareImageBehavior : Behavior<Image>
{
    private Image? _image;
    private Page? _parentPage;
    private Window? _parentWindow;
    private ImageSource? _originalSource;

    /// <summary>
    /// Called when the behavior is attached to an Image.
    /// </summary>
    protected override void OnAttachedTo(Image image)
    {
        base.OnAttachedTo(image);
        _image = image;

        // Find the parent page to monitor lifecycle
        _image.Loaded += OnImageLoaded;
        _image.Unloaded += OnImageUnloaded;
    }

    /// <summary>
    /// Called when the behavior is detached from an Image.
    /// </summary>
    protected override void OnDetachingFrom(Image image)
    {
        if (_image != null)
        {
            _image.Loaded -= OnImageLoaded;
            _image.Unloaded -= OnImageUnloaded;
        }

        UnsubscribeFromPage();
        UnsubscribeFromWindow();

        _image = null;
        _originalSource = null;
        base.OnDetachingFrom(image);
    }

    private void OnImageLoaded(object? sender, EventArgs e)
    {
        // Find and subscribe to the parent page and window
        var element = _image as Element;
        while (element != null)
        {
            if (element is Page page && _parentPage == null)
            {
                SubscribeToPage(page);
            }

            if (element is Window window && _parentWindow == null)
            {
                SubscribeToWindow(window);
                break;
            }

            element = element.Parent;
        }

        // Also try to get the window from Application if not found in parent chain
        if (_parentWindow == null && Application.Current?.Windows.Count > 0)
        {
            SubscribeToWindow(Application.Current.Windows[0]);
        }
    }

    private void OnImageUnloaded(object? sender, EventArgs e)
    {
        // Clear image source immediately when unloaded to cancel any pending loads
        ClearImageSource();
        UnsubscribeFromPage();
        UnsubscribeFromWindow();
    }

    private void SubscribeToPage(Page page)
    {
        if (_parentPage != null)
            UnsubscribeFromPage();

        _parentPage = page;
        _parentPage.Appearing += OnPageAppearing;
        _parentPage.Disappearing += OnPageDisappearing;
    }

    private void UnsubscribeFromPage()
    {
        if (_parentPage != null)
        {
            _parentPage.Appearing -= OnPageAppearing;
            _parentPage.Disappearing -= OnPageDisappearing;
            _parentPage = null;
        }
    }

    private void SubscribeToWindow(Window window)
    {
        if (_parentWindow != null)
            UnsubscribeFromWindow();

        _parentWindow = window;
        _parentWindow.Stopped += OnWindowStopped;
        _parentWindow.Resumed += OnWindowResumed;
        _parentWindow.Destroying += OnWindowDestroying;
    }

    private void UnsubscribeFromWindow()
    {
        if (_parentWindow != null)
        {
            _parentWindow.Stopped -= OnWindowStopped;
            _parentWindow.Resumed -= OnWindowResumed;
            _parentWindow.Destroying -= OnWindowDestroying;
            _parentWindow = null;
        }
    }

    private void OnPageAppearing(object? sender, EventArgs e)
    {
        RestoreImageSource();
    }

    private void OnPageDisappearing(object? sender, EventArgs e)
    {
        ClearImageSource();
    }

    private void OnWindowStopped(object? sender, EventArgs e)
    {
        // Clear image source when app goes to background to prevent Glide crashes
        // if the activity is destroyed while backgrounded
        ClearImageSource();
    }

    private void OnWindowResumed(object? sender, EventArgs e)
    {
        // Restore the image source when app comes to foreground
        RestoreImageSource();
    }

    private void OnWindowDestroying(object? sender, EventArgs e)
    {
        // Clear image source immediately when window is being destroyed
        // This is critical to prevent ObjectDisposedException
        if (_image != null)
        {
            _image.Source = null;
        }
        _originalSource = null;
    }

    /// <summary>
    /// Clears the image source and saves it for later restoration.
    /// </summary>
    private void ClearImageSource()
    {
        if (_image != null && _image.Source != null)
        {
            _originalSource = _image.Source;
            _image.Source = null;
        }
    }

    /// <summary>
    /// Restores the previously saved image source.
    /// </summary>
    private void RestoreImageSource()
    {
        if (_image != null && _originalSource != null && _image.Source == null)
        {
            _image.Source = _originalSource;
        }
    }
}

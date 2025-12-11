using Syncfusion.Maui.Toolkit.Shimmer;

namespace WayfarerMobile.Shared.Controls;

/// <summary>
/// Shimmer loading placeholder control for list views.
/// Shows animated skeleton placeholders while content is loading.
/// </summary>
public partial class ShimmerLoadingView : ContentView
{
    #region Bindable Properties

    /// <summary>
    /// Bindable property for whether shimmer animation is active.
    /// </summary>
    public static readonly BindableProperty IsActiveProperty =
        BindableProperty.Create(nameof(IsActive), typeof(bool), typeof(ShimmerLoadingView), false);

    /// <summary>
    /// Bindable property for shimmer type.
    /// </summary>
    public static readonly BindableProperty ShimmerTypeProperty =
        BindableProperty.Create(nameof(ShimmerType), typeof(ShimmerType), typeof(ShimmerLoadingView), ShimmerType.CirclePersona);

    /// <summary>
    /// Bindable property for repeat count.
    /// </summary>
    public static readonly BindableProperty RepeatCountProperty =
        BindableProperty.Create(nameof(RepeatCount), typeof(int), typeof(ShimmerLoadingView), 5);

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets whether shimmer animation is active.
    /// </summary>
    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>
    /// Gets or sets the shimmer type.
    /// </summary>
    public ShimmerType ShimmerType
    {
        get => (ShimmerType)GetValue(ShimmerTypeProperty);
        set => SetValue(ShimmerTypeProperty, value);
    }

    /// <summary>
    /// Gets or sets the repeat count for shimmer rows.
    /// </summary>
    public int RepeatCount
    {
        get => (int)GetValue(RepeatCountProperty);
        set => SetValue(RepeatCountProperty, value);
    }

    #endregion

    /// <summary>
    /// Creates a new instance of ShimmerLoadingView.
    /// </summary>
    public ShimmerLoadingView()
    {
        InitializeComponent();
    }
}

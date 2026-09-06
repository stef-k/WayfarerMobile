using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Views;

/// <summary>Viewport-sized presentation; selection and requests remain with the coordinator.</summary>
public partial class DirectionsPage : ContentPage
{
    private readonly DirectionsViewModel model;

    public DirectionsPage(DirectionsViewModel model)
    {
        InitializeComponent();
        this.model = model;
        BindingContext = model;
    }

    public static async Task ShowAsync(DirectionsViewModel model)
    {
        var owner = Application.Current?.Windows.FirstOrDefault()?.Page
            ?? throw new InvalidOperationException("Directions is unavailable. Reopen the map and try again.");
        var page = new DirectionsPage(model);
        await owner.Navigation.PushModalAsync(page);
        try
        {
            await model.Completion;
        }
        finally
        {
            // A native dismissal may already have removed this page.
            if (owner.Navigation.ModalStack.LastOrDefault() == page)
                await owner.Navigation.PopModalAsync();
        }
    }

    protected override bool OnBackButtonPressed()
    {
        model.CancelCommand.Execute(null);
        return true;
    }

    protected override void OnDisappearing()
    {
        model.CancelCommand.Execute(null);
        base.OnDisappearing();
    }
}

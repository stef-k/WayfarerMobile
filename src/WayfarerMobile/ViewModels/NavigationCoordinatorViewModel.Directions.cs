using WayfarerMobile.Core.Models;
using WayfarerMobile.Services;

namespace WayfarerMobile.ViewModels;

public partial class NavigationCoordinatorViewModel
{
    private async Task<NavigationRoute?> TryProgressiveDirectionsAsync(NavigationRoute direct,
        HostedRouteRequestContext context, Guid partition, CancellationTokenSource invocation,
        Func<bool>? startupCurrent, Func<Task>? externalMaps)
    {
        if (_callbacks == null) throw new InvalidOperationException("The Directions page is unavailable.");
        var retained = UsableCoordinate(context.Origin.Latitude, context.Origin.Longitude)
            ? await TrySelectRetainedAsync(context, partition) : null;
        if (startupCurrent?.Invoke() == false || !IsIntentCurrent(context, partition))
        { await StaleStartupAsync(); return null; }

        using var operation = CancellationTokenSource.CreateLinkedTokenSource(invocation.Token);
        var token = operation.Token;
        HostedRoutingResult? catalog = null;
        HostedProviderMode? selectedMode = null;
        HostedRoutingResult? result = null;
        DirectionsAction? completedChoice = null;
        var rediscoveryAvailable = true;
        var expectedSelection = _hostedRouting.CurrentSelection;
        DirectionsViewModel model = null!;
        model = new DirectionsViewModel(SubmitAsync, context.SegmentId != null,
            externalMaps != null, retained != null);

        async Task SubmitAsync(DirectionsAction action, HostedProviderMode? mode)
        {
            if (action == DirectionsAction.Cancel)
            {
                model.Complete();
                operation.Cancel();
                return;
            }
            if (startupCurrent?.Invoke() == false || !IsIntentCurrent(context, partition))
            {
                model.Status = "The destination or account changed. Cancel and try Directions again.";
                return;
            }
            if (action is DirectionsAction.Direct or DirectionsAction.ExternalMaps or DirectionsAction.Retained)
            {
                completedChoice = action;
                model.Complete();
                operation.Cancel();
                return;
            }
            if (!IsInvocationCurrent(context, partition, invocation))
            {
                model.Status = UsableLocation(_callbacks?.CurrentLocation)
                    ? "The location, destination or account changed. Try Directions again, or choose Direct for your current location."
                    : "Waiting for a usable location. Try Directions again when your location is available.";
                model.CanRetry = false;
                return;
            }
            if (action == DirectionsAction.Load) selectedMode = null;
            if (action == DirectionsAction.Mode)
            {
                if (catalog?.Choices?.Contains(mode!) != true) return;
                selectedMode = mode;
            }
            if (selectedMode == null)
            {
                catalog = null;
                model.Modes = [];
            }
            model.IsBusy = true;
            model.IsCalculating = selectedMode != null;
            model.CanRetry = false;
            model.Status = model.IsCalculating ? "Calculating route…" : "Loading route options…";
            try
            {
                var request = selectedMode == null ? context : context with
                {
                    ExpectedCatalogIdentity = catalog!.DiscoveryCatalogIdentity,
                    ExpectedProvider = catalog.Provider
                };
                _hostedRequest = request;
                var response = await _hostedRouting.RequestRouteAsync(request, selectedMode, token,
                    rediscoveryAvailable, () => startupCurrent?.Invoke() != false
                        && IsInvocationCurrent(context, partition, invocation)).WaitAsync(token);
                if (model.IsComplete || token.IsCancellationRequested) return;
                if (startupCurrent?.Invoke() == false || !IsInvocationCurrent(context, partition, invocation))
                {
                    model.Status = "The location, destination or account changed. Try Directions again, or choose Direct for your current location.";
                    return;
                }
                if (response.Outcome == HostedRoutingOutcome.CatalogUnavailable
                    || (response.Outcome == HostedRoutingOutcome.CatalogChanged && response.Choices == null))
                {
                    rediscoveryAvailable = false;
                    catalog = null;
                    selectedMode = null;
                    model.Modes = [];
                    model.Status = "Route options are unavailable. Try again or choose Direct.";
                    model.CanRetry = true;
                }
                else if (response.Outcome is HostedRoutingOutcome.RequiresChoice or HostedRoutingOutcome.CatalogChanged)
                {
                    if (response.Outcome == HostedRoutingOutcome.CatalogChanged) rediscoveryAvailable = false;
                    catalog = response;
                    selectedMode = null;
                    model.Modes = response.Choices!;
                    model.Status = string.Empty;
                }
                else if (response.Outcome == HostedRoutingOutcome.Success)
                {
                    result = response;
                    model.Complete();
                }
                else
                {
                    model.Status = model.IsCalculating
                        ? "The route could not be calculated. Try again or choose Direct."
                        : "Route options are unavailable. Try again or choose Direct.";
                    model.CanRetry = true;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally
            {
                // Late completion belongs only to the detached operation, never the surface.
                if (!model.IsComplete) { model.IsBusy = false; model.IsCalculating = false; }
            }
        }

        try
        {
            await _callbacks.ShowDirectionsAsync(model);
        }
        finally
        {
            model.Complete();
            operation.Cancel();
        }
        if (completedChoice == DirectionsAction.Direct)
            return await CurrentDirectAsync(direct, context, partition);
        if (completedChoice == DirectionsAction.ExternalMaps)
        {
            if (startupCurrent?.Invoke() != false && IsIntentCurrent(context, partition))
                await externalMaps!();
            ReleaseDismissedInvocation(context, invocation);
            return null;
        }
        if (completedChoice == DirectionsAction.Retained && retained?.HostedProvenance is { } provenance)
        {
            if (!IsInvocationCurrent(context, partition, invocation)) { await StaleStartupAsync(); return null; }
            if (!HostedRoutePublication.TryPublishRetained(retained, direct)) return null;
            RestoreRetainedSelection(context, partition, provenance);
            return direct;
        }
        if (result?.Candidate != null)
            return await PublishHostedRouteAsync(direct, context, partition, invocation,
                startupCurrent, result, expectedSelection);
        ReleaseDismissedInvocation(context, invocation);
        return null;
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using WayfarerMobile.Core.Interfaces;
using WayfarerMobile.Core.Models;
using WayfarerMobile.Interfaces;
using WayfarerMobile.Tests.Infrastructure.Mocks;
using WayfarerMobile.ViewModels;

namespace WayfarerMobile.Tests.Unit.ViewModels;

public sealed class TripItemEditorNavigationTests
{
    [Theory]
    [InlineData("dismissed or cancelled")]
    [InlineData("routing unavailable")]
    public async Task NavigationNotStarted_LeavesTripSheetAndSelectionUnchanged(string reason)
    {
        var place = new TripPlace { Id = Guid.NewGuid(), Name = "Destination" };
        var callbacks = new Mock<ITripItemEditorCallbacks>(MockBehavior.Strict);
        callbacks.SetupGet(value => value.SelectedTripPlace).Returns(place);
        callbacks.Setup(value => value.StartNavigationToPlaceAsync(place.Id.ToString()))
            .ReturnsAsync(false);
        var editor = CreateEditor(callbacks.Object);

        await editor.NavigateToTripPlaceCommand.ExecuteAsync(null);

        callbacks.Verify(value => value.CloseTripSheet(), Times.Never, reason);
        callbacks.VerifyGet(value => value.SelectedTripPlace, Times.Once);
        callbacks.Verify(value => value.StartNavigationToPlaceAsync(place.Id.ToString()), Times.Once);
        callbacks.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NavigationStarted_ClosesTripSheetExactlyOnce()
    {
        var place = new TripPlace { Id = Guid.NewGuid(), Name = "Destination" };
        var callbacks = new Mock<ITripItemEditorCallbacks>(MockBehavior.Strict);
        callbacks.SetupGet(value => value.SelectedTripPlace).Returns(place);
        callbacks.Setup(value => value.StartNavigationToPlaceAsync(place.Id.ToString()))
            .ReturnsAsync(true);
        callbacks.Setup(value => value.CloseTripSheet());
        var editor = CreateEditor(callbacks.Object);

        await editor.NavigateToTripPlaceCommand.ExecuteAsync(null);

        callbacks.Verify(value => value.CloseTripSheet(), Times.Once);
        callbacks.VerifyGet(value => value.SelectedTripPlace, Times.Once);
        callbacks.Verify(value => value.StartNavigationToPlaceAsync(place.Id.ToString()), Times.Once);
        callbacks.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NavigationCancellation_PropagatesAndLeavesTripSheetOpen()
    {
        var place = new TripPlace { Id = Guid.NewGuid(), Name = "Destination" };
        var callbacks = new Mock<ITripItemEditorCallbacks>(MockBehavior.Strict);
        callbacks.SetupGet(value => value.SelectedTripPlace).Returns(place);
        callbacks.Setup(value => value.StartNavigationToPlaceAsync(place.Id.ToString()))
            .ThrowsAsync(new OperationCanceledException());
        var editor = CreateEditor(callbacks.Object);

        var act = () => editor.NavigateToTripPlaceCommand.ExecuteAsync(null);

        await act.Should().ThrowAsync<OperationCanceledException>();
        callbacks.Verify(value => value.CloseTripSheet(), Times.Never);
    }

    [Fact]
    public async Task MissingCallbacks_ReturnsWithoutClosingTripSheet()
    {
        var editor = new TripItemEditorViewModel(
            Mock.Of<ITripSyncService>(), null!, Mock.Of<IWikipediaService>(),
            new MockToastService(), NullLogger<TripItemEditorViewModel>.Instance);

        await editor.NavigateToTripPlaceCommand.ExecuteAsync(null);

        editor.IsBusy.Should().BeFalse();
    }

    private static TripItemEditorViewModel CreateEditor(ITripItemEditorCallbacks callbacks)
    {
        var editor = new TripItemEditorViewModel(
            Mock.Of<ITripSyncService>(), null!, Mock.Of<IWikipediaService>(),
            new MockToastService(), NullLogger<TripItemEditorViewModel>.Instance);
        editor.SetCallbacks(callbacks);
        return editor;
    }
}

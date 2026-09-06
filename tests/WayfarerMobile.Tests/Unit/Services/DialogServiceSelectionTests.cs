using WayfarerMobile.Services;

namespace WayfarerMobile.Tests.Unit.Services;

public sealed class DialogServiceSelectionTests
{
    [Theory]
    [InlineData("Cancel", null)]
    [InlineData(null, null)]
    [InlineData("Direct", "Direct")]
    public async Task NativeResult_DismissalIsDistinctFromExplicitDirect(string? nativeResult, string? expected)
    {
        var page = new Page
        {
            ActionSheet = (_, cancel, _, choices) =>
            {
                cancel.Should().Be("Cancel");
                choices.Should().Contain("Direct");
                return Task.FromResult(nativeResult!);
            }
        };
        Application.Current = new Application();
        Application.Current.Windows.Add(new Window { Page = page });
        try
        {
            var selection = await new DialogService().SelectAsync("Navigate by", ["Wayfarer route", "Direct"]);
            selection.Should().Be(expected);
        }
        finally { Application.Current = null; }
    }

    [Fact]
    public async Task MissingPage_IsUnavailableRatherThanSuccessfulSelection()
    {
        Application.Current = null;
        var select = () => new DialogService().SelectAsync("Navigate by", ["Direct"]);
        await select.Should().ThrowAsync<InvalidOperationException>();
    }
}

namespace WayfarerMobile.Core.Interfaces;

/// <summary>
/// Interface for text-to-speech functionality.
/// </summary>
public interface ITextToSpeechService
{
    /// <summary>
    /// Gets or sets whether speech is muted.
    /// </summary>
    bool IsMuted { get; set; }

    /// <summary>
    /// Gets whether speech is currently playing.
    /// </summary>
    bool IsSpeaking { get; }

    /// <summary>
    /// Speaks the specified text.
    /// </summary>
    /// <param name="text">The text to speak.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task that completes when speech finishes.</returns>
    Task SpeakAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops any current speech.
    /// </summary>
    /// <returns>Task that completes when speech is stopped.</returns>
    Task StopAsync();

    /// <summary>
    /// Queues text to be spoken after current speech finishes.
    /// </summary>
    /// <param name="text">The text to speak.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task that completes when speech is queued.</returns>
    Task QueueAsync(string text, CancellationToken cancellationToken = default);
}

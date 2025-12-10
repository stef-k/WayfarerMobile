using Microsoft.Extensions.Logging;
using WayfarerMobile.Core.Interfaces;

namespace WayfarerMobile.Services;

/// <summary>
/// Cross-platform text-to-speech service for navigation announcements.
/// Provides queuing, muting, and cancellation support.
/// </summary>
public class TextToSpeechService : ITextToSpeechService
{
    private readonly ILogger<TextToSpeechService> _logger;
    private readonly SemaphoreSlim _speechLock = new(1, 1);
    private readonly Queue<string> _speechQueue = new();
    private CancellationTokenSource? _currentSpeechCts;
    private bool _isSpeaking;

    /// <summary>
    /// Gets or sets whether speech is muted.
    /// </summary>
    public bool IsMuted { get; set; }

    /// <summary>
    /// Gets whether speech is currently playing.
    /// </summary>
    public bool IsSpeaking => _isSpeaking;

    /// <summary>
    /// Creates a new instance of TextToSpeechService.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public TextToSpeechService(ILogger<TextToSpeechService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Speaks the specified text immediately, cancelling any current speech.
    /// </summary>
    /// <param name="text">The text to speak.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (IsMuted)
        {
            _logger.LogDebug("TTS muted, skipping: {Text}", text);
            return;
        }

        await _speechLock.WaitAsync(cancellationToken);
        try
        {
            // Cancel any current speech
            await CancelCurrentSpeechAsync();

            // Create new cancellation token for this speech
            _currentSpeechCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _isSpeaking = true;

            _logger.LogDebug("Speaking: {Text}", text);

            var options = new SpeechOptions
            {
                Pitch = 1.0f,
                Volume = 1.0f
            };

            await TextToSpeech.Default.SpeakAsync(text, options, _currentSpeechCts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Speech cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to speak text");
        }
        finally
        {
            _isSpeaking = false;
            _speechLock.Release();
        }
    }

    /// <summary>
    /// Queues text to be spoken after current speech finishes.
    /// </summary>
    /// <param name="text">The text to speak.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task QueueAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (IsMuted)
        {
            _logger.LogDebug("TTS muted, skipping queue: {Text}", text);
            return;
        }

        // If not currently speaking, speak immediately
        if (!_isSpeaking)
        {
            await SpeakAsync(text, cancellationToken);
            return;
        }

        // Queue for later
        lock (_speechQueue)
        {
            _speechQueue.Enqueue(text);
            _logger.LogDebug("Queued speech: {Text} (queue size: {Size})", text, _speechQueue.Count);
        }

        // Process queue when current speech finishes
        _ = ProcessQueueAsync(cancellationToken);
    }

    /// <summary>
    /// Stops any current speech and clears the queue.
    /// </summary>
    public async Task StopAsync()
    {
        // Clear queue
        lock (_speechQueue)
        {
            _speechQueue.Clear();
        }

        // Cancel current speech
        await CancelCurrentSpeechAsync();
        _logger.LogDebug("Speech stopped and queue cleared");
    }

    /// <summary>
    /// Cancels current speech.
    /// </summary>
    private async Task CancelCurrentSpeechAsync()
    {
        if (_currentSpeechCts != null)
        {
            await _currentSpeechCts.CancelAsync();
            _currentSpeechCts.Dispose();
            _currentSpeechCts = null;
        }
    }

    /// <summary>
    /// Processes queued speech items.
    /// </summary>
    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        // Wait for current speech to finish
        await _speechLock.WaitAsync(cancellationToken);
        _speechLock.Release();

        // Process next item in queue
        string? nextText = null;
        lock (_speechQueue)
        {
            if (_speechQueue.Count > 0)
            {
                nextText = _speechQueue.Dequeue();
            }
        }

        if (nextText != null)
        {
            await SpeakAsync(nextText, cancellationToken);
        }
    }
}

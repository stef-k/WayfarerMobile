using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace WayfarerMobile.Services;

/// <summary>
/// Service for monitoring and profiling application performance.
/// Tracks timing metrics, memory usage, and helps identify bottlenecks.
/// </summary>
public class PerformanceMonitorService
{
    private readonly ILogger<PerformanceMonitorService> _logger;
    private readonly ConcurrentDictionary<string, OperationMetrics> _metrics = new();
    private readonly ConcurrentDictionary<string, Stopwatch> _activeOperations = new();
    private readonly object _lock = new();
    private DateTime _sessionStart;
    private long _totalOperations;

    /// <summary>
    /// Initializes a new instance of the PerformanceMonitorService class.
    /// </summary>
    public PerformanceMonitorService(ILogger<PerformanceMonitorService> logger)
    {
        _logger = logger;
        _sessionStart = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets the total number of tracked operations.
    /// </summary>
    public long TotalOperations => _totalOperations;

    /// <summary>
    /// Gets the session duration.
    /// </summary>
    public TimeSpan SessionDuration => DateTime.UtcNow - _sessionStart;

    /// <summary>
    /// Starts timing an operation.
    /// </summary>
    /// <param name="operationName">Name of the operation to track.</param>
    /// <returns>Operation ID for stopping the timer.</returns>
    public string StartOperation(string operationName)
    {
        var operationId = $"{operationName}_{Guid.NewGuid():N}";
        var stopwatch = Stopwatch.StartNew();
        _activeOperations[operationId] = stopwatch;
        return operationId;
    }

    /// <summary>
    /// Stops timing an operation and records the metrics.
    /// </summary>
    /// <param name="operationId">The operation ID returned from StartOperation.</param>
    /// <param name="success">Whether the operation succeeded.</param>
    public void StopOperation(string operationId, bool success = true)
    {
        if (!_activeOperations.TryRemove(operationId, out var stopwatch))
        {
            _logger.LogWarning("Unknown operation ID: {OperationId}", operationId);
            return;
        }

        stopwatch.Stop();
        var operationName = operationId[..operationId.LastIndexOf('_')];
        var elapsed = stopwatch.ElapsedMilliseconds;

        RecordMetric(operationName, elapsed, success);
        Interlocked.Increment(ref _totalOperations);

        // Log slow operations
        if (elapsed > 1000)
        {
            _logger.LogWarning("Slow operation: {Operation} took {Elapsed}ms", operationName, elapsed);
        }
    }

    /// <summary>
    /// Times an operation using a disposable scope.
    /// </summary>
    /// <param name="operationName">Name of the operation.</param>
    /// <returns>A disposable that stops timing when disposed.</returns>
    public OperationScope TimeOperation(string operationName)
    {
        return new OperationScope(this, operationName);
    }

    /// <summary>
    /// Times an async operation.
    /// </summary>
    /// <typeparam name="T">Return type of the operation.</typeparam>
    /// <param name="operationName">Name of the operation.</param>
    /// <param name="operation">The async operation to time.</param>
    /// <returns>The operation result.</returns>
    public async Task<T> TimeAsync<T>(string operationName, Func<Task<T>> operation)
    {
        var operationId = StartOperation(operationName);
        var success = true;
        try
        {
            return await operation();
        }
        catch
        {
            success = false;
            throw;
        }
        finally
        {
            StopOperation(operationId, success);
        }
    }

    /// <summary>
    /// Times an async operation without a return value.
    /// </summary>
    /// <param name="operationName">Name of the operation.</param>
    /// <param name="operation">The async operation to time.</param>
    public async Task TimeAsync(string operationName, Func<Task> operation)
    {
        var operationId = StartOperation(operationName);
        var success = true;
        try
        {
            await operation();
        }
        catch
        {
            success = false;
            throw;
        }
        finally
        {
            StopOperation(operationId, success);
        }
    }

    /// <summary>
    /// Gets metrics for a specific operation.
    /// </summary>
    /// <param name="operationName">Name of the operation.</param>
    /// <returns>Metrics for the operation, or null if not found.</returns>
    public OperationMetrics? GetMetrics(string operationName)
    {
        return _metrics.TryGetValue(operationName, out var metrics) ? metrics : null;
    }

    /// <summary>
    /// Gets all recorded metrics.
    /// </summary>
    /// <returns>Dictionary of operation names to metrics.</returns>
    public IDictionary<string, OperationMetrics> GetAllMetrics()
    {
        return new Dictionary<string, OperationMetrics>(_metrics);
    }

    /// <summary>
    /// Gets a performance report as a formatted string.
    /// </summary>
    /// <returns>Performance report text.</returns>
    public string GetPerformanceReport()
    {
        var report = new System.Text.StringBuilder();
        report.AppendLine("PERFORMANCE REPORT");
        report.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        report.AppendLine($"Session Duration: {SessionDuration:hh\\:mm\\:ss}");
        report.AppendLine($"Total Operations: {TotalOperations}");
        report.AppendLine(new string('=', 60));

        // Memory info
        report.AppendLine("\nMEMORY:");
        var memoryInfo = GetMemoryInfo();
        report.AppendLine($"   Working Set: {memoryInfo.WorkingSet / (1024 * 1024):N1} MB");
        report.AppendLine($"   GC Total Memory: {memoryInfo.GcTotalMemory / (1024 * 1024):N1} MB");
        report.AppendLine($"   Gen0 Collections: {memoryInfo.Gen0Collections}");
        report.AppendLine($"   Gen1 Collections: {memoryInfo.Gen1Collections}");
        report.AppendLine($"   Gen2 Collections: {memoryInfo.Gen2Collections}");

        // Operation metrics
        report.AppendLine("\nOPERATION METRICS:");
        var sortedMetrics = _metrics.OrderByDescending(m => m.Value.TotalTime);

        foreach (var (name, metrics) in sortedMetrics)
        {
            report.AppendLine($"\n   {name}:");
            report.AppendLine($"      Count: {metrics.Count}");
            report.AppendLine($"      Success Rate: {metrics.SuccessRate:P1}");
            report.AppendLine($"      Avg Time: {metrics.AverageTime:N1}ms");
            report.AppendLine($"      Min Time: {metrics.MinTime}ms");
            report.AppendLine($"      Max Time: {metrics.MaxTime}ms");
            report.AppendLine($"      Total Time: {metrics.TotalTime}ms");
        }

        // Slow operations warning
        var slowOps = _metrics.Where(m => m.Value.AverageTime > 500).ToList();
        if (slowOps.Count != 0)
        {
            report.AppendLine("\nSLOW OPERATIONS (avg > 500ms):");
            foreach (var (name, metrics) in slowOps.OrderByDescending(m => m.Value.AverageTime))
            {
                report.AppendLine($"   {name}: {metrics.AverageTime:N1}ms avg");
            }
        }

        report.AppendLine(new string('=', 60));
        return report.ToString();
    }

    /// <summary>
    /// Gets current memory information.
    /// </summary>
    /// <returns>Memory information.</returns>
    public MemoryInfo GetMemoryInfo()
    {
        var process = Process.GetCurrentProcess();
        return new MemoryInfo
        {
            WorkingSet = process.WorkingSet64,
            GcTotalMemory = GC.GetTotalMemory(false),
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2)
        };
    }

    /// <summary>
    /// Resets all metrics.
    /// </summary>
    public void Reset()
    {
        _metrics.Clear();
        _activeOperations.Clear();
        _sessionStart = DateTime.UtcNow;
        Interlocked.Exchange(ref _totalOperations, 0);
        _logger.LogInformation("Performance metrics reset");
    }

    /// <summary>
    /// Logs a summary of current metrics.
    /// </summary>
    public void LogSummary()
    {
        _logger.LogInformation(
            "Performance Summary - Session: {Duration}, Operations: {Count}, Memory: {Memory:N1}MB",
            SessionDuration.ToString(@"hh\:mm\:ss"),
            TotalOperations,
            GetMemoryInfo().GcTotalMemory / (1024.0 * 1024.0));
    }

    private void RecordMetric(string operationName, long elapsedMs, bool success)
    {
        _metrics.AddOrUpdate(
            operationName,
            _ => new OperationMetrics
            {
                Count = 1,
                SuccessCount = success ? 1 : 0,
                TotalTime = elapsedMs,
                MinTime = elapsedMs,
                MaxTime = elapsedMs
            },
            (_, existing) =>
            {
                lock (_lock)
                {
                    existing.Count++;
                    if (success) existing.SuccessCount++;
                    existing.TotalTime += elapsedMs;
                    existing.MinTime = Math.Min(existing.MinTime, elapsedMs);
                    existing.MaxTime = Math.Max(existing.MaxTime, elapsedMs);
                }
                return existing;
            });
    }

    /// <summary>
    /// Disposable scope for timing operations.
    /// </summary>
    public class OperationScope : IDisposable
    {
        private readonly PerformanceMonitorService _service;
        private readonly string _operationId;
        private bool _disposed;
        private bool _success = true;

        internal OperationScope(PerformanceMonitorService service, string operationName)
        {
            _service = service;
            _operationId = service.StartOperation(operationName);
        }

        /// <summary>
        /// Marks the operation as failed.
        /// </summary>
        public void MarkFailed() => _success = false;

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            _service.StopOperation(_operationId, _success);
            _disposed = true;
        }
    }
}

/// <summary>
/// Metrics for a tracked operation.
/// </summary>
public class OperationMetrics
{
    /// <summary>
    /// Gets or sets the total count of operations.
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// Gets or sets the count of successful operations.
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// Gets or sets the total time in milliseconds.
    /// </summary>
    public long TotalTime { get; set; }

    /// <summary>
    /// Gets or sets the minimum time in milliseconds.
    /// </summary>
    public long MinTime { get; set; }

    /// <summary>
    /// Gets or sets the maximum time in milliseconds.
    /// </summary>
    public long MaxTime { get; set; }

    /// <summary>
    /// Gets the average time in milliseconds.
    /// </summary>
    public double AverageTime => Count > 0 ? (double)TotalTime / Count : 0;

    /// <summary>
    /// Gets the success rate (0.0 - 1.0).
    /// </summary>
    public double SuccessRate => Count > 0 ? (double)SuccessCount / Count : 0;
}

/// <summary>
/// Memory information snapshot.
/// </summary>
public class MemoryInfo
{
    /// <summary>
    /// Gets or sets the working set size in bytes.
    /// </summary>
    public long WorkingSet { get; set; }

    /// <summary>
    /// Gets or sets the GC total memory in bytes.
    /// </summary>
    public long GcTotalMemory { get; set; }

    /// <summary>
    /// Gets or sets the Gen0 collection count.
    /// </summary>
    public int Gen0Collections { get; set; }

    /// <summary>
    /// Gets or sets the Gen1 collection count.
    /// </summary>
    public int Gen1Collections { get; set; }

    /// <summary>
    /// Gets or sets the Gen2 collection count.
    /// </summary>
    public int Gen2Collections { get; set; }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace ConditioningControlPanel.Services.AIService
{
    /// <summary>
    /// Shared retry helper for AI provider HTTP calls. Handles transient failures
    /// (HTTP 429/5xx, network errors, timeouts) with a fixed backoff.
    /// </summary>
    public sealed class AiRetryPolicy
    {
        private readonly int _maxAttempts;
        private readonly TimeSpan _delay;
        private readonly ILogger? _logger;

        public AiRetryPolicy(int maxAttempts = 2, TimeSpan? delay = null, ILogger? logger = null)
        {
            _maxAttempts = Math.Max(1, maxAttempts);
            _delay = delay ?? TimeSpan.FromMilliseconds(1200);
            _logger = logger;
        }

        /// <summary>
        /// Executes <paramref name="action"/> up to <see cref="_maxAttempts"/> times,
        /// delaying between attempts when <paramref name="isRetryable"/> returns true.
        /// </summary>
        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            Func<Exception, bool> isRetryable,
            CancellationToken cancellationToken = default)
        {
            Exception? lastException = null;
            for (var attempt = 1; attempt <= _maxAttempts; attempt++)
            {
                try
                {
                    return await action(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (attempt < _maxAttempts && isRetryable(ex))
                {
                    lastException = ex;
                    _logger?.Debug(
                        "AiRetryPolicy: attempt {Attempt} failed ({Message}), retrying after {Delay}ms",
                        attempt,
                        ex.Message,
                        _delay.TotalMilliseconds);

                    try
                    {
                        await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                }
            }

            // The loop only exits here if the final attempt threw a retryable exception
            // (the exception filter on the last iteration is false, so it propagates instead).
            // This line is a defensive fallback and should be unreachable.
            throw lastException ?? new InvalidOperationException("Retry policy exhausted with no result.");
        }
    }
}

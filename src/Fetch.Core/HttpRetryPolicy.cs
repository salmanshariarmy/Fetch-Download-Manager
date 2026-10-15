using System.Net;
using System.Net.Http.Headers;

namespace Fetch.Core;

/// <summary>Retries temporary HTTP failures, not permanent authorization or missing-file errors.</summary>
public static class HttpRetryPolicy
{
    public static bool IsRetryable(HttpStatusCode status) => (int)status is
        408 or 425 or 429 or 500 or 502 or 503 or 504;

    public static TimeSpan Delay(int attempt, RetryConditionHeaderValue? retryAfter,
        DateTimeOffset? now = null)
    {
        if (attempt is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(attempt));
        // Cap Retry-After to keep the desktop app responsive, even on broken servers.
        TimeSpan backoff = TimeSpan.FromSeconds(1 << attempt);
        TimeSpan requested = retryAfter?.Delta ??
            (retryAfter?.Date - (now ?? DateTimeOffset.UtcNow) ?? backoff);
        return TimeSpan.FromMilliseconds(Math.Clamp(requested.TotalMilliseconds, 0, 30_000));
    }
}

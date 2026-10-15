using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;

namespace Fetch.Core;

public static class SchedulePolicy
{
    public const string Format = "yyyy-MM-dd HH:mm";

    public static bool TryParseLocal(string text, out DateTimeOffset scheduled)
    {
        scheduled = default;
        if (!DateTime.TryParseExact(text?.Trim(), Format, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var local)) return false;
        if (TimeZoneInfo.Local.IsInvalidTime(local)) return false;
        var offset = TimeZoneInfo.Local.GetUtcOffset(local);
        scheduled = new DateTimeOffset(local, offset);
        return true;
    }

    public static bool IsDue(DateTimeOffset? when, DateTimeOffset now) =>
        when.HasValue && when.Value <= now;
}

public static class HashVerifier
{
    public static bool IsValidSha256(string? hex) =>
        string.IsNullOrWhiteSpace(hex) ||
        (hex.Length == 64 && hex.All(Uri.IsHexDigit));

    public static async Task VerifySha256Async(string path, string? expected, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(expected)) return;
        if (!IsValidSha256(expected)) throw new ArgumentException("Expected SHA-256 must be exactly 64 hexadecimal characters.");
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(input, ct).ConfigureAwait(false);
        if (!Convert.ToHexString(hash).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("SHA-256 mismatch. The downloaded file was not finalized; partial segments are preserved.");
    }
}

/// <summary>Shared global token bucket. Never releases more bytes than the configured limit.
/// Reserving before reads can underutilize the limit on short reads, but cannot exceed it.</summary>
public sealed class BandwidthLimiter
{
    private readonly object _sync = new();
    private double _tokens;
    private long _tick = Stopwatch.GetTimestamp();
    private int _rateKiB;

    public BandwidthLimiter(int rateKiB) => SetLimit(rateKiB);
    public int LimitKiB { get { lock (_sync) return _rateKiB; } }

    public void SetLimit(int rateKiB)
    {
        if (rateKiB is < 0 or > 1_048_576) throw new ArgumentOutOfRangeException(nameof(rateKiB));
        lock (_sync)
        {
            _rateKiB = rateKiB;
            _tokens = rateKiB == 0 ? 0 : Math.Min(64 * 1024, rateKiB * 1024.0);
            _tick = Stopwatch.GetTimestamp();
        }
    }

    public async Task<int> ReserveAsync(int requested, CancellationToken cancellation)
    {
        if (requested < 1) throw new ArgumentOutOfRangeException(nameof(requested));
        for (;;)
        {
            cancellation.ThrowIfCancellationRequested();
            int allowed;
            int wait;
            lock (_sync)
            {
                if (_rateKiB == 0) return requested;
                double rate = _rateKiB * 1024.0;
                long now = Stopwatch.GetTimestamp();
                double elapsed = Math.Max(0, (now - _tick) / (double)Stopwatch.Frequency);
                _tokens = Math.Min(rate, _tokens + elapsed * rate);
                _tick = now;
                int quantum = Math.Min(requested, Math.Max(1, (int)Math.Min(32 * 1024, rate / 20)));
                allowed = _tokens >= quantum ? Math.Min(requested, (int)Math.Floor(_tokens)) : 0;
                if (allowed > 0) _tokens -= allowed;
                wait = allowed == 0 ? (int)Math.Clamp(Math.Ceiling(1000 * (quantum - _tokens) / rate), 5, 1000) : 0;
            }
            if (allowed > 0) return allowed;
            await Task.Delay(wait, cancellation).ConfigureAwait(false);
        }
    }
}

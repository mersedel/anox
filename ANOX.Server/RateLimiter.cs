using System;
using System.Diagnostics;

namespace ANOX.Server;

/// <summary>
/// Token bucket: allows short bursts up to <c>burst</c> messages, then <c>perSecond</c> on average.
/// Not thread-safe: each client uses its own instance from its receive thread only.
/// </summary>
public sealed class RateLimiter
{
    private readonly double _perSecond;
    private readonly double _capacity;
    private double _tokens;
    private long _lastTimestamp;

    public RateLimiter(double perSecond, int burst)
    {
        _perSecond = perSecond;
        _capacity = burst;
        _tokens = burst;
        _lastTimestamp = Stopwatch.GetTimestamp();
    }

    public bool TryTake()
    {
        long now = Stopwatch.GetTimestamp();
        double elapsed = Stopwatch.GetElapsedTime(_lastTimestamp, now).TotalSeconds;
        _lastTimestamp = now;

        _tokens = Math.Min(_capacity, _tokens + elapsed * _perSecond);

        if (_tokens < 1) return false;

        _tokens -= 1;
        return true;
    }
}

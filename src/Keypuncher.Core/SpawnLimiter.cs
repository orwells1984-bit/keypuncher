namespace Keypuncher.Core;

/// <summary>A token bucket: at most <paramref name="perSecond"/> new things a second on average,
/// with a small burst allowance, so frantic mashing cannot drop the frame rate.</summary>
public sealed class SpawnLimiter(double perSecond = 8, double burst = 4)
{
    private readonly double _burst = burst;
    private double _tokens = burst;
    private DateTimeOffset _last = DateTimeOffset.MinValue;

    public bool TryTake(DateTimeOffset now)
    {
        if (_last != DateTimeOffset.MinValue)
            _tokens = Math.Min(_burst, _tokens + ((now - _last).TotalSeconds * perSecond));
        _last = now;
        if (_tokens < 1) return false;
        _tokens -= 1;
        return true;
    }
}

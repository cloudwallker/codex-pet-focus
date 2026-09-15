namespace CodexPetFocus.Core;

/// <summary>
/// Wraps a clock with an idempotent freeze boundary so delayed UI work cannot accrue
/// time that elapsed while Windows was locked or suspended.
/// </summary>
public sealed class FreezableTimeProvider : TimeProvider
{
    private readonly TimeProvider inner;
    private readonly object sync = new();
    private bool frozen;
    private long frozenTimestamp;
    private DateTimeOffset frozenUtcNow;

    public FreezableTimeProvider(TimeProvider inner)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public override long TimestampFrequency => inner.TimestampFrequency;

    public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

    public bool IsFrozen
    {
        get
        {
            lock (sync)
                return frozen;
        }
    }

    public void Freeze()
    {
        lock (sync)
        {
            if (frozen)
                return;

            frozenTimestamp = inner.GetTimestamp();
            frozenUtcNow = inner.GetUtcNow();
            frozen = true;
        }
    }

    public void Unfreeze()
    {
        lock (sync)
            frozen = false;
    }

    public override long GetTimestamp()
    {
        lock (sync)
            return frozen ? frozenTimestamp : inner.GetTimestamp();
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (sync)
            return frozen ? frozenUtcNow : inner.GetUtcNow();
    }
}

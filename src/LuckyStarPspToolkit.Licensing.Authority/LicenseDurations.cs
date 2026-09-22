namespace LuckyStarPspToolkit.Licensing.Authority;

/// <summary>Exact UTC duration policy, including calendar years and leap-day handling.</summary>
public static class LicenseDurations
{
    /// <summary>Validates bounded whole durations. A day is 24 hours; a year uses UTC calendar AddYears semantics.</summary>
    /// <param name="unit">hours, days, years or permanent.</param>
    /// <param name="amount">Duration amount; zero only for perpetual entitlement.</param>
    public static void Validate(string unit, int amount)
    {
        bool valid = unit switch
        {
            "hours" => amount is >= 1 and <= 876000,
            "days" => amount is >= 1 and <= 36500,
            "years" => amount is >= 1 and <= 100,
            "permanent" => amount == 0,
            _ => false
        };
        if (!valid) throw new LicenseException("DURATION_INVALID", "Specify 1..876000 hours, 1..36500 days, 1..100 years, or permanent.");
    }

    /// <summary>Computes an exclusive deadline using server time, never the client clock.</summary>
    /// <param name="start">Start UTC Unix seconds.</param>
    /// <param name="unit">Validated unit.</param>
    /// <param name="amount">Validated amount.</param>
    /// <returns>Exclusive expiry or null for perpetual entitlement.</returns>
    public static long? Expires(long start, string unit, int amount)
    {
        Validate(unit, amount);
        try
        {
            DateTimeOffset value = DateTimeOffset.FromUnixTimeSeconds(start);
            return unit switch
            {
                "hours" => value.AddHours(amount).ToUnixTimeSeconds(),
                "days" => value.AddDays(amount).ToUnixTimeSeconds(),
                "years" => value.AddYears(amount).ToUnixTimeSeconds(),
                _ => null
            };
        }
        catch (ArgumentOutOfRangeException) { throw new LicenseException("DURATION_RANGE", "License deadline is outside the supported calendar."); }
    }
}

/// <summary>Server timeline that re-anchors on forward wall-clock corrections and continues advancing during rollback.</summary>
public sealed class AuthorityClock
{
    /// <summary>Time source; timestamps and UTC are sampled under the same timeline lock.</summary>
    private readonly TimeProvider provider;
    /// <summary>Latest accepted instant with subsecond precision; never rounded between observations.</summary>
    private DateTimeOffset anchor;
    /// <summary>Timestamp corresponding to the accepted anchor.</summary>
    private long timestamp;
    /// <summary>Serializes updates so concurrent callers cannot reintroduce an older anchor.</summary>
    private readonly object sync = new();

    /// <summary>Initializes UTC and monotonic anchors from one provider.</summary>
    /// <param name="provider">System clock or a deterministic test source.</param>
    public AuthorityClock(TimeProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        this.provider = provider;
        timestamp = provider.GetTimestamp();
        anchor = provider.GetUtcNow();
    }

    /// <summary>Advances the accepted timeline by elapsed monotonic time, then incorporates forward UTC corrections.</summary>
    /// <returns>Nondecreasing UTC seconds; subsecond progress is retained internally.</returns>
    /// <exception cref="LicenseException">The time provider moves its timestamp backward or exceeds the calendar range.</exception>
    public long Now()
    {
        lock (sync)
        {
            long currentTimestamp = provider.GetTimestamp();
            TimeSpan elapsed = provider.GetElapsedTime(timestamp, currentTimestamp);
            if (elapsed < TimeSpan.Zero)
                throw new LicenseException("SERVER_MONOTONIC_ROLLBACK", "Server monotonic time moved backward.");
            try
            {
                DateTimeOffset monotonic = anchor + elapsed;
                DateTimeOffset wall = provider.GetUtcNow();
                // Re-anchoring preserves progress after a forward correction followed by rollback.
                // Keeping full precision prevents frequent subsecond observations from freezing time.
                anchor = wall > monotonic ? wall : monotonic;
                timestamp = currentTimestamp;
                return anchor.ToUnixTimeSeconds();
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new LicenseException("SERVER_CLOCK_RANGE", "Server time exceeds the supported calendar range.");
            }
        }
    }
}

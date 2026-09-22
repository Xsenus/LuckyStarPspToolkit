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

/// <summary>Server clock anchored to monotonic elapsed time; a wall-clock rollback cannot lengthen a running license.</summary>
public sealed class AuthorityClock
{
    /// <summary>Underlying system or deterministic test clock.</summary>
    private readonly TimeProvider provider;
    /// <summary>UTC anchor at process startup.</summary>
    private readonly DateTimeOffset start;
    /// <summary>Monotonic timestamp at startup.</summary>
    private readonly long timestamp;
    /// <summary>Highest observed UTC second, protected by synchronization.</summary>
    private long highest;
    /// <summary>Serializes monotonic state updates.</summary>
    private readonly object sync = new();

    /// <summary>Initializes the authoritative timeline.</summary>
    /// <param name="provider">System time in production, controllable time in tests.</param>
    public AuthorityClock(TimeProvider provider)
    {
        this.provider = provider; start = provider.GetUtcNow(); timestamp = provider.GetTimestamp();
        highest = start.ToUnixTimeSeconds();
    }

    /// <summary>Returns a nondecreasing UTC Unix second within the process.</summary>
    /// <returns>Maximum of wall clock, monotonic timeline and previous observation.</returns>
    public long Now()
    {
        lock (sync)
        {
            long mono = (start + provider.GetElapsedTime(timestamp)).ToUnixTimeSeconds();
            highest = Math.Max(highest, Math.Max(mono, provider.GetUtcNow().ToUnixTimeSeconds()));
            return highest;
        }
    }
}

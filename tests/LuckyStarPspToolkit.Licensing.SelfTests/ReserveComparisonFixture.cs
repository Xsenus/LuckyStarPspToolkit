using System.Diagnostics;
using System.Text.Json;
using LuckyStarPspToolkit.Licensing;
using LuckyStarPspToolkit.Licensing.Authority;

/// <summary>Identical cross-version public-API probes and renewal measurements; only disposable synthetic authority data is used.</summary>
internal static class ReserveComparisonFixture
{
    /// <summary>Reports observed historical behavior and allocation samples without embedding any keys or signed grants.</summary>
    /// <param name="args">Reserved for the common diagnostic runner.</param>
    /// <returns>Zero after all observations were obtained; booleans describe behavior rather than faking historical passes.</returns>
    public static int Run(string[] args)
    {
        bool expiry, rollback, reset;
        using (var f = new Fixture())
        {
            var (issue, grant) = Prepare(f); var cache = Cache(f, issue, grant);
            f.Time.Advance(TimeSpan.FromDays(7)); _ = Denied(() => cache.Remaining(false));
            f.Time.ShiftWall(TimeSpan.FromDays(-7));
            expiry = Denied(() => new ReserveCache(f.Directory, f.Trust, f.Device, issue.Id, f.Time).Remaining());
        }
        using (var f = new Fixture())
        {
            var (issue, grant) = Prepare(f); var cache = Cache(f, issue, grant);
            f.Time.ShiftWall(TimeSpan.FromSeconds(-1)); _ = Denied(() => cache.Remaining(false));
            f.Time.ShiftWall(TimeSpan.FromSeconds(1));
            rollback = Denied(() => new ReserveCache(f.Directory, f.Trust, f.Device, issue.Id, f.Time).Remaining());
        }
        using (var f = new Fixture())
        {
            var (issue, grant) = Prepare(f);
            f.Authority.Change(new(Guid.NewGuid().ToString("D"), issue.Id, "reset-device", DeviceId: f.Device.Id));
            f.Grant(issue);
            reset = Denied(() => f.Authority.RefreshReserve(new(grant.Id, f.Request(issue, "check"))));
        }
        var samples = new List<object>();
        using (var f = new Fixture())
        {
            var (issue, grant) = Prepare(f);
            for (int n = -3; n < 7; n++)
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread(); long started = Stopwatch.GetTimestamp();
                for (int i = 0; i < 100; i++)
                {
                    var request = f.Request(issue, "check");
                    _ = f.Authority.RefreshReserve(new(grant.Id, request));
                }
                long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                double milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (n >= 0) samples.Add(new { bytes, milliseconds, requests = 100 });
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(new { schema = "lsptool.reserve-comparison.v1", framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            observedExpirySurvivesRestart = expiry, observedRollbackSurvivesRestart = rollback,
            resetRetiresHistoricalGrant = reset, samples, audit = AuditSamples() }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
    /// <summary>Measures bounded-tail reads against the previous whole-audit export using a fixed 50001-event history.</summary>
    /// <returns>Raw allocation samples and event count; reflection is used only to keep one harness compatible with both versions.</returns>
    private static object AuditSamples()
    {
        using var f = new Fixture(); var issue = f.Issue(); f.Authority.Dispose();
        var config = LicenseJson.Read<AuthorityConfiguration>(PrivateFiles.Read(Path.Combine(f.Directory, "authority.json")));
        using (var store = new LicenseStore(f.Directory, config))
        {
            long now = f.Time.GetUtcNow().ToUnixTimeSeconds();
            store.Change(now, db => { for (int i = 0; i < 50000; i++) db.Audit.Add(new(now, "resume", issue.Id, "")); return 0; });
        }
        f.Restart();
        var method = typeof(LicenseAuthority).GetMethod("AuditRecent");
        Func<LicenseAudit[]> read = method is null ? () => f.Authority.Audit().TakeLast(500).Reverse().ToArray()
            : () => (LicenseAudit[])method.Invoke(f.Authority, new object[] { 500 })!;
        if (!read().SequenceEqual(f.Authority.Audit().TakeLast(500).Reverse())) throw new InvalidOperationException("Audit order differs");
        var samples = new List<long>();
        for (int n = -3; n < 7; n++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) { if (read().Length != 500) throw new InvalidOperationException("Tail size differs"); }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (n >= 0) samples.Add(allocated);
        }
        return new { events = 50001, tail = 500, readsPerSample = 100, samples };
    }

    /// <summary>Observes controlled denial only; unrelated exceptions fail the probe.</summary>
    /// <param name="operation">Candidate forbidden operation.</param>
    /// <returns>True only for a licensing refusal.</returns>
    private static bool Denied(Action operation) { try { operation(); return false; } catch (LicenseException) { return true; } }
    /// <summary>Creates the same perpetual and seven-day configuration for each version.</summary>
    /// <param name="f">Isolated state.</param>
    /// <returns>Parent and reserve permission.</returns>
    private static (IssueLicenseRequest, ReserveGrant) Prepare(Fixture f)
    {
        var issue = f.Issue("permanent", 0); f.Grant(issue); f.Authority.SetReservePolicy(new(Guid.NewGuid().ToString("D"), true));
        return (issue, f.Authority.IssueReserve(new(Guid.NewGuid().ToString("D"), issue.Id, f.Device.Id, 168, "probe")));
    }
    /// <summary>Uses real signatures for the cached grant rather than a hand-built JSON allowance.</summary>
    /// <param name="f">Isolated state.</param>
    /// <param name="issue">Parent entitlement.</param>
    /// <param name="grant">Permission to verify.</param>
    /// <returns>Activated reserve cache.</returns>
    private static ReserveCache Cache(Fixture f, IssueLicenseRequest issue, ReserveGrant grant)
    {
        var request = f.Request(issue, "check"); var token = f.Authority.RefreshReserve(new(grant.Id, request));
        var cache = new ReserveCache(f.Directory, f.Trust, f.Device, issue.Id, f.Time);
        cache.Accept(token.Token, grant.Id, request.ClientNonce, TimeSpan.Zero); return cache;
    }
}

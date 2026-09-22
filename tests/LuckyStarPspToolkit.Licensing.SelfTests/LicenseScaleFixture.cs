using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LuckyStarPspToolkit.Licensing;
using LuckyStarPspToolkit.Licensing.Authority;

/// <summary>Identical public-API workload for old/new licensing assemblies; all authority files and secrets are synthetic and deleted.</summary>
internal static class LicenseScaleFixture
{
    /// <summary>Executes either historical defect probes or warmed snapshot measurements without claiming native .NET 9 validation.</summary>
    /// <param name="args">--probe selects behavior comparison; the default measures whole-store commits.</param>
    /// <returns>Zero after successfully recording observations, not necessarily after an old version passes a probe.</returns>
    public static int Run(string[] args)
    {
        object report = args.Contains("--probe", StringComparer.Ordinal) ? Probe() : Measure();
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    /// <summary>Observes historical clock, JSON and snapshot defects without modifying the harness between revisions.</summary>
    /// <returns>Non-secret booleans recording behavior.</returns>
    private static object Probe()
    {
        var time = new ProbeTime(); var clock = new AuthorityClock(time);
        long initial = clock.Now(); time.Shift(TimeSpan.FromDays(1)); long forward = clock.Now();
        time.Shift(TimeSpan.FromDays(-1)); time.Advance(TimeSpan.FromSeconds(7)); long after = clock.Now();
        bool missingRejected = RefusesJson("{}"), nullRejected = RefusesJson("{\"challenge\":null}");
        string directory = Path.Combine(Path.GetTempPath(), "lsp-store-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            var configuration = Config(); PrivateFiles.Directory(directory);
            LicenseStore.Initialize(directory, configuration, 2000000000);
            using var store = new LicenseStore(directory, configuration);
            string id = Id(1); Seed(store, 1);
            LicenseDatabase snapshot = store.Read(db => db); snapshot.Licenses.Clear();
            bool readIsolated = store.Read(db => db.Licenses.Count) == 1;
            if (!readIsolated) Seed(store, 1);
            var alias = store.Change(2000000000, db => db); alias.Licenses.Clear();
            bool changeIsolated = store.Read(db => db.Licenses.ContainsKey(id));
            return new { schema = "lsptool.license-probe.v1", version = Version(), standardNet9Validation = false,
                elapsedAfterForwardRollback = after - forward, forwardCorrection = forward - initial,
                clockContinuesAfterRollback = after == forward + 7,
                requiredJsonRejected = missingRejected, nullJsonRejected = nullRejected,
                readSnapshotIsolated = readIsolated, transactionResultIsolated = changeIsolated, expiredLicenseRestartBlocked = RestartProbe() };
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    /// <summary>Replays an expired-license restart behind its most recent check using the same public authority API in both versions.</summary>
    /// <returns>True if rollback is refused rather than reviving the expired installation.</returns>
    private static bool RestartProbe()
    {
        string directory = Path.Combine(Path.GetTempPath(), "lsp-restart-probe-" + Guid.NewGuid().ToString("N"));
        string password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var time = new ProbeTime();
        try
        {
            _ = LicenseAuthority.Initialize(directory, "http://127.0.0.1:17840/", password, true);
            using var device = new DeviceIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256), new string('a', 64));
            var issue = new IssueLicenseRequest(Guid.NewGuid().ToString("D"), LicenseCrypto.NewAccessKey(), "synthetic", "hours", 1, "activation");
            using (var authority = new LicenseAuthority(directory, password, time))
            {
                authority.Issue(issue); _ = RequestGrant(authority, device, issue, "activate");
                time.Advance(TimeSpan.FromHours(2));
                try { _ = RequestGrant(authority, device, issue, "check"); throw new InvalidOperationException("Expiry setup failed"); }
                catch (LicenseException ex) when (ex.Code == "LICENSE_EXPIRED") { }
            }
            time.Shift(TimeSpan.FromMinutes(-90));
            try
            {
                using var restarted = new LicenseAuthority(directory, password, time);
                _ = RequestGrant(restarted, device, issue, "check");
                return false;
            }
            catch (LicenseException ex) when (ex.Code is "SERVER_CLOCK_ROLLBACK" or "LICENSE_EXPIRED") { return true; }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    /// <summary>Sends a fresh correctly signed local request to the actual authority code, not a mocked permission check.</summary>
    /// <param name="authority">Revision under test.</param>
    /// <param name="device">Disposable synthetic installation key.</param>
    /// <param name="issue">Private in-memory test issuance request.</param>
    /// <param name="action">activate or check.</param>
    /// <returns>Signed server grant, or a controlled refusal.</returns>
    private static LeaseResponse RequestGrant(LicenseAuthority authority, DeviceIdentity device, IssueLicenseRequest issue, string action)
    {
        string nonce = authority.ChallengeFor(new(LicenseCrypto.Product, action, device.PublicKey, device.HostBinding)).Challenge;
        var request = new LicenseRequest(LicenseCrypto.Product, action, device.PublicKey, device.HostBinding, nonce,
            LicenseCrypto.Nonce(), action == "check" ? issue.Id : "", action == "activate" ? issue.AccessKey : "", "");
        return authority.Authorize(request with { Proof = device.Prove(request) });
    }

    /// <summary>Records new managed allocations and elapsed times for a representative multi-license transaction after warmup.</summary>
    /// <returns>Individual samples, medians and a digest of the final semantic database.</returns>
    private static object Measure()
    {
        const int count = 2000;
        string directory = Path.Combine(Path.GetTempPath(), "lsp-store-scale-" + Guid.NewGuid().ToString("N"));
        try
        {
            var configuration = Config(); PrivateFiles.Directory(directory);
            LicenseStore.Initialize(directory, configuration, 2000000000);
            using var store = new LicenseStore(directory, configuration); Seed(store, count);
            string id = Id(1);
            for (int i = 0; i < 3; i++) ChangeLabel(store, id, "warmup");
            var allocated = new long[7]; var milliseconds = new double[7];
            for (int i = 0; i < allocated.Length; i++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long before = GC.GetAllocatedBytesForCurrentThread(); long started = Stopwatch.GetTimestamp();
                ChangeLabel(store, id, "measured");
                milliseconds[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                allocated[i] = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            byte[] canonical = store.Read(db => LicenseJson.Write(db));
            return new { schema = "lsptool.license-store-benchmark.v1", version = Version(),
                framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                standardNet9Validation = false, syntheticLicenses = count, installedDeviceRecords = count / 2,
                samples = allocated.Length, allocatedBytes = allocated, elapsedMilliseconds = milliseconds,
                medianAllocatedBytes = allocated.Order().ElementAt(allocated.Length / 2),
                medianElapsedMilliseconds = milliseconds.Order().ElementAt(milliseconds.Length / 2),
                finalSemanticSha256 = LicenseCrypto.Digest(canonical), finalPayloadBytes = canonical.Length,
                measurementScope = "Whole LicenseStore.Change on the calling thread, including validation, copying, JSON, HMAC and local flushed file replacement. Not total process memory. No signing or clock checkpoint in this measurement." };
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    /// <summary>Updates an existing label without issuing credentials or changing entitlement durations.</summary>
    /// <param name="store">Synthetic database.</param>
    /// <param name="id">Deterministic test license UUID.</param>
    /// <param name="label">Non-secret value.</param>
    private static void ChangeLabel(LicenseStore store, string id, string label) =>
        store.Change(2000000000, db => { db.Licenses[id] = db.Licenses[id] with { Label = label }; return 0; });

    /// <summary>Populates deterministic entitlement contents in one transaction, outside measured sections.</summary>
    /// <param name="store">Isolated synthetic store.</param>
    /// <param name="count">Number of licenses, at most the production cap.</param>
    private static void Seed(LicenseStore store, int count)
    {
        // A fixed PUBLIC key is needed only for comparable identity records; there is no private signing material here.
        const string key = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEaxfR8uEsQkf4vOblY6RA8ncDfYEt6zOg9KE5RdiYwpZP40Li/hp/m47n60p8D54WK84zV2sxXs7LtkBoN79R9Q==";
        string deviceId = LicenseCrypto.Digest(Convert.FromBase64String(key));
        store.Change(2000000000, db =>
        {
            for (int i = 1; i <= count; i++)
            {
                string id = Id(i); bool installed = i % 2 == 0;
                var devices = new Dictionary<string, LicensedDevice>(StringComparer.Ordinal);
                if (installed) devices.Add(deviceId, new(deviceId, key, new string('b', 64), 2000000000));
                db.Licenses[id] = new(id, LicenseCrypto.Digest(Encoding.ASCII.GetBytes("test-credential-" + i)),
                    LicenseCrypto.Digest(Encoding.ASCII.GetBytes("test-request-" + i)), "synthetic license",
                    "hours", 6, "issue", 2000000000, installed ? 2000000000 : null, 2000021600, null, 1, "active", devices);
            }
            return 0;
        });
    }

    /// <summary>Creates deterministic non-secret comparison identifiers.</summary>
    /// <param name="index">Positive integer.</param>
    /// <returns>Canonical UUID used only inside generated fixtures.</returns>
    private static string Id(int index) => "00000000-0000-4000-8000-" + index.ToString("x12", CultureInfo.InvariantCulture);

    /// <summary>Constructs a minimal isolated store configuration, with a freshly generated integrity secret.</summary>
    /// <returns>Test-only configuration not usable as a signing authority.</returns>
    private static AuthorityConfiguration Config() => new(1, "00000000-0000-4000-8000-000000000001",
        "https://licenses.example/", "", "", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), new string('a', 64));

    /// <summary>Tests schema rejection without depending on messages or exception stack traces.</summary>
    /// <param name="json">Synthetic incomplete response.</param>
    /// <returns>True only if a controlled licensing exception occurs.</returns>
    private static bool RefusesJson(string json)
    {
        try { _ = LicenseJson.Read<ChallengeResponse>(Encoding.UTF8.GetBytes(json)); return false; }
        catch (LicenseException) { return true; }
    }

    /// <summary>Reads the actual referenced assembly version, rather than a caller-supplied label.</summary>
    /// <returns>Version being compared.</returns>
    private static string Version() => typeof(LicenseStore).Assembly.GetName().Version!.ToString();

    /// <summary>Independent clock with fixed start, explicit wall corrections and monotonic elapsed ticks.</summary>
    private sealed class ProbeTime : TimeProvider
    {
        /// <summary>Fixed non-secret UTC instant used for comparison.</summary>
        private DateTimeOffset utc = DateTimeOffset.FromUnixTimeSeconds(2000000000);
        /// <summary>Independent elapsed ticks.</summary>
        private long ticks;
        /// <summary>Uses TimeSpan ticks as the measurement frequency.</summary>
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        /// <summary>Returns the adjustable wall clock.</summary>
        /// <returns>Controlled UTC instant.</returns>
        public override DateTimeOffset GetUtcNow() => utc;
        /// <summary>Returns elapsed monotonic ticks.</summary>
        /// <returns>Controlled timestamp.</returns>
        public override long GetTimestamp() => ticks;
        /// <summary>Moves only UTC.</summary>
        /// <param name="amount">Signed correction.</param>
        public void Shift(TimeSpan amount) => utc += amount;
        /// <summary>Advances UTC and monotonic time together.</summary>
        /// <param name="amount">Nonnegative elapsed interval.</param>
        public void Advance(TimeSpan amount) { utc += amount; ticks += amount.Ticks; }
    }
}

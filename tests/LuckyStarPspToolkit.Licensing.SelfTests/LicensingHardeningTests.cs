using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using LuckyStarPspToolkit.Licensing;
using LuckyStarPspToolkit.Licensing.Authority;

/// <summary>Regressions for clock continuity, durable anti-rollback, snapshot isolation and proxy overload handling.</summary>
internal static class LicensingHardeningTests
{
    /// <summary>Throws without exposing credentials when an expected invariant fails.</summary>
    /// <param name="condition">Required invariant.</param>
    /// <param name="message">Non-secret diagnostic.</param>
    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    /// <summary>Requires one controlled refusal code and no incidental exceptions.</summary>
    /// <param name="code">Expected reason.</param>
    /// <param name="body">Operation expected to refuse.</param>
    private static void Reject(string code, Action body)
    {
        try { body(); }
        catch (LicenseException ex) { Assert(ex.Code == code, $"Expected {code}, received {ex.Code}"); return; }
        throw new InvalidOperationException("Missing refusal: " + code);
    }

    /// <summary>Time must keep advancing after a forward correction followed by rollback.</summary>
    public static void ClockForwardBack()
    {
        var time = new LicenseTestTime(); var clock = new AuthorityClock(time);
        long first = clock.Now(); time.ShiftWall(TimeSpan.FromDays(2)); long future = clock.Now();
        Assert(future == first + 172800, "Forward correction was not incorporated");
        time.ShiftWall(TimeSpan.FromDays(-2)); time.Advance(TimeSpan.FromSeconds(7));
        Assert(clock.Now() == future + 7, "Authority clock froze after forward/backward correction");
    }

    /// <summary>Frequent reads retain subsecond elapsed time rather than rounding it away on each rebase.</summary>
    public static void ClockSubseconds()
    {
        var time = new LicenseTestTime(); var clock = new AuthorityClock(time); long first = clock.Now();
        time.ShiftWall(TimeSpan.FromDays(-1));
        for (int i = 0; i < 1000; i++) { time.Advance(TimeSpan.FromMilliseconds(1)); _ = clock.Now(); }
        Assert(clock.Now() == first + 1, "Subsecond elapsed progress was lost");
    }

    /// <summary>Compares deterministic adversarial wall-clock changes against an independently maintained accepted timeline.</summary>
    public static void ClockSequence()
    {
        var time = new LicenseTestTime(); var clock = new AuthorityClock(time);
        DateTimeOffset expected = time.GetUtcNow(); var random = new Random(15001);
        for (int i = 0; i < 1024; i++)
        {
            var elapsed = TimeSpan.FromMilliseconds(random.Next(1, 4000)); time.Advance(elapsed);
            time.ShiftWall(TimeSpan.FromSeconds(random.Next(-10000, 10000)));
            expected += elapsed;
            if (time.GetUtcNow() > expected) expected = time.GetUtcNow();
            Assert(clock.Now() == expected.ToUnixTimeSeconds(), "Clock oracle disagreement");
        }
    }

    /// <summary>A restart must not revive a timed license by rolling back behind a previously returned expiry refusal.</summary>
    public static void RestartCannotRevive()
    {
        using var f = new Fixture(); var issue = f.Issue(); f.Grant(issue);
        f.Time.Advance(TimeSpan.FromHours(2)); Reject("LICENSE_EXPIRED", () => f.Grant(issue, "check"));
        f.Authority.Dispose(); f.Time.ShiftWall(TimeSpan.FromMinutes(-90));
        Reject("SERVER_CLOCK_ROLLBACK", () => { using var ignored = new LicenseAuthority(f.Directory, f.Password, f.Time); });
        f.Time.ShiftWall(TimeSpan.FromMinutes(90)); f.Restart();
        Reject("LICENSE_EXPIRED", () => f.Grant(issue, "check"));
    }

    /// <summary>A schema-two database requires its paired clock checkpoint even when its last entitlement write is old.</summary>
    public static void MissingCheckpoint()
    {
        using var f = new Fixture(); f.Issue(); f.Authority.Dispose();
        File.Delete(Path.Combine(f.Directory, "server-clock.json"));
        Reject("SERVER_CLOCK_MISSING", () => { using var ignored = new LicenseAuthority(f.Directory, f.Password, f.Time); });
    }

    /// <summary>A modified clock MAC fails closed and a failed constructor releases the process lock for a corrected restart.</summary>
    public static void CorruptCheckpoint()
    {
        using var f = new Fixture(); f.Authority.Dispose(); string path = Path.Combine(f.Directory, "server-clock.json");
        byte[] original = File.ReadAllBytes(path);
        var value = JsonNode.Parse(original)!.AsObject(); value["mac"] = new string('0', 64);
        PrivateFiles.Write(path, Encoding.UTF8.GetBytes(value.ToJsonString()));
        Reject("SERVER_CLOCK_INTEGRITY", () => { using var ignored = new LicenseAuthority(f.Directory, f.Password, f.Time); });
        PrivateFiles.Write(path, original); f.Restart(); Assert(f.Authority.List().Length == 0, "Constructor leaked the writer lock");
    }

    /// <summary>Repeated lease checks within one accepted second never replace the checkpoint or entitlement file.</summary>
    public static void CheckpointWriteCoalescing()
    {
        using var f = new Fixture(); var issue = f.Issue(); f.Grant(issue);
        string path = Path.Combine(f.Directory, "server-clock.json"); string dbPath = Path.Combine(f.Directory, "licenses.json");
        byte[] original = File.ReadAllBytes(path), database = File.ReadAllBytes(dbPath);
        // Setting the mtime far in the past detects replacement even if the new content would be identical.
        DateTime sentinel = DateTime.UtcNow.AddDays(-7); File.SetLastWriteTimeUtc(path, sentinel);
        DateTime captured = File.GetLastWriteTimeUtc(path);
        for (int i = 0; i < 20; i++) f.Grant(issue, "check");
        Assert(File.GetLastWriteTimeUtc(path) == captured, "Same-second requests rewrote the checkpoint");
        Assert(File.ReadAllBytes(path).AsSpan().SequenceEqual(original), "Same-second checkpoint changed");
        f.Time.Advance(TimeSpan.FromSeconds(1)); f.Grant(issue, "check");
        Assert(!File.ReadAllBytes(path).AsSpan().SequenceEqual(original), "New second was not persisted");
        Assert(File.ReadAllBytes(dbPath).AsSpan().SequenceEqual(database), "Heartbeat rewrote the entitlement database");
    }

    /// <summary>A genuine schema-one snapshot upgrades without changing existing key digests, UUIDs, expiry or device slots.</summary>
    public static void LegacyMigration()
    {
        using var f = new Fixture(); var issue = f.Issue(); var granted = f.Grant(issue);
        f.Authority.Dispose(); var config = Config(f);
        string path = Path.Combine(f.Directory, "licenses.json");
        var envelope = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
        var payload = JsonNode.Parse(Convert.FromBase64String(envelope["payload"]!.GetValue<string>()))!.AsObject();
        payload["schema"] = 1; byte[] legacy = Encoding.UTF8.GetBytes(payload.ToJsonString());
        envelope["payload"] = Convert.ToBase64String(legacy);
        envelope["mac"] = Convert.ToHexString(HMACSHA256.HashData(Convert.FromBase64String(config.Pepper), legacy));
        PrivateFiles.Write(path, Encoding.UTF8.GetBytes(envelope.ToJsonString())); File.Delete(Path.Combine(f.Directory, "server-clock.json"));
        f.Restart(); var updated = f.Grant(issue, "check");
        Assert(updated.LicenseId == issue.Id && updated.EntitlementExpires == granted.EntitlementExpires, "Migration changed entitlement");
        Assert(f.Authority.Get(issue.Id).Devices.Length == 1, "Migration changed installations");
        f.Authority.Dispose(); using var store = new LicenseStore(f.Directory, config);
        Assert(store.Read(db => db.Schema) == 2, "Migration marker was not committed");
    }

    /// <summary>Concurrent check/get operations cannot persist older timestamps after newer observations or corrupt the checkpoint.</summary>
    public static void ConcurrentClockReads()
    {
        using var f = new Fixture(); var issue = f.Issue(); f.Grant(issue);
        Parallel.For(0, 64, index => { _ = f.Authority.Get(issue.Id); });
        f.Restart(); Assert(f.Grant(issue, "check").LicenseId == issue.Id, "Concurrent queries corrupted restart state");
    }

    /// <summary>Mutating or retaining a public read result must not mutate a committed entitlement.</summary>
    public static void ReadSnapshotIsolation()
    {
        using var f = new Fixture(); var issue = f.Issue(); f.Grant(issue); f.Authority.Dispose();
        using var store = new LicenseStore(f.Directory, Config(f));
        var leaked = store.Read(db => db); leaked.Licenses[issue.Id].Devices.Clear(); leaked.Requests.Clear(); leaked.Audit.Clear();
        leaked.Licenses.Clear(); Assert(store.Read(db => db.Licenses[issue.Id].Devices.Count) == 1, "Read result exposed committed collections");
    }

    /// <summary>Mutation callbacks and returned records may be retained, but cannot mutate the newly published state after commit.</summary>
    public static void ChangeSnapshotIsolation()
    {
        using var f = new Fixture(); var issue = f.Issue(); f.Grant(issue); f.Authority.Dispose();
        using var store = new LicenseStore(f.Directory, Config(f)); LicenseDatabase? captured = null;
        var returned = store.Change(f.Time.GetUtcNow().ToUnixTimeSeconds(), db => { captured = db; return db.Licenses[issue.Id]; });
        returned.Devices.Clear(); captured!.Licenses.Clear(); captured.Audit.Clear();
        Assert(store.Read(db => db.Licenses[issue.Id].Devices.Count) == 1, "Transaction result exposed committed collections");
    }

    /// <summary>Exceptions after nested dictionary mutation preserve both persisted and in-memory state.</summary>
    public static void FailedMutationIsolation()
    {
        using var f = new Fixture(); var issue = f.Issue(); f.Grant(issue); f.Authority.Dispose();
        using var store = new LicenseStore(f.Directory, Config(f)); byte[] before = File.ReadAllBytes(Path.Combine(f.Directory, "licenses.json"));
        Reject("INJECTED_FAILURE", () => store.Change<int>(f.Time.GetUtcNow().ToUnixTimeSeconds(), db =>
        { db.Licenses[issue.Id].Devices.Clear(); db.Audit.Clear(); throw new LicenseException("INJECTED_FAILURE", "Synthetic callback failure."); }));
        Assert(store.Read(db => db.Licenses[issue.Id].Devices.Count) == 1, "Failed transaction affected live snapshot");
        Assert(File.ReadAllBytes(Path.Combine(f.Directory, "licenses.json")).AsSpan().SequenceEqual(before), "Failed transaction changed disk");
    }

    /// <summary>A failed durable snapshot replacement leaves the previously committed memory state and revision untouched.</summary>
    public static void CommitWriteFailure()
    {
        using var f = new Fixture(); var issue = f.Issue(); f.Authority.Dispose();
        using var store = new LicenseStore(f.Directory, Config(f));
        string path = Path.Combine(f.Directory, "licenses.json"), backup = path + ".test-backup";
        long revision = store.Read(db => db.Revision); byte[] before = File.ReadAllBytes(path);
        File.Move(path, backup); Directory.CreateDirectory(path);
        try
        {
            bool failed = false;
            try { store.Change(f.Time.GetUtcNow().ToUnixTimeSeconds(), db => { db.Licenses[issue.Id] = db.Licenses[issue.Id] with { Status = "suspended" }; return 0; }); }
            catch (IOException) { failed = true; }
            Assert(failed, "Injected output collision was ignored");
            Assert(store.Read(db => db.Revision) == revision && store.Read(db => db.Licenses[issue.Id].Status) == "active", "Failed commit changed live state");
            Assert(!Directory.EnumerateFiles(f.Directory, "*.tmp").Any(), "Failed commit leaked a temporary snapshot");
        }
        finally { Directory.Delete(path); File.Move(backup, path); }
        Assert(File.ReadAllBytes(path).AsSpan().SequenceEqual(before), "Original snapshot was not preserved");
    }

    /// <summary>Failure to persist a new time observation prevents issuance of any new execution lease.</summary>
    public static void CheckpointWriteFailure()
    {
        using var f = new Fixture(); var issue = f.Issue(); f.Grant(issue);
        string path = Path.Combine(f.Directory, "server-clock.json"), backup = path + ".test-backup";
        File.Move(path, backup); Directory.CreateDirectory(path); f.Time.Advance(TimeSpan.FromSeconds(1));
        try
        {
            bool failed = false;
            try { f.Grant(issue, "check"); } catch (IOException) { failed = true; }
            Assert(failed, "A grant escaped without a durable time checkpoint");
            Assert(!Directory.EnumerateFiles(f.Directory, "*.tmp").Any(), "Failed clock write leaked a temporary file");
        }
        finally { Directory.Delete(path); File.Move(backup, path); }
        Assert(f.Grant(issue, "check").LicenseId == issue.Id, "Corrected storage could not recover");
    }

    /// <summary>Null nested records are rejected before committing or replacing the old snapshot.</summary>
    public static void InvalidSnapshotRefused()
    {
        using var f = new Fixture(); var issue = f.Issue(); f.Authority.Dispose();
        using var store = new LicenseStore(f.Directory, Config(f));
        Reject("DATABASE_INVALID", () => store.Change(f.Time.GetUtcNow().ToUnixTimeSeconds(), db =>
        { db.Licenses[issue.Id] = null!; return 0; }));
        Assert(store.Read(db => db.Licenses.ContainsKey(issue.Id)), "Invalid callback destroyed original entitlement");
    }

    /// <summary>Absent mandatory constructor values cannot silently deserialize as null or zero.</summary>
    public static void MissingJsonMembers()
    {
        Reject("JSON_INVALID", () => LicenseJson.Read<ChallengeResponse>("{}"u8));
        Reject("JSON_INVALID", () => LicenseJson.Read<IssueLicenseRequest>("{}"u8));
        Reject("JSON_INVALID", () => LicenseJson.Read<ExecutionLease>("{}"u8));
    }

    /// <summary>Explicit null is rejected for non-nullable protocol fields independently of missing-field validation.</summary>
    public static void NullJsonMembers()
    {
        Reject("JSON_INVALID", () => LicenseJson.Read<ChallengeResponse>("{\"challenge\":null}"u8));
        Reject("JSON_INVALID", () => LicenseJson.Read<OwnerConnection>("{\"adminUrl\":null,\"token\":null}"u8));
    }

    /// <summary>Optional constructor defaults and explicitly nullable deadlines remain compatible with previous valid messages.</summary>
    public static void OptionalJsonMembers()
    {
        string text = "{\"requestId\":\"" + Guid.NewGuid().ToString("D") + "\",\"licenseId\":\"" + Guid.NewGuid().ToString("D") + "\",\"action\":\"suspend\"}";
        var request = LicenseJson.Read<ChangeLicenseRequest>(Encoding.UTF8.GetBytes(text));
        Assert(request.Unit == "" && request.Amount == 0 && request.DeviceId == "", "Optional defaults changed");
        var value = LicenseJson.Read<LicenseTrust>(LicenseJson.Write(new LicenseTrust(1, "p", "i", "https://licenses.example/", new())));
        Assert(!value.DevelopmentLoopback, "Optional trust default changed");
    }

    /// <summary>All selected transient status codes map to retryable failures even when a reverse proxy returns HTML or no body.</summary>
    public static void ProxyTransientErrors()
    {
        foreach (int status in new[] { 408, 429, 500, 502, 503, 504 })
            ProxyReply(status, "text/html", "unavailable", status == 429 ? "RATE_LIMIT" : "SERVER_BUSY");
    }

    /// <summary>An HTTP success without a valid signed protocol must never be interpreted as temporary permission.</summary>
    public static void ProxyInvalidSuccess() => ProxyReply(200, "text/html", "ok", "SERVER_RESPONSE");

    /// <summary>An unavailable proxy does not cause premature shutdown while a previous short grant is still valid, and never extends it.</summary>
    public static void ProxySessionExpiry() => ProxySessionExpiryAsync().GetAwaiter().GetResult();

    /// <summary>Runs an actual overload listener through at least one renewal and waits for the original monotonic deadline.</summary>
    /// <returns>Completion after the non-renewable session is denied.</returns>
    private static async Task ProxySessionExpiryAsync()
    {
        using var f = new Fixture(); int port = FreePort(); using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start(); int replies = 0;
        var responder = Task.Run(async () =>
        {
            try
            {
                while (listener.IsListening)
                {
                    var context = await listener.GetContextAsync();
                    context.Response.StatusCode = 503; context.Response.Close(); Interlocked.Increment(ref replies);
                }
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { }
        });
        try
        {
            using var transport = new LicenseTransport(f.Trust with { ServerUrl = $"http://127.0.0.1:{port}/" }, f.Device);
            using var signal = new ManualResetEventSlim(); string reason = "";
            var started = System.Diagnostics.Stopwatch.StartNew();
            using var session = new LicenseSession(transport, Guid.NewGuid().ToString("D"), TimeSpan.FromSeconds(3), code => { reason = code; signal.Set(); });
            Assert(await Task.Run(() => signal.Wait(TimeSpan.FromSeconds(6))), "Session did not expire");
            Assert(replies > 0 && started.Elapsed >= TimeSpan.FromSeconds(2.7), "Proxy overload caused immediate invalidation");
            Assert(reason == "LICENSE_LEASE_EXPIRED", "Proxy response was not treated as an unrenewed old grant");
        }
        finally { listener.Stop(); await responder; }
    }

    /// <summary>Returns private configuration only inside disposable synthetic tests.</summary>
    /// <param name="fixture">Owner fixture whose files never leave the test directory.</param>
    /// <returns>Matching configuration.</returns>
    private static AuthorityConfiguration Config(Fixture fixture) =>
        LicenseJson.Read<AuthorityConfiguration>(PrivateFiles.Read(Path.Combine(fixture.Directory, "authority.json")));

    /// <summary>Reserves and releases an ephemeral loopback port for a synthetic HTTP listener.</summary>
    /// <returns>Available TCP port; bind conflicts fail the test instead of being hidden.</returns>
    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port;
    }

    /// <summary>Executes one real HTTP exchange against a controlled non-protocol response.</summary>
    /// <param name="status">HTTP response status.</param>
    /// <param name="type">Response media type.</param>
    /// <param name="body">Non-secret body.</param>
    /// <param name="expected">Exact controlled error code.</param>
    private static void ProxyReply(int status, string type, string body, string expected)
    {
        using var f = new Fixture(); int port = FreePort(); using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
        var reply = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            context.Response.StatusCode = status; context.Response.ContentType = type;
            byte[] bytes = Encoding.UTF8.GetBytes(body); context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes); context.Response.Close();
        });
        try
        {
            using var transport = new LicenseTransport(f.Trust with { ServerUrl = $"http://127.0.0.1:{port}/" }, f.Device);
            Reject(expected, () => transport.ExchangeAsync("check", Guid.NewGuid().ToString("D"), "").GetAwaiter().GetResult());
            reply.GetAwaiter().GetResult();
        }
        finally { listener.Stop(); }
    }
}

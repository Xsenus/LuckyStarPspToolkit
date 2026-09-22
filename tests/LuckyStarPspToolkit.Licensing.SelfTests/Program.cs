using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using LuckyStarPspToolkit.Licensing;
using LuckyStarPspToolkit.Licensing.Authority;
using LuckyStarPspToolkit.Cli;

return LicensingTests.Run();

/// <summary>Executable licensing regressions: lifecycle, cryptography, replay, persistence, HTTP separation and watchdog behavior.</summary>
internal static class LicensingTests
{
    /// <summary>Number of completed groups; assertions are executed inside each group.</summary>
    private static int count;

    /// <summary>Runs every licensing group and returns nonzero on the first reproducible failure.</summary>
    /// <returns>Zero only after all groups complete.</returns>
    public static int Run()
    {
        try
        {
            Test("atomic initialization refuses an existing authority", InitializationSafety);
            Test("bounded challenge cache recovers after expiration", ChallengeCapacity);
            Test("initial artifacts contain no customer entitlement", InitialArtifacts);
            Test("hours/days/calendar years and leap days", Durations);
            Test("invalid and ambiguous duration ranges rejected", BadDurations);
            Test("first activation starts trial only once", ActivationStart);
            Test("issuance-start trial expires without activation", IssueStart);
            Test("exclusive expiry boundary rejects activation and check", ExpiryBoundary);
            Test("permanent entitlement still requires online grant", Permanent);
            Test("activation deadline is enforced", ActivationDeadline);
            Test("active installation reactivation is idempotent", RepeatedActivation);
            Test("device count under concurrent activation", ConcurrentDeviceLimit);
            Test("possession proof required even with license ID", Possession);
            Test("one-use challenge rejects replay", Replay);
            Test("expired challenges rejected", ChallengeExpiry);
            Test("request fields are bound to proof", ProofBinding);
            Test("signed grant binds client nonce", ResponseNonce);
            Test("grant tampering and untrusted signer rejected", Tampering);
            Test("fixed product and issuer scopes", ProductIssuer);
            Test("invalid encodings and extra JSON fields rejected", Malformed);
            Test("literal and escaped duplicate JSON keys rejected", DuplicateJson);
            Test("issuance UUID is idempotent without key recovery on server", IssueIdempotency);
            Test("suspend and resume do not reset expiry", SuspendResume);
            Test("revocation cannot be undone", Revoke);
            Test("extension retries cannot double-extend", ExtendIdempotency);
            Test("expired trial extension starts from server now", ExtendExpired);
            Test("permanent conversion and reset preserve original activation", PermanentReset);
            Test("device reset rejects old check", DeviceReset);
            Test("restart retains first activation and device slots", Persistence);
            Test("missing and tampered database fail closed", CorruptStore);
            Test("wrong signing passphrase rejected", WrongPassword);
            Test("second writer is rejected", ExclusiveWriter);
            Test("server backward clock is guarded", ClockRollback);
            Test("database and audit omit raw access key", NoRawKey);
            Test("HTTPS policy and fixed public key IDs", TrustPolicy);
            Test("unconfigured executable cannot process files", UnconfiguredGate);
            Test("private state writes preserve old output on collision", PrivateWrites);
            Test("actual loopback HTTP activation/check and owner isolation", HttpLifecycle);
            Test("watchdog expires even while renewal could block", WatchdogExpiry);
            Test("long-running session notices revocation", WatchdogRevocation);
            Test("clock progresses after forward/backward correction", LicensingHardeningTests.ClockForwardBack);
            Test("clock retains subsecond progress", LicensingHardeningTests.ClockSubseconds);
            Test("clock agrees with 1024 deterministic adjustments", LicensingHardeningTests.ClockSequence);
            Test("restart cannot revive expired license", LicensingHardeningTests.RestartCannotRevive);
            Test("required checkpoint deletion blocks restart", LicensingHardeningTests.MissingCheckpoint);
            Test("checkpoint tampering releases constructor lock", LicensingHardeningTests.CorruptCheckpoint);
            Test("checkpoint coalesces same-second checks", LicensingHardeningTests.CheckpointWriteCoalescing);
            Test("schema one upgrades without resetting entitlements", LicensingHardeningTests.LegacyMigration);
            Test("concurrent clock queries preserve checkpoint", LicensingHardeningTests.ConcurrentClockReads);
            Test("read snapshot cannot mutate committed collections", LicensingHardeningTests.ReadSnapshotIsolation);
            Test("returned transaction cannot mutate committed collections", LicensingHardeningTests.ChangeSnapshotIsolation);
            Test("failed nested mutation leaves memory and disk unchanged", LicensingHardeningTests.FailedMutationIsolation);
            Test("failed store replacement preserves committed state", LicensingHardeningTests.CommitWriteFailure);
            Test("failed clock checkpoint prevents new permission", LicensingHardeningTests.CheckpointWriteFailure);
            Test("invalid nested record is refused before persistence", LicensingHardeningTests.InvalidSnapshotRefused);
            Test("required JSON constructor fields are enforced", LicensingHardeningTests.MissingJsonMembers);
            Test("nonnullable JSON fields reject null", LicensingHardeningTests.NullJsonMembers);
            Test("optional JSON values remain compatible", LicensingHardeningTests.OptionalJsonMembers);
            Test("HTTP proxy errors map to bounded retries", LicensingHardeningTests.ProxyTransientErrors);
            Test("HTTP success still requires valid protocol", LicensingHardeningTests.ProxyInvalidSuccess);
            Test("proxy overload cannot extend lease or revoke it early", LicensingHardeningTests.ProxySessionExpiry);
            Test("reserve/session regression ExpirySurvivesRestart", ReserveSafetyTests.ExpirySurvivesRestart);
            Test("reserve/session regression RollbackSurvivesRestart", ReserveSafetyTests.RollbackSurvivesRestart);
            Test("reserve/session regression FreshTokenRecoversExpiry", ReserveSafetyTests.FreshTokenRecoversExpiry);
            Test("reserve/session regression DenialWriteFailure", ReserveSafetyTests.DenialWriteFailure);
            Test("reserve/session regression ResetDoesNotReviveReserve", ReserveSafetyTests.ResetDoesNotReviveReserve);
            Test("reserve/session regression ResetScope", ReserveSafetyTests.ResetScope);
            Test("reserve/session regression ReserveProofAndNonce", ReserveSafetyTests.ReserveProofAndNonce);
            Test("reserve/session regression AuditTail", ReserveSafetyTests.AuditTail);
            Test("reserve/session regression SessionPrivacy", ReserveSafetyTests.SessionPrivacy);
            Test("reserve/session regression SessionRevokeOthers", ReserveSafetyTests.SessionRevokeOthers);
            Test("reserve/session regression SessionRevokeGuards", ReserveSafetyTests.SessionRevokeGuards);
            Test("reserve/session regression SessionInventoryExpiration", ReserveSafetyTests.SessionInventoryExpiration);
            Test("web actual HTTP admission and incomplete uploads", WebHttpAdmissionTests.HttpAdmission);
            Test("web concurrency MalformedDirectApi", WebConcurrencyTests.MalformedDirectApi);
            Test("web concurrency SessionProgress", WebConcurrencyTests.SessionProgress);
            Test("web concurrency ReservedLane", WebConcurrencyTests.ReservedLane);
            Test("web concurrency LoginAdmission", WebConcurrencyTests.LoginAdmission);
            Test("web concurrency ReauthenticationAdmission", WebConcurrencyTests.ReauthenticationAdmission);
            Test("web concurrency LogoutWins", WebConcurrencyTests.LogoutWins);
            Test("web concurrency RevocationWins", WebConcurrencyTests.RevocationWins);
            Test("web concurrency IdleExpiryWins", WebConcurrencyTests.IdleExpiryWins);
            Test("web concurrency AbsoluteExpiryWins", WebConcurrencyTests.AbsoluteExpiryWins);
            Test("web concurrency FreshBoundary", WebConcurrencyTests.FreshBoundary);
            Test("web concurrency RecoveryCrossLane", WebConcurrencyTests.RecoveryCrossLane);
            Test("web concurrency TotpCrossLane", WebConcurrencyTests.TotpCrossLane);
            Test("web concurrency PreCancelled", WebConcurrencyTests.PreCancelled);
            Test("web concurrency LoginCancellation", WebConcurrencyTests.LoginCancellation);
            Test("web concurrency ReauthenticationCancellation", WebConcurrencyTests.ReauthenticationCancellation);
            Test("web concurrency DerivationFailure", WebConcurrencyTests.DerivationFailure);
            Test("web concurrency BadFactor", WebConcurrencyTests.BadFactor);
            Test("web concurrency ShutdownWins", WebConcurrencyTests.ShutdownWins);
            Test("web concurrency SeparateLoginBudget", WebConcurrencyTests.SeparateLoginBudget);
            Test("web concurrency SeparateReauthenticationBudget", WebConcurrencyTests.SeparateReauthenticationBudget);
            Test("web concurrency SessionBudgetBinding", WebConcurrencyTests.SessionBudgetBinding);
            Test("web concurrency TotpExpiryDuringVerification", WebConcurrencyTests.TotpExpiryDuringVerification);
            Test("web concurrency AccountWriteFailure", WebConcurrencyTests.AccountWriteFailure);
            Test("web concurrency AdmissionAuthentication", WebConcurrencyTests.AdmissionAuthentication);
            Test("owner query CompleteTraversal", OwnerQueryTests.CompleteTraversal);
            Test("owner query FiltersAndCounts", OwnerQueryTests.FiltersAndCounts);
            Test("owner query EmptyPages", OwnerQueryTests.EmptyPages);
            Test("owner query InvalidQueries", OwnerQueryTests.InvalidQueries);
            Test("owner query CursorBindings", OwnerQueryTests.CursorBindings);
            Test("owner query ChangedRevision", OwnerQueryTests.ChangedRevision);
            Test("owner query CursorDeadline", OwnerQueryTests.CursorDeadline);
            Test("owner query RestartInvalidatesCursor", OwnerQueryTests.RestartInvalidatesCursor);
            Test("owner query ExpirySnapshot", OwnerQueryTests.ExpirySnapshot);
            Test("owner query ProjectionPrivacy", OwnerQueryTests.ProjectionPrivacy);
            Test("owner query ReserveTraversal", OwnerQueryTests.ReserveTraversal);
            Test("owner query ReserveEligibility", OwnerQueryTests.ReserveEligibility);
            Test("reserve/web DefaultOff", WebReserveTests.DefaultOff);
            Test("reserve/web ActivationRequired", WebReserveTests.ActivationRequired);
            Test("reserve/web HourBounds", WebReserveTests.HourBounds);
            Test("reserve/web ParentExpiry", WebReserveTests.ParentExpiry);
            Test("reserve/web PermanentBound", WebReserveTests.PermanentBound);
            Test("reserve/web EpochRetirement", WebReserveTests.EpochRetirement);
            Test("reserve/web PolicyIdempotency", WebReserveTests.PolicyIdempotency);
            Test("reserve/web IssueIdempotency", WebReserveTests.IssueIdempotency);
            Test("reserve/web RevokeIndependence", WebReserveTests.RevokeIndependence);
            Test("reserve/web ParentDenial", WebReserveTests.ParentDenial);
            Test("reserve/web Replay", WebReserveTests.Replay);
            Test("reserve/web Scope", WebReserveTests.Scope);
            Test("reserve/web DomainSeparation", WebReserveTests.DomainSeparation);
            Test("reserve/web Tamper", WebReserveTests.Tamper);
            Test("reserve/web CacheExpiry", WebReserveTests.CacheExpiry);
            Test("reserve/web CacheRestart", WebReserveTests.CacheRestart);
            Test("reserve/web CacheRollback", WebReserveTests.CacheRollback);
            Test("reserve/web CacheMonotonic", WebReserveTests.CacheMonotonic);
            Test("reserve/web CacheBlock", WebReserveTests.CacheBlock);
            Test("reserve/web CacheTamper", WebReserveTests.CacheTamper);
            Test("reserve/web ReservePersistence", WebReserveTests.ReservePersistence);
            Test("reserve/web TotpVectors", WebReserveTests.TotpVectors);
            Test("reserve/web TotpReplay", WebReserveTests.TotpReplay);
            Test("reserve/web RecoveryOnce", WebReserveTests.RecoveryOnce);
            Test("reserve/web RecoveryPersistence", WebReserveTests.RecoveryPersistence);
            Test("reserve/web CsrfLogout", WebReserveTests.CsrfLogout);
            Test("reserve/web IdleExpiry", WebReserveTests.IdleExpiry);
            Test("reserve/web AbsoluteExpiry", WebReserveTests.AbsoluteExpiry);
            Test("reserve/web FreshAuth", WebReserveTests.FreshAuth);
            Test("reserve/web LoginRate", WebReserveTests.LoginRate);
            Test("reserve/web SetupSafety", WebReserveTests.SetupSafety);
            Test("reserve/web ConcurrentRecovery", WebReserveTests.ConcurrentRecovery);
            Test("reserve/web EnrollmentFailure", WebReserveTests.EnrollmentFailure);
            Test("reserve/web FractionalCacheRestart", WebReserveTests.FractionalCacheRestart);
            Test("reserve/web RecoveryNeedsPassword", WebReserveTests.RecoveryNeedsPassword);
            Test("reserve/web WatchdogReserveFallback", WebReserveTests.WatchdogReserveFallback);
            Console.WriteLine($"LICENSING TESTS: {count} groups passed; native Windows CNG/TLS deployment are separate acceptance gates.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"LICENSING TEST FAILED after {count} groups: {ex}"); return 1; }
    }

    /// <summary>Runs one group without counting it until all assertions pass.</summary>
    /// <param name="name">Non-secret test description.</param>
    /// <param name="action">Group body.</param>
    private static void Test(string name, Action action) { action(); count++; Console.WriteLine("PASS " + name); }

    /// <summary>Asserts a condition without printing secret values.</summary>
    /// <param name="value">Condition.</param>
    /// <param name="message">Safe failure label.</param>
    private static void Assert(bool value, string message = "Assertion failed") { if (!value) throw new InvalidOperationException(message); }

    /// <summary>Requires an exact controlled refusal code.</summary>
    /// <param name="code">Expected code.</param>
    /// <param name="action">Operation that must fail.</param>
    private static void Reject(string code, Action action)
    {
        try { action(); }
        catch (LicenseException ex) { Assert(ex.Code == code, $"Expected {code}, got {ex.Code}"); return; }
        throw new InvalidOperationException("Expected rejection: " + code);
    }

    /// <summary>Initialization cannot overwrite a populated or even an empty existing destination.</summary>
    private static void InitializationSafety()
    {
        using var f = new Fixture(); byte[] original = File.ReadAllBytes(Path.Combine(f.Directory, "authority.json"));
        Reject("INIT_EXISTS", () => LicenseAuthority.Initialize(f.Directory, "https://licenses.example/", f.Password));
        Assert(original.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(f.Directory, "authority.json"))));
        string empty = Path.Combine(Path.GetTempPath(), "lsp-init-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try { Reject("INIT_EXISTS", () => LicenseAuthority.Initialize(empty, "https://licenses.example/", f.Password)); Assert(!Directory.EnumerateFileSystemEntries(empty).Any()); }
        finally { Directory.Delete(empty); }
    }

    /// <summary>Expired challenges free capacity without retaining unbounded request state.</summary>
    private static void ChallengeCapacity()
    {
        using var f = new Fixture(); var request = new ChallengeRequest(LicenseCrypto.Product, "check", f.Device.PublicKey, f.Device.HostBinding);
        for (int i = 0; i < 4096; i++) f.Authority.ChallengeFor(request);
        Reject("CHALLENGE_BUSY", () => f.Authority.ChallengeFor(request));
        f.Time.Advance(TimeSpan.FromSeconds(60)); Assert(f.Authority.ChallengeFor(request).Challenge.Length == 43);
    }

    /// <summary>Owner initialization generates distinct public trust and protected owner files, without any working customer key.</summary>
    private static void InitialArtifacts()
    {
        using var f = new Fixture(); Assert(f.Authority.List().Length == 0);
        var trust = LicenseJson.Read<LicenseTrust>(PrivateFiles.Read(Path.Combine(f.Directory, "client-trust.json")));
        Assert(trust.Issuer == f.Trust.Issuer && trust.PublicKeys.Count == 1);
        string publicJson = File.ReadAllText(Path.Combine(f.Directory, "client-trust.json"));
        Assert(!publicJson.Contains("encryptedPkcs8", StringComparison.Ordinal) && !publicJson.Contains("pepper", StringComparison.Ordinal));
        if (!OperatingSystem.IsWindows()) Assert(File.GetUnixFileMode(Path.Combine(f.Directory, "authority.json")) == (UnixFileMode.UserRead | UnixFileMode.UserWrite));
    }

    /// <summary>Checks UTC calendar durations independently of local timezone.</summary>
    private static void Durations()
    {
        long start = new DateTimeOffset(2032, 2, 29, 15, 30, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        Assert(LicenseDurations.Expires(start, "hours", 6) == start + 21600);
        Assert(LicenseDurations.Expires(start, "days", 7) == start + 604800);
        Assert(DateTimeOffset.FromUnixTimeSeconds(LicenseDurations.Expires(start, "years", 1)!.Value) == new DateTimeOffset(2033, 2, 28, 15, 30, 0, TimeSpan.Zero));
        Assert(LicenseDurations.Expires(start, "permanent", 0) is null);
    }

    /// <summary>Rejects zero/negative/oversized durations and nonzero perpetual quantities.</summary>
    private static void BadDurations()
    {
        foreach (var item in new[] { ("hours", 0), ("days", -1), ("years", 101), ("permanent", 1), ("seconds", 1) })
            Reject("DURATION_INVALID", () => LicenseDurations.Validate(item.Item1, item.Item2));
    }

    /// <summary>Delayed first use does not consume the trial; resetting/restarting never moves its start.</summary>
    private static void ActivationStart()
    {
        using var f = new Fixture(); var issue = f.Issue(); f.Time.Advance(TimeSpan.FromDays(4)); long start = f.Time.GetUtcNow().ToUnixTimeSeconds();
        var lease = f.Grant(issue);
        Assert(lease.EntitlementExpires == start + 3600); f.Time.Advance(TimeSpan.FromMinutes(10));
        Assert(f.Grant(issue).EntitlementExpires == start + 3600);
    }

    /// <summary>Issue-time licenses can expire before first use.</summary>
    private static void IssueStart()
    {
        using var f = new Fixture(); var issue = f.Issue(starts: "issue"); f.Time.Advance(TimeSpan.FromHours(1));
        Reject("LICENSE_EXPIRED", () => f.Grant(issue));
    }

    /// <summary>Expiry is exclusive at the exact UTC second.</summary>
    private static void ExpiryBoundary()
    {
        using var f = new Fixture(); var issue = f.Issue(); var lease = f.Grant(issue); f.Time.Advance(TimeSpan.FromSeconds(3599));
        Assert(f.Grant(issue, "check").ValidUntil == lease.EntitlementExpires); f.Time.Advance(TimeSpan.FromSeconds(1));
        Reject("LICENSE_EXPIRED", () => f.Grant(issue, "check")); Reject("LICENSE_EXPIRED", () => f.Grant(issue));
    }

    /// <summary>Perpetual entitlement has a bounded, not perpetual, execution grant.</summary>
    private static void Permanent()
    {
        using var f = new Fixture(); var issue = f.Issue("permanent", 0); f.Grant(issue); f.Time.Advance(TimeSpan.FromDays(3650));
        var grant = f.Grant(issue, "check"); Assert(grant.EntitlementExpires is null && grant.ValidUntil - grant.ServerNow <= 120);
    }

    /// <summary>A first-use trial cannot be first activated after the separately configured activation deadline.</summary>
    private static void ActivationDeadline()
    {
        using var f = new Fixture(); var issue = f.NewIssue() with { ActivateBefore = f.Time.GetUtcNow().ToUnixTimeSeconds() + 30 };
        f.Authority.Issue(issue); f.Time.Advance(TimeSpan.FromSeconds(30)); Reject("ACTIVATION_EXPIRED", () => f.Grant(issue));
    }

    /// <summary>Repeated activation on the same key does not consume slots or write a new expiry.</summary>
    private static void RepeatedActivation()
    {
        using var f = new Fixture(); var issue = f.Issue(); f.Grant(issue); string before = File.ReadAllText(Path.Combine(f.Directory, "licenses.json"));
        f.Grant(issue); Assert(f.Authority.Get(issue.Id).Devices.Length == 1); Assert(File.ReadAllText(Path.Combine(f.Directory, "licenses.json")) == before);
    }

    /// <summary>Only one parallel caller may obtain the last device slot.</summary>
    private static void ConcurrentDeviceLimit()
    {
        using var f = new Fixture(); var issue = f.Issue(); int accepted = 0, denied = 0;
        Parallel.For(0, 12, _ =>
        {
            using var device = Fixture.NewDevice();
            try { f.Grant(issue, device: device); Interlocked.Increment(ref accepted); }
            catch (LicenseException ex) when (ex.Code == "DEVICE_LIMIT") { Interlocked.Increment(ref denied); }
        });
        Assert(accepted == 1 && denied == 11); Assert(f.Authority.Get(issue.Id).Devices.Length == 1);
    }

    /// <summary>A stolen license ID without the activated private installation key cannot authorize.</summary>
    private static void Possession()
    {
        using var f = new Fixture(); var issue = f.Issue(); f.Grant(issue); using var other = Fixture.NewDevice();
        Reject("DEVICE_NOT_ACTIVATED", () => f.Grant(issue, "check", other));
    }

    /// <summary>A valid recorded request can consume its challenge only once.</summary>
    private static void Replay()
    {
        using var f = new Fixture(); var issue = f.Issue(); var request = f.Request(issue); f.Authority.Authorize(request);
        Reject("CHALLENGE_INVALID", () => f.Authority.Authorize(request));
    }

    /// <summary>Old server nonces expire even with valid possession signatures.</summary>
    private static void ChallengeExpiry()
    {
        using var f = new Fixture(); var request = f.Request(f.Issue()); f.Time.Advance(TimeSpan.FromSeconds(60));
        Reject("CHALLENGE_INVALID", () => f.Authority.Authorize(request));
    }

    /// <summary>Operation and host binding cannot be changed without re-signing.</summary>
    private static void ProofBinding()
    {
        using var f = new Fixture(); var request = f.Request(f.Issue());
        Reject("DEVICE_PROOF", () => f.Authority.Authorize(request with { HostBinding = new string('a', 64) }));
    }

    /// <summary>A previously signed valid lease is not valid for a new launch/request nonce.</summary>
    private static void ResponseNonce()
    {
        using var f = new Fixture(); var request = f.Request(f.Issue()); var response = f.Authority.Authorize(request);
        Reject("LEASE_BINDING", () => LicenseCrypto.VerifyLease(response.Token, f.Trust, request with { ClientNonce = LicenseCrypto.Nonce() }));
    }

    /// <summary>Single-byte token mutations cannot produce permission; unknown key IDs are not accepted.</summary>
    private static void Tampering()
    {
        using var f = new Fixture(); var request = f.Request(f.Issue()); string token = f.Authority.Authorize(request).Token;
        string[] parts = token.Split('.'); byte[] signature = LicenseCrypto.Decode(parts[3], 64); signature[7] ^= 1; parts[3] = LicenseCrypto.Encode(signature);
        Reject("LEASE_SIGNATURE", () => LicenseCrypto.VerifyLease(string.Join('.', parts), f.Trust, request));
        parts = token.Split('.'); parts[1] = new string('0', 16);
        Reject("LEASE_UNTRUSTED", () => LicenseCrypto.VerifyLease(string.Join('.', parts), f.Trust, request));
        parts = token.Split('.'); byte[] payload = LicenseCrypto.Decode(parts[2], 4096); payload[^3] ^= 1; parts[2] = LicenseCrypto.Encode(payload);
        Reject("LEASE_SIGNATURE", () => LicenseCrypto.VerifyLease(string.Join('.', parts), f.Trust, request));
    }

    /// <summary>Different issuer or product claims cannot reuse a grant.</summary>
    private static void ProductIssuer()
    {
        using var f = new Fixture(); var request = f.Request(f.Issue()); var response = f.Authority.Authorize(request);
        Reject("LEASE_BINDING", () => LicenseCrypto.VerifyLease(response.Token, f.Trust with { Issuer = Guid.NewGuid().ToString("D") }, request));
        Reject("REQUEST_INVALID", () => f.Authority.ChallengeFor(new("other-product", "check", f.Device.PublicKey, f.Device.HostBinding)));
    }

    /// <summary>Parsers reject malformed, oversized and schema-expanded data.</summary>
    private static void Malformed()
    {
        Reject("ENCODING_INVALID", () => LicenseCrypto.Decode("abc=", 8));
        Reject("ENCODING_INVALID", () => LicenseCrypto.Decode("_", 8));
        Reject("JSON_INVALID", () => LicenseJson.Read<ChallengeResponse>("{\"challenge\":\"x\",\"admin\":true}"u8));
        Reject("JSON_LIMIT", () => LicenseJson.Read<ChallengeResponse>(new byte[32769]));
        Reject("KEY_INVALID", () => LicenseCrypto.ImportPublic("abcd"));
        Reject("LICENSE_INVALID", () => LicenseCrypto.ValidateAccessKey("12345"));
    }

    /// <summary>Unicode-escaped aliases are duplicates just like repeated literal field names.</summary>
    private static void DuplicateJson()
    {
        Reject("JSON_DUPLICATE", () => LicenseJson.Read<ChallengeResponse>("{\"challenge\":\"x\",\"challenge\":\"y\"}"u8));
        Reject("JSON_DUPLICATE", () => LicenseJson.Read<ChallengeResponse>("{\"challenge\":\"x\",\"challen\\u0067e\":\"y\"}"u8));
    }

    /// <summary>Reusing an issuance UUID with changed semantics is rejected; exact retry returns the existing entitlement.</summary>
    private static void IssueIdempotency()
    {
        using var f = new Fixture(); var issue = f.Issue(); Assert(f.Authority.Issue(issue).Id == issue.Id); Assert(f.Authority.List().Length == 1);
        Reject("IDEMPOTENCY_CONFLICT", () => f.Authority.Issue(issue with { Amount = 2 }));
    }

    /// <summary>Suspension blocks checks and resume does not extend trial time.</summary>
    private static void SuspendResume()
    {
        using var f = new Fixture(); var issue = f.Issue(); long? expiry = f.Grant(issue).EntitlementExpires;
        f.Change(issue, "suspend"); Reject("LICENSE_SUSPENDED", () => f.Grant(issue, "check"));
        f.Change(issue, "resume"); Assert(f.Grant(issue, "check").EntitlementExpires == expiry);
    }

    /// <summary>Revoked entitlements never become valid through extension, resume or perpetual conversion.</summary>
    private static void Revoke()
    {
        using var f = new Fixture(); var issue = f.Issue(); f.Grant(issue); f.Change(issue, "revoke");
        Reject("LICENSE_REVOKED", () => f.Grant(issue, "check")); Reject("LICENSE_REVOKED", () => f.Change(issue, "resume"));
        Reject("LICENSE_REVOKED", () => f.Change(issue, "permanent"));
    }

    /// <summary>A lost extension response can be retried without adding time twice.</summary>
    private static void ExtendIdempotency()
    {
        using var f = new Fixture(); var issue = f.Issue(); long? original = f.Grant(issue).EntitlementExpires;
        var change = new ChangeLicenseRequest(Guid.NewGuid().ToString("D"), issue.Id, "extend", "days", 1);
        Assert(f.Authority.Change(change).ExpiresAt == original + 86400);
        Assert(f.Authority.Change(change).ExpiresAt == original + 86400);
        Reject("IDEMPOTENCY_CONFLICT", () => f.Authority.Change(change with { Amount = 2 }));
    }

    /// <summary>Expired licenses extend from now rather than wasting paid time in the past.</summary>
    private static void ExtendExpired()
    {
        using var f = new Fixture(); var issue = f.Issue(); f.Grant(issue); f.Time.Advance(TimeSpan.FromDays(3));
        var change = new ChangeLicenseRequest(Guid.NewGuid().ToString("D"), issue.Id, "extend", "hours", 2);
        Assert(f.Authority.Change(change).ExpiresAt == f.Time.GetUtcNow().ToUnixTimeSeconds() + 7200);
    }

    /// <summary>Converting a valid license to perpetual access clears only the deadline, not activation history.</summary>
    private static void PermanentReset()
    {
        using var f = new Fixture(); var issue = f.Issue(); f.Grant(issue); long? original = f.Authority.Get(issue.Id).ActivatedAt;
        Assert(f.Change(issue, "permanent").ExpiresAt is null); Assert(f.Grant(issue, "check").EntitlementExpires is null);
        Assert(f.Authority.Get(issue.Id).ActivatedAt == original);
    }

    /// <summary>Removing a device stops ID-only checks and never restarts the original trial timer.</summary>
    private static void DeviceReset()
    {
        using var f = new Fixture(); var issue = f.Issue(); long? expiry = f.Grant(issue).EntitlementExpires;
        f.Authority.Change(new(Guid.NewGuid().ToString("D"), issue.Id, "reset-device", DeviceId: f.Device.Id));
        Reject("DEVICE_NOT_ACTIVATED", () => f.Grant(issue, "check"));
        using var other = Fixture.NewDevice(); f.Time.Advance(TimeSpan.FromSeconds(50)); Assert(f.Grant(issue, device: other).EntitlementExpires == expiry);
    }

    /// <summary>Committed licenses/slots survive server restart, while outstanding nonces do not.</summary>
    private static void Persistence()
    {
        using var f = new Fixture(); var issue = f.Issue(); var first = f.Grant(issue); var request = f.Request(issue, "check"); f.Restart();
        Reject("CHALLENGE_INVALID", () => f.Authority.Authorize(request)); Assert(f.Grant(issue, "check").EntitlementExpires == first.EntitlementExpires);
    }

    /// <summary>Missing or tampered authoritative state is not silently recreated as an empty licensed database.</summary>
    private static void CorruptStore()
    {
        using var f = new Fixture(); f.Issue(); f.Authority.Dispose(); string path = Path.Combine(f.Directory, "licenses.json");
        string original = File.ReadAllText(path); using var document = System.Text.Json.JsonDocument.Parse(original);
        byte[] payload = Convert.FromBase64String(document.RootElement.GetProperty("payload").GetString()!); payload[^1] ^= 1;
        PrivateFiles.Write(path, LicenseJson.Write(new { schema = 1, payload = Convert.ToBase64String(payload), mac = document.RootElement.GetProperty("mac").GetString() }));
        Reject("DATABASE_INTEGRITY", () => { using var ignored = new LicenseAuthority(f.Directory, f.Password, f.Time); });
        File.Delete(path); bool missing = false;
        try { using var ignored = new LicenseAuthority(f.Directory, f.Password, f.Time); } catch (FileNotFoundException) { missing = true; }
        Assert(missing);
    }

    /// <summary>Encrypted authority private material cannot be opened with another passphrase.</summary>
    private static void WrongPassword()
    {
        using var f = new Fixture(); f.Authority.Dispose(); bool failed = false;
        try { using var wrong = new LicenseAuthority(f.Directory, "wrong-passphrase-for-test", f.Time); }
        catch (CryptographicException) { failed = true; }
        Assert(failed);
    }

    /// <summary>Two independent authority instances cannot write the same snapshot concurrently.</summary>
    private static void ExclusiveWriter()
    {
        using var f = new Fixture(); Reject("STATE_BUSY", () => { using var other = new LicenseAuthority(f.Directory, f.Password, f.Time); });
    }

    /// <summary>A running server uses monotonic time and a restarted server refuses time before its last commit.</summary>
    private static void ClockRollback()
    {
        using var f = new Fixture(); var issue = f.Issue(); f.Grant(issue); f.Time.Advance(TimeSpan.FromHours(1)); f.Time.ShiftWall(TimeSpan.FromDays(-2));
        Reject("LICENSE_EXPIRED", () => f.Grant(issue, "check")); f.Authority.Dispose();
        Reject("SERVER_CLOCK_ROLLBACK", () => { using var other = new LicenseAuthority(f.Directory, f.Password, f.Time); });
    }

    /// <summary>Database payload and administrative audit do not retain the raw activation key.</summary>
    private static void NoRawKey()
    {
        using var f = new Fixture(); var issue = f.Issue(); f.Grant(issue);
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(f.Directory, "licenses.json")));
        string payload = Encoding.UTF8.GetString(Convert.FromBase64String(document.RootElement.GetProperty("payload").GetString()!));
        Assert(!payload.Contains(issue.AccessKey, StringComparison.Ordinal));
        Assert(!Encoding.UTF8.GetString(LicenseJson.Write(f.Authority.Audit())).Contains(issue.AccessKey, StringComparison.Ordinal));
    }

    /// <summary>Customer profiles cannot downgrade HTTPS to non-loopback HTTP or silently substitute key IDs.</summary>
    private static void TrustPolicy()
    {
        using var f = new Fixture(); _ = LicenseCrypto.ValidateTrust(f.Trust);
        Reject("LICENSE_HTTPS_REQUIRED", () => LicenseCrypto.ValidateTrust(f.Trust with { ServerUrl = "http://example.com/", DevelopmentLoopback = true }));
        Reject("LICENSE_HTTPS_REQUIRED", () => LicenseCrypto.ValidateTrust(f.Trust with { DevelopmentLoopback = false }));
        Reject("LICENSE_KEY_ID", () => LicenseCrypto.ValidateTrust(f.Trust with { PublicKeys = new() { ["bad-id"] = f.Trust.PublicKeys.Values.Single() } }));
    }

    /// <summary>The actual customer-facing command API is blocked when no vendor profile is embedded.</summary>
    private static void UnconfiguredGate()
    {
        Assert(CommandApplication.Run(["version"]) == 0);
        Assert(CommandApplication.Run(["license", "help"]) == 0);
        // Test clients with embedded ephemeral trust are covered separately by the subprocess integration runner.
        if (typeof(CommandApplication).Assembly.GetManifestResourceStream("LuckyStarPspToolkit.license-trust.json") is null)
        {
            Assert(CommandApplication.Run(["cpk-list", "missing.cpk"]) == 77);
            Assert(CommandApplication.Run(["self-test"]) == 77);
        }
    }

    /// <summary>Private file creation does not truncate an existing issued credential.</summary>
    private static void PrivateWrites()
    {
        string root = Path.Combine(Path.GetTempPath(), "lsp-write-" + Guid.NewGuid().ToString("N")); PrivateFiles.Directory(root);
        try
        {
            string path = Path.Combine(root, "key.txt"); PrivateFiles.Write(path, "original"u8, false); bool rejected = false;
            try { PrivateFiles.Write(path, "replacement"u8, false); } catch (IOException) { rejected = true; }
            Assert(rejected && File.ReadAllText(path) == "original");
            Assert(!Directory.EnumerateFiles(root, "*.tmp").Any());
            if (!OperatingSystem.IsWindows()) Assert(File.GetUnixFileMode(path) == (UnixFileMode.UserRead | UnixFileMode.UserWrite));
        }
        finally { Directory.Delete(root, true); }
    }

    /// <summary>Runs real HTTP client and server exchanges plus independent port/authentication checks.</summary>
    private static void HttpLifecycle() => HttpLifecycleAsync().GetAwaiter().GetResult();

    /// <summary>Verifies public/private routes, valid activation, fresh checks and explicit refusal over actual sockets.</summary>
    /// <returns>Asynchronous regression completion.</returns>
    private static async Task HttpLifecycleAsync()
    {
        using var f = new Fixture(); int publicPort = FreePort(), adminPort = FreePort(); while (adminPort == publicPort) adminPort = FreePort();
        await using var server = new LicenseHttpServer(f.Authority, publicPort, adminPort); server.Start();
        using var client = new LicenseTransport(f.Trust with { ServerUrl = $"http://127.0.0.1:{publicPort}/" }, f.Device);
        var issue = f.Issue(); var grant = await client.ExchangeAsync("activate", "", issue.AccessKey);
        Assert(grant.Lease.LicenseId == issue.Id && grant.Remaining > TimeSpan.Zero);
        _ = await client.ExchangeAsync("check", issue.Id, "");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var noAuth = await http.GetAsync($"http://127.0.0.1:{adminPort}/admin/licenses"); Assert(noAuth.StatusCode == HttpStatusCode.Unauthorized);
        using var notPublic = await http.GetAsync($"http://127.0.0.1:{publicPort}/admin/licenses"); Assert(notPublic.StatusCode == HttpStatusCode.NotFound);
        var owner = LicenseJson.Read<OwnerConnection>(PrivateFiles.Read(Path.Combine(f.Directory, "owner-connection.json")));
        using var message = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{adminPort}/admin/licenses");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", owner.Token);
        using var allowed = await http.SendAsync(message); Assert(allowed.IsSuccessStatusCode);
        f.Change(issue, "revoke"); Reject("LICENSE_REVOKED", () => client.ExchangeAsync("check", issue.Id, "").GetAwaiter().GetResult());
    }

    /// <summary>Allocates a currently free loopback test port; production uses explicitly reserved service ports.</summary>
    /// <returns>Ephemeral TCP port.</returns>
    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port;
    }

    /// <summary>A short monotonic grant expires independently of renewal HTTP completion.</summary>
    private static void WatchdogExpiry()
    {
        using var f = new Fixture(); using var transport = new LicenseTransport(f.Trust, f.Device);
        using var signaled = new ManualResetEventSlim(); int calls = 0;
        using var session = new LicenseSession(transport, Guid.NewGuid().ToString("D"), TimeSpan.FromMilliseconds(300), _ => { Interlocked.Increment(ref calls); signaled.Set(); });
        Assert(signaled.Wait(TimeSpan.FromSeconds(3))); Reject("LICENSE_ACCESS_DENIED", session.RequireValid); Assert(calls == 1);
    }

    /// <summary>A running operation receives an explicit revocation on its next bounded online renewal.</summary>
    private static void WatchdogRevocation() => WatchdogRevocationAsync().GetAwaiter().GetResult();

    /// <summary>Starts a real short-lease service, revokes an active license and waits for the session's one-way denial transition.</summary>
    /// <returns>Asynchronous test completion.</returns>
    private static async Task WatchdogRevocationAsync()
    {
        using var f = new Fixture(15); int port = FreePort(), admin = FreePort(); while (admin == port) admin = FreePort();
        await using var server = new LicenseHttpServer(f.Authority, port, admin); server.Start();
        using var transport = new LicenseTransport(f.Trust with { ServerUrl = $"http://127.0.0.1:{port}/" }, f.Device);
        var issue = f.Issue(); var grant = await transport.ExchangeAsync("activate", "", issue.AccessKey);
        using var signal = new ManualResetEventSlim(); string reason = "";
        using var session = new LicenseSession(transport, issue.Id, grant.Remaining, code => { reason = code; signal.Set(); });
        f.Change(issue, "revoke"); Assert(await Task.Run(() => signal.Wait(TimeSpan.FromSeconds(9)))); Assert(reason == "LICENSE_REVOKED");
    }
}

/// <summary>Deterministic clock with independent wall-clock adjustment and monotonic elapsed time.</summary>
internal sealed class LicenseTestTime : TimeProvider
{
    /// <summary>Current wall clock; starts after real initialization to satisfy database rollback checks.</summary>
    private DateTimeOffset utc = DateTimeOffset.UtcNow.AddDays(1);
    /// <summary>Independent monotonic tick counter.</summary>
    private long ticks;
    /// <summary>Serializes clock changes used by concurrent activation tests.</summary>
    private readonly object sync = new();
    /// <summary>Uses TimeSpan tick frequency for exact test intervals.</summary>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    /// <summary>Reads controlled wall clock.</summary>
    /// <returns>UTC instant.</returns>
    public override DateTimeOffset GetUtcNow() { lock (sync) return utc; }
    /// <summary>Reads monotonic timestamp.</summary>
    /// <returns>Elapsed ticks.</returns>
    public override long GetTimestamp() { lock (sync) return ticks; }
    /// <summary>Moves both clocks forward.</summary>
    /// <param name="duration">Nonnegative interval.</param>
    public void Advance(TimeSpan duration) { lock (sync) { utc += duration; ticks += duration.Ticks; } }
    /// <summary>Changes only the wall clock to reproduce rollback.</summary>
    /// <param name="duration">Positive or negative wall-clock adjustment.</param>
    public void ShiftWall(TimeSpan duration) { lock (sync) utc += duration; }
}

/// <summary>Isolated authority with randomly generated test secrets; all files are deleted after the group.</summary>
internal sealed class Fixture : IDisposable
{
    /// <summary>Private temporary state path.</summary>
    public string Directory { get; } = Path.Combine(Path.GetTempPath(), "lsp-license-tests-" + Guid.NewGuid().ToString("N"));
    /// <summary>Ephemeral test-only master passphrase, never logged or committed.</summary>
    public string Password { get; } = "ephemeral-" + Guid.NewGuid().ToString("N");
    /// <summary>Controlled server clock.</summary>
    public LicenseTestTime Time { get; } = new();
    /// <summary>Public ephemeral test trust.</summary>
    public LicenseTrust Trust { get; }
    /// <summary>Disposable authority, replaced on explicit restart tests.</summary>
    public LicenseAuthority Authority { get; private set; }
    /// <summary>Default software test identity.</summary>
    public DeviceIdentity Device { get; } = NewDevice();

    /// <summary>Creates a new authenticated authority without network dependencies.</summary>
    /// <param name="leaseSeconds">Test service lease lifetime.</param>
    public Fixture(int leaseSeconds = 90)
    {
        Trust = LicenseAuthority.Initialize(Directory, "http://127.0.0.1:17840/", Password, true, leaseSeconds);
        Authority = new(Directory, Password, Time);
    }

    /// <summary>Generates an independent random installation for cloning/slot tests.</summary>
    /// <returns>Owned P-256 identity.</returns>
    public static DeviceIdentity NewDevice() => new(ECDsa.Create(ECCurve.NamedCurves.nistP256), LicenseCrypto.Digest(RandomNumberGenerator.GetBytes(32)));

    /// <summary>Creates an owner issuance request with one-hour activation-start defaults.</summary>
    /// <param name="unit">Duration unit.</param>
    /// <param name="amount">Duration amount.</param>
    /// <param name="starts">Start policy.</param>
    /// <returns>New private request.</returns>
    public IssueLicenseRequest NewIssue(string unit = "hours", int amount = 1, string starts = "activation") =>
        new(Guid.NewGuid().ToString("D"), LicenseCrypto.NewAccessKey(), "test", unit, amount, starts);

    /// <summary>Commits one new test entitlement.</summary>
    /// <param name="unit">Duration unit.</param>
    /// <param name="amount">Duration amount.</param>
    /// <param name="starts">Start policy.</param>
    /// <returns>Private request retained only for local tests.</returns>
    public IssueLicenseRequest Issue(string unit = "hours", int amount = 1, string starts = "activation")
    { var issue = NewIssue(unit, amount, starts); Authority.Issue(issue); return issue; }

    /// <summary>Creates a fresh challenge and valid possession signature.</summary>
    /// <param name="issue">Entitlement request supplying ID/key.</param>
    /// <param name="action">activate or check.</param>
    /// <param name="device">Optional independent installation.</param>
    /// <returns>Signed protocol request.</returns>
    public LicenseRequest Request(IssueLicenseRequest issue, string action = "activate", DeviceIdentity? device = null)
    {
        device ??= Device;
        string nonce = Authority.ChallengeFor(new(LicenseCrypto.Product, action, device.PublicKey, device.HostBinding)).Challenge;
        var request = new LicenseRequest(LicenseCrypto.Product, action, device.PublicKey, device.HostBinding, nonce,
            LicenseCrypto.Nonce(), action == "check" ? issue.Id : "", action == "activate" ? issue.AccessKey : "", "");
        return request with { Proof = device.Prove(request) };
    }

    /// <summary>Exercises both authoritative signing and customer-side verification.</summary>
    /// <param name="issue">Target entitlement.</param>
    /// <param name="action">Requested operation.</param>
    /// <param name="device">Optional independent installation.</param>
    /// <returns>Verified execution grant.</returns>
    public ExecutionLease Grant(IssueLicenseRequest issue, string action = "activate", DeviceIdentity? device = null)
    {
        var request = Request(issue, action, device); return LicenseCrypto.VerifyLease(Authority.Authorize(request).Token, Trust, request);
    }

    /// <summary>Performs an owner lifecycle transition with a new idempotency ID.</summary>
    /// <param name="issue">Target entitlement.</param>
    /// <param name="action">Lifecycle action.</param>
    /// <returns>Updated view.</returns>
    public LicenseOverview Change(IssueLicenseRequest issue, string action) => Authority.Change(new(Guid.NewGuid().ToString("D"), issue.Id, action));

    /// <summary>Restarts from committed disk state, discarding all transient challenges.</summary>
    public void Restart() { Authority.Dispose(); Authority = new(Directory, Password, Time); }

    /// <summary>Closes all key handles and removes generated credentials; nothing is published.</summary>
    public void Dispose()
    {
        Authority.Dispose(); Device.Dispose(); if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true);
    }
}

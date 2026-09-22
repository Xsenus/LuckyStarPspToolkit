using LuckyStarPspToolkit.Licensing;
using LuckyStarPspToolkit.Licensing.Authority;

/// <summary>Regression tests for durable local denials, reset retirement, bounded audit and private browser-session controls.</summary>
internal static class ReserveSafetyTests
{
    /// <summary>Requires an invariant without disclosing fixture secrets.</summary>
    /// <param name="value">Expected true condition.</param>
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Reserve/session regression failed."); }
    /// <summary>Requires a specific controlled error instead of accepting unrelated failures.</summary>
    /// <param name="code">Stable expected code.</param>
    /// <param name="action">Operation that must fail.</param>
    private static void Reject(string code, Action action)
    {
        try { action(); } catch (LicenseException ex) { Check(ex.Code == code); return; }
        throw new InvalidOperationException("Missing denial: " + code);
    }
    /// <summary>Creates an activated perpetual license and an installation-bound seven-day permission.</summary>
    /// <param name="f">Disposable authority.</param>
    /// <returns>Parent issuance and grant.</returns>
    private static (IssueLicenseRequest Issue, ReserveGrant Grant) Prepare(Fixture f)
    {
        var issue = f.Issue("permanent", 0); f.Grant(issue);
        f.Authority.SetReservePolicy(new(Guid.NewGuid().ToString("D"), true));
        return (issue, f.Authority.IssueReserve(new(Guid.NewGuid().ToString("D"), issue.Id, f.Device.Id, 168, "regression")));
    }
    /// <summary>Requests and validates a fresh signed token with the real authority and installation keys.</summary>
    /// <param name="f">Disposable authority.</param>
    /// <param name="issue">Parent issuance.</param>
    /// <param name="grant">Expected permission.</param>
    /// <returns>New cache containing the signed permission.</returns>
    private static ReserveCache Cache(Fixture f, IssueLicenseRequest issue, ReserveGrant grant)
    {
        var request = f.Request(issue, "check");
        var token = f.Authority.RefreshReserve(new(grant.Id, request));
        var cache = new ReserveCache(f.Directory, f.Trust, f.Device, issue.Id, f.Time);
        cache.Accept(token.Token, grant.Id, request.ClientNonce, TimeSpan.Zero); return cache;
    }
    /// <summary>A read-only deadline check persists denial before the calendar is restored and the process restarted.</summary>
    public static void ExpirySurvivesRestart()
    {
        using var f = new Fixture(); var (issue, grant) = Prepare(f); var cache = Cache(f, issue, grant);
        f.Time.Advance(TimeSpan.FromDays(7)); Reject("RESERVE_EXPIRED", () => cache.Remaining(false));
        f.Time.ShiftWall(TimeSpan.FromDays(-7));
        var reopened = new ReserveCache(f.Directory, f.Trust, f.Device, issue.Id, f.Time);
        Check(!reopened.Enabled); Reject("RESERVE_EXPIRED", () => reopened.Remaining());
    }
    /// <summary>Observed backward clock movement cannot be undone by restoring the calendar and reopening the cache.</summary>
    public static void RollbackSurvivesRestart()
    {
        using var f = new Fixture(); var (issue, grant) = Prepare(f); var cache = Cache(f, issue, grant);
        f.Time.ShiftWall(TimeSpan.FromSeconds(-1)); Reject("RESERVE_CLOCK", () => cache.Remaining(false));
        f.Time.ShiftWall(TimeSpan.FromSeconds(1));
        Reject("RESERVE_CLOCK", () => new ReserveCache(f.Directory, f.Trust, f.Device, issue.Id, f.Time).Remaining());
    }
    /// <summary>Only a fresh successfully verified online token clears a durable observed expiry.</summary>
    public static void FreshTokenRecoversExpiry()
    {
        using var f = new Fixture(); var (issue, grant) = Prepare(f); var cache = Cache(f, issue, grant);
        f.Time.Advance(TimeSpan.FromDays(7)); Reject("RESERVE_EXPIRED", () => cache.Remaining());
        cache = Cache(f, issue, grant); Check(cache.Enabled && cache.Remaining() == TimeSpan.FromDays(7));
    }
    /// <summary>Failed denial persistence leaves this process denied rather than reverting its in-memory snapshot.</summary>
    public static void DenialWriteFailure()
    {
        using var f = new Fixture(); var (issue, grant) = Prepare(f); var cache = Cache(f, issue, grant);
        string path = Path.Combine(f.Directory, "reserve.json"); File.Move(path, path + ".saved"); System.IO.Directory.CreateDirectory(path);
        bool refused = false;
        try { cache.Block(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or LicenseException) { refused = true; }
        Check(refused && !cache.Enabled); Reject("RESERVE_UNAVAILABLE", () => cache.Remaining());
    }
    /// <summary>Resetting a device permanently retires its old grants even after the same key pair is activated again.</summary>
    public static void ResetDoesNotReviveReserve()
    {
        using var f = new Fixture(); var (issue, grant) = Prepare(f);
        var second = f.Authority.IssueReserve(new(Guid.NewGuid().ToString("D"), issue.Id, f.Device.Id, 1, "second"));
        var reset = new ChangeLicenseRequest(Guid.NewGuid().ToString("D"), issue.Id, "reset-device", DeviceId: f.Device.Id);
        f.Authority.Change(reset); f.Grant(issue);
        foreach (var item in new[] { grant, second }) Reject("RESERVE_REVOKED", () => f.Authority.RefreshReserve(new(item.Id, f.Request(issue, "check"))));
        int auditCount = f.Authority.Audit().Length; f.Authority.Change(reset); Check(auditCount == f.Authority.Audit().Length);
        Check(f.Authority.ReserveStatus().Grants.All(x => x.RevokedAt.HasValue));
        var replacement = f.Authority.IssueReserve(new(Guid.NewGuid().ToString("D"), issue.Id, f.Device.Id, 1, "replacement"));
        Check(Cache(f, issue, replacement).Remaining() == TimeSpan.FromHours(1));
    }
    /// <summary>A device reset does not retire grants belonging to another license using the same installation key.</summary>
    public static void ResetScope()
    {
        using var f = new Fixture(); var (a, ga) = Prepare(f); var (b, gb) = Prepare(f);
        f.Authority.Change(new(Guid.NewGuid().ToString("D"), a.Id, "reset-device", DeviceId: f.Device.Id));
        Check(Cache(f, b, gb).Enabled); Check(f.Authority.ReserveStatus().Grants.Single(x => x.Id == ga.Id).RevokedAt.HasValue);
        f.Restart(); Check(Cache(f, b, gb).Enabled);
    }
    /// <summary>Invalid possession proof is refused before nonce consumption, and the corrected request then consumes it exactly once.</summary>
    public static void ReserveProofAndNonce()
    {
        using var f = new Fixture(); var (issue, grant) = Prepare(f); var request = f.Request(issue, "check");
        using var other = Fixture.NewDevice(); var invalid = request with { Proof = other.Prove(request) };
        Reject("DEVICE_PROOF", () => f.Authority.RefreshReserve(new(grant.Id, invalid)));
        var result = f.Authority.RefreshReserve(new(grant.Id, request));
        var verified = ReserveCrypto.Verify(result.Token, f.Trust, f.Device, issue.Id, grant.Id, request.ClientNonce);
        Check(verified.ValidUntil - verified.ServerNow == 604800);
        Reject("CHALLENGE_INVALID", () => f.Authority.RefreshReserve(new(grant.Id, request)));
    }
    /// <summary>Audit tail order matches legacy reverse insertion order and returned arrays cannot mutate stored history.</summary>
    public static void AuditTail()
    {
        using var f = new Fixture(); var (issue, _) = Prepare(f); f.Change(issue, "suspend"); f.Change(issue, "resume");
        Check(f.Authority.AuditRecent(2).SequenceEqual(f.Authority.Audit().TakeLast(2).Reverse()));
        var list = f.Authority.AuditRecent(); int count = list.Length; list[0] = new(0, "changed", "", "");
        Check(f.Authority.AuditRecent().Length == count && f.Authority.AuditRecent()[0].Action == "resume");
        foreach (int n in new[] { 0, -1, 501, int.MaxValue }) Reject("AUDIT_LIMIT", () => f.Authority.AuditRecent(n));
    }
    /// <summary>Session inventory exposes independent handles and never exposes cookie or CSRF values.</summary>
    public static void SessionPrivacy()
    {
        using var f = new Fixture(); var setup = Setup(f); using var auth = new WebAdminAuthentication(f.Directory, f.Time);
        var a = auth.Login(new("owner", f.Password, setup.RecoveryCodes[0]), "test");
        var b = auth.Login(new("owner", f.Password, setup.RecoveryCodes[1]), "test");
        var views = auth.ListSessions(a.SessionId); Check(views.Length == 2 && views[0].Current && !views[1].Current);
        string json = System.Text.Encoding.UTF8.GetString(LicenseJson.Write(views));
        Check(!json.Contains(a.SessionId) && !json.Contains(b.SessionId) && !json.Contains(a.CsrfToken));
        foreach (var item in views) Reject("WEB_UNAUTHORIZED", () => auth.Require(item.Id));
    }
    /// <summary>Revoke-all keeps the caller, rejects the other cookie and is idempotent on retry.</summary>
    public static void SessionRevokeOthers()
    {
        using var f = new Fixture(); var setup = Setup(f); using var auth = new WebAdminAuthentication(f.Directory, f.Time);
        var a = auth.Login(new("owner", f.Password, setup.RecoveryCodes[0]), "test");
        var b = auth.Login(new("owner", f.Password, setup.RecoveryCodes[1]), "test");
        Check(auth.RevokeSessions(a.SessionId, a.CsrfToken, new(Others: true)) == 1);
        Reject("WEB_UNAUTHORIZED", () => auth.Require(b.SessionId)); Check(auth.Require(a.SessionId) == a);
        Check(auth.RevokeSessions(a.SessionId, a.CsrfToken, new(Others: true)) == 0);
    }
    /// <summary>Single-session revocation refuses self-targeting, bad CSRF and stale MFA before altering any session.</summary>
    public static void SessionRevokeGuards()
    {
        using var f = new Fixture(); var setup = Setup(f); using var auth = new WebAdminAuthentication(f.Directory, f.Time);
        var a = auth.Login(new("owner", f.Password, setup.RecoveryCodes[0]), "test");
        var b = auth.Login(new("owner", f.Password, setup.RecoveryCodes[1]), "test");
        var views = auth.ListSessions(a.SessionId);
        Reject("WEB_SESSION_CURRENT", () => auth.RevokeSessions(a.SessionId, a.CsrfToken, new(views[0].Id)));
        Reject("WEB_CSRF", () => auth.RevokeSessions(a.SessionId, "wrong", new(views[1].Id)));
        Reject("WEB_SESSION_REQUEST", () => auth.RevokeSessions(a.SessionId, a.CsrfToken, new()));
        f.Time.Advance(TimeSpan.FromMinutes(6));
        Reject("WEB_REAUTH_REQUIRED", () => auth.RevokeSessions(a.SessionId, a.CsrfToken, new(views[1].Id)));
        Check(auth.Require(b.SessionId) == b);
        auth.Reauthenticate(a.SessionId, a.CsrfToken, new("owner", f.Password, setup.RecoveryCodes[2]), "test");
        Check(auth.RevokeSessions(a.SessionId, a.CsrfToken, new(views[1].Id)) == 1);
        Check(auth.RevokeSessions(a.SessionId, a.CsrfToken, new(views[1].Id)) == 0);
    }
    /// <summary>Session inspection does not keep another browser alive and expired sessions disappear from the inventory.</summary>
    public static void SessionInventoryExpiration()
    {
        using var f = new Fixture(); var setup = Setup(f); using var auth = new WebAdminAuthentication(f.Directory, f.Time);
        var a = auth.Login(new("owner", f.Password, setup.RecoveryCodes[0]), "test");
        var b = auth.Login(new("owner", f.Password, setup.RecoveryCodes[1]), "test");
        f.Time.Advance(TimeSpan.FromMinutes(10)); Check(auth.ListSessions(a.SessionId).Length == 2);
        f.Time.Advance(TimeSpan.FromMinutes(5)); Check(auth.ListSessions(a.SessionId).Length == 1);
        Reject("WEB_UNAUTHORIZED", () => auth.Require(b.SessionId));
    }
    /// <summary>Creates ephemeral local-only MFA credentials outside published files.</summary>
    /// <param name="f">Isolated authority.</param>
    /// <returns>Disposable enrollment values.</returns>
    private static WebAdminSetup Setup(Fixture f) => WebAdminAuthentication.Initialize(f.Directory, "owner", f.Password, "http://127.0.0.1:17842", true);
}

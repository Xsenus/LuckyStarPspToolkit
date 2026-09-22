using System.Security.Cryptography;
using System.Text;
using LuckyStarPspToolkit.Licensing;
using LuckyStarPspToolkit.Licensing.Authority;

/// <summary>Reserve and MFA regressions use real signing, durable state and deterministic clocks; no keys survive fixtures.</summary>
internal static class WebReserveTests
{
    /// <summary>Requires a condition without logging credentials.</summary>
    /// <param name="ok">Expected invariant.</param>
    private static void Check(bool ok) { if (!ok) throw new InvalidOperationException("Reserve/MFA invariant failed"); }
    /// <summary>Requires an exact controlled denial.</summary>
    /// <param name="code">Expected denial code.</param>
    /// <param name="action">Operation that must not succeed.</param>
    private static void Reject(string code, Action action) { try { action(); } catch (LicenseException ex) { if (ex.Code != code) throw new InvalidOperationException($"Expected {code}, got {ex.Code}"); return; } throw new InvalidOperationException("Missing denial: " + code); }
    /// <summary>Enables opt-in reserve without sharing request IDs between operations.</summary>
    /// <param name="f">Isolated authority.</param>
    private static void Enable(Fixture f) => f.Authority.SetReservePolicy(new(Guid.NewGuid().ToString("D"), true));
    /// <summary>Creates a bounded grant for a previously activated installation.</summary>
    /// <param name="f">Isolated authority.</param>
    /// <param name="issue">Parent entitlement.</param>
    /// <param name="hours">Offline interval.</param>
    /// <returns>Persisted grant.</returns>
    private static ReserveGrant Issue(Fixture f, IssueLicenseRequest issue, int hours = 168) => f.Authority.IssueReserve(new(Guid.NewGuid().ToString("D"), issue.Id, f.Device.Id, hours, "isolated-test"));
    /// <summary>Exercises the actual authority signature and device proof for a fresh offline token.</summary>
    /// <param name="f">Isolated authority.</param>
    /// <param name="issue">Parent entitlement.</param>
    /// <param name="grant">Expected reserve.</param>
    /// <returns>Token and its fresh request nonce.</returns>
    private static (string Token, string Nonce) Token(Fixture f, IssueLicenseRequest issue, ReserveGrant grant) { var request = f.Request(issue, "check"); return (f.Authority.RefreshReserve(new(grant.Id, request)).Token, request.ClientNonce); }
    /// <summary>Global reserve is off by default, including the migrated database.</summary>
    public static void DefaultOff() { using var f = new Fixture(); var x = f.Issue(); f.Grant(x); Reject("RESERVE_DISABLED", () => Issue(f, x)); Check(!f.Authority.ReserveStatus().Enabled); }
    /// <summary>Anonymous, never-activated installations cannot receive reserve grants.</summary>
    public static void ActivationRequired() { using var f = new Fixture(); var x = f.Issue(); Enable(f); Reject("DEVICE_NOT_ACTIVATED", () => Issue(f, x)); }
    /// <summary>Hard seven-day cap is enforced at owner issuance.</summary>
    public static void HourBounds() { using var f = new Fixture(); var x = f.Issue(); f.Grant(x); Enable(f); foreach (int n in new[] {0,-1,169,int.MaxValue}) Reject("RESERVE_POLICY", () => Issue(f,x,n)); Check(Issue(f,x,1).WindowSeconds==3600); }
    /// <summary>Seven days never extend a shorter underlying entitlement.</summary>
    public static void ParentExpiry() { using var f = new Fixture(); var x = f.Issue(); f.Grant(x); Enable(f); var g=Issue(f,x); var t=Token(f,x,g); var v=ReserveCrypto.Verify(t.Token,f.Trust,f.Device,x.Id,g.Id,t.Nonce); Check(v.ValidUntil-v.ServerNow==3600); }
    /// <summary>Perpetual licenses still receive only a seven-day renewable reserve window.</summary>
    public static void PermanentBound() { using var f=new Fixture();var x=f.Issue("permanent",0);f.Grant(x);Enable(f);var g=Issue(f,x);var t=Token(f,x,g);var v=ReserveCrypto.Verify(t.Token,f.Trust,f.Device,x.Id,g.Id,t.Nonce);Check(v.ValidUntil-v.ServerNow==604800); }
    /// <summary>Turning reserve off and back on permanently retires previous grant generations.</summary>
    public static void EpochRetirement() { using var f=new Fixture();var x=f.Issue();f.Grant(x);Enable(f);var g=Issue(f,x);f.Authority.SetReservePolicy(new(Guid.NewGuid().ToString("D"),false));Reject("RESERVE_DISABLED",()=>Token(f,x,g));Enable(f);Reject("RESERVE_REVOKED",()=>Token(f,x,g));Check(f.Authority.ReserveStatus().Epoch==3); }
    /// <summary>Repeated policy request is idempotent and a same-state transition does not rotate live grants.</summary>
    public static void PolicyIdempotency() { using var f=new Fixture();var a=new ReservePolicyRequest(Guid.NewGuid().ToString("D"),true);f.Authority.SetReservePolicy(a);f.Authority.SetReservePolicy(a);Enable(f);Check(f.Authority.ReserveStatus().Epoch==1);Reject("IDEMPOTENCY_CONFLICT",()=>f.Authority.SetReservePolicy(a with {Enabled=false})); }
    /// <summary>Lost issuance replies do not create additional grants, and different parameters cannot reuse an ID.</summary>
    public static void IssueIdempotency() { using var f=new Fixture();var x=f.Issue();f.Grant(x);Enable(f);var q=new ReserveIssueRequest(Guid.NewGuid().ToString("D"),x.Id,f.Device.Id,1,"test");var g=f.Authority.IssueReserve(q);Check(g==f.Authority.IssueReserve(q));Check(f.Authority.ReserveStatus().Grants.Length==1);Reject("IDEMPOTENCY_CONFLICT",()=>f.Authority.IssueReserve(q with {Hours=2})); }
    /// <summary>Revoking reserve does not revoke the ordinary online entitlement.</summary>
    public static void RevokeIndependence() { using var f=new Fixture();var x=f.Issue();f.Grant(x);Enable(f);var g=Issue(f,x);var q=new ReserveRevokeRequest(Guid.NewGuid().ToString("D"),g.Id);f.Authority.RevokeReserve(q);f.Authority.RevokeReserve(q);Reject("RESERVE_REVOKED",()=>Token(f,x,g));Check(f.Grant(x,"check").LicenseId==x.Id); }
    /// <summary>Current license suspension and reset are rechecked before each reserve refresh.</summary>
    public static void ParentDenial() { using var f=new Fixture();var x=f.Issue();f.Grant(x);Enable(f);var g=Issue(f,x);f.Change(x,"suspend");Reject("LICENSE_SUSPENDED",()=>Token(f,x,g));f.Change(x,"resume");f.Authority.Change(new(Guid.NewGuid().ToString("D"),x.Id,"reset-device",DeviceId:f.Device.Id));Reject("DEVICE_NOT_ACTIVATED",()=>Token(f,x,g)); }
    /// <summary>The existing one-use installation challenge remains mandatory for reserve refresh.</summary>
    public static void Replay() { using var f=new Fixture();var x=f.Issue();f.Grant(x);Enable(f);var g=Issue(f,x);var q=new ReserveRefreshRequest(g.Id,f.Request(x,"check"));f.Authority.RefreshReserve(q);Reject("CHALLENGE_INVALID",()=>f.Authority.RefreshReserve(q)); }
    /// <summary>Device, host, product and nonce binding prevent transferring a signed reserve token.</summary>
    public static void Scope() { using var f=new Fixture();var x=f.Issue();f.Grant(x);Enable(f);var g=Issue(f,x);var t=Token(f,x,g);using var other=Fixture.NewDevice();Reject("RESERVE_BINDING",()=>ReserveCrypto.Verify(t.Token,f.Trust,other,x.Id,g.Id,t.Nonce));Reject("RESERVE_BINDING",()=>ReserveCrypto.Verify(t.Token,f.Trust,f.Device,x.Id,g.Id,LicenseCrypto.Nonce()));Reject("RESERVE_BINDING",()=>ReserveCrypto.Verify(t.Token,f.Trust,f.Device,Guid.NewGuid().ToString("D"),g.Id)); }
    /// <summary>A normal execution token cannot be substituted for an offline reserve permission.</summary>
    public static void DomainSeparation() {using var f=new Fixture();var x=f.Issue();var q=f.Request(x);var t=f.Authority.Authorize(q).Token;Reject("RESERVE_SIGNATURE",()=>ReserveCrypto.Verify(t,f.Trust,f.Device,x.Id,Guid.NewGuid().ToString("D")));}
    /// <summary>Modified payload bytes fail signature verification before JSON is trusted.</summary>
    public static void Tamper() {using var f=new Fixture();var x=f.Issue();f.Grant(x);Enable(f);var g=Issue(f,x);var t=Token(f,x,g);var parts=t.Token.Split('.');byte[] bytes=LicenseCrypto.Decode(parts[2],4096);bytes[^2]^=1;parts[2]=LicenseCrypto.Encode(bytes);Reject("RESERVE_SIGNATURE",()=>ReserveCrypto.Verify(string.Join('.',parts),f.Trust,f.Device,x.Id,g.Id));}
    /// <summary>Seven-day expiry remains exclusive when time is advanced without waiting in real time.</summary>
    public static void CacheExpiry() {using var f=new Fixture();var x=f.Issue("permanent",0);f.Grant(x);Enable(f);var g=Issue(f,x);var t=Token(f,x,g);var cache=new ReserveCache(f.Directory,f.Trust,f.Device,x.Id,f.Time);cache.Accept(t.Token,g.Id,t.Nonce,TimeSpan.Zero);Check(cache.Remaining()==TimeSpan.FromDays(7));f.Time.Advance(TimeSpan.FromDays(7));Reject("RESERVE_EXPIRED",()=>cache.Remaining());}
    /// <summary>Restarting a client does not reset the remaining offline interval.</summary>
    public static void CacheRestart() {using var f=new Fixture();var x=f.Issue("permanent",0);f.Grant(x);Enable(f);var g=Issue(f,x);var t=Token(f,x,g);var cache=new ReserveCache(f.Directory,f.Trust,f.Device,x.Id,f.Time);cache.Accept(t.Token,g.Id,t.Nonce,TimeSpan.Zero);f.Time.Advance(TimeSpan.FromDays(2));Check(cache.Remaining()<=TimeSpan.FromDays(5));var reopened=new ReserveCache(f.Directory,f.Trust,f.Device,x.Id,f.Time);Check(reopened.Remaining()<=TimeSpan.FromDays(5));}
    /// <summary>Backward wall time is refused instead of extending offline access.</summary>
    public static void CacheRollback() {using var f=new Fixture();var x=f.Issue();f.Grant(x);Enable(f);var g=Issue(f,x);var t=Token(f,x,g);var cache=new ReserveCache(f.Directory,f.Trust,f.Device,x.Id,f.Time);cache.Accept(t.Token,g.Id,t.Nonce,TimeSpan.Zero);f.Time.ShiftWall(TimeSpan.FromSeconds(-1));Reject("RESERVE_CLOCK",()=>cache.Remaining());}
    /// <summary>A frozen calendar clock cannot freeze the live process's monotonic deadline.</summary>
    public static void CacheMonotonic() {using var f=new Fixture();var x=f.Issue();f.Grant(x);Enable(f);var g=Issue(f,x);var t=Token(f,x,g);var cache=new ReserveCache(f.Directory,f.Trust,f.Device,x.Id,f.Time);cache.Accept(t.Token,g.Id,t.Nonce,TimeSpan.Zero);f.Time.Advance(TimeSpan.FromSeconds(17));f.Time.ShiftWall(TimeSpan.FromSeconds(-17));Check(cache.Remaining()<=TimeSpan.FromSeconds(3600-17));}
    /// <summary>Learned online denial survives restart and cannot be cleared by simply reading cached state.</summary>
    public static void CacheBlock() {using var f=new Fixture();var x=f.Issue();f.Grant(x);Enable(f);var g=Issue(f,x);var t=Token(f,x,g);var cache=new ReserveCache(f.Directory,f.Trust,f.Device,x.Id,f.Time);cache.Accept(t.Token,g.Id,t.Nonce,TimeSpan.Zero);cache.Block();var other=new ReserveCache(f.Directory,f.Trust,f.Device,x.Id,f.Time);Check(!other.Enabled);Reject("RESERVE_UNAVAILABLE",()=>other.Remaining());}
    /// <summary>Corrupting the durable installation signature is detected at startup.</summary>
    public static void CacheTamper() {using var f=new Fixture();var x=f.Issue();f.Grant(x);Enable(f);var g=Issue(f,x);var t=Token(f,x,g);var cache=new ReserveCache(f.Directory,f.Trust,f.Device,x.Id,f.Time);cache.Accept(t.Token,g.Id,t.Nonce,TimeSpan.Zero);string path=Path.Combine(f.Directory,"reserve.json");string text=File.ReadAllText(path);using var doc=System.Text.Json.JsonDocument.Parse(text);string sig=doc.RootElement.GetProperty("signature").GetString()!;byte[] b=LicenseCrypto.Decode(sig,64,64);b[0]^=1;File.WriteAllText(path,text.Replace(sig,LicenseCrypto.Encode(b)));Reject("RESERVE_CACHE",()=>new ReserveCache(f.Directory,f.Trust,f.Device,x.Id,f.Time));}
    /// <summary>Reserve policy and revocation survive an actual authority restart.</summary>
    public static void ReservePersistence() {using var f=new Fixture();var x=f.Issue();f.Grant(x);Enable(f);var g=Issue(f,x);f.Restart();Check(f.Authority.ReserveStatus().Grants.Single().Id==g.Id);_ = Token(f,x,g);}
    /// <summary>Source RFC vectors verify TOTP code generation independently of enrollment/login code.</summary>
    public static void TotpVectors() {string key=Totp.Base32(Encoding.ASCII.GetBytes("12345678901234567890"));long[] seconds={59,1111111109,1111111111,1234567890,2000000000,20000000000};string[] expected={"287082","081804","050471","005924","279037","353130"};for(int i=0;i<seconds.Length;i++)Check(Totp.Code(key,seconds[i]/30)==expected[i]);}
    /// <summary>Creates one ephemeral web enrollment, never writing its plaintext seed to reports.</summary>
    /// <param name="f">Existing private authority.</param>
    /// <returns>Owner-only in-memory enrollment.</returns>
    private static WebAdminSetup Setup(Fixture f) => WebAdminAuthentication.Initialize(f.Directory,"owner",f.Password,"https://admin.example");
    /// <summary>Successful MFA returns separate cookie and CSRF values and rejects reuse of the TOTP time step.</summary>
    public static void TotpReplay() {using var f=new Fixture();var setup=Setup(f);using var auth=new WebAdminAuthentication(f.Directory,f.Time);var q=new WebLoginRequest("owner",f.Password,Totp.Code(setup.TotpSecret,f.Time.GetUtcNow().ToUnixTimeSeconds()/30));var view=auth.Login(q,"source");Check(view.SessionId!=view.CsrfToken);auth.Require(view.SessionId,view.CsrfToken,true);Reject("WEB_AUTH_FAILED",()=>auth.Login(q,"source"));}
    /// <summary>A recovery code is a one-time second factor, never a replacement for the password.</summary>
    public static void RecoveryOnce() {using var f=new Fixture();var setup=Setup(f);using var auth=new WebAdminAuthentication(f.Directory,f.Time);Reject("WEB_AUTH_FAILED",()=>auth.Login(new("owner","wrong",setup.RecoveryCodes[0]),"source"));auth.Login(new("owner",f.Password,setup.RecoveryCodes[0]),"source");Reject("WEB_AUTH_FAILED",()=>auth.Login(new("owner",f.Password,setup.RecoveryCodes[0]),"source"));}
    /// <summary>Consumed recovery state survives restarting the browser authentication service.</summary>
    public static void RecoveryPersistence() {using var f=new Fixture();var setup=Setup(f);WebSession view;using(var auth=new WebAdminAuthentication(f.Directory,f.Time))view=auth.Login(new("owner",f.Password,setup.RecoveryCodes[0]),"source");using var again=new WebAdminAuthentication(f.Directory,f.Time);Reject("WEB_UNAUTHORIZED",()=>again.Require(view.SessionId));Reject("WEB_AUTH_FAILED",()=>again.Login(new("owner",f.Password,setup.RecoveryCodes[0]),"source"));again.Login(new("owner",f.Password,setup.RecoveryCodes[1]),"source");}
    /// <summary>CSRF mismatches and server-side logout both block previously authenticated mutations.</summary>
    public static void CsrfLogout() {using var f=new Fixture();var setup=Setup(f);using var auth=new WebAdminAuthentication(f.Directory,f.Time);var view=auth.Login(new("owner",f.Password,setup.RecoveryCodes[0]),"source");Reject("WEB_CSRF",()=>auth.Require(view.SessionId,"wrong"));auth.Logout(view.SessionId);Reject("WEB_UNAUTHORIZED",()=>auth.Require(view.SessionId,view.CsrfToken));}
    /// <summary>Idle sessions expire at fifteen minutes, without relying on JavaScript timers.</summary>
    public static void IdleExpiry() {using var f=new Fixture();var setup=Setup(f);using var auth=new WebAdminAuthentication(f.Directory,f.Time);var view=auth.Login(new("owner",f.Password,setup.RecoveryCodes[0]),"source");f.Time.Advance(TimeSpan.FromMinutes(15));Reject("WEB_UNAUTHORIZED",()=>auth.Require(view.SessionId));}
    /// <summary>Activity cannot extend the absolute eight-hour session lifetime.</summary>
    public static void AbsoluteExpiry() {using var f=new Fixture();var setup=Setup(f);using var auth=new WebAdminAuthentication(f.Directory,f.Time);var view=auth.Login(new("owner",f.Password,setup.RecoveryCodes[0]),"source");for(int i=0;i<47;i++){f.Time.Advance(TimeSpan.FromMinutes(10));auth.Require(view.SessionId);}f.Time.Advance(TimeSpan.FromMinutes(10));Reject("WEB_UNAUTHORIZED",()=>auth.Require(view.SessionId));}
    /// <summary>High-impact changes require fresh MFA after five minutes; reauthentication renews only that requirement.</summary>
    public static void FreshAuth() {using var f=new Fixture();var setup=Setup(f);using var auth=new WebAdminAuthentication(f.Directory,f.Time);var view=auth.Login(new("owner",f.Password,setup.RecoveryCodes[0]),"source");f.Time.Advance(TimeSpan.FromMinutes(6));Reject("WEB_REAUTH_REQUIRED",()=>auth.Require(view.SessionId,view.CsrfToken,true));auth.Reauthenticate(view.SessionId,view.CsrfToken,new("owner",f.Password,setup.RecoveryCodes[1]),"source");auth.Require(view.SessionId,view.CsrfToken,true);}
    /// <summary>Login rate limits stop repeated password derivations and recover only after the fixed window.</summary>
    public static void LoginRate() {using var f=new Fixture();Setup(f);using var auth=new WebAdminAuthentication(f.Directory,f.Time);for(int i=0;i<8;i++)Reject("WEB_AUTH_FAILED",()=>auth.Login(new("owner","wrong","000000"),"source"));Reject("WEB_RATE_LIMIT",()=>auth.Login(new("owner","wrong","000000"),"source"));f.Time.Advance(TimeSpan.FromMinutes(5));Reject("WEB_AUTH_FAILED",()=>auth.Login(new("owner","wrong","000000"),"source"));}
    /// <summary>Existing account and insecure/wildcard deployment origins are never silently overwritten.</summary>
    public static void SetupSafety() {using var f=new Fixture();foreach(var origin in new[]{"http://admin.example","https://admin.example/","https://admin.example/path","https://admin.example?x=1"})Reject("WEB_ORIGIN",()=>WebAdminAuthentication.Initialize(f.Directory,"owner",f.Password,origin));Setup(f);Reject("WEB_EXISTS",()=>Setup(f));}
    /// <summary>Concurrent use of one recovery code authorizes exactly one browser session.</summary>
    public static void ConcurrentRecovery() {using var f=new Fixture();var setup=Setup(f);using var auth=new WebAdminAuthentication(f.Directory,f.Time);int success=0;Parallel.For(0,4,_=>{try{auth.Login(new("owner",f.Password,setup.RecoveryCodes[0]),"source");Interlocked.Increment(ref success);}catch(LicenseException ex)when(ex.Code is "WEB_AUTH_FAILED" or "WEB_BUSY"){} });Check(success==1);}

    /// <summary>A refused bootstrap destination never creates an account whose second factor was not delivered.</summary>
    public static void EnrollmentFailure()
    {
        using var f = new Fixture();
        string output = Path.Combine(f.Directory, "existing-enrollment.json");
        PrivateFiles.Write(output, Encoding.UTF8.GetBytes("preserve"), false);
        bool failed = false;
        try { WebAdminAuthentication.Initialize(f.Directory, "owner", f.Password, "http://127.0.0.1:17842", true, output); }
        catch (Exception ex) when (ex is IOException or LicenseException) { failed = true; }
        Check(failed && !File.Exists(Path.Combine(f.Directory, "web-account.json")));
        Check(Encoding.UTF8.GetString(PrivateFiles.Read(output)) == "preserve");
        File.Delete(output);
        var setup = WebAdminAuthentication.Initialize(f.Directory, "owner", f.Password, "http://127.0.0.1:17842", true, output);
        Check(LicenseJson.Read<WebAdminSetup>(PrivateFiles.Read(output)).TotpSecret == setup.TotpSecret);
    }

    /// <summary>Subsecond restarts cannot recover fractional time discarded by a persisted watermark.</summary>
    public static void FractionalCacheRestart()
    {
        using var f = new Fixture(); var x = f.Issue("permanent", 0); f.Grant(x); Enable(f); var g = Issue(f, x);
        var t = Token(f, x, g); var cache = new ReserveCache(f.Directory, f.Trust, f.Device, x.Id, f.Time);
        cache.Accept(t.Token, g.Id, t.Nonce, TimeSpan.Zero);
        double previous = cache.Remaining().TotalSeconds;
        for (int i = 0; i < 5; i++)
        {
            f.Time.Advance(TimeSpan.FromMilliseconds(100));
            double remaining = cache.Remaining().TotalSeconds;
            Check(remaining < previous);
            cache = new ReserveCache(f.Directory, f.Trust, f.Device, x.Id, f.Time);
            Check(cache.Remaining().TotalSeconds <= remaining); previous = remaining;
        }
    }

    /// <summary>A recovery code cannot replace the password and a wrong password does not consume that code.</summary>
    public static void RecoveryNeedsPassword()
    {
        using var f = new Fixture(); var setup = Setup(f); using var auth = new WebAdminAuthentication(f.Directory, f.Time);
        Reject("WEB_AUTH_FAILED", () => auth.Login(new("owner", "wrong-password", setup.RecoveryCodes[0]), "source"));
        var session = auth.Login(new("owner", f.Password, setup.RecoveryCodes[0]), "source");
        Check(auth.Require(session.SessionId).Username == "owner");
    }

    /// <summary>An already valid reserve bridges short-lease expiry without waiting for a network timeout; it never invents an unsigned interval.</summary>
    public static void WatchdogReserveFallback()
    {
        using var f = new Fixture(); var x = f.Issue("permanent", 0); f.Grant(x); Enable(f); var g = Issue(f, x);
        var t = Token(f, x, g); var cache = new ReserveCache(f.Directory, f.Trust, f.Device, x.Id, f.Time);
        cache.Accept(t.Token, g.Id, t.Nonce, TimeSpan.Zero);
        using var transport = new LicenseTransport(f.Trust, f.Device);
        using var denied = new ManualResetEventSlim();
        using var session = new LicenseSession(transport, x.Id, TimeSpan.FromMilliseconds(250), _ => denied.Set(), cache);
        Check(!denied.Wait(TimeSpan.FromMilliseconds(800))); session.RequireValid();
        Check(cache.ValidUntil == ReserveCrypto.Verify(t.Token, f.Trust, f.Device, x.Id, g.Id).ValidUntil);
    }
}

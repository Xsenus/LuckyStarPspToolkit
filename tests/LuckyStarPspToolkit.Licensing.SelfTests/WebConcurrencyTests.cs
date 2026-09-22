using System.Security.Cryptography;
using LuckyStarPspToolkit.Licensing;
using LuckyStarPspToolkit.Licensing.Authority;

/// <summary>Deterministic owner-authentication races: every admitted derivation uses real PBKDF2, with optional test-only barriers outside state locks.</summary>
internal static class WebConcurrencyTests
{
    /// <summary>Requires a non-secret invariant.</summary>
    /// <param name="ok">Condition that must hold.</param>
    private static void Check(bool ok) { if (!ok) throw new InvalidOperationException("Web authentication concurrency invariant failed."); }

    /// <summary>Checks one exact controlled refusal.</summary>
    /// <param name="code">Expected public error.</param>
    /// <param name="action">Rejected action.</param>
    private static void Reject(string code, Action action)
    {
        try { action(); }
        catch (LicenseException ex) { Check(ex.Code == code); return; }
        throw new InvalidOperationException("Missing refusal: " + code);
    }

    /// <summary>Captures worker completion without allowing an aggregate exception to hide a wrong failure type.</summary>
    /// <param name="action">Operation executed on a dedicated test thread.</param>
    /// <returns>Completed exception, or null on success.</returns>
    private static Exception? Capture(Action action)
    { try { action(); return null; } catch (Exception ex) { return ex; } }

    /// <summary>Runs a bounded independent worker rather than relying on a saturated thread pool.</summary>
    /// <param name="action">Authentication operation.</param>
    /// <returns>Task carrying the outcome.</returns>
    private static Task<Exception?> Worker(Action action) => Task.Factory.StartNew(() => Capture(action), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    /// <summary>Requires a worker to finish and return one specific error without leaking credentials.</summary>
    /// <param name="task">Released authentication task.</param>
    /// <param name="code">Expected refusal.</param>
    private static void Failed(Task<Exception?> task, string code)
    { Check(task.Wait(TimeSpan.FromSeconds(10))); Check(task.Result is LicenseException ex && ex.Code == code); }

    /// <summary>Requires a worker to complete successfully.</summary>
    /// <param name="task">Worker that must no longer be blocked.</param>
    private static void Succeeded(Task<Exception?> task)
    { Check(task.Wait(TimeSpan.FromSeconds(10))); if (task.Result is { } ex) throw new InvalidOperationException("Unexpected authentication failure", ex); }

    /// <summary>Ephemeral real authority, web enrollment and password verifier barrier; no fixture secrets are written to reports.</summary>
    private sealed class Harness : IDisposable
    {
        /// <summary>Existing shared license-test fixture and deterministic time.</summary>
        internal readonly Fixture Data = new();
        /// <summary>In-memory recovery codes and TOTP enrollment.</summary>
        internal readonly WebAdminSetup Setup;
        /// <summary>Subject under test.</summary>
        internal readonly WebAdminAuthentication Auth;
        /// <summary>Signals entry into the test-controlled expensive operation.</summary>
        internal readonly ManualResetEventSlim Entered = new(false);
        /// <summary>Always released during cleanup, even when an assertion fails.</summary>
        internal readonly ManualResetEventSlim Continue = new(true);
        /// <summary>Arms only one upcoming verification.</summary>
        private int blockNext;
        /// <summary>Number of actual derivation calls, including failed credentials.</summary>
        internal int Calls;
        /// <summary>Injects a verifier exception before deriving, for reservation cleanup testing only.</summary>
        internal bool FailNext;

        /// <summary>Creates credentials and uses the internal friend-assembly seam only to pause the real verifier.</summary>
        internal Harness()
        {
            Setup = WebAdminAuthentication.Initialize(Data.Directory, "owner", Data.Password, "https://admin.example");
            Auth = new WebAdminAuthentication(Data.Directory, Data.Time, Verify);
        }

        /// <summary>Blocks an armed call before performing the real production password derivation.</summary>
        /// <param name="password">Submitted bounded password.</param>
        /// <param name="salt">Private account salt snapshot.</param>
        /// <param name="expected">Private password hash snapshot.</param>
        /// <returns>Actual PBKDF2 comparison result.</returns>
        private bool Verify(string password, byte[] salt, byte[] expected)
        {
            Interlocked.Increment(ref Calls);
            if (Interlocked.Exchange(ref blockNext, 0) == 1)
            { Entered.Set(); if (!Continue.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Verifier barrier was not released."); }
            if (FailNext) { FailNext = false; throw new IOException("Injected test verifier fault"); }
            return WebPasswordVerification.Verify(password, salt, expected);
        }

        /// <summary>Pauses the next derivation without weakening its cryptographic verification.</summary>
        internal void Arm() { Entered.Reset(); Continue.Reset(); Interlocked.Exchange(ref blockNext, 1); }
        /// <summary>Waits for the worker to reach the deliberately blocked operation.</summary>
        internal void Wait() { Check(Entered.Wait(TimeSpan.FromSeconds(10))); }
        /// <summary>Creates a new recovery-backed request, never an authentication bypass.</summary>
        /// <param name="index">One of eight private enrollment codes.</param>
        /// <returns>Complete real credentials.</returns>
        internal WebLoginRequest Request(int index) => new("owner", Data.Password, Setup.RecoveryCodes[index]);
        /// <summary>Creates a normal authenticated test session.</summary>
        /// <param name="index">Unused recovery code index.</param>
        /// <param name="source">Trusted test source bucket.</param>
        /// <returns>Created session.</returns>
        internal WebSession Login(int index = 0, string source = "source") => Auth.Login(Request(index), source);
        /// <summary>Releases barriers and closes the private writer before deleting credentials.</summary>
        public void Dispose() { Continue.Set(); Auth.Dispose(); Data.Dispose(); Entered.Dispose(); Continue.Dispose(); }
    }

    /// <summary>Session reads and logout complete while another password derivation is deliberately suspended.</summary>
    public static void SessionProgress()
    {
        using var h = new Harness(); var session = h.Login(); h.Arm();
        var login = Worker(() => h.Login(1)); h.Wait();
        try
        {
            var read = Worker(() => { h.Auth.Require(session.SessionId); h.Auth.ListSessions(session.SessionId); h.Auth.Logout(session.SessionId); });
            Succeeded(read);
        }
        finally { h.Continue.Set(); }
        Succeeded(login); Reject("WEB_UNAUTHORIZED", () => h.Auth.Require(session.SessionId));
    }

    /// <summary>Anonymous password work cannot occupy the one reserved reauthentication derivation.</summary>
    public static void ReservedLane()
    {
        using var h = new Harness(); var session = h.Login(); h.Arm();
        var login = Worker(() => h.Login(1)); h.Wait();
        try { Succeeded(Worker(() => h.Auth.Reauthenticate(session.SessionId, session.CsrfToken, h.Request(2), "source"))); }
        finally { h.Continue.Set(); }
        Succeeded(login);
    }

    /// <summary>Excess logins fail immediately, run no derivation and create no waiting password queue.</summary>
    public static void LoginAdmission()
    {
        using var h = new Harness(); h.Arm(); var first = Worker(() => h.Login()); h.Wait();
        int calls = h.Calls;
        try { for (int i = 0; i < 16; i++) Reject("WEB_BUSY", () => h.Login(1)); Check(h.Calls == calls); }
        finally { h.Continue.Set(); }
        Succeeded(first); h.Login(1);
    }

    /// <summary>Only one recent-MFA verification is active; ordinary session access remains available.</summary>
    public static void ReauthenticationAdmission()
    {
        using var h = new Harness(); var session = h.Login(); h.Arm();
        var first = Worker(() => h.Auth.Reauthenticate(session.SessionId, session.CsrfToken, h.Request(1), "source")); h.Wait();
        int calls = h.Calls;
        try
        {
            Reject("WEB_BUSY", () => h.Auth.Reauthenticate(session.SessionId, session.CsrfToken, h.Request(2), "other-source"));
            Check(h.Calls == calls); h.Auth.Require(session.SessionId);
        }
        finally { h.Continue.Set(); }
        Succeeded(first);
    }

    /// <summary>Logout during password work cannot be undone by a late reauthentication result, and the factor is not burned.</summary>
    public static void LogoutWins()
    {
        using var h = new Harness(); var session = h.Login(); h.Arm();
        var task = Worker(() => h.Auth.Reauthenticate(session.SessionId, session.CsrfToken, h.Request(1), "source")); h.Wait();
        try { h.Auth.Logout(session.SessionId); } finally { h.Continue.Set(); }
        Failed(task, "WEB_UNAUTHORIZED"); h.Login(1);
    }

    /// <summary>Explicit revocation in a different owner session wins over an already-started reauthentication.</summary>
    public static void RevocationWins()
    {
        using var h = new Harness(); var victim = h.Login(); var owner = h.Login(1);
        string victimId = h.Auth.ListSessions(owner.SessionId).Single(x => !x.Current).Id;
        h.Arm(); var task = Worker(() => h.Auth.Reauthenticate(victim.SessionId, victim.CsrfToken, h.Request(2), "source")); h.Wait();
        try { Check(h.Auth.RevokeSessions(owner.SessionId, owner.CsrfToken, new(victimId)) == 1); }
        finally { h.Continue.Set(); }
        Failed(task, "WEB_UNAUTHORIZED"); h.Login(2);
    }

    /// <summary>A session expiring during password work must not be revived; its unused recovery code remains available.</summary>
    public static void IdleExpiryWins()
    {
        using var h = new Harness(); var session = h.Login(); h.Arm();
        var task = Worker(() => h.Auth.Reauthenticate(session.SessionId, session.CsrfToken, h.Request(1), "source")); h.Wait();
        h.Data.Time.Advance(WebAdminAuthentication.IdleLimit); h.Continue.Set();
        Failed(task, "WEB_UNAUTHORIZED"); h.Login(1);
    }

    /// <summary>The absolute lifetime is checked after derivation, even when normal requests maintained the idle lifetime.</summary>
    public static void AbsoluteExpiryWins()
    {
        using var h = new Harness(); var session = h.Login();
        for (int i = 0; i < 47; i++) { h.Data.Time.Advance(TimeSpan.FromMinutes(10)); h.Auth.Require(session.SessionId); }
        h.Data.Time.Advance(TimeSpan.FromMinutes(9)); h.Arm();
        var task = Worker(() => h.Auth.Reauthenticate(session.SessionId, session.CsrfToken, h.Request(1), "source")); h.Wait();
        h.Data.Time.Advance(TimeSpan.FromMinutes(1)); h.Continue.Set();
        Failed(task, "WEB_UNAUTHORIZED"); h.Login(1);
    }

    /// <summary>Exactly five minutes is the exclusive recent-authentication deadline, matching the displayed countdown.</summary>
    public static void FreshBoundary()
    {
        using var h = new Harness(); var session = h.Login();
        h.Data.Time.Advance(WebAdminAuthentication.FreshLimit - TimeSpan.FromTicks(1)); h.Auth.Require(session.SessionId, session.CsrfToken, true);
        h.Data.Time.Advance(TimeSpan.FromTicks(1)); Reject("WEB_REAUTH_REQUIRED", () => h.Auth.Require(session.SessionId, session.CsrfToken, true));
    }

    /// <summary>Concurrent login and reauthentication check recovery consumption against current state, not a stale admission snapshot.</summary>
    public static void RecoveryCrossLane()
    {
        using var h = new Harness(); var session = h.Login(); h.Arm(); var login = Worker(() => h.Login(1)); h.Wait();
        try { h.Auth.Reauthenticate(session.SessionId, session.CsrfToken, h.Request(1), "source"); }
        finally { h.Continue.Set(); }
        Failed(login, "WEB_AUTH_FAILED");
    }

    /// <summary>The two verification lanes cannot both accept one TOTP step.</summary>
    public static void TotpCrossLane()
    {
        using var h = new Harness(); var session = h.Login();
        var request = new WebLoginRequest("owner", h.Data.Password, Totp.Code(h.Setup.TotpSecret, h.Data.Time.GetUtcNow().ToUnixTimeSeconds() / 30));
        h.Arm(); var login = Worker(() => h.Auth.Login(request, "source")); h.Wait();
        try { h.Auth.Reauthenticate(session.SessionId, session.CsrfToken, request, "source"); }
        finally { h.Continue.Set(); }
        Failed(login, "WEB_AUTH_FAILED");
    }

    /// <summary>Already cancelled requests do not derive, consume recovery state or charge the login attempt budget.</summary>
    public static void PreCancelled()
    {
        using var h = new Harness(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        for (int i = 0; i < 16; i++) Check(Capture(() => h.Auth.Login(h.Request(0), "source", cancellation.Token)) is OperationCanceledException);
        Check(h.Calls == 0); h.Login();
    }

    /// <summary>Cancellation during expensive login prevents MFA commitment and releases the admission lane.</summary>
    public static void LoginCancellation()
    {
        using var h = new Harness(); using var cancellation = new CancellationTokenSource(); h.Arm();
        var task = Worker(() => h.Auth.Login(h.Request(0), "source", cancellation.Token)); h.Wait(); cancellation.Cancel(); h.Continue.Set();
        Check(task.Wait(TimeSpan.FromSeconds(10))); Check(task.Result is OperationCanceledException); h.Login();
    }

    /// <summary>Cancelled reauthentication does not refresh the recent-MFA clock or consume the recovery code.</summary>
    public static void ReauthenticationCancellation()
    {
        using var h = new Harness(); var session = h.Login(); h.Data.Time.Advance(TimeSpan.FromMinutes(6));
        using var cancellation = new CancellationTokenSource(); h.Arm();
        var task = Worker(() => h.Auth.Reauthenticate(session.SessionId, session.CsrfToken, h.Request(1), "source", cancellation.Token));
        h.Wait(); cancellation.Cancel(); h.Continue.Set(); Check(task.Wait(TimeSpan.FromSeconds(10))); Check(task.Result is OperationCanceledException);
        Reject("WEB_REAUTH_REQUIRED", () => h.Auth.Require(session.SessionId, session.CsrfToken, true));
        h.Auth.Reauthenticate(session.SessionId, session.CsrfToken, h.Request(1), "source");
    }

    /// <summary>Unexpected derivation errors release reservations instead of stranding the only login slot.</summary>
    public static void DerivationFailure()
    {
        using var h = new Harness(); h.FailNext = true;
        Check(Capture(() => h.Login()) is IOException); h.Login();
    }

    /// <summary>Bad MFA never poisons the next correctly admitted request.</summary>
    public static void BadFactor()
    {
        using var h = new Harness(); Reject("WEB_AUTH_FAILED", () => h.Auth.Login(h.Request(0) with { Code = "invalid" }, "source")); h.Login();
    }

    /// <summary>Disposal marks the authority closed before releasing the writer; a late password result cannot consume an on-disk factor.</summary>
    public static void ShutdownWins()
    {
        using var h = new Harness(); h.Arm(); var task = Worker(() => h.Login()); h.Wait();
        h.Auth.Dispose(); h.Continue.Set(); Failed(task, "WEB_CLOSED");
        using var reopened = new WebAdminAuthentication(h.Data.Directory, h.Data.Time); reopened.Login(h.Request(0), "source");
        Reject("WEB_CLOSED", () => h.Auth.Login(h.Request(1), "source"));
    }

    /// <summary>Anonymous login failures cannot exhaust the recent-MFA budget of an existing owner session on the same ingress address.</summary>
    public static void SeparateLoginBudget()
    {
        using var h = new Harness(); var session = h.Login();
        for (int i = 0; i < 7; i++) Reject("WEB_AUTH_FAILED", () => h.Auth.Login(h.Request(1) with { Password = "wrong" }, "source"));
        Reject("WEB_RATE_LIMIT", () => h.Login(1));
        h.Auth.Reauthenticate(session.SessionId, session.CsrfToken, h.Request(1), "source");
    }

    /// <summary>Failed reauthentication attempts do not spend the independent anonymous login budget.</summary>
    public static void SeparateReauthenticationBudget()
    {
        using var h = new Harness(); var session = h.Login();
        for (int i = 0; i < 8; i++) Reject("WEB_AUTH_FAILED", () => h.Auth.Reauthenticate(session.SessionId, session.CsrfToken, h.Request(1) with { Password = "wrong" }, "source"));
        Reject("WEB_RATE_LIMIT", () => h.Auth.Reauthenticate(session.SessionId, session.CsrfToken, h.Request(1), "source"));
        h.Login(1);
    }

    /// <summary>Changing ingress addresses does not bypass the authenticated-session attempt budget.</summary>
    public static void SessionBudgetBinding()
    {
        using var h = new Harness(); var session = h.Login();
        for (int i = 0; i < 8; i++) Reject("WEB_AUTH_FAILED", () => h.Auth.Reauthenticate(session.SessionId, session.CsrfToken, h.Request(1) with { Password = "wrong" }, "source-" + i));
        Reject("WEB_RATE_LIMIT", () => h.Auth.Reauthenticate(session.SessionId, session.CsrfToken, h.Request(1), "new-source"));
        h.Data.Time.Advance(TimeSpan.FromMinutes(5)); h.Auth.Reauthenticate(session.SessionId, session.CsrfToken, h.Request(1), "source");
    }

    /// <summary>TOTP age is evaluated after slow verification, not at request arrival.</summary>
    public static void TotpExpiryDuringVerification()
    {
        using var h = new Harness(); var request = h.Request(0) with { Code = Totp.Code(h.Setup.TotpSecret, h.Data.Time.GetUtcNow().ToUnixTimeSeconds() / 30) };
        h.Arm(); var task = Worker(() => h.Auth.Login(request, "source")); h.Wait();
        h.Data.Time.Advance(TimeSpan.FromMinutes(2)); h.Continue.Set(); Failed(task, "WEB_AUTH_FAILED"); h.Login();
    }

    /// <summary>Failed atomic account persistence consumes neither a factor nor the verification lane.</summary>
    public static void AccountWriteFailure()
    {
        using var h = new Harness(); string path = Path.Combine(h.Data.Directory, "web-account.json"); string saved = path + ".saved";
        File.Move(path, saved); Directory.CreateDirectory(path);
        try { Check(Capture(() => h.Login()) is IOException); }
        finally { Directory.Delete(path); File.Move(saved, path); }
        h.Login();
    }

    /// <summary>CSRF and session errors are rejected before password work or second-factor consumption.</summary>
    public static void AdmissionAuthentication()
    {
        using var h = new Harness(); var session = h.Login(); int before = h.Calls;
        Reject("WEB_CSRF", () => h.Auth.Reauthenticate(session.SessionId, "wrong", h.Request(1), "source"));
        Reject("WEB_UNAUTHORIZED", () => h.Auth.Reauthenticate(LicenseCrypto.Nonce(), session.CsrfToken, h.Request(1), "source"));
        Check(h.Calls == before); h.Auth.Reauthenticate(session.SessionId, session.CsrfToken, h.Request(1), "source");
    }
    /// <summary>Null direct-API fields cannot select the wrong admission lane, bypass CSRF or poison future logins.</summary>
    public static void MalformedDirectApi()
    {
        using var h = new Harness(); var session = h.Login(); int calls = h.Calls;
        Reject("WEB_UNAUTHORIZED", () => h.Auth.Reauthenticate(null!, session.CsrfToken, h.Request(1), "source"));
        Reject("WEB_CSRF", () => h.Auth.Reauthenticate(session.SessionId, null!, h.Request(1), "source"));
        Reject("WEB_UNAUTHORIZED", () => h.Auth.Require(null!));
        Reject("WEB_AUTH_FAILED", () => h.Auth.Login(null!, "source"));
        Reject("WEB_AUTH_FAILED", () => h.Auth.Login(h.Request(1) with { Password = null! }, "source"));
        Check(h.Calls == calls); h.Login(1);
    }

}

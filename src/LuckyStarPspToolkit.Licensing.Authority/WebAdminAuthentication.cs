using System.Security.Cryptography;
using System.Text;

namespace LuckyStarPspToolkit.Licensing.Authority;

/// <summary>Login or recent-authentication proof; passwords and one-time codes are never returned by the API.</summary>
/// <param name="Username">Configured owner account name.</param>
/// <param name="Password">Password over same-origin HTTPS.</param>
/// <param name="Code">Six-digit TOTP or a single-use recovery code.</param>
public sealed record WebLoginRequest(string Username, string Password, string Code);

/// <summary>Private authenticated account state; only the local initialization command can replace credentials.</summary>
/// <param name="Schema">Fixed schema one.</param>
/// <param name="Username">Owner name.</param>
/// <param name="Origin">Exact HTTPS origin, no path or wildcard.</param>
/// <param name="DevelopmentLoopback">Allows literal loopback HTTP solely for explicit local tests.</param>
/// <param name="PasswordSalt">Random PBKDF2 salt.</param>
/// <param name="PasswordHash">PBKDF2-SHA256 result, 600000 iterations.</param>
/// <param name="TotpSecret">Base32 160-bit authenticator seed, retained only in the private authority directory.</param>
/// <param name="LastTotpStep">Last consumed time step, guarding replay across server restarts.</param>
/// <param name="RecoveryHashes">Digests of unused high-entropy recovery codes.</param>
internal sealed record WebAccount(int Schema, string Username, string Origin, bool DevelopmentLoopback,
    string PasswordSalt, string PasswordHash, string TotpSecret, long LastTotpStep, string[] RecoveryHashes);

/// <summary>Private one-time bootstrap output; move it to the owner's password manager, never to the web root.</summary>
/// <param name="Username">Account name.</param>
/// <param name="Origin">Public administration origin.</param>
/// <param name="TotpSecret">Seed to import into an authenticator.</param>
/// <param name="OtpAuthUri">Authenticator enrollment URI; not an authentication bypass.</param>
/// <param name="RecoveryCodes">One-time second-factor recovery codes; password remains mandatory.</param>
public sealed record WebAdminSetup(string Username, string Origin, string TotpSecret, string OtpAuthUri, string[] RecoveryCodes);

/// <summary>Opaque cookie and anti-CSRF token returned internally after successful MFA.</summary>
/// <param name="SessionId">256-bit cookie value; must be HttpOnly.</param>
/// <param name="CsrfToken">Separate random token required for every authenticated mutation.</param>
/// <param name="Username">Authenticated owner.</param>
public sealed record WebSession(string SessionId, string CsrfToken, string Username);

/// <summary>Single-owner browser authentication: PBKDF2, replay-resistant TOTP, one-use recovery, expiring opaque sessions and exact CSRF.</summary>
public sealed partial class WebAdminAuthentication : IDisposable
{
    /// <summary>Session idle limit; activity does not extend the absolute eight-hour lifetime.</summary>
    public static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(15);
    /// <summary>Mandatory fresh MFA interval for reserve policy and other high-impact operations.</summary>
    public static readonly TimeSpan FreshLimit = TimeSpan.FromMinutes(5);
    /// <summary>Private account path.</summary>
    private readonly string path;
    /// <summary>Authority HMAC key authenticates account state in a separate domain.</summary>
    private readonly byte[] pepper;
    /// <summary>Prevents a second server or offline reset from racing credential/recovery updates.</summary>
    private readonly FileStream writerLock;
    /// <summary>Serializes credential consumption and sessions.</summary>
    private readonly object sync = new();
    /// <summary>Injected clock, normally the platform clock.</summary>
    private readonly TimeProvider time;
    /// <summary>Current authenticated account data.</summary>
    private WebAccount account;
    /// <summary>Bounded in-memory session store; no reusable sessions survive restart.</summary>
    private readonly Dictionary<string, SessionEntry> sessions = new(StringComparer.Ordinal);
    /// <summary>Bounded per-ingress login attempt windows, independently protected by proxy rate limiting.</summary>
    private readonly Dictionary<string, AttemptWindow> attempts = new(StringComparer.Ordinal);

    /// <summary>Mutable timestamps for one opaque session, used only under the authentication lock.</summary>
    /// <param name="View">Cookie and CSRF values.</param>
    /// <param name="Created">Monotonic creation time.</param>
    /// <param name="Seen">Last activity.</param>
    /// <param name="Authenticated">Last password plus MFA verification.</param>
    /// <param name="ManagementId">Independent public handle; never accepted as an authentication cookie.</param>
    private sealed record SessionEntry(WebSession View, long Created, long Seen, long Authenticated, string ManagementId);
    /// <summary>Per-source fixed login window.</summary>
    /// <param name="Started">Monotonic start.</param>
    /// <param name="Count">Attempts including successful ones.</param>
    private sealed record AttemptWindow(long Started, int Count);

    /// <summary>Creates new credentials offline without overwriting an existing account.</summary>
    /// <param name="directory">Existing private authority directory; server must be stopped for initial setup.</param>
    /// <param name="username">ASCII account identifier, 1..64 characters.</param>
    /// <param name="password">Password of 16..256 characters, never written to bootstrap output.</param>
    /// <param name="origin">Exact HTTPS administration origin, normally a dedicated subdomain.</param>
    /// <param name="development">Explicit loopback-only HTTP mode for testing.</param>
    /// <param name="enrollmentFile">Optional private bootstrap output, created before account commit so an output failure cannot strand the owner.</param>
    /// <returns>Private enrollment seed and single-use recovery codes.</returns>
    public static WebAdminSetup Initialize(string directory, string username, string password, string origin, bool development = false, string? enrollmentFile = null)
    {
        ValidateOrigin(origin, development);
        if (username.Length is < 1 or > 64 || username.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.')) ||
            password.Length is < 16 or > 256) throw new LicenseException("WEB_SETUP", "Use a 1..64 character ASCII username and a 16..256 character password.");
        var config = LicenseJson.Read<AuthorityConfiguration>(PrivateFiles.Read(Path.Combine(directory, "authority.json")));
        using FileStream guard = PrivateFiles.Lock(Path.Combine(directory, "web-account.lock"));
        string path = Path.Combine(directory, "web-account.json");
        if (File.Exists(path)) throw new LicenseException("WEB_EXISTS", "Web account already exists; preserve it or perform a documented stopped-server reset.");
        byte[] salt = RandomNumberGenerator.GetBytes(32);
        string secret = Totp.Base32(RandomNumberGenerator.GetBytes(20));
        string[] recovery = Enumerable.Range(0, 8).Select(_ => "RCV-" + LicenseCrypto.Nonce()).ToArray();
        var account = new WebAccount(1, username, origin, development, Convert.ToBase64String(salt),
            Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, 600000, HashAlgorithmName.SHA256, 32)),
            secret, -1, recovery.Select(x => LicenseCrypto.Digest(Encoding.UTF8.GetBytes(x))).ToArray());
        string uri = "otpauth://totp/" + Uri.EscapeDataString("LSP Owner:" + username) + "?secret=" + secret + "&issuer=LSP%20Owner&algorithm=SHA1&digits=6&period=30";
        var setup = new WebAdminSetup(username, origin, secret, uri, recovery);
        byte[] pepper = Convert.FromBase64String(config.Pepper);
        bool enrollmentCreated = false;
        try
        {
            if (enrollmentFile is not null)
            {
                if (Path.GetFullPath(enrollmentFile) == Path.GetFullPath(path)) throw new LicenseException("WEB_SETUP", "Enrollment and account files must differ.");
                PrivateFiles.Write(enrollmentFile, LicenseJson.Write(setup), false); enrollmentCreated = true;
            }
            WriteAccount(path, account, pepper, false);
        }
        catch
        {
            if (enrollmentCreated) { try { File.Delete(PrivateFiles.SafePath(enrollmentFile!)); } catch (IOException) { } }
            throw;
        }
        finally { CryptographicOperations.ZeroMemory(pepper); }
        return setup;
    }

    /// <summary>Opens private account state and holds the single-writer lock for the server lifetime.</summary>
    /// <param name="directory">Authority directory.</param>
    /// <param name="time">Optional deterministic test clock.</param>
    public WebAdminAuthentication(string directory, TimeProvider? time = null)
    {
        this.time = time ?? TimeProvider.System;
        var config = LicenseJson.Read<AuthorityConfiguration>(PrivateFiles.Read(Path.Combine(directory, "authority.json")));
        pepper = Convert.FromBase64String(config.Pepper); path = Path.Combine(directory, "web-account.json");
        writerLock = PrivateFiles.Lock(Path.Combine(directory, "web-account.lock"));
        try
        {
            var envelope = LicenseJson.Read<DatabaseEnvelope>(PrivateFiles.Read(path));
            byte[] bytes = Convert.FromBase64String(envelope.Payload);
            if (envelope.Schema != 1 || bytes.Length > 16384 || !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(envelope.Mac), AccountMac(bytes, pepper))) throw new LicenseException("WEB_INTEGRITY", "Web account integrity check failed.");
            account = LicenseJson.Read<WebAccount>(bytes, 16384);
            ValidateOrigin(account.Origin, account.DevelopmentLoopback);
            if (account.Schema != 1 || account.LastTotpStep < -1 || account.RecoveryHashes is null || account.RecoveryHashes.Length > 8 ||
                Convert.FromBase64String(account.PasswordSalt).Length != 32 || Convert.FromBase64String(account.PasswordHash).Length != 32 ||
                Totp.DecodeBase32(account.TotpSecret).Length != 20)
                throw new LicenseException("WEB_CONFIG", "Invalid web account configuration.");
            foreach (string hash in account.RecoveryHashes) LicenseCrypto.ValidateDigest(hash);
        }
        catch { writerLock.Dispose(); CryptographicOperations.ZeroMemory(pepper); throw; }
    }

    /// <summary>Fixed origin, validated at startup; never inferred from forwarded request headers.</summary>
    public string Origin => account.Origin;
    /// <summary>Explicit loopback test mode; production cookies always require Secure.</summary>
    public bool DevelopmentLoopback => account.DevelopmentLoopback;
    /// <summary>Host-prefixed production cookie prevents domain/path weakening.</summary>
    public string CookieName => DevelopmentLoopback ? "lsp-dev-session" : "__Host-lsp-session";

    /// <summary>Verifies password and a fresh second factor before creating an opaque session.</summary>
    /// <param name="request">Bounded untrusted login fields.</param>
    /// <param name="source">Trusted proxy ingress bucket, or literal loopback in tests.</param>
    /// <returns>New cookie/CSRF pair, never the owner bearer token.</returns>
    public WebSession Login(WebLoginRequest request, string source)
    {
        lock (sync)
        {
            Admit(source); Prune();
            if (sessions.Count >= 64) throw new LicenseException("WEB_BUSY", "Session capacity reached; retry after idle sessions expire.");
            Authenticate(request);
            long now = time.GetTimestamp();
            var view = new WebSession(LicenseCrypto.Nonce(), LicenseCrypto.Nonce(), account.Username);
            sessions.Add(view.SessionId, new(view, now, now, now, Guid.NewGuid().ToString("D"))); return view;
        }
    }

    /// <summary>Requires an unexpired session and optional CSRF/fresh-MFA proof, then advances only its idle timestamp.</summary>
    /// <param name="id">Opaque cookie value.</param>
    /// <param name="csrf">Header token for mutations; null for safe authenticated reads.</param>
    /// <param name="fresh">Whether recent password and MFA are required.</param>
    /// <returns>Authenticated view.</returns>
    public WebSession Require(string id, string? csrf = null, bool fresh = false)
    {
        lock (sync)
        {
            Prune();
            if (id.Length != 43 || !sessions.TryGetValue(id, out var entry)) throw new LicenseException("WEB_UNAUTHORIZED", "Sign in to continue.");
            if (csrf is not null && !Equal(csrf, entry.View.CsrfToken)) throw new LicenseException("WEB_CSRF", "The request CSRF token is invalid.");
            if (fresh && time.GetElapsedTime(entry.Authenticated) > FreshLimit) throw new LicenseException("WEB_REAUTH_REQUIRED", "Confirm your password and a new authenticator code.");
            sessions[id] = entry with { Seen = time.GetTimestamp() }; return entry.View;
        }
    }

    /// <summary>Consumes fresh password/MFA proof for a currently authenticated, CSRF-protected session.</summary>
    /// <param name="id">Current session.</param>
    /// <param name="csrf">Current anti-CSRF token.</param>
    /// <param name="request">Password and fresh second factor.</param>
    /// <param name="source">Trusted ingress bucket.</param>
    public void Reauthenticate(string id, string csrf, WebLoginRequest request, string source)
    {
        lock (sync)
        {
            _ = Require(id, csrf); Admit(source); Authenticate(request);
            sessions[id] = sessions[id] with { Authenticated = time.GetTimestamp() };
        }
    }

    /// <summary>Invalidates a session server-side; deleting a browser cookie alone is not sufficient.</summary>
    /// <param name="id">Opaque current cookie value.</param>
    public void Logout(string id) { lock (sync) sessions.Remove(id); }

    /// <summary>Consumes a second factor durably before granting any new authentication state.</summary>
    /// <param name="request">Bounded login fields; errors deliberately do not disclose which factor was wrong.</param>
    private void Authenticate(WebLoginRequest request)
    {
        if (request.Username.Length > 64 || request.Password.Length > 256 || request.Code.Length > 64)
            throw new LicenseException("WEB_AUTH_FAILED", "Invalid account, password or one-time code.");
        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(request.Password, Convert.FromBase64String(account.PasswordSalt),
            600000, HashAlgorithmName.SHA256, 32);
        bool passwordOk = CryptographicOperations.FixedTimeEquals(actual, Convert.FromBase64String(account.PasswordHash));
        CryptographicOperations.ZeroMemory(actual);
        if (!passwordOk || request.Username != account.Username) throw new LicenseException("WEB_AUTH_FAILED", "Invalid account, password or one-time code.");
        long step = Totp.Match(account.TotpSecret, request.Code, time.GetUtcNow().ToUnixTimeSeconds(), account.LastTotpStep);
        string hash = LicenseCrypto.Digest(Encoding.UTF8.GetBytes(request.Code));
        bool recovery = request.Code.StartsWith("RCV-", StringComparison.Ordinal) && account.RecoveryHashes.Any(x => Equal(x, hash));
        if (step < 0 && !recovery) throw new LicenseException("WEB_AUTH_FAILED", "Invalid account, password or one-time code.");
        WebAccount updated = account with
        {
            LastTotpStep = step >= 0 ? step : account.LastTotpStep,
            RecoveryHashes = recovery ? account.RecoveryHashes.Where(x => !Equal(x, hash)).ToArray() : account.RecoveryHashes
        };
        WriteAccount(path, updated, pepper, true); account = updated;
    }

    /// <summary>Limits expensive password derivations and source buckets, without accepting arbitrary client IP headers directly.</summary>
    /// <param name="source">Proxy-supplied address already selected by the web listener.</param>
    private void Admit(string source)
    {
        long now = time.GetTimestamp();
        foreach (string key in attempts.Where(x => time.GetElapsedTime(x.Value.Started) >= TimeSpan.FromMinutes(5)).Select(x => x.Key).ToArray()) attempts.Remove(key);
        if (source.Length > 80 || (!attempts.ContainsKey(source) && attempts.Count >= 1024)) throw new LicenseException("WEB_RATE_LIMIT", "Too many sign-in attempts.");
        var entry = attempts.GetValueOrDefault(source) ?? new AttemptWindow(now, 0);
        if (entry.Count >= 8) throw new LicenseException("WEB_RATE_LIMIT", "Too many sign-in attempts. Retry later or use the private owner console.");
        attempts[source] = entry with { Count = entry.Count + 1 };
    }

    /// <summary>Removes expired sessions using monotonic idle and absolute lifetimes.</summary>
    private void Prune()
    {
        foreach (string key in sessions.Where(x => time.GetElapsedTime(x.Value.Seen) >= IdleLimit ||
            time.GetElapsedTime(x.Value.Created) >= TimeSpan.FromHours(8)).Select(x => x.Key).ToArray()) sessions.Remove(key);
    }

    /// <summary>Checks exact same-origin deployment and forbids wildcard hosts or production HTTP.</summary>
    /// <param name="origin">Configured canonical origin without trailing slash.</param>
    /// <param name="development">Literal loopback HTTP exception.</param>
    private static void ValidateOrigin(string origin, bool development)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.UserInfo != "" || uri.Query != "" || uri.Fragment != "" ||
            uri.AbsolutePath != "/" || uri.GetLeftPart(UriPartial.Authority) != origin ||
            (uri.Scheme != "https" && !(development && uri.Scheme == "http" && System.Net.IPAddress.TryParse(uri.Host, out var ip) && System.Net.IPAddress.IsLoopback(ip))))
            throw new LicenseException("WEB_ORIGIN", "Configure an exact HTTPS origin (no path/trailing slash); test HTTP must use a literal loopback address.");
    }

    /// <summary>Compares bounded secret strings without content-dependent early exit.</summary>
    /// <param name="left">Candidate.</param>
    /// <param name="right">Expected value.</param>
    /// <returns>Equality.</returns>
    private static bool Equal(string left, string right) => left.Length == right.Length &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    /// <summary>Computes a purpose-bound authentication tag distinct from database envelopes.</summary>
    /// <param name="bytes">Account payload.</param>
    /// <param name="pepper">Authority integrity secret.</param>
    /// <returns>HMAC-SHA256.</returns>
    private static byte[] AccountMac(byte[] bytes, byte[] pepper) => HMACSHA256.HashData(pepper, Encoding.ASCII.GetBytes("LSP-WEB-ACCOUNT-1\n").Concat(bytes).ToArray());

    /// <summary>Atomically commits account state before replayable factors are accepted.</summary>
    /// <param name="path">Private destination.</param>
    /// <param name="account">New account snapshot.</param>
    /// <param name="pepper">Authority integrity secret.</param>
    /// <param name="overwrite">Whether this is a normal state update rather than initial setup.</param>
    private static void WriteAccount(string path, WebAccount account, byte[] pepper, bool overwrite)
    {
        byte[] bytes = LicenseJson.Write(account);
        PrivateFiles.Write(path, LicenseJson.Write(new DatabaseEnvelope(1, Convert.ToBase64String(bytes), Convert.ToHexString(AccountMac(bytes, pepper)))), overwrite);
    }

    /// <summary>Invalidates all browser sessions and releases private resources after requests are drained.</summary>
    public void Dispose() { lock (sync) { sessions.Clear(); CryptographicOperations.ZeroMemory(pepper); writerLock.Dispose(); } }
}

/// <summary>RFC 6238-compatible SHA-1 TOTP with a fixed six-digit/30-second profile and replay checks supplied by the caller.</summary>
public static class Totp
{
    /// <summary>RFC 4648 alphabet, without ambiguous numeric substitutions.</summary>
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    /// <summary>Encodes a small random seed as unpadded canonical Base32.</summary>
    /// <param name="bytes">Seed bytes.</param>
    /// <returns>Canonical Base32 text.</returns>
    public static string Base32(ReadOnlySpan<byte> bytes)
    {
        var result = new StringBuilder(); int bits = 0, value = 0;
        foreach (byte b in bytes) { value = (value << 8) | b; bits += 8; while (bits >= 5) { bits -= 5; result.Append(Alphabet[(value >> bits) & 31]); } }
        if (bits != 0) result.Append(Alphabet[(value << (5 - bits)) & 31]); return result.ToString();
    }
    /// <summary>Decodes a canonical bounded unpadded Base32 seed.</summary>
    /// <param name="text">Uppercase seed.</param>
    /// <returns>Decoded bytes.</returns>
    public static byte[] DecodeBase32(string text)
    {
        if (text.Length is < 16 or > 64) throw new LicenseException("WEB_TOTP", "Invalid authenticator seed.");
        var result = new List<byte>(); int value = 0, bits = 0;
        foreach (char c in text) { int n = Alphabet.IndexOf(c); if (n < 0) throw new LicenseException("WEB_TOTP", "Invalid authenticator seed.");
            value = (value << 5) | n; bits += 5; if (bits >= 8) { bits -= 8; result.Add((byte)(value >> bits)); } }
        byte[] bytes = result.ToArray(); if (Base32(bytes) != text) throw new LicenseException("WEB_TOTP", "Noncanonical authenticator seed."); return bytes;
    }
    /// <summary>Computes one six-digit code for a nonnegative 30-second time step.</summary>
    /// <param name="secret">Base32 seed.</param>
    /// <param name="step">UTC Unix seconds divided by 30.</param>
    /// <returns>Six ASCII digits, including leading zeros.</returns>
    public static string Code(string secret, long step)
    {
        if (step < 0) return "";
        byte[] counter = new byte[8]; System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, step);
        byte[] hash = HMACSHA1.HashData(DecodeBase32(secret), counter); int offset = hash[^1] & 15;
        uint value = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(offset, 4)) & 0x7fffffff;
        return (value % 1000000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }
    /// <summary>Accepts only a matching unused step within one adjacent time window.</summary>
    /// <param name="secret">Private seed.</param>
    /// <param name="code">Six-digit code; recovery codes are handled separately.</param>
    /// <param name="utc">Current authoritative wall time.</param>
    /// <param name="lastUsed">Largest previously accepted step.</param>
    /// <returns>Matched step or minus one.</returns>
    public static long Match(string secret, string code, long utc, long lastUsed)
    {
        if (code.Length != 6 || code.Any(c => c is < '0' or > '9')) return -1;
        long result = -1;
        for (long step = utc / 30 - 1; step <= utc / 30 + 1; step++)
            if (step > lastUsed && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(code), Encoding.ASCII.GetBytes(Code(secret, step)))) result = step;
        return result;
    }
}

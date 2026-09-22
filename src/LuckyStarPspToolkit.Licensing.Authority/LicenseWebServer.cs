using System.Net;
using System.Text;

namespace LuckyStarPspToolkit.Licensing.Authority;

/// <summary>Browser-only loopback backend. It never exposes the private bearer API and authenticates every management operation with MFA sessions.</summary>
public sealed class LicenseWebServer : IAsyncDisposable
{
    /// <summary>Shared authority; the server process owns its lifetime.</summary>
    private readonly LicenseAuthority authority;
    /// <summary>Independent browser authentication and account writer.</summary>
    private readonly WebAdminAuthentication authentication;
    /// <summary>Dedicated third loopback port; only its exact origin is reverse-proxied.</summary>
    private readonly HttpListener listener = new();
    /// <summary>Bounded admitted workers; excessive requests fail rather than queue without limit.</summary>
    private readonly SemaphoreSlim capacity = new(8, 8);
    /// <summary>Cancellation for all active body reads and listener shutdown.</summary>
    private readonly CancellationTokenSource stop = new();
    /// <summary>Request task registry for orderly shutdown.</summary>
    private readonly List<Task> workers = new();
    /// <summary>Synchronization for the registry.</summary>
    private readonly object sync = new();
    /// <summary>Explicitly allow-listed static bytes loaded once; no request may read arbitrary files.</summary>
    private readonly Dictionary<string, (byte[] Bytes, string Type)> assets = new(StringComparer.Ordinal);
    /// <summary>Lifetime accept loop.</summary>
    private Task loop = Task.CompletedTask;

    /// <summary>Opens a private account and a fixed frontend build, never using a development web server in production.</summary>
    /// <param name="authority">Initialized shared authority.</param>
    /// <param name="directory">Private authority state directory.</param>
    /// <param name="webRoot">Compiled frontend directory containing index.html and fixed assets.</param>
    /// <param name="port">Dedicated unprivileged loopback port.</param>
    public LicenseWebServer(LicenseAuthority authority, string directory, string webRoot, int port = 17842)
    {
        if (port is < 1024 or > 65535) throw new LicenseException("WEB_PORT", "Use an unprivileged loopback port.");
        this.authority = authority;
        authentication = new WebAdminAuthentication(directory);
        try
        {
            string root = PrivateFiles.SafePath(webRoot);
            foreach (var item in new[] { ("index.html", "text/html; charset=utf-8"), ("assets/app.js", "text/javascript; charset=utf-8"),
                ("assets/vendor.js", "text/javascript; charset=utf-8"), ("assets/app.css", "text/css; charset=utf-8") })
                assets.Add("/" + item.Item1, (PrivateFiles.Read(Path.Combine(root, item.Item1), 2 * 1024 * 1024), item.Item2));
            assets.Add("/", assets["/index.html"]);
            listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.IgnoreWriteExceptions = true;
        }
        catch { authentication.Dispose(); listener.Close(); throw; }
    }

    /// <summary>Task completed on listener shutdown or failure.</summary>
    public Task Completion => loop;
    /// <summary>Starts accepting browser requests. Bind errors are fatal.</summary>
    public void Start() { listener.Start(); loop = AcceptAsync(); }

    /// <summary>Accepts bounded work and removes completed tasks so the registry does not grow with traffic.</summary>
    /// <returns>Listener lifetime.</returns>
    private async Task AcceptAsync()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var context = await listener.GetContextAsync().ConfigureAwait(false);
                if (!capacity.Wait(0)) { context.Response.StatusCode = 503; context.Response.Close(); continue; }
                lock (sync)
                {
                    workers.RemoveAll(t => t.IsCompleted);
                    workers.Add(Task.Run(async () => { try { await HandleAsync(context).ConfigureAwait(false); } finally { capacity.Release(); } }));
                }
            }
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
        { if (!stop.IsCancellationRequested) throw; }
    }

    /// <summary>Enforces same-origin, body bounds, session/CSRF and recent MFA before invoking authority methods.</summary>
    /// <param name="context">Accepted loopback request.</param>
    /// <returns>Response completion.</returns>
    private async Task HandleAsync(HttpListenerContext context)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            HttpListenerRequest request = context.Request; HttpListenerResponse response = context.Response;
            SecureHeaders(response);
            if (request.RemoteEndPoint is null || !IPAddress.IsLoopback(request.RemoteEndPoint.Address))
                throw new LicenseException("WEB_ORIGIN", "Backend must be reached through the local reverse proxy.");
            if (request.Url is null || request.Url.Query != "") throw new LicenseException("WEB_REQUEST", "Query strings are not accepted.");
            string path = request.Url.AbsolutePath;
            if (request.HttpMethod == "GET" && assets.TryGetValue(path, out var asset))
            {
                response.ContentType = asset.Type; response.ContentLength64 = asset.Bytes.Length;
                await response.OutputStream.WriteAsync(asset.Bytes, timeout.Token).ConfigureAwait(false); return;
            }
            if (!path.StartsWith("/api/", StringComparison.Ordinal)) throw new LicenseException("WEB_NOT_FOUND", "Unknown browser endpoint.");
            if (request.Headers["Sec-Fetch-Site"] is "cross-site" or "same-site") throw new LicenseException("WEB_ORIGIN", "Cross-origin requests are not accepted.");
            if (request.HttpMethod == "POST" && request.Headers["Origin"] != authentication.Origin)
                throw new LicenseException("WEB_ORIGIN", "Exact same-origin requests are required.");
            string? suppliedOrigin = request.Headers["Origin"];
            if (suppliedOrigin is not null && suppliedOrigin != authentication.Origin) throw new LicenseException("WEB_ORIGIN", "Origin does not match configuration.");
            string sessionId = request.Cookies[authentication.CookieName]?.Value ?? "";
            string csrf = request.Headers["X-CSRF-Token"] ?? "";
            // Only the loopback reverse proxy is trusted to overwrite this header. No application authorization uses it.
            string source = request.RemoteEndPoint.Address.ToString();
            if (!authentication.DevelopmentLoopback && IPAddress.TryParse(request.Headers["X-Real-IP"], out var ingress)) source = ingress.ToString();
            object result;
            if (request.HttpMethod == "GET")
            {
                var session = authentication.Require(sessionId);
                result = path switch
                {
                    "/api/session" => SessionView(session),
                    "/api/sessions" => authentication.ListSessions(sessionId),
                    "/api/licenses" => authority.List(),
                    "/api/audit" => authority.AuditRecent(),
                    "/api/reserve" => authority.ReserveStatus(),
                    _ => throw new LicenseException("WEB_NOT_FOUND", "Unknown browser endpoint.")
                };
            }
            else if (request.HttpMethod == "POST")
            {
                if ((request.ContentType ?? "").Split(';')[0].Trim() != "application/json" || request.Headers["Content-Encoding"] is not null)
                    throw new LicenseException("WEB_CONTENT", "Use uncompressed JSON.");
                byte[] body = await ReadBodyAsync(request, timeout.Token).ConfigureAwait(false);
                if (path == "/api/login")
                {
                    var session = authentication.Login(LicenseJson.Read<WebLoginRequest>(body), source);
                    authentication.Logout(sessionId); // session rotation; failed logins never erase the previous session
                    SetCookie(response, session.SessionId, false); result = SessionView(session);
                }
                else
                {
                    _ = authentication.Require(sessionId, csrf);
                    switch (path)
                    {
                        case "/api/logout":
                            _ = LicenseJson.Read<Dictionary<string, string>>(body);
                            authentication.Logout(sessionId); SetCookie(response, "", true); result = new { ok = true }; break;
                        case "/api/sessions/revoke":
                            result = new { revoked = authentication.RevokeSessions(sessionId, csrf, LicenseJson.Read<WebSessionRevokeRequest>(body)) }; break;
                        case "/api/reauth":
                            authentication.Reauthenticate(sessionId, csrf, LicenseJson.Read<WebLoginRequest>(body), source);
                            result = new { ok = true }; break;
                        case "/api/issue":
                            var issue = LicenseJson.Read<IssueLicenseRequest>(body);
                            if (issue.Unit == "permanent") _ = authentication.Require(sessionId, csrf, true);
                            result = authority.Issue(issue); break;
                        case "/api/change":
                            var change = LicenseJson.Read<ChangeLicenseRequest>(body);
                            if (change.Action is "revoke" or "permanent" or "reset-device") _ = authentication.Require(sessionId, csrf, true);
                            result = authority.Change(change); break;
                        case "/api/reserve/policy":
                            _ = authentication.Require(sessionId, csrf, true); result = authority.SetReservePolicy(LicenseJson.Read<ReservePolicyRequest>(body)); break;
                        case "/api/reserve/issue":
                            _ = authentication.Require(sessionId, csrf, true); result = authority.IssueReserve(LicenseJson.Read<ReserveIssueRequest>(body)); break;
                        case "/api/reserve/revoke":
                            _ = authentication.Require(sessionId, csrf, true); result = authority.RevokeReserve(LicenseJson.Read<ReserveRevokeRequest>(body)); break;
                        default: throw new LicenseException("WEB_NOT_FOUND", "Unknown browser endpoint.");
                    }
                }
            }
            else throw new LicenseException("WEB_NOT_FOUND", "Unsupported method.");
            await JsonAsync(response, 200, result, timeout.Token).ConfigureAwait(false);
        }
        catch (LicenseException ex)
        {
            int status = ex.Code switch { "WEB_UNAUTHORIZED" or "WEB_AUTH_FAILED" => 401, "WEB_ORIGIN" or "WEB_CSRF" or "WEB_REAUTH_REQUIRED" => 403,
                "WEB_NOT_FOUND" => 404, "WEB_RATE_LIMIT" or "WEB_BUSY" => 429, "WEB_BODY" => 413, "WEB_CONTENT" => 415, _ => 400 };
            try { await JsonAsync(context.Response, status, new LicenseError(ex.Code, ex.Message), timeout.Token).ConfigureAwait(false); } catch { context.Response.Abort(); }
        }
        catch (Exception)
        {
            try { await JsonAsync(context.Response, 500, new LicenseError("WEB_FAILURE", "Management operation could not complete; retry with the same request ID."), timeout.Token).ConfigureAwait(false); }
            catch { context.Response.Abort(); }
        }
        finally { context.Response.Close(); }
    }

    /// <summary>Returns only the username, CSRF token and authentication policy, never a cookie/session value to JavaScript.</summary>
    /// <param name="session">Authenticated internal session.</param>
    /// <returns>Browser-safe session view.</returns>
    private static object SessionView(WebSession session) => new { username = session.Username, csrfToken = session.CsrfToken,
        idleMinutes = 15, absoluteHours = 8, freshMinutes = 5 };

    /// <summary>Sets or expires an HttpOnly host-scoped cookie; plain HTTP is limited to explicit local test mode.</summary>
    /// <param name="response">Outgoing response.</param>
    /// <param name="value">Opaque CSPRNG session ID, never user content.</param>
    /// <param name="expire">Whether to delete the cookie.</param>
    private void SetCookie(HttpListenerResponse response, string value, bool expire) => response.Headers.Add("Set-Cookie",
        authentication.CookieName + "=" + value + "; Path=/; HttpOnly; SameSite=Strict" +
        (authentication.DevelopmentLoopback ? "" : "; Secure") + (expire ? "; Max-Age=0" : ""));

    /// <summary>Applies conservative browser headers to assets, errors and API responses.</summary>
    /// <param name="response">Outgoing response.</param>
    private static void SecureHeaders(HttpListenerResponse response)
    {
        response.Headers["Cache-Control"] = "no-store"; response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["X-Frame-Options"] = "DENY"; response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        response.Headers["Content-Security-Policy"] = "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'; object-src 'none'";
    }

    /// <summary>Bounds actual bytes independently of the declared content length.</summary>
    /// <param name="request">POST body source.</param>
    /// <param name="cancellation">Request timeout.</param>
    /// <returns>One JSON message.</returns>
    private static async Task<byte[]> ReadBodyAsync(HttpListenerRequest request, CancellationToken cancellation)
    {
        if (request.ContentLength64 > 32768) throw new LicenseException("WEB_BODY", "Request exceeds 32 KiB.");
        using var buffer = new MemoryStream(); byte[] block = new byte[4096]; int count;
        while ((count = await request.InputStream.ReadAsync(block, cancellation).ConfigureAwait(false)) != 0)
        { if (buffer.Length + count > 32768) throw new LicenseException("WEB_BODY", "Request exceeds 32 KiB."); buffer.Write(block, 0, count); }
        return buffer.ToArray();
    }

    /// <summary>Writes bounded management responses without logging raw request data.</summary>
    /// <param name="response">Outgoing response.</param>
    /// <param name="status">HTTP status.</param>
    /// <param name="result">Known model.</param>
    /// <param name="cancellation">Timeout.</param>
    /// <returns>Write completion.</returns>
    private static async Task JsonAsync(HttpListenerResponse response, int status, object result, CancellationToken cancellation)
    {
        byte[] bytes = LicenseJson.Write(result);
        if (bytes.Length > 16 * 1024 * 1024) throw new LicenseException("WEB_RESPONSE_LIMIT", "Result is too large; use the private owner console.");
        response.StatusCode = status; response.ContentType = "application/json; charset=utf-8"; response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellation).ConfigureAwait(false);
    }

    /// <summary>Stops acceptance, drains admitted work and invalidates all browser sessions before releasing account secrets.</summary>
    /// <returns>Shutdown completion.</returns>
    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); listener.Close(); try { await loop.ConfigureAwait(false); } catch { }
        Task[] pending; lock (sync) pending = workers.ToArray(); await Task.WhenAll(pending).ConfigureAwait(false);
        authentication.Dispose(); capacity.Dispose(); stop.Dispose();
    }
}

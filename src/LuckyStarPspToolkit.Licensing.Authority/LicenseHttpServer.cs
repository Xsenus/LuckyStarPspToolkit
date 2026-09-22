using System.Net;
using System.Text;

namespace LuckyStarPspToolkit.Licensing.Authority;

/// <summary>Two loopback-only HTTP listeners: public protocol for a TLS reverse proxy and a separate owner-only management port.</summary>
public sealed class LicenseHttpServer : IAsyncDisposable
{
    /// <summary>Authority whose lifetime is owned by the caller.</summary>
    private readonly LicenseAuthority authority;
    /// <summary>Public backend listener, never directly Internet-facing.</summary>
    private readonly HttpListener publicListener = new();
    /// <summary>Separate loopback admin listener, excluded from the reverse-proxy configuration.</summary>
    private readonly HttpListener adminListener = new();
    /// <summary>Caps concurrently processed public requests independently of the reserved management workers.</summary>
    private readonly SemaphoreSlim publicCapacity = new(24, 24);
    /// <summary>Reserved management workers keep revocation available during public endpoint saturation.</summary>
    private readonly SemaphoreSlim adminCapacity = new(4, 4);
    /// <summary>Shutdown cancellation.</summary>
    private readonly CancellationTokenSource stop = new();
    /// <summary>Server-owned request tasks, drained on disposal.</summary>
    private readonly List<Task> workers = new();
    /// <summary>Synchronization for the task registry and process-wide ingress limiter.</summary>
    private readonly object sync = new();
    /// <summary>Per-minute in-process request count; the proxy separately limits real client IPs.</summary>
    private int requestCount;
    /// <summary>Independent administrative request count; public traffic cannot exhaust it.</summary>
    private int adminRequestCount;
    /// <summary>Monotonic rate-limit window start.</summary>
    private long rateStart = System.Diagnostics.Stopwatch.GetTimestamp();
    /// <summary>Listener accept loops.</summary>
    private Task[] loops = [];

    /// <summary>Constructs isolated backend listeners.</summary>
    /// <param name="authority">Initialized license authority.</param>
    /// <param name="publicPort">Loopback public backend port.</param>
    /// <param name="adminPort">Distinct loopback owner port.</param>
    public LicenseHttpServer(LicenseAuthority authority, int publicPort = 17840, int adminPort = 17841)
    {
        if (publicPort is < 1024 or > 65535 || adminPort is < 1024 or > 65535 || publicPort == adminPort)
            throw new LicenseException("LISTENER_CONFIG", "Use two distinct unprivileged loopback ports.");
        this.authority = authority;
        publicListener.Prefixes.Add($"http://127.0.0.1:{publicPort}/");
        adminListener.Prefixes.Add($"http://127.0.0.1:{adminPort}/");
        publicListener.IgnoreWriteExceptions = true;
        adminListener.IgnoreWriteExceptions = true;
    }

    /// <summary>Completes or faults when listener loops stop; service managers can restart failed listeners.</summary>
    public Task Completion => loops.Length == 0 ? Task.CompletedTask : Task.WhenAny(loops).Unwrap();

    /// <summary>Starts accepting requests; bind failures are fatal, never treated as successful service startup.</summary>
    public void Start()
    {
        try
        {
            publicListener.Start(); adminListener.Start();
            loops = [AcceptAsync(publicListener, false), AcceptAsync(adminListener, true)];
        }
        catch { publicListener.Close(); adminListener.Close(); throw; }
    }

    /// <summary>Accepts one connection at a time with bounded worker admission instead of an unbounded task queue.</summary>
    /// <param name="listener">One of the two fixed listeners.</param>
    /// <param name="admin">Whether this is the private owner port.</param>
    /// <returns>Lifetime accept loop.</returns>
    private async Task AcceptAsync(HttpListener listener, bool admin)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                HttpListenerContext context = await listener.GetContextAsync().ConfigureAwait(false);
                SemaphoreSlim capacity = admin ? adminCapacity : publicCapacity;
                if (!capacity.Wait(0))
                {
                    context.Response.StatusCode = 503;
                    context.Response.Close();
                    continue;
                }
                lock (sync)
                {
                    workers.RemoveAll(task => task.IsCompleted);
                    workers.Add(Task.Run(async () =>
                    {
                        try { await HandleAsync(context, admin).ConfigureAwait(false); }
                        finally { capacity.Release(); }
                    }));
                }
            }
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
        { if (!stop.IsCancellationRequested) throw; }
    }

    /// <summary>Applies an independent process ingress cap. It deliberately does not trust X-Forwarded-For for authorization.</summary>
    /// <param name="admin">Selects the separately reserved management budget.</param>
    /// <returns>Whether this request may be processed.</returns>
    private bool Admit(bool admin)
    {
        lock (sync)
        {
            if (System.Diagnostics.Stopwatch.GetElapsedTime(rateStart) >= TimeSpan.FromMinutes(1))
            { rateStart = System.Diagnostics.Stopwatch.GetTimestamp(); requestCount = 0; adminRequestCount = 0; }
            return admin ? ++adminRequestCount <= 600 : ++requestCount <= 6000;
        }
    }

    /// <summary>Authenticates endpoints, bounds bodies and sanitizes errors without logging request secrets.</summary>
    /// <param name="context">Accepted request/response.</param>
    /// <param name="admin">Selected private listener.</param>
    /// <returns>Completes after the response is closed.</returns>
    private async Task HandleAsync(HttpListenerContext context, bool admin)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var request = context.Request;
            if (request.RemoteEndPoint is null || !IPAddress.IsLoopback(request.RemoteEndPoint.Address))
                throw new LicenseException("ORIGIN_DENIED", "Backend connections must originate on loopback.");
            if (request.Url is null || request.Url.Query != "") throw new LicenseException("REQUEST_INVALID", "Query parameters are not accepted.");
            if (!Admit(admin)) throw new LicenseException("RATE_LIMIT", "Too many licensing requests.");
            string path = request.Url.AbsolutePath;
            if (admin)
            {
                string header = request.Headers["Authorization"] ?? "";
                if (!header.StartsWith("Bearer ", StringComparison.Ordinal) || !authority.IsOwner(header[7..]))
                    throw new LicenseException("OWNER_UNAUTHORIZED", "Owner authentication is required.");
            }
            object response;
            if (request.HttpMethod == "GET" && path == "/health" && !admin)
                response = new { status = "ok", schema = 1 };
            else if (admin && request.HttpMethod == "GET" && path == "/admin/licenses") response = authority.List();
            else if (admin && request.HttpMethod == "GET" && path == "/admin/audit") response = authority.Audit();
            else if (admin && request.HttpMethod == "GET" && path == "/admin/reserve") response = authority.ReserveStatus();
            else if (request.HttpMethod == "POST")
            {
                string contentType = (request.ContentType ?? "").Split(';')[0].Trim();
                if (contentType != "application/json" || request.Headers["Content-Encoding"] is not null)
                    throw new LicenseException("CONTENT_TYPE", "Use uncompressed application/json requests.");
                byte[] body = await ReadBodyAsync(request, timeout.Token).ConfigureAwait(false);
                response = (admin, path) switch
                {
                    (false, "/v1/challenge") => authority.ChallengeFor(LicenseJson.Read<ChallengeRequest>(body)),
                    (false, "/v1/authorize") => authority.Authorize(LicenseJson.Read<LicenseRequest>(body)),
                    (false, "/v1/reserve") => authority.RefreshReserve(LicenseJson.Read<ReserveRefreshRequest>(body)),
                    (true, "/admin/reserve/policy") => authority.SetReservePolicy(LicenseJson.Read<ReservePolicyRequest>(body)),
                    (true, "/admin/reserve/issue") => authority.IssueReserve(LicenseJson.Read<ReserveIssueRequest>(body)),
                    (true, "/admin/reserve/revoke") => authority.RevokeReserve(LicenseJson.Read<ReserveRevokeRequest>(body)),
                    (true, "/admin/issue") => authority.Issue(LicenseJson.Read<IssueLicenseRequest>(body)),
                    (true, "/admin/change") => authority.Change(LicenseJson.Read<ChangeLicenseRequest>(body)),
                    _ => throw new LicenseException("ENDPOINT_UNKNOWN", "Unknown endpoint on this listener.")
                };
            }
            else throw new LicenseException("ENDPOINT_UNKNOWN", "Unsupported endpoint or method.");
            await ReplyAsync(context.Response, 200, response, timeout.Token).ConfigureAwait(false);
        }
        catch (LicenseException ex)
        {
            int status = ex.Code switch
            {
                "OWNER_UNAUTHORIZED" => 401,
                "RATE_LIMIT" or "CHALLENGE_BUSY" => 429,
                "ENDPOINT_UNKNOWN" => 404,
                "CONTENT_TYPE" => 415,
                "BODY_LIMIT" => 413,
                "LICENSE_REVOKED" or "LICENSE_SUSPENDED" or "LICENSE_EXPIRED" or "LICENSE_INVALID" or
                "DEVICE_LIMIT" or "DEVICE_NOT_ACTIVATED" or "DEVICE_PROOF" => 403,
                _ => 400
            };
            try { await ReplyAsync(context.Response, status, new LicenseError(ex.Code, ex.Message), timeout.Token).ConfigureAwait(false); }
            catch (Exception) { context.Response.Abort(); }
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpListenerException or IOException)
        { context.Response.Abort(); }
        catch (Exception)
        {
            // No exception details or secret-bearing body is written to stderr or returned to the public endpoint.
            try { await ReplyAsync(context.Response, 500, new LicenseError("SERVER_FAILURE", "The license authority could not complete this request."), timeout.Token).ConfigureAwait(false); }
            catch (Exception) { context.Response.Abort(); }
        }
        finally { context.Response.Close(); }
    }

    /// <summary>Reads a small body with a timeout and a second bound independent of declared Content-Length.</summary>
    /// <param name="request">Incoming request.</param>
    /// <param name="cancellation">Request timeout.</param>
    /// <returns>Complete bounded JSON bytes.</returns>
    private static async Task<byte[]> ReadBodyAsync(HttpListenerRequest request, CancellationToken cancellation)
    {
        if (request.ContentLength64 > 32768) throw new LicenseException("BODY_LIMIT", "Licensing request is too large.");
        using var buffer = new MemoryStream();
        byte[] block = new byte[4096];
        int read;
        while ((read = await request.InputStream.ReadAsync(block, cancellation).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + read > 32768) throw new LicenseException("BODY_LIMIT", "Licensing request is too large.");
            buffer.Write(block, 0, read);
        }
        return buffer.ToArray();
    }

    /// <summary>Writes one UTF-8 JSON response with cache suppression and no reflected content type.</summary>
    /// <param name="response">Response stream.</param>
    /// <param name="status">HTTP status.</param>
    /// <param name="body">Known response model.</param>
    /// <param name="cancellation">Timeout token.</param>
    /// <returns>Asynchronous write completion.</returns>
    private static async Task ReplyAsync(HttpListenerResponse response, int status, object body, CancellationToken cancellation)
    {
        byte[] bytes = LicenseJson.Write(body);
        response.StatusCode = status; response.ContentType = "application/json"; response.ContentEncoding = Encoding.UTF8;
        response.Headers["Cache-Control"] = "no-store"; response.Headers["X-Content-Type-Options"] = "nosniff";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellation).ConfigureAwait(false);
    }

    /// <summary>Stops both listeners, cancels body reads and drains admitted work before authority secrets are disposed.</summary>
    /// <returns>Asynchronous shutdown completion.</returns>
    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); publicListener.Close(); adminListener.Close();
        try { await Task.WhenAll(loops).ConfigureAwait(false); } catch (Exception) { }
        Task[] pending;
        lock (sync) pending = workers.ToArray();
        await Task.WhenAll(pending).ConfigureAwait(false);
        stop.Dispose(); publicCapacity.Dispose(); adminCapacity.Dispose();
    }
}

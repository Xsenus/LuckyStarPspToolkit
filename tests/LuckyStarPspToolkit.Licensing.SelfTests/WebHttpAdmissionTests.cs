using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LuckyStarPspToolkit.Licensing;
using LuckyStarPspToolkit.Licensing.Authority;

/// <summary>Real loopback sockets verify web-worker isolation and authentication before incomplete body uploads; TLS/browser enforcement remains separate.</summary>
internal static class WebHttpAdmissionTests
{
    /// <summary>Requires a bounded HTTP invariant with no secret-bearing output.</summary>
    /// <param name="ok">Condition to assert.</param>
    private static void Check(bool ok) { if (!ok) throw new InvalidOperationException("Web HTTP admission invariant failed."); }

    /// <summary>Runs actual unauthenticated slow uploads alongside authenticated reads and recent MFA.</summary>
    public static void HttpAdmission() => RunAsync().GetAwaiter().GetResult();

    /// <summary>Allocates a loopback candidate port, preserving bind collisions as failures instead of silently changing deployment.</summary>
    /// <returns>Candidate unprivileged port.</returns>
    private static int Port()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    /// <summary>Sends headers and only the first JSON byte; the incomplete body must not bypass route admission.</summary>
    /// <param name="port">Test backend port.</param>
    /// <param name="path">Fixed test endpoint.</param>
    /// <param name="cookie">Optional opaque session cookie.</param>
    /// <param name="csrf">Optional CSRF token.</param>
    /// <param name="length">Promised byte count; body is completed only explicitly by the test.</param>
    /// <returns>Caller-owned TCP stream keeping the request incomplete.</returns>
    private static async Task<TcpClient> SlowAsync(int port, string path, string cookie = "", string csrf = "", int length = 2048)
    {
        var socket = new TcpClient();
        try
        {
            await socket.ConnectAsync(IPAddress.Loopback, port);
            string text = $"POST {path} HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nOrigin: http://127.0.0.1:{port}\r\nContent-Type: application/json\r\nContent-Length: {length}\r\nConnection: close\r\n";
            if (cookie != "") text += "Cookie: " + cookie + "\r\n";
            if (csrf != "") text += "X-CSRF-Token: " + csrf + "\r\n";
            await socket.GetStream().WriteAsync(Encoding.ASCII.GetBytes(text + "\r\n{")); return socket;
        }
        catch { socket.Dispose(); throw; }
    }

    /// <summary>Reads only bounded response headers with a short timeout, without completing the uploaded request.</summary>
    /// <param name="socket">Pending raw request.</param>
    /// <returns>Actual response status and header text.</returns>
    private static async Task<string> HeadersAsync(TcpClient socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        byte[] block = new byte[1024]; var text = new StringBuilder();
        while (text.Length < 8192)
        {
            int read = await socket.GetStream().ReadAsync(block, timeout.Token);
            if (read == 0) break;
            text.Append(Encoding.ASCII.GetString(block, 0, read));
            if (text.ToString().Contains("\r\n\r\n", StringComparison.Ordinal)) return text.ToString();
        }
        throw new InvalidOperationException("No bounded HTTP response received.");
    }

    /// <summary>Posts one real JSON request using the cookie jar and optional CSRF proof.</summary>
    /// <param name="client">Isolated HTTP client.</param>
    /// <param name="path">Relative endpoint.</param>
    /// <param name="body">Bounded payload model.</param>
    /// <param name="csrf">Optional mutation proof.</param>
    /// <returns>Caller-owned actual response.</returns>
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body, string csrf = "")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        { Content = new StringContent(Encoding.UTF8.GetString(LicenseJson.Write(body)), Encoding.UTF8, "application/json") };
        if (csrf != "") request.Headers.Add("X-CSRF-Token", csrf);
        return await client.SendAsync(request);
    }

    /// <summary>Exercises slow-login capacity, reserved reauthentication, early authorization, and revocation while uploading.</summary>
    /// <returns>Completion only after actual response assertions and listener cleanup.</returns>
    private static async Task RunAsync()
    {
        using var f = new Fixture(); int port = Port(); var origin = new Uri($"http://127.0.0.1:{port}");
        var setup = WebAdminAuthentication.Initialize(f.Directory, "owner", f.Password, origin.ToString().TrimEnd('/'), true);
        string root = Path.Combine(f.Directory, "synthetic-web"); Directory.CreateDirectory(Path.Combine(root, "assets"));
        foreach (string name in new[] { "index.html", "assets/app.js", "assets/vendor.js", "assets/app.css" })
            File.WriteAllText(Path.Combine(root, name), "/* SYNTHETIC HTTP ADMISSION TEST, NOT FRONTEND */");
        await using var server = new LicenseWebServer(f.Authority, f.Directory, root, port); server.Start();
        var jar = new CookieContainer(); using var handler = new HttpClientHandler { CookieContainer = jar, UseProxy = false };
        using var client = new HttpClient(handler) { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.Add("Origin", origin.ToString().TrimEnd('/'));
        using var login = await PostAsync(client, "/api/login", new WebLoginRequest("owner", f.Password, setup.RecoveryCodes[0]));
        Check(login.StatusCode == HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        string csrf = document.RootElement.GetProperty("csrfToken").GetString()!;
        string cookie = jar.GetCookieHeader(origin);
        using (var first = await SlowAsync(port, "/api/login"))
        using (var second = await SlowAsync(port, "/api/login"))
        {
            // Confirm admission by observing the third request's actual overload response. No fake verifier or transport bridge is involved.
            await Task.Delay(100);
            using var overloaded = await PostAsync(client, "/api/login", new WebLoginRequest("owner", "wrong", "invalid"));
            Check(overloaded.StatusCode == HttpStatusCode.ServiceUnavailable);
            Check(overloaded.Headers.RetryAfter?.Delta == TimeSpan.FromSeconds(1));
            Check(overloaded.Headers.CacheControl?.NoStore == true);
            using var session = await client.GetAsync("/api/session"); Check(session.StatusCode == HttpStatusCode.OK);
            using var reauth = await PostAsync(client, "/api/reauth", new WebLoginRequest("owner", f.Password, setup.RecoveryCodes[1]), csrf);
            Check(reauth.StatusCode == HttpStatusCode.OK);
        }
        using (var anonymous = await SlowAsync(port, "/api/change"))
            Check((await HeadersAsync(anonymous)).StartsWith("HTTP/1.1 401", StringComparison.Ordinal));
        using (var wrongCsrf = await SlowAsync(port, "/api/change", cookie, "wrong"))
            Check((await HeadersAsync(wrongCsrf)).StartsWith("HTTP/1.1 403", StringComparison.Ordinal));
        using (var uploading = await SlowAsync(port, "/api/licenses/query", cookie, csrf, 2))
        {
            await Task.Delay(100);
            using var logout = await PostAsync(client, "/api/logout", new Dictionary<string, string>(), csrf);
            Check(logout.StatusCode == HttpStatusCode.OK);
            await uploading.GetStream().WriteAsync("}"u8.ToArray());
            Check((await HeadersAsync(uploading)).StartsWith("HTTP/1.1 401", StringComparison.Ordinal));
        }
    }
}

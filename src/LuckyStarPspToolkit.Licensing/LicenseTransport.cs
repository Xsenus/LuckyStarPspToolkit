using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace LuckyStarPspToolkit.Licensing;

/// <summary>HTTPS licensing client with bounded response bodies, no redirects and no certificate-validation bypass.</summary>
public sealed class LicenseTransport : IDisposable
{
    /// <summary>Lifetime HTTP pool; not re-created for each heartbeat.</summary>
    private readonly HttpClient http;
    /// <summary>Immutable customer build trust anchors.</summary>
    private readonly LicenseTrust trust;
    /// <summary>Proof-of-possession identity.</summary>
    private readonly DeviceIdentity identity;

    /// <summary>Creates a transport for a validated trust profile and owned installation identity.</summary>
    /// <param name="trust">Embedded profile; a developer-only profile may use literal loopback HTTP.</param>
    /// <param name="identity">Installation signing key, owned by the caller.</param>
    public LicenseTransport(LicenseTrust trust, DeviceIdentity identity)
    {
        this.trust = trust; this.identity = identity;
        var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None };
        // The normal platform TLS chain/hostname validation is intentionally not overridden.
        http = new HttpClient(handler) { BaseAddress = LicenseCrypto.ValidateTrust(trust), Timeout = TimeSpan.FromSeconds(8) };
    }

    /// <summary>Executes a fresh challenge/proof exchange. Any captured response is useless for a different client nonce.</summary>
    /// <param name="action">activate or check.</param>
    /// <param name="licenseId">Previously activated ID for check, otherwise empty.</param>
    /// <param name="accessKey">Raw activation credential only for activate; never persisted by this class.</param>
    /// <param name="cancellation">Caller cancellation.</param>
    /// <returns>Verified lease plus conservative remaining runtime, excluding the full exchange duration.</returns>
    public async Task<(ExecutionLease Lease, TimeSpan Remaining)> ExchangeAsync(string action, string licenseId,
        string accessKey, CancellationToken cancellation = default)
    {
        long started = Stopwatch.GetTimestamp();
        var challenge = await PostAsync<ChallengeRequest, ChallengeResponse>("v1/challenge",
            new(trust.ProductId, action, identity.PublicKey, identity.HostBinding), cancellation).ConfigureAwait(false);
        var request = new LicenseRequest(trust.ProductId, action, identity.PublicKey, identity.HostBinding,
            challenge.Challenge, LicenseCrypto.Nonce(), licenseId, accessKey, "");
        request = request with { Proof = identity.Prove(request) };
        LeaseResponse response = await PostAsync<LicenseRequest, LeaseResponse>("v1/authorize", request, cancellation).ConfigureAwait(false);
        ExecutionLease lease = LicenseCrypto.VerifyLease(response.Token, trust, request);
        TimeSpan remaining = TimeSpan.FromSeconds(lease.ValidUntil - lease.ServerNow) - Stopwatch.GetElapsedTime(started);
        if (remaining <= TimeSpan.Zero) throw new LicenseException("LEASE_EXPIRED", "The grant expired during network delivery.");
        return (lease, remaining);
    }

    /// <summary>Refreshes an explicitly owner-authorized reserve grant with a fresh installation proof.</summary>
    /// <param name="licenseId">Activated parent license.</param>
    /// <param name="grantId">Owner-issued permission UUID.</param>
    /// <param name="cancellation">Caller cancellation.</param>
    /// <returns>Verified token, its request nonce and full exchange duration for conservative caching.</returns>
    public async Task<(string Token, string Nonce, TimeSpan Elapsed)> RefreshReserveAsync(string licenseId, string grantId,
        CancellationToken cancellation = default)
    {
        long started = Stopwatch.GetTimestamp();
        var challenge = await PostAsync<ChallengeRequest, ChallengeResponse>("v1/challenge",
            new(trust.ProductId, "check", identity.PublicKey, identity.HostBinding), cancellation).ConfigureAwait(false);
        var request = new LicenseRequest(trust.ProductId, "check", identity.PublicKey, identity.HostBinding,
            challenge.Challenge, LicenseCrypto.Nonce(), licenseId, "", "");
        request = request with { Proof = identity.Prove(request) };
        var response = await PostAsync<ReserveRefreshRequest, LeaseResponse>("v1/reserve", new(grantId, request), cancellation).ConfigureAwait(false);
        _ = ReserveCrypto.Verify(response.Token, trust, identity, licenseId, grantId, request.ClientNonce);
        return (response.Token, request.ClientNonce, Stopwatch.GetElapsedTime(started));
    }

    /// <summary>Posts one strictly bounded JSON message and returns only success-schema data or a controlled refusal.</summary>
    /// <typeparam name="TRequest">Request schema.</typeparam>
    /// <typeparam name="TResponse">Success schema.</typeparam>
    /// <param name="path">Fixed relative endpoint.</param>
    /// <param name="request">Request, potentially containing the activation key; never logged.</param>
    /// <param name="cancellation">Operation cancellation.</param>
    /// <returns>Parsed response.</returns>
    private async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest request, CancellationToken cancellation)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(LicenseJson.Write(request)) };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            using HttpResponseMessage response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            // A TLS proxy or the bounded listener may return an empty/HTML overload response.
            // Classify it before JSON parsing. This never grants access or extends the old deadline.
            if ((int)response.StatusCode is 408 or 429 or 500 or 502 or 503 or 504)
                throw new LicenseException(response.StatusCode == HttpStatusCode.TooManyRequests ? "RATE_LIMIT" : "SERVER_BUSY",
                    "The licensing service is temporarily unavailable. No new execution permission was granted.");
            if (response.Content.Headers.ContentLength > 32768) throw new LicenseException("SERVER_RESPONSE", "License response exceeds its size limit.");
            if (response.Content.Headers.ContentType?.MediaType != "application/json")
                throw new LicenseException("SERVER_RESPONSE", "The licensing service did not return JSON.");
            await using Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            byte[] block = new byte[4096];
            int read;
            while ((read = await stream.ReadAsync(block, timeout.Token).ConfigureAwait(false)) != 0)
            {
                if (buffer.Length + read > 32768) throw new LicenseException("SERVER_RESPONSE", "License response exceeds its size limit.");
                buffer.Write(block, 0, read);
            }
            byte[] data = buffer.ToArray();
            if (!response.IsSuccessStatusCode)
            {
                LicenseError error = LicenseJson.Read<LicenseError>(data);
                // A failed response is never converted into offline permission.
                throw new LicenseException(error.Code, error.Message);
            }
            return LicenseJson.Read<TResponse>(data);
        }
        catch (HttpRequestException) { throw new LicenseException("LICENSE_NETWORK", "The licensing service is unavailable or its TLS certificate is invalid."); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new LicenseException("LICENSE_NETWORK", "The licensing request timed out."); }
    }

    /// <summary>Closes pooled connections. The installation identity is owned and disposed by the caller.</summary>
    public void Dispose() => http.Dispose();
}

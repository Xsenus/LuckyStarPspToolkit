using System.Diagnostics;

namespace LuckyStarPspToolkit.Licensing;

/// <summary>Monotonic short-lease watchdog. It never persists execution permission across launches or grants offline grace.</summary>
public sealed class LicenseSession : IDisposable
{
    /// <summary>Underlying authenticated transport.</summary>
    private readonly LicenseTransport transport;
    /// <summary>Activated license ID.</summary>
    private readonly string licenseId;
    /// <summary>Termination callback. The production CLI exits itself, never touches customer files or other processes.</summary>
    private readonly Action<string> denied;
    /// <summary>Cancellation for both monitor tasks.</summary>
    private readonly CancellationTokenSource stopping = new();
    /// <summary>Protects deadline replacement.</summary>
    private readonly object sync = new();
    /// <summary>Monotonic lease start; unrelated to the client wall clock.</summary>
    private long grantedAt;
    /// <summary>Conservative permitted interval from the last verified exchange.</summary>
    private TimeSpan remaining;
    /// <summary>One-way transition to denied.</summary>
    private int invalidated;
    /// <summary>Background expiration monitor.</summary>
    private readonly Task watchdog;
    /// <summary>Background online renewal monitor.</summary>
    private readonly Task renewer;

    /// <summary>Begins monitoring an already authenticated grant.</summary>
    /// <param name="transport">Reusable online verifier.</param>
    /// <param name="licenseId">Activated entitlement.</param>
    /// <param name="initial">Initial verified interval.</param>
    /// <param name="denied">Callback invoked once if the license expires, is revoked or cannot be renewed.</param>
    public LicenseSession(LicenseTransport transport, string licenseId, TimeSpan initial, Action<string> denied)
    {
        if (initial <= TimeSpan.Zero || initial > TimeSpan.FromSeconds(LicenseCrypto.MaximumLeaseSeconds))
            throw new LicenseException("LEASE_TIME", "Invalid initial runtime interval.");
        this.transport = transport; this.licenseId = licenseId; this.denied = denied;
        grantedAt = Stopwatch.GetTimestamp(); remaining = initial;
        watchdog = Task.Run(WatchAsync); renewer = Task.Run(RenewAsync);
    }

    /// <summary>Checks access synchronously at command boundaries, independently of heartbeat scheduling.</summary>
    public void RequireValid()
    {
        if (Volatile.Read(ref invalidated) != 0 || Remaining() <= TimeSpan.Zero)
            throw new LicenseException("LICENSE_ACCESS_DENIED", "The execution lease is no longer valid.");
    }

    /// <summary>Calculates remaining time without DateTime.Now, timezone or editable local timestamps.</summary>
    /// <returns>Remaining monotonic interval, possibly negative.</returns>
    private TimeSpan Remaining() { lock (sync) return remaining - Stopwatch.GetElapsedTime(grantedAt); }

    /// <summary>Checks expiration separately from network renewal, so an in-flight HTTP request cannot extend access.</summary>
    /// <returns>Completes after cancellation or denial.</returns>
    private async Task WatchAsync()
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                if (Remaining() <= TimeSpan.Zero) { Deny("LICENSE_LEASE_EXPIRED"); return; }
                await Task.Delay(200, stopping.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
    }

    /// <summary>Renews at most every thirty seconds, with bounded retries while the previous grant remains valid.</summary>
    /// <returns>Completes after cancellation or irreversible denial.</returns>
    private async Task RenewAsync()
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                double seconds = Math.Clamp(Remaining().TotalSeconds / 3, 1, 30);
                await Task.Delay(TimeSpan.FromSeconds(seconds), stopping.Token).ConfigureAwait(false);
                try
                {
                    var grant = await transport.ExchangeAsync("check", licenseId, "", stopping.Token).ConfigureAwait(false);
                    lock (sync)
                    {
                        if (Volatile.Read(ref invalidated) != 0 || remaining - Stopwatch.GetElapsedTime(grantedAt) <= TimeSpan.Zero)
                        { Deny("LICENSE_LEASE_EXPIRED"); return; }
                        grantedAt = Stopwatch.GetTimestamp(); remaining = grant.Remaining;
                    }
                }
                catch (LicenseException ex)
                {
                    // Temporary network/overload errors never extend the previous grant. All explicit refusals terminate immediately.
                    if (ex.Code is not ("LICENSE_NETWORK" or "RATE_LIMIT" or "CHALLENGE_BUSY" or "SERVER_BUSY"))
                    { Deny(ex.Code); return; }
                }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch { Deny("LICENSE_MONITOR_FAILED"); }
    }

    /// <summary>Irreversibly invalidates this process session and calls the configured shutdown once.</summary>
    /// <param name="code">Safe refusal reason.</param>
    private void Deny(string code)
    {
        if (Interlocked.Exchange(ref invalidated, 1) == 0)
        {
            stopping.Cancel();
            denied(code);
        }
    }

    /// <summary>Stops monitoring and waits for outstanding network work before the caller disposes the transport/key.</summary>
    public void Dispose()
    {
        stopping.Cancel();
        try { Task.WhenAll(watchdog, renewer).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        stopping.Dispose();
    }
}

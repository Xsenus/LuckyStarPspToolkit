using System.Diagnostics;

namespace LuckyStarPspToolkit.Licensing;

/// <summary>Monotonic runtime watchdog. Ordinary leases remain online-only; an explicit signed reserve may bridge bounded availability failures.</summary>
public sealed class LicenseSession : IDisposable
{
    /// <summary>Underlying authenticated transport.</summary>
    private readonly LicenseTransport transport;
    /// <summary>Activated license ID.</summary>
    private readonly string licenseId;
    /// <summary>Termination callback. The production CLI exits itself, never touches customer files or other processes.</summary>
    private readonly Action<string> denied;
    /// <summary>Optional explicitly configured offline permission; ordinary licenses retain strict online behavior.</summary>
    private readonly ReserveCache? reserve;
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
    /// <param name="reserve">Explicit reserve cache, or null to retain short online-only leases.</param>
    public LicenseSession(LicenseTransport transport, string licenseId, TimeSpan initial, Action<string> denied, ReserveCache? reserve = null)
    {
        if (initial <= TimeSpan.Zero || initial > TimeSpan.FromSeconds(reserve is null ? LicenseCrypto.MaximumLeaseSeconds : ReserveCrypto.MaximumSeconds))
            throw new LicenseException("LEASE_TIME", "Invalid initial runtime interval.");
        this.transport = transport; this.licenseId = licenseId; this.denied = denied; this.reserve = reserve;
        grantedAt = Stopwatch.GetTimestamp(); remaining = initial;
        watchdog = Task.Run(WatchAsync); renewer = Task.Run(RenewAsync);
    }

    /// <summary>Checks access synchronously at command boundaries, independently of heartbeat scheduling.</summary>
    public void RequireValid()
    {
        if (Volatile.Read(ref invalidated) != 0 || (Remaining() <= TimeSpan.Zero && !TryReserve()))
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
                if (Remaining() <= TimeSpan.Zero && !TryReserve()) { Deny("LICENSE_LEASE_EXPIRED"); return; }
                await Task.Delay(200, stopping.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch { Deny("LICENSE_MONITOR_FAILED"); }
    }

    /// <summary>Uses only an existing signed reserve when a short online lease expires during an in-flight network request.</summary>
    /// <returns>True when a nonexpired reserve was checkpointed and installed; false never grants additional time.</returns>
    private bool TryReserve()
    {
        if (Volatile.Read(ref invalidated) != 0 || reserve is not { Enabled: true }) return false;
        try
        {
            TimeSpan fallback = reserve.Remaining();
            lock (sync)
            {
                if (Volatile.Read(ref invalidated) != 0) return false;
                grantedAt = Stopwatch.GetTimestamp(); remaining = fallback; return true;
            }
        }
        catch (LicenseException) { return false; }
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
                    long renewedAt = Stopwatch.GetTimestamp();
                    if (reserve is not null) await reserve.RenewAsync(transport, stopping.Token).ConfigureAwait(false);
                    TimeSpan onlineRemaining = grant.Remaining - Stopwatch.GetElapsedTime(renewedAt);
                    if (onlineRemaining <= TimeSpan.Zero) { Deny("LICENSE_LEASE_EXPIRED"); return; }
                    lock (sync)
                    {
                        if (Volatile.Read(ref invalidated) != 0 || remaining - Stopwatch.GetElapsedTime(grantedAt) <= TimeSpan.Zero)
                        { Deny("LICENSE_LEASE_EXPIRED"); return; }
                        grantedAt = Stopwatch.GetTimestamp(); remaining = onlineRemaining;
                    }
                }
                catch (LicenseException ex)
                {
                    // Availability errors may use a previously signed reserve but cannot extend its deadline. Explicit refusals terminate immediately.
                    if (!ReserveCache.IsTransient(ex.Code))
                    { reserve?.Block(); Deny(ex.Code); return; }
                    if (reserve is { Enabled: true })
                    {
                        try
                        {
                            TimeSpan fallback = reserve.Remaining();
                            lock (sync)
                            {
                                if (Volatile.Read(ref invalidated) != 0) return;
                                grantedAt = Stopwatch.GetTimestamp(); remaining = fallback;
                            }
                        }
                        catch (LicenseException failure) { Deny(failure.Code); return; }
                    }
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

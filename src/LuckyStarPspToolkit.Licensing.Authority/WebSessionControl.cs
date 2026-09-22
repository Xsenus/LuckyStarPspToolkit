namespace LuckyStarPspToolkit.Licensing.Authority;

/// <summary>Browser-safe metadata for an active owner session; it contains neither cookie nor CSRF credentials.</summary>
/// <param name="Id">Independent management UUID, unusable for authentication.</param>
/// <param name="Current">Whether this is the caller's current session.</param>
/// <param name="AgeSeconds">Monotonic time since login; no private IP address or user agent is disclosed.</param>
/// <param name="IdleSeconds">Monotonic time since the last authenticated request.</param>
/// <param name="ReauthInSeconds">Seconds until sensitive operations require password and a fresh second factor.</param>
public sealed record WebSessionInfo(string Id, bool Current, long AgeSeconds, long IdleSeconds, long ReauthInSeconds);

/// <summary>Revoke either one independent session handle or all other sessions; current-session logout has its own endpoint.</summary>
/// <param name="SessionId">Management UUID, empty when Others is true.</param>
/// <param name="Others">Whether to keep only the caller's current session.</param>
public sealed record WebSessionRevokeRequest(string SessionId = "", bool Others = false);

/// <summary>Session visibility and revocation share the existing authentication lock and monotonic expiration policy.</summary>
public sealed partial class WebAdminAuthentication
{
    /// <summary>Returns detached bounded metadata and prunes expired sessions without exposing bearer credentials.</summary>
    /// <param name="id">Authenticated current cookie, provided by the server only.</param>
    /// <returns>Current session first, followed by other live sessions in creation order.</returns>
    public WebSessionInfo[] ListSessions(string id)
    {
        lock (sync)
        {
            _ = Require(id);
            return sessions.Values.OrderByDescending(x => x.View.SessionId == id).ThenBy(x => x.Created)
                .ThenBy(x => x.ManagementId, StringComparer.Ordinal)
                .Select(x => new WebSessionInfo(x.ManagementId, x.View.SessionId == id,
                    (long)time.GetElapsedTime(x.Created).TotalSeconds, (long)time.GetElapsedTime(x.Seen).TotalSeconds,
                    Math.Max(0, (long)(FreshLimit - time.GetElapsedTime(x.Authenticated)).TotalSeconds))).ToArray();
        }
    }

    /// <summary>Removes other sessions atomically after CSRF and recent MFA checks; an absent target is an idempotent no-op.</summary>
    /// <param name="id">Current server-selected authentication cookie.</param>
    /// <param name="csrf">Browser anti-CSRF header.</param>
    /// <param name="request">One management UUID or the explicit all-others option.</param>
    /// <returns>Number of removed sessions, never their cookies; already admitted requests are not rolled back.</returns>
    public int RevokeSessions(string id, string csrf, WebSessionRevokeRequest request)
    {
        lock (sync)
        {
            _ = Require(id, csrf, true);
            if ((request.Others && request.SessionId != "") || (!request.Others && !Guid.TryParseExact(request.SessionId, "D", out _)))
                throw new LicenseException("WEB_SESSION_REQUEST", "Select one session UUID or all other sessions.");
            if (!request.Others && sessions[id].ManagementId == request.SessionId)
                throw new LicenseException("WEB_SESSION_CURRENT", "Use logout to close your current session.");
            string[] remove = sessions.Where(x => x.Key != id && (request.Others || x.Value.ManagementId == request.SessionId))
                .Select(x => x.Key).ToArray();
            foreach (string key in remove) sessions.Remove(key);
            return remove.Length;
        }
    }
}

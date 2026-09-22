using System.Security.Cryptography;

namespace LuckyStarPspToolkit.Licensing.Authority;

/// <summary>Bounded owner-only query. A continuation is bound to every filter and page size, not an authorization credential.</summary>
/// <param name="Limit">Number of rows from 1 to 100.</param>
/// <param name="Search">Literal case-insensitive label/ID substring, at most 120 characters; no regular expressions.</param>
/// <param name="Status">Exact effective status or empty for all statuses.</param>
/// <param name="Cursor">Opaque continuation returned by the server; empty begins a new snapshot.</param>
/// <param name="ActivatedOnly">License chooser filter requiring at least one active installation; invalid for reserve queries.</param>
public sealed record OwnerPageRequest(int Limit = 25, string Search = "", string Status = "", string Cursor = "", bool ActivatedOnly = false);

/// <summary>Exact license lookup without putting owner identifiers in query strings or fetching every installation.</summary>
/// <param name="Id">License UUID, not an access key.</param>
public sealed record OwnerLicenseRequest(string Id);

/// <summary>Small list row; public keys, host-binding digests and device arrays are deliberately omitted.</summary>
/// <param name="Id">License UUID.</param>
/// <param name="Label">Non-secret owner label.</param>
/// <param name="Status">Effective state at the page's AsOfUtc.</param>
/// <param name="CreatedAt">Issue timestamp.</param>
/// <param name="ExpiresAt">Exclusive deadline or null.</param>
/// <param name="Permanent">Whether the license has no deadline.</param>
/// <param name="MaxDevices">Installation capacity.</param>
/// <param name="DeviceCount">Number of activated installations.</param>
public sealed record OwnerLicenseRow(string Id, string Label, string Status, long CreatedAt, long? ExpiresAt,
    bool Permanent, int MaxDevices, int DeviceCount);

/// <summary>Whole-database totals computed in the same read/time snapshot as a license page, independent of filters.</summary>
/// <param name="Total">All licenses.</param>
/// <param name="Active">Currently usable licenses.</param>
/// <param name="Pending">Waiting for first activation.</param>
/// <param name="Expired">Past the exclusive deadline.</param>
/// <param name="Suspended">Temporarily suspended.</param>
/// <param name="Revoked">Irreversibly revoked.</param>
public sealed record OwnerLicenseCounts(int Total, int Active, int Pending, int Expired, int Suspended, int Revoked);

/// <summary>One bounded page. Rows and counts use a fixed time; a database change invalidates its continuation.</summary>
/// <param name="Items">At most the requested limit, newest issuance first and ordinal ID for ties.</param>
/// <param name="Total">All rows matching the filters, including previously returned pages.</param>
/// <param name="NextCursor">Empty at the end, otherwise a signed two-minute continuation.</param>
/// <param name="Revision">Committed database revision.</param>
/// <param name="AsOfUtc">Server UTC used for all effective states in this traversal.</param>
/// <param name="Counts">Global counters, not just the displayed page.</param>
public sealed record OwnerLicensePage(OwnerLicenseRow[] Items, int Total, string NextCursor, long Revision,
    long AsOfUtc, OwnerLicenseCounts Counts);

/// <summary>Reserve policy without materializing its historical allow-list.</summary>
/// <param name="Enabled">Whether grants can currently be issued or renewed.</param>
/// <param name="Epoch">Current policy generation.</param>
/// <param name="MaximumHours">Hard offline ceiling.</param>
public sealed record OwnerReservePolicy(bool Enabled, long Epoch, int MaximumHours);

/// <summary>Owner reserve row omitting internal request fingerprints.</summary>
/// <param name="Id">Permission UUID.</param>
/// <param name="LicenseId">Parent license UUID.</param>
/// <param name="DeviceId">Installation digest needed for administration, not a credential.</param>
/// <param name="Epoch">Generation at issuance.</param>
/// <param name="WindowSeconds">Maximum offline interval, not an observation that a client is currently offline.</param>
/// <param name="CreatedAt">Issuance timestamp.</param>
/// <param name="RevokedAt">Retirement timestamp, or null.</param>
/// <param name="Reason">Non-secret owner rationale.</param>
/// <param name="Status">available, revoked, retired, inactive-license or missing-device; these describe renewal eligibility.</param>
public sealed record OwnerReserveRow(string Id, string LicenseId, string DeviceId, long Epoch, int WindowSeconds,
    long CreatedAt, long? RevokedAt, string Reason, string Status);

/// <summary>Bounded reserve history and its policy from one authenticated read snapshot.</summary>
/// <param name="Items">At most the requested number of grants.</param>
/// <param name="Total">All grants matching the query.</param>
/// <param name="NextCursor">Continuation or empty.</param>
/// <param name="Revision">Database revision.</param>
/// <param name="AsOfUtc">Fixed status-evaluation timestamp.</param>
/// <param name="Policy">Current global switch and epoch.</param>
public sealed record OwnerReservePage(OwnerReserveRow[] Items, int Total, string NextCursor, long Revision,
    long AsOfUtc, OwnerReservePolicy Policy);

/// <summary>Server-side keyset pagination with bounded top-k memory; never clones the database or every device array.</summary>
public sealed partial class LicenseAuthority
{
    /// <summary>Random process-local cursor authentication key; restart intentionally requires the owner to refresh the first page.</summary>
    private readonly byte[] ownerCursorKey = RandomNumberGenerator.GetBytes(32);
    /// <summary>Two-minute snapshot lifetime limits stale time-based statuses without retaining server-side snapshots.</summary>
    private const int OwnerCursorSeconds = 120;

    /// <summary>Queries license summaries with O(N log P) selection and O(P) temporary storage, where P is at most 101.</summary>
    /// <param name="request">Bounded literal filters and optional authenticated continuation.</param>
    /// <returns>Detached rows and coherent global counts without installation public keys.</returns>
    /// <exception cref="LicenseException">Query is invalid, the cursor was modified, or its snapshot is stale.</exception>
    public OwnerLicensePage QueryLicenses(OwnerPageRequest request)
    {
        ValidateOwnerQuery(request, false);
        lock (sync)
        {
            long now = Now();
            return store.ReadCommitted(db =>
            {
                var cursor = OpenOwnerCursor(request, "licenses", db.Revision, now);
                long at = cursor?.AsOfUtc ?? now;
                var selected = new OwnerPageSelection<LicenseRecord>(request.Limit, cursor);
                int total = 0, active = 0, pending = 0, expired = 0, suspended = 0, revoked = 0;
                foreach (var record in db.Licenses.Values)
                {
                    string status = EffectiveStatus(record, at);
                    switch (status) { case "active": active++; break; case "pending": pending++; break;
                        case "expired": expired++; break; case "suspended": suspended++; break; case "revoked": revoked++; break; }
                    if ((request.Status != "" && request.Status != status) || (request.ActivatedOnly && record.Devices.Count == 0) ||
                        (request.Search != "" && !record.Label.Contains(request.Search, StringComparison.OrdinalIgnoreCase) &&
                         !record.Id.Contains(request.Search, StringComparison.OrdinalIgnoreCase))) continue;
                    total++; selected.Consider(record, record.CreatedAt, record.Id);
                }
                var page = selected.Finish();
                var rows = page.Items.Select(x => new OwnerLicenseRow(x.Id, x.Label, EffectiveStatus(x, at), x.CreatedAt,
                    x.ExpiresAt, x.Unit == "permanent", x.MaxDevices, x.Devices.Count)).ToArray();
                string next = page.More ? SealOwnerCursor(request, "licenses", db.Revision, at, rows[^1].CreatedAt, rows[^1].Id) : "";
                return new OwnerLicensePage(rows, total, next, db.Revision, at,
                    new(db.Licenses.Count, active, pending, expired, suspended, revoked));
            });
        }
    }

    /// <summary>Returns only a small reserve page, including explicit renewal-ineligibility reasons rather than a misleading active label.</summary>
    /// <param name="request">Search grant/license/device IDs or rationale, with an optional exact renewal status.</param>
    /// <returns>Detached bounded history and current policy.</returns>
    /// <exception cref="LicenseException">Query/cursor validation or snapshot freshness failed.</exception>
    public OwnerReservePage QueryReserves(OwnerPageRequest request)
    {
        ValidateOwnerQuery(request, true);
        lock (sync)
        {
            long now = Now();
            return store.ReadCommitted(db =>
            {
                var cursor = OpenOwnerCursor(request, "reserves", db.Revision, now);
                long at = cursor?.AsOfUtc ?? now;
                var selected = new OwnerPageSelection<ReserveGrant>(request.Limit, cursor);
                int total = 0;
                foreach (var grant in db.ReserveGrants.Values)
                {
                    if ((request.Status != "" && request.Status != ReserveEligibility(grant, db, at)) ||
                        (request.Search != "" && !grant.Id.Contains(request.Search, StringComparison.OrdinalIgnoreCase) &&
                         !grant.LicenseId.Contains(request.Search, StringComparison.OrdinalIgnoreCase) &&
                         !grant.DeviceId.Contains(request.Search, StringComparison.OrdinalIgnoreCase) &&
                         !grant.Reason.Contains(request.Search, StringComparison.OrdinalIgnoreCase))) continue;
                    total++; selected.Consider(grant, grant.CreatedAt, grant.Id);
                }
                var page = selected.Finish();
                var rows = page.Items.Select(x => new OwnerReserveRow(x.Id, x.LicenseId, x.DeviceId, x.Epoch, x.WindowSeconds,
                    x.CreatedAt, x.RevokedAt, x.Reason, ReserveEligibility(x, db, at))).ToArray();
                string next = page.More ? SealOwnerCursor(request, "reserves", db.Revision, at, rows[^1].CreatedAt, rows[^1].Id) : "";
                return new OwnerReservePage(rows, total, next, db.Revision, at, new(db.ReserveEnabled, db.ReserveEpoch, 168));
            });
        }
    }

    /// <summary>Gets bounded per-license detail separately from list rows; does not authorize a mutation.</summary>
    /// <param name="request">Exact UUID.</param>
    /// <returns>Existing detached overview with at most the configured 100 device slots.</returns>
    public LicenseOverview QueryLicense(OwnerLicenseRequest request) { ValidateUuid(request.Id); return Get(request.Id); }

    /// <summary>Computes one effective license status without allocating its device array.</summary>
    /// <param name="record">Committed license.</param>
    /// <param name="now">Snapshot server second.</param>
    /// <returns>Lifecycle state at that time; mutations still evaluate the current authoritative time.</returns>
    private static string EffectiveStatus(LicenseRecord record, long now) => record.Status != "active" ? record.Status
        : record.ExpiresAt.HasValue && now >= record.ExpiresAt.Value ? "expired"
        : record.Starts == "activation" && record.ActivatedAt is null ? "pending" : "active";

    /// <summary>Computes permission renewal eligibility, never a claim about a disconnected client's locally held token.</summary>
    /// <param name="grant">Registered permission.</param>
    /// <param name="db">Committed database.</param>
    /// <param name="now">Snapshot second.</param>
    /// <returns>Highest-priority retirement/ineligibility reason, otherwise available.</returns>
    private static string ReserveEligibility(ReserveGrant grant, LicenseDatabase db, long now)
    {
        if (grant.RevokedAt.HasValue) return "revoked";
        if (!db.ReserveEnabled || grant.Epoch != db.ReserveEpoch) return "retired";
        if (!db.Licenses.TryGetValue(grant.LicenseId, out var license) || EffectiveStatus(license, now) != "active") return "inactive-license";
        return license.Devices.ContainsKey(grant.DeviceId) ? "available" : "missing-device";
    }

    /// <summary>Rejects oversized/ambiguous queries before reading the database or allocating the selection heap.</summary>
    /// <param name="request">Untrusted query object.</param>
    /// <param name="reserves">Selects the supported status vocabulary.</param>
    private static void ValidateOwnerQuery(OwnerPageRequest request, bool reserves)
    {
        if (request is null || request.Search is null || request.Status is null || request.Cursor is null || request.Limit is < 1 or > 100 || request.Search.Length > 120 || request.Search.Any(char.IsControl) || request.Cursor.Length > 2048 ||
            (reserves ? request.Status is not ("" or "available" or "revoked" or "retired" or "inactive-license" or "missing-device") || request.ActivatedOnly
                      : request.Status is not ("" or "active" or "pending" or "expired" or "suspended" or "revoked")))
            throw new LicenseException("OWNER_PAGE_QUERY", "Use 1..100 rows, a literal search up to 120 characters and a supported status.");
    }

    /// <summary>Authenticated continuation carries no data rows, access keys, device keys or private state.</summary>
    /// <param name="Kind">Endpoint domain.</param>
    /// <param name="QueryHash">Hash of normalized query fields including the page limit.</param>
    /// <param name="Revision">Database write revision.</param>
    /// <param name="AsOfUtc">Fixed traversal time.</param>
    /// <param name="AfterAt">Issue timestamp of the last returned row.</param>
    /// <param name="AfterId">Ordinal UUID tie-breaker of that row.</param>
    private sealed record OwnerCursor(string Kind, string QueryHash, long Revision, long AsOfUtc, long AfterAt, string AfterId);

    /// <summary>Hashes all filters without the token so changing page size or query cannot reuse an existing continuation.</summary>
    /// <param name="request">Validated query.</param>
    /// <returns>Non-secret SHA-256 fingerprint.</returns>
    private static string OwnerQueryHash(OwnerPageRequest request) => LicenseCrypto.Digest(LicenseJson.Write(request with { Cursor = "" }));

    /// <summary>Checks MAC before deserialization, binds endpoint/filter/revision and enforces exclusive two-minute expiry.</summary>
    /// <param name="request">Validated filters and token.</param>
    /// <param name="kind">Expected endpoint domain.</param>
    /// <param name="revision">Current database revision.</param>
    /// <param name="now">Durably observed server time.</param>
    /// <returns>Validated continuation or null for the first page.</returns>
    private OwnerCursor? OpenOwnerCursor(OwnerPageRequest request, string kind, long revision, long now)
    {
        if (request.Cursor == "") return null;
        OwnerCursor cursor;
        try
        {
            string[] parts = request.Cursor.Split('.');
            if (parts.Length != 2) throw new LicenseException("OWNER_PAGE_CURSOR", "Invalid continuation.");
            byte[] bytes = LicenseCrypto.Decode(parts[0], 1024);
            byte[] tag = LicenseCrypto.Decode(parts[1], 32, 32);
            if (!CryptographicOperations.FixedTimeEquals(tag, HMACSHA256.HashData(ownerCursorKey, bytes)))
                throw new LicenseException("OWNER_PAGE_CURSOR", "Invalid continuation.");
            cursor = LicenseJson.Read<OwnerCursor>(bytes, 1024);
            if (cursor.Kind != kind || cursor.QueryHash != OwnerQueryHash(request) ||
                cursor.AsOfUtc < 0 || cursor.AfterAt < 0 || !Guid.TryParseExact(cursor.AfterId, "D", out _))
                throw new LicenseException("OWNER_PAGE_CURSOR", "Continuation does not match this query.");
        }
        catch (LicenseException) { throw new LicenseException("OWNER_PAGE_CURSOR", "Invalid continuation; refresh the list."); }
        if (cursor.Revision != revision || now < cursor.AsOfUtc || now - cursor.AsOfUtc >= OwnerCursorSeconds)
            throw new LicenseException("OWNER_PAGE_STALE", "The list changed or expired; refresh from the first page.");
        return cursor;
    }

    /// <summary>Seals a new continuation using the original traversal time; moving to another page never extends its lifetime.</summary>
    /// <param name="request">Validated filters.</param>
    /// <param name="kind">Endpoint domain.</param>
    /// <param name="revision">Current database revision.</param>
    /// <param name="at">Original traversal timestamp.</param>
    /// <param name="afterAt">Last returned row's issue timestamp.</param>
    /// <param name="afterId">Last returned row's UUID.</param>
    /// <returns>MAC-authenticated bounded cursor.</returns>
    private string SealOwnerCursor(OwnerPageRequest request, string kind, long revision, long at, long afterAt, string afterId)
    {
        byte[] bytes = LicenseJson.Write(new OwnerCursor(kind, OwnerQueryHash(request), revision, at, afterAt, afterId));
        return LicenseCrypto.Encode(bytes) + "." + LicenseCrypto.Encode(HMACSHA256.HashData(ownerCursorKey, bytes));
    }

    /// <summary>Immutable newest-first key; UUID order breaks same-second issuance ties deterministically.</summary>
    /// <param name="At">Issuance timestamp.</param>
    /// <param name="Id">UUID tie-breaker.</param>
    private readonly record struct OwnerOrder(long At, string Id);

    /// <summary>Bounded top-k selection retains only one page plus a look-ahead row, never the entire result set.</summary>
    /// <typeparam name="T">Immutable committed record used only within the authority read lock.</typeparam>
    private sealed class OwnerPageSelection<T>
    {
        /// <summary>Requested page size; the heap also retains one look-ahead row.</summary>
        private readonly int limit;
        /// <summary>Exclusive keyset boundary, or null on the first page.</summary>
        private readonly OwnerCursor? cursor;
        /// <summary>Newest-first ordering without subtraction overflow.</summary>
        private static readonly IComparer<OwnerOrder> Forward = Comparer<OwnerOrder>.Create(Compare);
        /// <summary>Inverse ordering makes the worst retained candidate the minimum heap priority.</summary>
        private readonly PriorityQueue<T, OwnerOrder> heap;
        /// <summary>Creates a heap with bounded capacity and no retained database references outside the current query.</summary>
        /// <param name="limit">Validated row limit.</param>
        /// <param name="cursor">Validated continuation.</param>
        public OwnerPageSelection(int limit, OwnerCursor? cursor)
        { this.limit = limit; this.cursor = cursor; heap = new(limit + 1, Comparer<OwnerOrder>.Create((a, b) => Compare(b, a))); }
        /// <summary>Compares keys by descending time then ascending ordinal UUID.</summary>
        /// <param name="a">First key.</param>
        /// <param name="b">Second key.</param>
        /// <returns>Negative when the first key should be displayed earlier.</returns>
        private static int Compare(OwnerOrder a, OwnerOrder b) { int c = b.At.CompareTo(a.At); return c != 0 ? c : StringComparer.Ordinal.Compare(a.Id, b.Id); }
        /// <summary>Considers an eligible record; candidates preceding the cursor or worse than the bounded heap are skipped.</summary>
        /// <param name="item">Immutable record.</param>
        /// <param name="at">Issue timestamp.</param>
        /// <param name="id">Stable UUID.</param>
        public void Consider(T item, long at, string id)
        {
            var order = new OwnerOrder(at, id);
            if (cursor is not null && Compare(order, new(cursor.AfterAt, cursor.AfterId)) <= 0) return;
            if (heap.Count < limit + 1) heap.Enqueue(item, order);
            else if (heap.TryPeek(out _, out var worst) && Compare(order, worst) < 0)
            { heap.Dequeue(); heap.Enqueue(item, order); }
        }
        /// <summary>Sorts only retained candidates and returns a detached page without the look-ahead item.</summary>
        /// <returns>At most Limit records and whether a continuation exists.</returns>
        public (T[] Items, bool More) Finish() => (heap.UnorderedItems.OrderBy(x => x.Priority, Forward).Take(limit).Select(x => x.Element).ToArray(), heap.Count > limit);
    }
}

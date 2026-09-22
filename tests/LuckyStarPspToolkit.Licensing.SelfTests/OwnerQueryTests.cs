using System.Text;
using LuckyStarPspToolkit.Licensing;
using LuckyStarPspToolkit.Licensing.Authority;

/// <summary>Executable owner-query invariants: stable keysets, bounded rows, cursor authenticity and time/revision isolation.</summary>
internal static class OwnerQueryTests
{
    /// <summary>Throws on a failed invariant rather than reporting a synthetic pass.</summary>
    /// <param name="ok">Required condition.</param>
    private static void Check(bool ok) { if (!ok) throw new InvalidOperationException("Owner query invariant failed."); }
    /// <summary>Requires a particular controlled failure and lets unexpected exceptions fail the suite.</summary>
    /// <param name="code">Expected diagnostic.</param>
    /// <param name="operation">Forbidden operation.</param>
    private static void Reject(string code, Action operation)
    { try { operation(); } catch (LicenseException ex) when (ex.Code == code) { return; } throw new InvalidOperationException("Missing denial: " + code); }
    /// <summary>Creates mixed timestamp ties and deterministic labels; credentials remain private temporary data.</summary>
    /// <param name="f">Isolated issuer.</param>
    /// <param name="count">Small number of records.</param>
    /// <returns>Committed issuance requests for later mutation tests.</returns>
    private static IssueLicenseRequest[] Seed(Fixture f, int count = 63)
    {
        var list = new List<IssueLicenseRequest>();
        for (int i = 0; i < count; i++)
        {
            if (i % 7 == 0) f.Time.Advance(TimeSpan.FromSeconds(1));
            var request = f.NewIssue("permanent", 0, "issue") with { Label = i % 2 == 0 ? "Клиент Альфа " + i : "Client Beta " + i };
            f.Authority.Issue(request); list.Add(request);
        }
        return list.ToArray();
    }
    /// <summary>Traversal with timestamp ties equals an independently sorted full list and returns each UUID once.</summary>
    public static void CompleteTraversal()
    {
        using var f = new Fixture(); Seed(f);
        string[] expected = f.Authority.List().OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id, StringComparer.Ordinal).Select(x => x.Id).ToArray();
        foreach (int size in new[] { 1, 7, 25, 100 })
        {
            var ids = new List<string>(); string cursor = ""; long revision = -1, at = -1;
            do
            {
                var page = f.Authority.QueryLicenses(new(size, Cursor: cursor));
                Check(page.Items.Length <= size && page.Total == expected.Length && page.Counts.Active == expected.Length);
                if (revision >= 0) Check(page.Revision == revision && page.AsOfUtc == at);
                revision = page.Revision; at = page.AsOfUtc;
                ids.AddRange(page.Items.Select(x => x.Id)); cursor = page.NextCursor;
            } while (cursor != "");
            Check(ids.SequenceEqual(expected) && ids.Distinct().Count() == expected.Length);
        }
    }
    /// <summary>Literal case-insensitive search and lifecycle filters do not change whole-database statistics.</summary>
    public static void FiltersAndCounts()
    {
        using var f = new Fixture(); var list = Seed(f, 8);
        f.Change(list[0], "suspend"); f.Change(list[1], "revoke");
        var pending = f.Issue(); var expired = f.Issue("hours", 1, "issue"); f.Time.Advance(TimeSpan.FromHours(1));
        var all = f.Authority.QueryLicenses(new());
        Check(all.Counts == new OwnerLicenseCounts(10, 6, 1, 1, 1, 1));
        var filtered = f.Authority.QueryLicenses(new(Search: "клиент альфа", Status: "active"));
        Check(filtered.Total == 3 && filtered.Items.All(x => x.Status == "active") && filtered.Counts == all.Counts);
        Check(f.Authority.QueryLicenses(new(Search: "[.*]")).Total == 0);
        Check(f.Authority.QueryLicenses(new(Search: list[2].Id.ToUpperInvariant())).Items.Single().Id == list[2].Id);
        Check(f.Authority.QueryLicenses(new(Status: "pending")).Items.Single().Id == pending.Id);
        Check(f.Authority.QueryLicenses(new(Status: "expired")).Items.Single().Id == expired.Id);
        Check(f.Authority.QueryLicenses(new(ActivatedOnly: true)).Total == 0);
        f.Grant(list[2]); Check(f.Authority.QueryLicenses(new(ActivatedOnly: true)).Items.Single().DeviceCount == 1);
    }
    /// <summary>Empty lists return bounded coherent metadata and no continuation.</summary>
    public static void EmptyPages()
    {
        using var f = new Fixture(); var licenses = f.Authority.QueryLicenses(new()); var reserves = f.Authority.QueryReserves(new());
        Check(licenses.Total == 0 && licenses.Items.Length == 0 && licenses.NextCursor == "" && licenses.Counts.Total == 0);
        Check(reserves.Total == 0 && reserves.Items.Length == 0 && reserves.NextCursor == "" && !reserves.Policy.Enabled);
        Check(licenses.Revision == f.Authority.QueryLicenses(new()).Revision);
    }
    /// <summary>Oversized fields, unsupported states and invalid limits are rejected before querying the store.</summary>
    public static void InvalidQueries()
    {
        using var f = new Fixture();
        foreach (var query in new[] { new OwnerPageRequest(0), new(-1), new(101), new(int.MaxValue), new(Search: new('x', 121)),
            new(Search: "bad\nlabel"), new(Status: "ACTIVE"), new(Cursor: new('a', 2049)), new(Search: null!) })
            Reject("OWNER_PAGE_QUERY", () => f.Authority.QueryLicenses(query));
        Reject("OWNER_PAGE_QUERY", () => f.Authority.QueryReserves(new(ActivatedOnly: true)));
        Reject("OWNER_PAGE_QUERY", () => f.Authority.QueryReserves(new(Status: "active")));
        Reject("REQUEST_INVALID", () => f.Authority.QueryLicense(new("not-a-uuid")));
        Check(LicenseJson.Read<OwnerPageRequest>(Encoding.UTF8.GetBytes("{}")).Limit == 25);
    }
    /// <summary>Cursors are MAC-bound to filters, endpoint and page size and never accepted as arbitrary offset input.</summary>
    public static void CursorBindings()
    {
        using var f = new Fixture(); Seed(f, 4); string cursor = f.Authority.QueryLicenses(new(1)).NextCursor;
        Reject("OWNER_PAGE_CURSOR", () => f.Authority.QueryLicenses(new(2, Cursor: cursor)));
        Reject("OWNER_PAGE_CURSOR", () => f.Authority.QueryLicenses(new(1, Search: "Beta", Cursor: cursor)));
        Reject("OWNER_PAGE_CURSOR", () => f.Authority.QueryLicenses(new(1, Status: "active", Cursor: cursor)));
        Reject("OWNER_PAGE_CURSOR", () => f.Authority.QueryReserves(new(1, Cursor: cursor)));
        string[] parts = cursor.Split('.'); byte[] tag = LicenseCrypto.Decode(parts[1], 32, 32); tag[0] ^= 1;
        Reject("OWNER_PAGE_CURSOR", () => f.Authority.QueryLicenses(new(1, Cursor: parts[0] + "." + LicenseCrypto.Encode(tag))));
        foreach (string bad in new[] { "a", "a.b.c", "$.abc", "." }) Reject("OWNER_PAGE_CURSOR", () => f.Authority.QueryLicenses(new(1, Cursor: bad)));
    }
    /// <summary>Changing the database invalidates existing continuation tokens; refresh does not silently omit new rows.</summary>
    public static void ChangedRevision()
    {
        using var f = new Fixture(); var list = Seed(f, 3); var a = f.Authority.QueryLicenses(new(1));
        f.Change(list[0], "suspend"); Reject("OWNER_PAGE_STALE", () => f.Authority.QueryLicenses(new(1, Cursor: a.NextCursor)));
        var fresh = f.Authority.QueryLicenses(new(1)); Check(fresh.Revision > a.Revision && fresh.Counts.Suspended == 1);
    }
    /// <summary>Moving through pages preserves the original two-minute deadline; an exact boundary is stale.</summary>
    public static void CursorDeadline()
    {
        using var f = new Fixture(); Seed(f, 5); var a = f.Authority.QueryLicenses(new(1));
        f.Time.Advance(TimeSpan.FromSeconds(119)); var b = f.Authority.QueryLicenses(new(1, Cursor: a.NextCursor));
        Check(b.AsOfUtc == a.AsOfUtc); f.Time.Advance(TimeSpan.FromSeconds(1));
        Reject("OWNER_PAGE_STALE", () => f.Authority.QueryLicenses(new(1, Cursor: b.NextCursor)));
    }
    /// <summary>Process-local cursor keys reject tokens after restart without changing license activation or duration.</summary>
    public static void RestartInvalidatesCursor()
    {
        using var f = new Fixture(); Seed(f, 3); var a = f.Authority.QueryLicenses(new(1)); f.Restart();
        Reject("OWNER_PAGE_CURSOR", () => f.Authority.QueryLicenses(new(1, Cursor: a.NextCursor)));
        Check(f.Authority.QueryLicenses(new()).Total == 3);
    }
    /// <summary>Effective state remains coherent inside a short traversal; a fresh query observes actual expiry.</summary>
    public static void ExpirySnapshot()
    {
        using var f = new Fixture(); f.Issue("hours", 1, "issue"); f.Issue("hours", 1, "issue");
        f.Time.Advance(TimeSpan.FromSeconds(3599)); var a = f.Authority.QueryLicenses(new(1, Status: "active"));
        f.Time.Advance(TimeSpan.FromSeconds(1)); var b = f.Authority.QueryLicenses(new(1, Status: "active", Cursor: a.NextCursor));
        Check(b.Total == 2 && b.Items.Single().Status == "active" && b.AsOfUtc == a.AsOfUtc);
        Check(f.Authority.QueryLicenses(new(Status: "active")).Total == 0);
        Check(f.Authority.QueryLicenses(new(Status: "expired")).Total == 2);
    }
    /// <summary>List JSON contains no device arrays, public keys, host bindings or internal key/request fingerprints.</summary>
    public static void ProjectionPrivacy()
    {
        using var f = new Fixture(); var issue = f.Issue("permanent", 0); f.Grant(issue);
        var page = f.Authority.QueryLicenses(new()); string json = Encoding.UTF8.GetString(LicenseJson.Write(page));
        Check(!json.Contains(f.Device.PublicKey) && !json.Contains(f.Device.HostBinding) && !json.Contains("\"devices\"") && !json.Contains("keyDigest"));
        var detail = f.Authority.QueryLicense(new(issue.Id)); Check(detail.Devices.Length == 1);
        detail.Devices[0] = new("changed", "", "", 0); Check(f.Authority.QueryLicense(new(issue.Id)).Devices[0].Id == f.Device.Id);
        page.Items[0] = page.Items[0] with { Label = "changed" }; Check(f.Authority.Get(issue.Id).Label != "changed");
    }
    /// <summary>Reserve history traverses all entries, not just the former UI's first 200, and hides request fingerprints.</summary>
    public static void ReserveTraversal()
    {
        using var f = new Fixture(); var issue = f.Issue("permanent", 0); f.Grant(issue); f.Authority.SetReservePolicy(new(Guid.NewGuid().ToString("D"), true));
        for (int i = 0; i < 231; i++) f.Authority.IssueReserve(new(Guid.NewGuid().ToString("D"), issue.Id, f.Device.Id, 1, "allow " + i));
        var ids = new List<string>(); string cursor = "";
        do { var page = f.Authority.QueryReserves(new(7, Cursor: cursor)); Check(page.Items.Length <= 7 && page.Total == 231);
            Check(page.Items.All(x => x.Status == "available")); ids.AddRange(page.Items.Select(x => x.Id)); cursor = page.NextCursor;
            Check(!Encoding.UTF8.GetString(LicenseJson.Write(page)).Contains("fingerprint")); } while (cursor != "");
        Check(ids.SequenceEqual(f.Authority.ReserveStatus().Grants.Select(x => x.Id)));
        var policy = f.Authority.SetReservePolicy(new(Guid.NewGuid().ToString("D"), false), false);
        Check(!policy.Enabled && policy.Grants.Length == 0 && f.Authority.ReserveStatus().Grants.Length == 231);
    }
    /// <summary>Parent suspension/expiry, policy epochs and device reset affect renewal eligibility, not assertions of instant offline revocation.</summary>
    public static void ReserveEligibility()
    {
        using var f = new Fixture(); var issue = f.Issue("hours", 1, "issue"); f.Grant(issue); f.Authority.SetReservePolicy(new(Guid.NewGuid().ToString("D"), true));
        var grant = f.Authority.IssueReserve(new(Guid.NewGuid().ToString("D"), issue.Id, f.Device.Id, 2, "Аварийный резерв"));
        Check(f.Authority.QueryReserves(new(Search: "аварийный", Status: "available")).Total == 1);
        f.Change(issue, "suspend"); Check(f.Authority.QueryReserves(new()).Items[0].Status == "inactive-license");
        f.Change(issue, "resume"); f.Time.Advance(TimeSpan.FromHours(1)); Check(f.Authority.QueryReserves(new()).Items[0].Status == "inactive-license");
        f.Authority.SetReservePolicy(new(Guid.NewGuid().ToString("D"), false)); Check(f.Authority.QueryReserves(new()).Items[0].Status == "retired");
        f.Authority.RevokeReserve(new(Guid.NewGuid().ToString("D"), grant.Id)); Check(f.Authority.QueryReserves(new(Status: "revoked")).Total == 1);
    }
}

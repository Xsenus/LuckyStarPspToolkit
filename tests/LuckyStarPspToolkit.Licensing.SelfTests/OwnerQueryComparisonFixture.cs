using System.Diagnostics;
using System.Text.Json;
using LuckyStarPspToolkit.Licensing;
using LuckyStarPspToolkit.Licensing.Authority;

/// <summary>Cross-version owner-page comparison on 2000 synthetic licenses and 4000 installations; no customer data is used.</summary>
internal static class OwnerQueryComparisonFixture
{
    /// <summary>Measures the previous full-response-plus-client-selection path versus one server page, with identical visible UUIDs.</summary>
    /// <param name="args">Reserved runner arguments.</param>
    /// <returns>Zero after actual measurements and selection equivalence checks complete.</returns>
    public static int Run(string[] args)
    {
        using var f = new Fixture(); var first = f.Issue("permanent", 0, "issue"); f.Grant(first);
        using var other = Fixture.NewDevice(); f.Authority.Dispose();
        var config = LicenseJson.Read<AuthorityConfiguration>(PrivateFiles.Read(Path.Combine(f.Directory, "authority.json")));
        using (var store = new LicenseStore(f.Directory, config))
        {
            long now = f.Time.GetUtcNow().ToUnixTimeSeconds();
            store.Change(now, db =>
            {
                var template = db.Licenses[first.Id]; db.Licenses.Clear(); db.Requests.Clear(); db.Audit.Clear();
                var devices = new Dictionary<string, LicensedDevice>(template.Devices, StringComparer.Ordinal)
                { [other.Id] = new(other.Id, other.PublicKey, other.HostBinding, now) };
                for (int i = 0; i < 2000; i++)
                {
                    string id = $"00000000-0000-0000-0000-{i + 1:D12}";
                    db.Licenses.Add(id, template with { Id = id, Label = "Synthetic client " + i, CreatedAt = now - i / 5,
                        KeyDigest = LicenseCrypto.Digest(System.Text.Encoding.UTF8.GetBytes(id)), MaxDevices = 2,
                        Devices = new(devices, StringComparer.Ordinal) });
                }
                return 0;
            });
        }
        f.Restart();
        string[] expected = f.Authority.List().OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id, StringComparer.Ordinal).Take(25).Select(x => x.Id).ToArray();
        var query = typeof(LicenseAuthority).GetMethod("QueryLicenses");
        object? request = query is null ? null : Activator.CreateInstance(query.GetParameters()[0].ParameterType, 25, "", "", "", false);
        /// <summary>Performs the version-appropriate browser listing operation and returns its actual UTF-8 response bytes.</summary>
        /// <returns>Serialized wire representation; no private access keys are contained in either projection.</returns>
        byte[] Read() => query is null ? LicenseJson.Write(f.Authority.List()) : LicenseJson.Write(query.Invoke(f.Authority, new[] { request })!);
        byte[] response = Read();
        using var document = JsonDocument.Parse(response);
        string[] observed = query is null ? document.RootElement.EnumerateArray().OrderByDescending(x => x.GetProperty("createdAt").GetInt64())
            .ThenBy(x => x.GetProperty("id").GetString(), StringComparer.Ordinal).Take(25).Select(x => x.GetProperty("id").GetString()!).ToArray()
            : document.RootElement.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetString()!).ToArray();
        if (!observed.SequenceEqual(expected)) throw new InvalidOperationException("Visible rows differ from the independent ordered reference.");
        var samples = new List<object>();
        for (int sample = -3; sample < 7; sample++)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread(); long started = Stopwatch.GetTimestamp();
            for (int i = 0; i < 20; i++) { byte[] bytes = Read(); if (bytes.Length != response.Length) throw new InvalidOperationException("Response size changed."); }
            long count = GC.GetAllocatedBytesForCurrentThread() - allocated;
            if (sample >= 0) samples.Add(new { allocatedBytes = count, elapsedMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds });
        }
        Console.WriteLine(JsonSerializer.Serialize(new { schema = "lsptool.owner-page-comparison.v1", framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            mode = query is null ? "legacy-full-list" : "bounded-server-page", licenses = 2000, installationRecords = 4000,
            readsPerSample = 20, warmups = 3, responseBytes = response.Length, visibleRows = expected,
            snapshotsMatch = true, samples }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}

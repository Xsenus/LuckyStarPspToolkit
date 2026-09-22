namespace LuckyStarPspToolkit.Licensing.Authority;

/// <summary>Explicit bounded snapshot validation and copying; no JSON round-trip or repeated string allocation is needed for isolation.</summary>
internal static class LicenseDatabaseSnapshot
{
    /// <summary>Checks collection shape, cross-record identities and bounded scalar values before the state can be published.</summary>
    /// <param name="database">Candidate authenticated or in-process database.</param>
    /// <param name="issuer">Expected authority identifier.</param>
    /// <exception cref="LicenseException">Invalid state or a configured capacity limit.</exception>
    public static void Validate(LicenseDatabase database, string issuer)
    {
        if (database.Schema is not (1 or 2) || database.Issuer != issuer || database.Revision < 0 ||
            database.LastWriteUtc is < 0 or > 253402300799L || database.Licenses is null || database.Requests is null || database.Audit is null)
            throw new LicenseException("DATABASE_INVALID", "Invalid license database schema.");
        if (database.Licenses.Count > 10000 || database.Requests.Count > 100000 || database.Audit.Count > 100000)
            throw new LicenseException("DATABASE_LIMIT", "Authority capacity reached; archive and migrate before adding records.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in database.Licenses)
        {
            LicenseRecord record = pair.Value;
            if (record is null || pair.Key != record.Id || !Guid.TryParseExact(pair.Key, "D", out _) ||
                record.Status is not ("active" or "suspended" or "revoked") || record.Starts is not ("issue" or "activation") ||
                record.Devices is null || record.MaxDevices is < 1 or > 100 || record.Devices.Count > record.MaxDevices ||
                record.Label is null || record.Label.Length > 120 || record.Label.Any(char.IsControl) ||
                record.CreatedAt < 0 || record.CreatedAt > database.LastWriteUtc ||
                record.ActivatedAt is < 0 or > 253402300799L || record.ExpiresAt is < 0 or > 253402300799L ||
                record.ActivateBefore is < 0 or > 253402300799L ||
                (record.ActivatedAt.HasValue && (record.ActivatedAt.Value < record.CreatedAt || record.ActivatedAt.Value > database.LastWriteUtc)) ||
                (record.Unit == "permanent" && record.ExpiresAt.HasValue) ||
                (record.Unit != "permanent" && (record.Starts == "issue" || record.ActivatedAt.HasValue) && !record.ExpiresAt.HasValue) ||
                (record.Devices.Count != 0 && !record.ActivatedAt.HasValue))
                throw new LicenseException("DATABASE_INVALID", "Inconsistent entitlement record.");
            try
            {
                LicenseDurations.Validate(record.Unit, record.Amount);
                LicenseCrypto.ValidateDigest(record.KeyDigest);
                LicenseCrypto.ValidateDigest(record.IssueDigest);
                if (!keys.Add(record.KeyDigest)) throw new LicenseException("DATABASE_INVALID", "Duplicate credential digest.");
                foreach (var item in record.Devices)
                {
                    LicensedDevice device = item.Value;
                    if (device is null || device.Id != item.Key || device.ActivatedAt < record.CreatedAt ||
                        device.ActivatedAt > database.LastWriteUtc || device.PublicKey is null || device.PublicKey.Length > 160)
                        throw new LicenseException("DATABASE_INVALID", "Inconsistent installation record.");
                    LicenseCrypto.ValidateDigest(device.Id);
                    LicenseCrypto.ValidateDigest(device.HostBinding);
                    // The network path already imports and validates the curve before activation.
                    // This cheap check also catches swapped identities in authenticated snapshots.
                    if (LicenseCrypto.Digest(Convert.FromBase64String(device.PublicKey)) != device.Id)
                        throw new LicenseException("DATABASE_INVALID", "Installation public-key digest mismatch.");
                }
            }
            catch (Exception ex) when (ex is LicenseException or FormatException)
            { throw new LicenseException("DATABASE_INVALID", "Invalid entitlement or installation fields."); }
        }
        foreach (var pair in database.Requests)
        {
            if (!Guid.TryParseExact(pair.Key, "D", out _)) throw new LicenseException("DATABASE_INVALID", "Invalid mutation identifier.");
            try { LicenseCrypto.ValidateDigest(pair.Value); }
            catch (LicenseException) { throw new LicenseException("DATABASE_INVALID", "Invalid mutation fingerprint."); }
        }
        foreach (var entry in database.Audit)
        {
            if (entry is null || entry.At < 0 || entry.At > database.LastWriteUtc ||
                entry.LicenseId is null || !database.Licenses.ContainsKey(entry.LicenseId) || entry.Action is not
                ("issue" or "activate" or "suspend" or "resume" or "revoke" or "extend" or "permanent" or "reset-device") || entry.DeviceId is null)
                throw new LicenseException("DATABASE_INVALID", "Invalid audit record.");
        }
    }

    /// <summary>Copies every mutable collection while sharing only immutable strings and immutable leaf records.</summary>
    /// <param name="database">Previously validated snapshot, used while the caller holds its store lock.</param>
    /// <returns>Detached root, license records, device dictionaries, request dictionary and audit list.</returns>
    public static LicenseDatabase Copy(LicenseDatabase database)
    {
        var licenses = new Dictionary<string, LicenseRecord>(database.Licenses.Count, StringComparer.Ordinal);
        foreach (var pair in database.Licenses)
            licenses.Add(pair.Key, pair.Value with { Devices = new(pair.Value.Devices, StringComparer.Ordinal) });
        return database with
        {
            Licenses = licenses,
            Requests = new(database.Requests, StringComparer.Ordinal),
            Audit = new(database.Audit)
        };
    }
}

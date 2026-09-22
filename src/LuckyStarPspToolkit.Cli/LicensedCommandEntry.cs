using System.Reflection;
using System.Text;
using LuckyStarPspToolkit.Licensing;

namespace LuckyStarPspToolkit.Cli;

/// <summary>Fail-closed executable gate. Only help, version and license management work before online authorization.</summary>
internal static class LicensedCommandEntry
{
    /// <summary>Distinct exit code for licensing refusal; no game-processing operation is run on failure.</summary>
    internal const int AccessDenied = 77;

    /// <summary>Reads immutable public trust from this executable, never from an editable runtime JSON file or environment override.</summary>
    /// <returns>Validated customer build profile.</returns>
    private static LicenseTrust ReadTrust()
    {
        using Stream? stream = typeof(LicensedCommandEntry).Assembly.GetManifestResourceStream("LuckyStarPspToolkit.license-trust.json");
        if (stream is null || stream.Length is < 1 or > 8192)
            throw new LicenseException("LICENSE_NOT_CONFIGURED", "Ask the owner for a customer build with an embedded license authority.");
        byte[] bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes);
        LicenseTrust trust = LicenseJson.Read<LicenseTrust>(bytes, 8192);
        _ = LicenseCrypto.ValidateTrust(trust);
        return trust;
    }

    /// <summary>Authorizes every operational command and maintains a watchdog until its synchronous work finishes.</summary>
    /// <param name="args">CLI arguments; access keys are accepted only interactively or from a file, never as argument values.</param>
    /// <returns>Original command code, or 77 for any licensing/state/network failure.</returns>
    public static int Run(string[] args)
    {
        string command = args.Length == 0 ? "help" : args[0].ToLowerInvariant();
        if (command is "help" or "--help" or "-h" or "version" or "--version" or "-v") return CommandApplication.RunCore(args);
        if (command == "license" && (args.Length == 1 || args[1] is "help" or "--help"))
        { PrintLicenseHelp(); return 0; }
        try
        {
            LicenseTrust trust = ReadTrust();
            if (command == "license" && args.Length == 2 && args[1] == "build-info")
            {
                Console.WriteLine(Encoding.UTF8.GetString(LicenseJson.Write(new
                { issuer = trust.Issuer, productId = trust.ProductId, serverUrl = trust.ServerUrl,
                  keyIds = trust.PublicKeys.Keys.Order(StringComparer.Ordinal).ToArray(), developmentLoopback = trust.DevelopmentLoopback })));
                return 0;
            }
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
            if (string.IsNullOrWhiteSpace(appData) || !Path.IsPathFullyQualified(appData))
                throw new LicenseException("LICENSE_STATE_PATH", "A private per-user application-data directory is required.");
            string directory = Path.Combine(appData, "LuckyStarPspToolkit", "licenses", trust.Issuer);
            using FileStream stateLock = PrivateFiles.Lock(Path.Combine(directory, "installation.lock"));
            string statePath = Path.Combine(directory, "activation.json");
            if (command == "license" && args.Length == 2 && args[1] == "forget")
            {
                if (File.Exists(statePath)) File.Delete(PrivateFiles.SafePath(statePath));
                string reservePath = Path.Combine(directory, "reserve.json");
                if (File.Exists(reservePath)) File.Delete(PrivateFiles.SafePath(reservePath));
                Console.WriteLine("Local activation removed. Server slots are released only by the owner.");
                return 0;
            }
            using DeviceIdentity identity = DeviceIdentity.Open(directory);
            using var transport = new LicenseTransport(trust, identity);
            if (command == "license" && args.Length >= 2 && args[1] == "activate")
            {
                string key;
                if (args.Length == 4 && args[2] == "--key-file") key = Encoding.UTF8.GetString(PrivateFiles.Read(args[3], 1024)).Trim();
                else if (args.Length == 2) key = ReadKey();
                else throw new LicenseException("LICENSE_USAGE", "Use license activate or license activate --key-file path.");
                LicenseCrypto.ValidateAccessKey(key);
                var grant = transport.ExchangeAsync("activate", "", key).GetAwaiter().GetResult();
                // A new online activation never inherits another entitlement's cached reserve token.
                string oldReserve = Path.Combine(directory, "reserve.json");
                if (File.Exists(oldReserve)) File.Delete(PrivateFiles.SafePath(oldReserve));
                PrivateFiles.Write(statePath, LicenseJson.Write(new ActivationState(1, trust.Issuer, trust.ProductId, grant.Lease.LicenseId, identity.Id)));
                PrintStatus(grant.Lease, identity.Id, trust.DevelopmentLoopback);
                return 0;
            }
            if (command == "license" && args.Length == 2 && args[1] == "device")
            { Console.WriteLine(identity.Id); return 0; }
            bool reserveEnable = command == "license" && args.Length == 4 && args[1] == "reserve-enable" && args[2] == "--id";
            bool reserveDisable = command == "license" && args.Length == 2 && args[1] == "reserve-disable";
            if (command == "license" && !reserveEnable && !reserveDisable && (args.Length != 2 || args[1] != "status"))
                throw new LicenseException("LICENSE_USAGE", "Unknown license command. Run license help.");
            if (!File.Exists(statePath)) throw new LicenseException("LICENSE_REQUIRED", "Activate first: lsptool license activate");
            var state = LicenseJson.Read<ActivationState>(PrivateFiles.Read(statePath));
            if (state.Schema != 1 || state.Issuer != trust.Issuer || state.ProductId != trust.ProductId || state.DeviceId != identity.Id)
                throw new LicenseException("ACTIVATION_INVALID", "The activation belongs to another vendor or installation.");
            var reserve = new ReserveCache(directory, trust, identity, state.LicenseId);
            if (reserveEnable)
            {
                try
                {
                    var permit = transport.RefreshReserveAsync(state.LicenseId, args[3]).GetAwaiter().GetResult();
                    reserve.Accept(permit.Token, args[3], permit.Nonce, permit.Elapsed);
                }
                catch (LicenseException ex) when (!ReserveCache.IsTransient(ex.Code)) { reserve.Block(); throw; }
                Console.WriteLine("Reserve permission enabled; exclusive UTC deadline: " + DateTimeOffset.FromUnixTimeSeconds(reserve.ValidUntil!.Value).ToString("O"));
                return 0;
            }
            if (reserveDisable) { reserve.Block(); Console.WriteLine("Local reserve permission disabled. Normal online license is unchanged."); return 0; }
            TimeSpan interval;
            try
            {
                var initial = transport.ExchangeAsync("check", state.LicenseId, "").GetAwaiter().GetResult();
                long receivedAt = System.Diagnostics.Stopwatch.GetTimestamp();
                reserve.RenewAsync(transport).GetAwaiter().GetResult();
                interval = initial.Remaining - System.Diagnostics.Stopwatch.GetElapsedTime(receivedAt);
                if (interval <= TimeSpan.Zero) throw new LicenseException("LEASE_EXPIRED", "Online grant expired during reserve renewal.");
                if (command == "license") { PrintStatus(initial.Lease, identity.Id, trust.DevelopmentLoopback); return 0; }
            }
            catch (LicenseException ex) when (ReserveCache.IsTransient(ex.Code) && reserve.Enabled)
            {
                interval = reserve.Remaining();
                Console.Error.WriteLine("RESERVE MODE: offline execution is limited by the signed deadline; revocation is checked when connectivity returns.");
                if (command == "license")
                {
                    Console.WriteLine(Encoding.UTF8.GetString(LicenseJson.Write(new { state = "reserve-offline", grantId = reserve.GrantId,
                        validUntil = reserve.ValidUntil, remainingSeconds = (long)interval.TotalSeconds })));
                    return 0;
                }
            }
            catch (LicenseException ex) when (!ReserveCache.IsTransient(ex.Code)) { reserve.Block(); throw; }
            using var session = new LicenseSession(transport, state.LicenseId, interval, TerminateExpired, reserve.Enabled ? reserve : null);
            session.RequireValid();
            int code = CommandApplication.RunCore(args);
            session.RequireValid();
            return code;
        }
        catch (LicenseException ex) { Console.Error.WriteLine($"license [{ex.Code}]: {ex.Message}"); return AccessDenied; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or FormatException)
        { Console.Error.WriteLine("license [LICENSE_STATE]: Cannot read or verify licensing state. Contact the owner."); return AccessDenied; }
        catch (Exception) { Console.Error.WriteLine("license [LICENSE_FAILURE]: Authorization could not be completed."); return AccessDenied; }
    }

    /// <summary>Terminates only the current process when continued access is no longer authorized; no user data is deleted.</summary>
    /// <param name="code">Non-secret reason recorded on stderr.</param>
    private static void TerminateExpired(string code)
    {
        Console.Error.WriteLine($"license [{code}]: Execution stopped. Original files are not deleted.");
        Environment.Exit(AccessDenied);
    }

    /// <summary>Reads a key without exposing it in shell history or terminal echo.</summary>
    /// <returns>Trimmed credential.</returns>
    private static string ReadKey()
    {
        Console.Error.Write("Access key: ");
        if (Console.IsInputRedirected) return (Console.ReadLine() ?? "").Trim();
        var text = new StringBuilder();
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Enter) { Console.Error.WriteLine(); return text.ToString().Trim(); }
            if (key.Key == ConsoleKey.Backspace && text.Length > 0) { text.Length--; continue; }
            if (!char.IsControl(key.KeyChar) && text.Length < 128) text.Append(key.KeyChar);
        }
    }

    /// <summary>Prints only authenticated status with an explicit distinction between perpetual entitlement and online execution.</summary>
    /// <param name="lease">Fresh verified lease.</param>
    /// <param name="device">Public installation ID.</param>
    /// <param name="development">Whether the executable is an isolated-loopback test build.</param>
    private static void PrintStatus(ExecutionLease lease, string device, bool development)
    {
        Console.WriteLine(Encoding.UTF8.GetString(LicenseJson.Write(new
        {
            state = "active", licenseId = lease.LicenseId, deviceId = device,
            permanent = lease.EntitlementExpires is null,
            expiresUtc = lease.EntitlementExpires.HasValue ? DateTimeOffset.FromUnixTimeSeconds(lease.EntitlementExpires.Value).ToString("O") : null,
            leaseSeconds = lease.ValidUntil - lease.ServerNow,
            offlineAccess = false, developmentLoopback = development
        })));
    }

    /// <summary>Shows the unlicensed management surface without revealing private state.</summary>
    private static void PrintLicenseHelp() => Console.WriteLine("Licensing (game commands require an online lease or an explicitly enabled signed reserve):\n  lsptool license activate                     Read the separately delivered key without echo.\n  lsptool license activate --key-file key.txt   Read key from a private file, not shell arguments.\n  lsptool license build-info                   Public embedded issuer metadata, no activation secrets.\n  lsptool license status                       Fresh authenticated online status.\n  lsptool license device                       Public installation ID for owner support.\n  lsptool license forget                       Remove local activation only; no slot reset.\nReserve access is opt-in, per installation and capped at seven days.\n  lsptool license reserve-enable --id UUID     Fetch an owner-approved reserve permission online.\n  lsptool license reserve-disable              Block the local fallback without affecting normal online access.\nPublic trust is embedded into customer builds.");
}

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using LuckyStarPspToolkit.Licensing;
using LuckyStarPspToolkit.Licensing.Authority;

return await OwnerProgram.RunAsync(args);

/// <summary>Owner-only issuer. Credentials are generated locally and written privately before requests, making retries safe.</summary>
internal static class OwnerProgram
{
    /// <summary>Parses a small explicit command grammar and refuses unknown or duplicate options.</summary>
    /// <param name="args">Owner command arguments.</param>
    /// <returns>Zero for committed operations; nonzero on refusal or uncertain network completion.</returns>
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help") { Help(); return 0; }
            string command = args[0];
            var options = Parse(args[1..]);
            if (command == "init")
            {
                RequireOptions(options, "--data", "--url", "--password-file", "--development-loopback", "--lease-seconds");
                string password = ReadPassword(options);
                bool dev = options.ContainsKey("--development-loopback");
                LicenseTrust trust = LicenseAuthority.Initialize(Required(options, "--data"), Required(options, "--url"), password,
                    dev, Number(options, "--lease-seconds", 90));
                Console.WriteLine($"Authority created: {trust.Issuer}. Keep authority.json and owner-connection.json private. Embed only client-trust.json.");
                return 0;
            }
            string connection = Required(options, "--connection");
            if (command is "list" or "audit")
            {
                RequireOptions(options, "--connection", "--out");
                byte[] response = await SendAsync(connection, command == "list" ? "admin/licenses" : "admin/audit", null).ConfigureAwait(false);
                if (options.TryGetValue("--out", out string? path)) PrivateFiles.Write(path, response, false);
                else Console.WriteLine(Encoding.UTF8.GetString(response));
                return 0;
            }
            if (command is "issue" or "retry-issue")
            {
                IssueLicenseRequest issue;
                if (command == "issue")
                {
                    RequireOptions(options, "--connection", "--out", "--hours", "--days", "--years", "--permanent", "--starts", "--devices", "--label", "--activate-before");
                    var duration = Duration(options);
                    long? deadline = options.TryGetValue("--activate-before", out string? at) ?
                        DateTimeOffset.ParseExact(at, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal).ToUnixTimeSeconds() : null;
                    issue = new(Guid.NewGuid().ToString("D"), LicenseCrypto.NewAccessKey(), options.GetValueOrDefault("--label", ""),
                        duration.Unit, duration.Amount, options.GetValueOrDefault("--starts", "activation"), Number(options, "--devices", 1), deadline);
                    string pending = Required(options, "--out") + ".issue-request.json";
                    PrivateFiles.Write(pending, LicenseJson.Write(issue), false);
                    Console.WriteLine("Saved private idempotent request: " + pending);
                }
                else
                {
                    RequireOptions(options, "--connection", "--request", "--out");
                    issue = LicenseJson.Read<IssueLicenseRequest>(PrivateFiles.Read(Required(options, "--request")));
                }
                string output = Required(options, "--out");
                if (File.Exists(output) && Encoding.UTF8.GetString(PrivateFiles.Read(output, 1024)).Trim() != issue.AccessKey)
                    throw new LicenseException("KEY_FILE_EXISTS", "Refusing to overwrite a different key file.");
                byte[] response = await SendAsync(connection, "admin/issue", LicenseJson.Write(issue)).ConfigureAwait(false);
                var overview = LicenseJson.Read<LicenseOverview>(response);
                if (!File.Exists(output)) PrivateFiles.Write(output, Encoding.UTF8.GetBytes(issue.AccessKey + "\n"), false);
                PrivateFiles.Write(output + ".receipt.json", response);
                Console.WriteLine($"Issued license {overview.Id}; status={overview.Status}. Send ONLY {output} to the customer, not the request/owner files.");
                return 0;
            }
            if (command is "suspend" or "resume" or "revoke" or "extend" or "permanent" or "reset-device" or "retry-change")
            {
                ChangeLicenseRequest change;
                if (command == "retry-change")
                {
                    RequireOptions(options, "--connection", "--request");
                    change = LicenseJson.Read<ChangeLicenseRequest>(PrivateFiles.Read(Required(options, "--request")));
                }
                else
                {
                    if (command == "extend") RequireOptions(options, "--connection", "--id", "--hours", "--days", "--years");
                    else if (command == "reset-device") RequireOptions(options, "--connection", "--id", "--device");
                    else RequireOptions(options, "--connection", "--id");
                    var duration = command == "extend" ? Duration(options) : (Unit: "", Amount: 0);
                    change = new(Guid.NewGuid().ToString("D"), Required(options, "--id"), command, duration.Unit, duration.Amount,
                        options.GetValueOrDefault("--device", ""));
                    string journal = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(connection))!, "requests", change.RequestId + ".change.json");
                    PrivateFiles.Write(journal, LicenseJson.Write(change), false);
                    Console.WriteLine("Saved retry-safe owner request: " + journal);
                }
                byte[] response = await SendAsync(connection, "admin/change", LicenseJson.Write(change)).ConfigureAwait(false);
                Console.WriteLine(Encoding.UTF8.GetString(response));
                return 0;
            }
            throw new LicenseException("USAGE", "Unknown owner command.");
        }
        catch (LicenseException ex) { Console.Error.WriteLine($"license-owner [{ex.Code}]: {ex.Message}"); return 77; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        { Console.Error.WriteLine("license-owner: network completion is uncertain. Retry the SAVED request, do not issue a new key or extension."); return 75; }
        catch (Exception)
        { Console.Error.WriteLine("license-owner: invalid options or inaccessible private files. No secrets are printed; check help and saved requests."); return 70; }
    }

    /// <summary>Reads a passphrase from a restricted file or hidden terminal input; never accepts it as a command argument.</summary>
    /// <param name="options">Parsed options.</param>
    /// <returns>Unlogged passphrase.</returns>
    private static string ReadPassword(Dictionary<string, string> options)
    {
        if (options.TryGetValue("--password-file", out string? file)) return Encoding.UTF8.GetString(PrivateFiles.Read(file, 4096)).TrimEnd('\r', '\n');
        Console.Error.Write("New signing-key passphrase (16+ characters): ");
        if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
        var text = new StringBuilder();
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Enter) { Console.Error.WriteLine(); return text.ToString(); }
            if (key.Key == ConsoleKey.Backspace && text.Length > 0) { text.Length--; continue; }
            if (!char.IsControl(key.KeyChar) && text.Length < 1024) text.Append(key.KeyChar);
        }
    }

    /// <summary>Sends one owner request only over HTTPS or literal loopback HTTP, with no redirects or secret-bearing URLs.</summary>
    /// <param name="connectionFile">Private owner connection file.</param>
    /// <param name="endpoint">Fixed administrative relative endpoint.</param>
    /// <param name="payload">JSON body, or null for a listing.</param>
    /// <returns>Bounded authenticated response.</returns>
    private static async Task<byte[]> SendAsync(string connectionFile, string endpoint, byte[]? payload)
    {
        var connection = LicenseJson.Read<OwnerConnection>(PrivateFiles.Read(connectionFile));
        if (!Uri.TryCreate(connection.AdminUrl, UriKind.Absolute, out Uri? uri) || uri.UserInfo != "" || uri.Query != "" || uri.Fragment != "" || uri.AbsolutePath != "/" ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && IPAddress.TryParse(uri.Host, out var ip) && IPAddress.IsLoopback(ip))))
            throw new LicenseException("OWNER_ENDPOINT", "Use HTTPS or a literal loopback admin endpoint through SSH.");
        _ = LicenseCrypto.Decode(connection.Token, 32, 32);
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(15) };
        using var message = new HttpRequestMessage(payload is null ? HttpMethod.Get : HttpMethod.Post, endpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.Token);
        if (payload is not null)
        { message.Content = new ByteArrayContent(payload); message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json"); }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var result = new MemoryStream(); byte[] buffer = new byte[8192]; int read;
        while ((read = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) != 0)
        {
            if (result.Length + read > 32 * 1024 * 1024) throw new LicenseException("OWNER_RESPONSE", "Owner response is too large.");
            result.Write(buffer, 0, read);
        }
        byte[] bytes = result.ToArray();
        if (!response.IsSuccessStatusCode)
        {
            var error = LicenseJson.Read<LicenseError>(bytes);
            throw new LicenseException(error.Code, error.Message);
        }
        return bytes;
    }

    /// <summary>Parses long options with explicit boolean switches and duplicate rejection.</summary>
    /// <param name="args">Option tokens.</param>
    /// <returns>Case-sensitive option dictionary.</returns>
    private static Dictionary<string, string> Parse(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i];
            if (!key.StartsWith("--", StringComparison.Ordinal)) throw new LicenseException("USAGE", "Expected a long option.");
            string value = key is "--permanent" or "--development-loopback" ? "true" :
                ++i < args.Length ? args[i] : throw new LicenseException("USAGE", "Missing option value.");
            if (!result.TryAdd(key, value)) throw new LicenseException("USAGE", "Duplicate option.");
        }
        return result;
    }

    /// <summary>Rejects options that do not belong to the selected operation.</summary>
    /// <param name="options">Parsed options.</param>
    /// <param name="allowed">Exact allowed names.</param>
    private static void RequireOptions(Dictionary<string, string> options, params string[] allowed)
    {
        if (options.Keys.Any(key => !allowed.Contains(key, StringComparer.Ordinal))) throw new LicenseException("USAGE", "Unknown option for this operation.");
    }

    /// <summary>Reads a required nonempty option.</summary>
    /// <param name="options">Parsed options.</param>
    /// <param name="name">Option name.</param>
    /// <returns>Value.</returns>
    private static string Required(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : throw new LicenseException("USAGE", "Missing " + name);

    /// <summary>Reads an optional invariant integer.</summary>
    /// <param name="options">Parsed options.</param>
    /// <param name="name">Option name.</param>
    /// <param name="fallback">Default value.</param>
    /// <returns>Parsed value or default.</returns>
    private static int Number(Dictionary<string, string> options, string name, int fallback) =>
        options.TryGetValue(name, out string? value) ? int.Parse(value, CultureInfo.InvariantCulture) : fallback;

    /// <summary>Requires exactly one duration specification, avoiding ambiguous hours-plus-days combinations.</summary>
    /// <param name="options">Parsed options.</param>
    /// <returns>Validated unit and amount.</returns>
    private static (string Unit, int Amount) Duration(Dictionary<string, string> options)
    {
        string[] keys = ["--hours", "--days", "--years", "--permanent"];
        string[] selected = keys.Where(options.ContainsKey).ToArray();
        if (selected.Length != 1) throw new LicenseException("DURATION_INVALID", "Specify exactly one of --hours, --days, --years, --permanent.");
        string unit = selected[0][2..]; int amount = unit == "permanent" ? 0 : Number(options, selected[0], 0);
        LicenseDurations.Validate(unit, amount); return (unit, amount);
    }

    /// <summary>Shows the owner-only lifecycle and retry instructions without sample working credentials.</summary>
    private static void Help() => Console.WriteLine("OWNER ONLY — never distribute this program, authority data or owner connection to a customer.\n  init --data PRIVATE_DIR --url https://licenses.example/ [--password-file PRIVATE_FILE]\n  issue --connection OWNER_JSON --hours 6 --starts activation --devices 1 --label customer --out key.txt\n  issue --connection OWNER_JSON --days 7 --out key.txt\n  issue --connection OWNER_JSON --years 1 --out key.txt\n  issue --connection OWNER_JSON --permanent --out key.txt\n  retry-issue --connection OWNER_JSON --request key.txt.issue-request.json --out key.txt\n  list --connection OWNER_JSON [--out list.json]\n  suspend|resume|revoke|permanent --connection OWNER_JSON --id LICENSE_UUID\n  extend --connection OWNER_JSON --id LICENSE_UUID --days 30\n  reset-device --connection OWNER_JSON --id LICENSE_UUID --device DEVICE_SHA256\n  retry-change --connection OWNER_JSON --request SAVED_CHANGE_JSON\n  audit --connection OWNER_JSON --out audit.json\nTime: --starts issue|activation (default activation); optional --activate-before 2027-01-01T00:00:00Z.\nInitialization-only --development-loopback permits an isolated loopback test profile, not a customer release.\nRaw keys and passwords are never accepted as ordinary option values or printed by issue.");
}

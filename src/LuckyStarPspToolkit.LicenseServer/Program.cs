using LuckyStarPspToolkit.Licensing;
using LuckyStarPspToolkit.Licensing.Authority;

return await ServerProgram.RunAsync(args);

/// <summary>Owner-only server process. It accepts no public bind address and logs no activation credentials.</summary>
internal static class ServerProgram
{
    /// <summary>Loads the private authority, starts isolated loopback listeners and waits for graceful cancellation.</summary>
    /// <param name="args">--data directory --password-file file, with optional --public-port/--admin-port.</param>
    /// <returns>Zero after graceful shutdown or nonzero on startup failure.</returns>
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--help")
            { Console.WriteLine("lsp-license-server --data PRIVATE_DIR --password-file PRIVATE_FILE [--public-port 17840 --admin-port 17841] [--web-root COMPILED_FRONTEND --web-port 17842]"); return 0; }
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length || args[i] is not ("--data" or "--password-file" or "--public-port" or "--admin-port" or "--web-root" or "--web-port") || !values.TryAdd(args[i], args[i + 1]))
                    throw new LicenseException("USAGE", "Invalid server option.");
            }
            if (!values.TryGetValue("--data", out string? directory) || !values.TryGetValue("--password-file", out string? passwordFile))
                throw new LicenseException("USAGE", "A private data directory and passphrase file are required.");
            string password = System.Text.Encoding.UTF8.GetString(PrivateFiles.Read(passwordFile, 4096)).TrimEnd('\r', '\n');
            using var authority = new LicenseAuthority(directory, password);
            int publicPort = int.Parse(values.GetValueOrDefault("--public-port", "17840"), System.Globalization.CultureInfo.InvariantCulture);
            int adminPort = int.Parse(values.GetValueOrDefault("--admin-port", "17841"), System.Globalization.CultureInfo.InvariantCulture);
            await using var server = new LicenseHttpServer(authority, publicPort, adminPort);
            int webPort = int.Parse(values.GetValueOrDefault("--web-port", "17842"), System.Globalization.CultureInfo.InvariantCulture);
            if (values.ContainsKey("--web-port") && !values.ContainsKey("--web-root")) throw new LicenseException("WEB_CONFIG", "--web-port requires --web-root.");
            if (values.ContainsKey("--web-root") && (webPort == publicPort || webPort == adminPort)) throw new LicenseException("WEB_PORT", "All three listener ports must differ.");
            await using var web = values.TryGetValue("--web-root", out string? webRoot) ? new LicenseWebServer(authority, directory, webRoot, webPort) : null;
            server.Start(); web?.Start();
            using var shutdown = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
            using var sigterm = OperatingSystem.IsWindows() ? null : System.Runtime.InteropServices.PosixSignalRegistration.Create(
                System.Runtime.InteropServices.PosixSignal.SIGTERM, context => { context.Cancel = true; shutdown.Cancel(); });
            Console.WriteLine($"License authority ready. Public backend=127.0.0.1:{publicPort}; private admin=127.0.0.1:{adminPort}; browser={(web is null ? "disabled" : "127.0.0.1:" + webPort)}. TLS proxy is required.");
            try
            {
                Task wait = Task.Delay(Timeout.Infinite, shutdown.Token);
                Task completed = await Task.WhenAny(wait, server.Completion, web?.Completion ?? wait).ConfigureAwait(false);
                await completed.ConfigureAwait(false);
                if (!shutdown.IsCancellationRequested) throw new LicenseException("LISTENER_STOPPED", "License listeners stopped unexpectedly.");
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
            return 0;
        }
        catch (LicenseException ex) { Console.Error.WriteLine($"license-server [{ex.Code}]: {ex.Message}"); return 77; }
        catch (Exception) { Console.Error.WriteLine("license-server: startup failed. Check private paths, passphrase, database integrity and port permissions."); return 70; }
    }
}

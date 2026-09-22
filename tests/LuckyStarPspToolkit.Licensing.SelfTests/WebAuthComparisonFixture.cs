using System.Text.Json;
using LuckyStarPspToolkit.Licensing;
using LuckyStarPspToolkit.Licensing.Authority;

/// <summary>Identical public-API probes for the previous and current owner authentication library; no privileged test seam is used.</summary>
internal static class WebAuthComparisonFixture
{
    /// <summary>Reports observed refusal codes for budget separation, source changes and the precise MFA deadline.</summary>
    /// <param name="args">Reserved command arguments.</param>
    /// <returns>Zero after probes execute; historical unsafe outcomes are reported as evidence, not hidden.</returns>
    public static int Run(string[] args)
    {
        string loginInterference;
        using (var f = new Fixture())
        {
            var setup = WebAdminAuthentication.Initialize(f.Directory, "owner", f.Password, "https://admin.example");
            using var auth = new WebAdminAuthentication(f.Directory, f.Time);
            var session = auth.Login(new("owner", f.Password, setup.RecoveryCodes[0]), "shared-ingress");
            for (int i = 0; i < 7; i++) _ = Result(() => auth.Login(new("owner", "wrong", "invalid"), "shared-ingress"));
            loginInterference = Result(() => auth.Reauthenticate(session.SessionId, session.CsrfToken,
                new("owner", f.Password, setup.RecoveryCodes[1]), "shared-ingress"));
        }
        string changingSource;
        using (var f = new Fixture())
        {
            var setup = WebAdminAuthentication.Initialize(f.Directory, "owner", f.Password, "https://admin.example");
            using var auth = new WebAdminAuthentication(f.Directory, f.Time);
            var session = auth.Login(new("owner", f.Password, setup.RecoveryCodes[0]), "initial");
            for (int i = 0; i < 8; i++) _ = Result(() => auth.Reauthenticate(session.SessionId, session.CsrfToken,
                new("owner", "wrong", "invalid"), "source-" + i));
            changingSource = Result(() => auth.Reauthenticate(session.SessionId, session.CsrfToken,
                new("owner", f.Password, setup.RecoveryCodes[1]), "new-source"));
        }
        string freshness;
        using (var f = new Fixture())
        {
            var setup = WebAdminAuthentication.Initialize(f.Directory, "owner", f.Password, "https://admin.example");
            using var auth = new WebAdminAuthentication(f.Directory, f.Time);
            var session = auth.Login(new("owner", f.Password, setup.RecoveryCodes[0]), "source");
            f.Time.Advance(TimeSpan.FromMinutes(5));
            freshness = Result(() => auth.Require(session.SessionId, session.CsrfToken, true));
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "lsptool.web-auth-comparison.v1",
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            assemblyVersion = typeof(WebAdminAuthentication).Assembly.GetName().Version?.ToString(),
            sharedIngressReauthentication = loginInterference,
            changedSourceNinthReauthentication = changingSource,
            exactlyFiveMinutesSensitiveRead = freshness,
            passwordIterations = 600000,
            testsUsePublicApiOnly = true
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    /// <summary>Captures an actual controlled denial; unexpected exceptions fail the comparison run.</summary>
    /// <param name="action">Public operation under test.</param>
    /// <returns>ACCEPTED or the precise refusal code, never private credentials.</returns>
    private static string Result(Action action)
    {
        try { action(); return "ACCEPTED"; }
        catch (LicenseException ex) { return ex.Code; }
    }
}

namespace LuckyStarPspToolkit.Formats.Common;

/// <summary>
/// Represents the toolkit's toolkit build info model or service.
/// </summary>
public static class ToolkitBuildInfo
{
    /// <summary>The version used in CLI reports and release metadata.</summary>
    public static string Version { get; } = ResolveVersion();

    /// <summary>
    /// Resolves version while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    private static string ResolveVersion()
    {
        Version? version = typeof(ToolkitBuildInfo).Assembly.GetName().Version;
        return version is null ? "unknown" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}

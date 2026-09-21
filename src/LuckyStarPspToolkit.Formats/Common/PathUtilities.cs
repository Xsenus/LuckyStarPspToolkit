namespace LuckyStarPspToolkit.Formats.Common;

/// <summary>
/// Provides the toolkit's path utilities workflow.
/// </summary>
public static class PathUtilities
{
    /// <summary>The case-aware comparer for normalized host filesystem paths.</summary>
    public static StringComparer FileSystemComparer { get; } = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    /// <summary>The host case-sensitivity rule for normalized path prefix checks.</summary>
    public static StringComparison FileSystemComparison { get; } = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>
    /// Returns an absolute filesystem path without a trailing separator, except for a filesystem root.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    /// <summary>
    /// Normalizes a path and checks every existing ancestor from its filesystem root before protected I/O.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <param name="code">The code value.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    public static string NormalizeProtectedPath(string path, string code)
    {
        string normalized = Normalize(path);
        string? root = Path.GetPathRoot(normalized);
        if (string.IsNullOrEmpty(root))
        {
            throw new ToolkitException(code, $"Cannot determine filesystem root for path: {path}");
        }
        RejectExistingReparsePoints(Normalize(root), normalized, code);
        return normalized;
    }

    /// <summary>
    /// Compares normalized path spellings using the host platform case rules; it does not compare hard-link identities.
    /// </summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <returns><see langword="true"/> when the condition is satisfied; otherwise <see langword="false"/>.</returns>
    public static bool AreSame(string left, string right)
        => FileSystemComparer.Equals(Normalize(left), Normalize(right));

    /// <summary>
    /// Checks normalized path equality or containment with a separator boundary, including filesystem roots.
    /// </summary>
    /// <param name="candidate">The candidate value.</param>
    /// <param name="root">The root value.</param>
    /// <returns><see langword="true"/> when the condition is satisfied; otherwise <see langword="false"/>.</returns>
    public static bool IsWithinOrSame(string candidate, string root)
    {
        string normalizedCandidate = Normalize(candidate);
        string normalizedRoot = Normalize(root);
        if (FileSystemComparer.Equals(normalizedCandidate, normalizedRoot))
        {
            return true;
        }
        string prefix = Path.EndsInDirectorySeparator(normalizedRoot)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(prefix, FileSystemComparison);
    }

    /// <summary>
    /// Rejects an output path whose normalized spelling equals a protected input path.
    /// </summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <param name="code">The code value.</param>
    /// <param name="message">The diagnostic message used when validation fails.</param>
    public static void RequireDifferent(string left, string right, string code, string message)
    {
        if (AreSame(left, right))
        {
            throw new ToolkitException(code, message);
        }
    }

    /// <summary>
    /// Resolves a portable relative manifest path under its root, rejecting traversal, drive syntax and existing links.
    /// </summary>
    /// <param name="root">The root value.</param>
    /// <param name="relative">The relative value.</param>
    /// <param name="code">The code value.</param>
    /// <returns>The resulting text, path, identifier, or hexadecimal digest.</returns>
    public static string ResolveContainedPath(string root, string relative, string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(relative);
        if (Path.IsPathRooted(relative) || relative.StartsWith('\\')
            || relative.Contains(':')
            || relative.Split(['/', '\\']).Contains("..", StringComparer.Ordinal))
        {
            throw new ToolkitException(code, $"Path must be relative: {relative}");
        }

        string normalizedRoot = NormalizeProtectedPath(root, code);
        string portableRelative = relative.Replace('\\', Path.DirectorySeparatorChar);
        string candidate = Path.GetFullPath(Path.Combine(normalizedRoot, portableRelative));
        string prefix = Path.EndsInDirectorySeparator(normalizedRoot)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, FileSystemComparison) && !FileSystemComparer.Equals(candidate, normalizedRoot))
        {
            throw new ToolkitException(code, $"Path escapes its root directory: {relative}");
        }

        RejectExistingReparsePoints(normalizedRoot, candidate, code);
        return candidate;
    }

    /// <summary>
    /// Checks each existing ancestor between the root and candidate for symbolic links or reparse points.
    /// </summary>
    /// <param name="root">The root value.</param>
    /// <param name="candidate">The candidate value.</param>
    /// <param name="code">The code value.</param>
    private static void RejectExistingReparsePoints(string root, string candidate, string code)
    {
        CheckReparsePoint(root, code);
        string relative = Path.GetRelativePath(root, candidate);
        if (relative == ".")
        {
            return;
        }

        string current = root;
        foreach (string part in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            CheckReparsePoint(current, code);
        }
    }

    /// <summary>
    /// Queries an existing directory entry directly and rejects symbolic links or Windows reparse points.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <param name="code">The code value.</param>
    private static void CheckReparsePoint(string path, string code)
    {
        FileAttributes attributes;
        try
        {
            // Query the entry itself; File.Exists hides dangling links and access failures.
            attributes = File.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new ToolkitException(code, $"Symbolic links and reparse points are not accepted in protected paths: {path}");
        }
    }
}

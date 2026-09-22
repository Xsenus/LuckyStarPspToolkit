using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;

namespace LuckyStarPspToolkit.Licensing;

/// <summary>Bounded, no-symlink, same-directory atomic storage for licensing state and owner credentials.</summary>
public static class PrivateFiles
{
    /// <summary>Rejects symbolic links/reparse points in every existing ancestor.</summary>
    /// <param name="path">File or directory to validate.</param>
    /// <returns>Absolute normalized path.</returns>
    public static string SafePath(string path)
    {
        string absolute = Path.GetFullPath(path);
        string? cursor = absolute;
        while (cursor is not null)
        {
            try
            {
                if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    throw new LicenseException("STATE_LINK", "License paths must not use symbolic links.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            cursor = Path.GetDirectoryName(cursor);
        }
        return absolute;
    }

    /// <summary>Creates a private directory: Unix 0700, or a current-user/LocalSystem Windows ACL for newly created directories.</summary>
    /// <param name="path">State directory.</param>
    public static void Directory(string path)
    {
        string absolute = SafePath(path);
        if (OperatingSystem.IsWindows())
        {
            bool existed = System.IO.Directory.Exists(absolute);
            System.IO.Directory.CreateDirectory(absolute);
            if (!existed) new DirectoryInfo(absolute).SetAccessControl((DirectorySecurity)WindowsSecurity(true));
        }
        else System.IO.Directory.CreateDirectory(absolute, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        _ = SafePath(absolute);
        // Existing directories are not chmod-ed: writing a key must not change the permissions of an unrelated parent.
    }

    /// <summary>Reads a single validated file snapshot with a strict pre-allocation bound.</summary>
    /// <param name="path">State file.</param>
    /// <param name="limit">Maximum bytes.</param>
    /// <returns>The complete snapshot.</returns>
    public static byte[] Read(string path, int limit = 32768)
    {
        using var stream = new FileStream(SafePath(path), FileMode.Open, FileAccess.Read, FileShare.Read);
        long length = stream.Length;
        if (length <= 0 || length > limit) throw new LicenseException("STATE_LIMIT", "Invalid license state size.");
        byte[] bytes = new byte[(int)length];
        stream.ReadExactly(bytes);
        if (stream.Length != length || stream.ReadByte() != -1) throw new LicenseException("STATE_CHANGED", "License state changed during reading.");
        return bytes;
    }

    /// <summary>Writes through a new private temporary file and renames only after flushing; existing state is never truncated first.</summary>
    /// <param name="path">Destination file.</param>
    /// <param name="bytes">Complete validated snapshot.</param>
    /// <param name="overwrite">False for initialization or one-time issued credentials.</param>
    public static void Write(string path, ReadOnlySpan<byte> bytes, bool overwrite = true)
    {
        path = SafePath(path);
        string parent = Path.GetDirectoryName(path)!;
        Directory(parent);
        string temp = Path.Combine(parent, ".lsp-" + LicenseCrypto.Encode(RandomNumberGenerator.GetBytes(12)) + ".tmp");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write,
                Share = FileShare.None, Options = FileOptions.WriteThrough };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temp, options))
            {
                // Protect the empty Windows file before any credential bytes are written.
                if (OperatingSystem.IsWindows()) new FileInfo(temp).SetAccessControl((FileSecurity)WindowsSecurity(false));
                stream.Write(bytes); stream.Flush(true);
            }
            _ = SafePath(path);
            File.Move(temp, path, overwrite);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    /// <summary>Creates a non-inherited Windows ACL allowing only the current identity and LocalSystem.</summary>
    /// <param name="directory">Whether child inheritance flags are needed for a newly created private directory.</param>
    /// <returns>Directory or file security descriptor; privileged administrators remain outside this protection boundary.</returns>
    [SupportedOSPlatform("windows")]
    private static FileSystemSecurity WindowsSecurity(bool directory)
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier user = identity.User ?? throw new LicenseException("STATE_IDENTITY", "A Windows user SID is required.");
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        FileSystemSecurity security = directory ? new DirectorySecurity() : new FileSecurity();
        security.SetAccessRuleProtection(true, false);
        security.SetOwner(user);
        InheritanceFlags inheritance = directory ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit : InheritanceFlags.None;
        foreach (SecurityIdentifier sid in new[] { user, system })
        {
            var rule = new FileSystemAccessRule(sid, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow);
            if (directory) ((DirectorySecurity)security).AddAccessRule(rule);
            else ((FileSecurity)security).AddAccessRule(rule);
        }
        return security;
    }

    /// <summary>Acquires a process-lifetime exclusive lock without retaining credential contents in the lock file.</summary>
    /// <param name="path">Lock path in the private directory.</param>
    /// <returns>Disposable exclusive file handle.</returns>
    public static FileStream Lock(string path)
    {
        path = SafePath(path);
        Directory(Path.GetDirectoryName(path)!);
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            return new FileStream(path, options);
        }
        catch (IOException) { throw new LicenseException("STATE_BUSY", "Another process owns this licensing state."); }
    }
}

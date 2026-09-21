namespace LuckyStarPspToolkit.Formats.Common;

/// <summary>
/// Represents immutable atomic write request data exchanged by the toolkit.
/// </summary>
/// <param name="Path">The path value used by this model or operation.</param>
/// <param name="Data">The byte payload associated with this record.</param>
public sealed record AtomicWriteRequest(string Path, ReadOnlyMemory<byte> Data);

/// <summary>
/// Represents the toolkit's atomic file model or service.
/// </summary>
public static class AtomicFile
{
    /// <summary>
    /// Writes all bytes while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <param name="data">The binary data to process.</param>
    public static void WriteAllBytes(string path, ReadOnlySpan<byte> data)
        => WriteAll([new AtomicWriteRequest(path, data.ToArray())]);

    /// <summary>
    /// Writes all text while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <param name="value">The value to process.</param>
    public static void WriteAllText(string path, string value)
        => WriteAllBytes(path, BinaryUtilities.Utf8(value));

    /// <summary>
    /// Writes a file through a staged stream and atomically replaces the destination after a durable flush.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <param name="writer">The callback that writes the staged output stream.</param>
    public static void WriteStream(string path, Action<Stream> writer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(writer);

        string target = PathUtilities.NormalizeProtectedPath(path, "ATOMIC_REPARSE_POINT");
        if (Directory.Exists(target))
        {
            throw new ToolkitException("ATOMIC_TARGET_DIRECTORY", $"Atomic write target is a directory: {target}");
        }

        string directory = Path.GetDirectoryName(target)
            ?? throw new ToolkitException("ATOMIC_DIRECTORY", $"Cannot determine output directory for {target}.");
        Directory.CreateDirectory(directory);
        _ = PathUtilities.NormalizeProtectedPath(directory, "ATOMIC_REPARSE_POINT");

        string token = Guid.NewGuid().ToString("N");
        string temporary = Path.Combine(directory, $".{Path.GetFileName(target)}.{token}.tmp");
        string backup = Path.Combine(directory, $".{Path.GetFileName(target)}.{token}.bak");
        bool backupCreated = false;
        bool committed = false;
        bool preserveBackup = false;
        try
        {
            using (FileStream stream = new(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.ReadWrite,
                       FileShare.None,
                       1024 * 1024,
                       FileOptions.WriteThrough))
            {
                writer(stream);
                stream.Flush(true);
            }

            if (File.Exists(target))
            {
                File.Move(target, backup);
                backupCreated = true;
            }
            File.Move(temporary, target);
            committed = true;

            if (backupCreated && File.Exists(backup))
            {
                File.Delete(backup);
                backupCreated = false;
            }
        }
        catch (Exception original)
        {
            try
            {
                if (committed && File.Exists(target))
                {
                    File.Delete(target);
                }
                if (backupCreated && File.Exists(backup))
                {
                    File.Move(backup, target, true);
                    backupCreated = false;
                }
            }
            catch (Exception cleanup)
            {
                preserveBackup = backupCreated && File.Exists(backup);
                throw new AggregateException(
                    "Atomic stream write failed and the original target could not be restored. Any surviving .bak file has been preserved.",
                    original,
                    cleanup);
            }
            throw;
        }
        finally
        {
            TryDelete(temporary);
            if (!preserveBackup)
            {
                TryDelete(backup);
            }
        }
    }

    /// <summary>
    /// Commits a set of file writes as one best-effort transactional operation with rollback.
    /// </summary>
    /// <param name="requests">The complete set of transactional write requests.</param>
    public static void WriteAll(params AtomicWriteRequest[] requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Length == 0)
        {
            return;
        }

        var staged = new List<StagedWrite>(requests.Length);
        HashSet<string> targets = new(PathUtilities.FileSystemComparer);
        Exception? rollbackFailure = null;
        try
        {
            foreach (AtomicWriteRequest request in requests)
            {
                ArgumentNullException.ThrowIfNull(request);
                ArgumentException.ThrowIfNullOrWhiteSpace(request.Path);
                string target = PathUtilities.NormalizeProtectedPath(request.Path, "ATOMIC_REPARSE_POINT");
                if (!targets.Add(target))
                {
                    throw new ToolkitException("ATOMIC_DUPLICATE_TARGET", $"Atomic write contains duplicate target: {target}");
                }
                if (Directory.Exists(target))
                {
                    throw new ToolkitException("ATOMIC_TARGET_DIRECTORY", $"Atomic write target is a directory: {target}");
                }

                string directory = Path.GetDirectoryName(target)
                    ?? throw new ToolkitException("ATOMIC_DIRECTORY", $"Cannot determine output directory for {target}.");
                Directory.CreateDirectory(directory);
                _ = PathUtilities.NormalizeProtectedPath(directory, "ATOMIC_REPARSE_POINT");
                string token = Guid.NewGuid().ToString("N");
                string temporary = Path.Combine(directory, $".{Path.GetFileName(target)}.{token}.tmp");
                string backup = Path.Combine(directory, $".{Path.GetFileName(target)}.{token}.bak");
                WriteStagingFile(temporary, request.Data.Span);
                staged.Add(new StagedWrite(target, temporary, backup));
            }

            foreach (StagedWrite item in staged)
            {
                if (File.Exists(item.Target))
                {
                    File.Move(item.Target, item.Backup);
                    item.BackupCreated = true;
                }
                File.Move(item.Temporary, item.Target);
                item.Committed = true;
            }

            foreach (StagedWrite item in staged)
            {
                if (item.BackupCreated && File.Exists(item.Backup))
                {
                    File.Delete(item.Backup);
                }
                item.BackupCreated = false;
            }
        }
        catch (Exception original)
        {
            for (int index = staged.Count - 1; index >= 0; index--)
            {
                StagedWrite item = staged[index];
                try
                {
                    if (item.Committed && File.Exists(item.Target))
                    {
                        File.Delete(item.Target);
                    }
                    item.Committed = false;
                    if (item.BackupCreated && File.Exists(item.Backup))
                    {
                        File.Move(item.Backup, item.Target, true);
                        item.BackupCreated = false;
                    }
                }
                catch (Exception cleanup)
                {
                    item.PreserveBackup = item.BackupCreated && File.Exists(item.Backup);
                    rollbackFailure ??= cleanup;
                }
            }

            if (rollbackFailure is not null)
            {
                throw new AggregateException(
                    "Atomic write failed and at least one original target could not be restored. Any surviving .bak file has been preserved.",
                    original,
                    rollbackFailure);
            }
            throw;
        }
        finally
        {
            foreach (StagedWrite item in staged)
            {
                TryDelete(item.Temporary);
                if (!item.PreserveBackup)
                {
                    TryDelete(item.Backup);
                }
            }
        }
    }

    /// <summary>
    /// Writes staging file while enforcing the relevant format and safety invariants.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    /// <param name="data">The binary data to process.</param>
    private static void WriteStagingFile(string path, ReadOnlySpan<byte> data)
    {
        using FileStream stream = new(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.WriteThrough);
        stream.Write(data);
        stream.Flush(true);
    }

    /// <summary>
    /// Attempts to delete.
    /// </summary>
    /// <param name="path">The file-system path to process.</param>
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Cleanup is best effort and must not hide the primary result.
        }
    }

    /// <summary>
    /// Represents the toolkit's staged write model or service.
    /// </summary>
    /// <param name="target">The target value used by this model or operation.</param>
    /// <param name="temporary">The temporary value used by this model or operation.</param>
    /// <param name="backup">The backup value used by this model or operation.</param>
    private sealed class StagedWrite(string target, string temporary, string backup)
    {
        /// <summary>The target value used by this model or operation.</summary>
        public string Target { get; } = target;
        /// <summary>The temporary value used by this model or operation.</summary>
        public string Temporary { get; } = temporary;
        /// <summary>The backup value used by this model or operation.</summary>
        public string Backup { get; } = backup;
        /// <summary>The backup created value used by this model or operation.</summary>
        public bool BackupCreated { get; set; }
        /// <summary>The committed value used by this model or operation.</summary>
        public bool Committed { get; set; }
        /// <summary>The preserve backup value used by this model or operation.</summary>
        public bool PreserveBackup { get; set; }
    }
}

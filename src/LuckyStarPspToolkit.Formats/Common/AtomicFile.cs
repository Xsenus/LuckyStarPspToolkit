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

            // Commit has succeeded. Failure to delete an obsolete backup must not undo it.
            TryDelete(backup);
            backupCreated = false;
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
        => WriteAllCore(requests, File.Delete);

    /// <summary>Runs the same write transaction with an injected cleanup operation for deterministic I/O-failure testing.</summary>
    /// <param name="requests">The output paths and byte snapshots to commit as a group.</param>
    /// <param name="deleteBackup">Deletes obsolete backups after commit; I/O failures preserve the backup without rolling back committed outputs.</param>
    internal static void WriteAllCore(AtomicWriteRequest[] requests, Action<string> deleteBackup)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(deleteBackup);
        if (requests.Length == 0)
        {
            return;
        }

        // Validate the complete target set before creating even the first staging directory.
        AtomicWriteRequest[] normalized = new AtomicWriteRequest[requests.Length];
        HashSet<string> targets = new(PathUtilities.FileSystemComparer);
        for (int index = 0; index < requests.Length; index++)
        {
            AtomicWriteRequest request = requests[index];
            ArgumentNullException.ThrowIfNull(request);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Path);
            string target = PathUtilities.NormalizeProtectedPath(request.Path, "ATOMIC_REPARSE_POINT");
            if (!targets.Add(target)) throw new ToolkitException("ATOMIC_DUPLICATE_TARGET", $"Atomic write contains duplicate target: {target}");
            if (Directory.Exists(target)) throw new ToolkitException("ATOMIC_TARGET_DIRECTORY", $"Atomic write target is a directory: {target}");
            normalized[index] = request with { Path = target };
        }
        foreach (string target in targets)
        {
            string? ancestor = Path.GetDirectoryName(target);
            while (ancestor is not null)
            {
                if (targets.Contains(ancestor)) throw new ToolkitException("ATOMIC_TARGET_OVERLAP", $"Output file is also an output ancestor: {ancestor}");
                ancestor = Path.GetDirectoryName(ancestor);
            }
        }
        var staged = new List<StagedWrite>(requests.Length);
        Exception? rollbackFailure = null;
        bool allCommitted = false;
        try
        {
            foreach (AtomicWriteRequest request in normalized)
            {
                ArgumentNullException.ThrowIfNull(request);
                ArgumentException.ThrowIfNullOrWhiteSpace(request.Path);
                string target = PathUtilities.NormalizeProtectedPath(request.Path, "ATOMIC_REPARSE_POINT");
                string directory = Path.GetDirectoryName(target)
                    ?? throw new ToolkitException("ATOMIC_DIRECTORY", $"Cannot determine output directory for {target}.");
                Directory.CreateDirectory(directory);
                _ = PathUtilities.NormalizeProtectedPath(directory, "ATOMIC_REPARSE_POINT");
                string token = Guid.NewGuid().ToString("N");
                string temporary = Path.Combine(directory, $".{Path.GetFileName(target)}.{token}.tmp");
                string backup = Path.Combine(directory, $".{Path.GetFileName(target)}.{token}.bak");
                // Register before writing: a disk-full error can leave a partially written temporary file.
                staged.Add(new StagedWrite(target, temporary, backup));
                WriteStagingFile(temporary, request.Data.Span);
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

            // This is the commit boundary. Once cleanup starts an old backup may already
            // be gone; rolling back after that point would destroy both old and new data.
            allCommitted = true;
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
                if (allCommitted && item.BackupCreated)
                {
                    TryDeleteBackup(item.Backup, deleteBackup);
                }
                else if (!item.PreserveBackup)
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

    /// <summary>Removes an obsolete backup without interpreting post-commit cleanup failure as transaction failure.</summary>
    /// <param name="path">The backup owned by the successful transaction.</param>
    /// <param name="deleteBackup">The filesystem deletion operation; injectable only inside the assembly.</param>
    private static void TryDeleteBackup(string path, Action<string> deleteBackup)
    {
        try
        {
            if (File.Exists(path)) deleteBackup(path);
        }
        catch (IOException) { /* A stale .bak is safer than losing the committed file. */ }
        catch (UnauthorizedAccessException) { /* The caller may clean up the preserved backup later. */ }
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

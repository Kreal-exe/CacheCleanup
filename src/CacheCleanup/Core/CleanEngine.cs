using System.Diagnostics;

namespace CacheCleanup.Core;

public sealed class CleanOptions
{
    public bool UseRecycleBin { get; init; } = true;
    public bool DryRun { get; init; }
}

public sealed record CleanProgress(
    string TargetId,
    bool TargetDone,
    long BytesFreed,
    int FilesDeleted,
    int Failed,
    int TargetsDone,
    int TargetsTotal,
    TargetCleanResult? TargetResult);

public sealed class TargetCleanResult
{
    public required string TargetId { get; init; }
    public long BytesFreed { get; set; }
    public long BytesRemaining { get; set; }
    public int FilesDeleted { get; set; }
    public int Failed { get; set; }
    public string? Error { get; set; }
}

public sealed class CleanReport
{
    public long BytesFreed { get; set; }
    public int FilesDeleted { get; set; }
    public int Failed { get; set; }
    public TimeSpan Elapsed { get; set; }
    public Dictionary<string, TargetCleanResult> Targets { get; } = new(StringComparer.Ordinal);
}

/// <summary>Deletes scanned files, either into the Recycle Bin or permanently, never leaving the scanned roots.</summary>
public sealed class CleanEngine
{
    private const int RecycleBatchSize = 200;

    public async Task<CleanReport> CleanAsync(
        IReadOnlyList<TargetScanResult> targets,
        CleanOptions options,
        IProgress<CleanProgress>? progress,
        CancellationToken ct)
    {
        var report = new CleanReport();
        var sw = Stopwatch.StartNew();

        for (var i = 0; i < targets.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var scan = targets[i];
            var index = i;

            var result = await Task.Run(() => CleanTarget(scan, options, (freed, deleted, failed) =>
                progress?.Report(new CleanProgress(scan.Target.Id, false,
                    report.BytesFreed + freed, report.FilesDeleted + deleted, report.Failed + failed,
                    index, targets.Count, null)), ct), ct);

            report.BytesFreed += result.BytesFreed;
            report.FilesDeleted += result.FilesDeleted;
            report.Failed += result.Failed;
            report.Targets[scan.Target.Id] = result;

            progress?.Report(new CleanProgress(scan.Target.Id, true,
                report.BytesFreed, report.FilesDeleted, report.Failed, index + 1, targets.Count, result));
        }

        report.Elapsed = sw.Elapsed;
        return report;
    }

    public TargetCleanResult CleanTarget(TargetScanResult scan, CleanOptions options, Action<long, int, int>? onProgress, CancellationToken ct)
    {
        var result = new TargetCleanResult { TargetId = scan.Target.Id };

        try
        {
            if (scan.Target.Kind == TargetKind.RecycleBin)
            {
                if (options.DryRun || RecycleBinNative.Empty())
                {
                    result.BytesFreed = scan.TotalBytes;
                    result.FilesDeleted = scan.FileCount;
                }
                else
                {
                    result.Failed = scan.FileCount;
                    result.BytesRemaining = scan.TotalBytes;
                }
                return result;
            }

            var roots = scan.Roots;
            var safeFiles = new List<FileEntry>(scan.Files.Count);
            foreach (var file in scan.Files)
            {
                if (PathGuard.IsInside(file.Path, roots)) safeFiles.Add(file);
                else result.Failed++;
            }

            if (options.DryRun)
            {
                result.BytesFreed = safeFiles.Sum(f => f.Length);
                result.FilesDeleted = safeFiles.Count;
                return result;
            }

            var lastReport = Stopwatch.StartNew();
            void Report()
            {
                if (lastReport.ElapsedMilliseconds < 40) return;
                lastReport.Restart();
                onProgress?.Invoke(result.BytesFreed, result.FilesDeleted, result.Failed);
            }

            if (options.UseRecycleBin)
            {
                foreach (var batch in safeFiles.Chunk(RecycleBatchSize))
                {
                    ct.ThrowIfCancellationRequested();
                    RecycleBinNative.MoveToRecycleBin(batch.Select(f => f.Path).ToList());

                    // The shell does not tell us which files it skipped, so verify each one.
                    foreach (var file in batch)
                    {
                        if (File.Exists(file.Path))
                        {
                            result.Failed++;
                            result.BytesRemaining += file.Length;
                        }
                        else
                        {
                            result.FilesDeleted++;
                            result.BytesFreed += file.Length;
                        }
                    }

                    Report();
                }
            }
            else
            {
                foreach (var file in safeFiles)
                {
                    ct.ThrowIfCancellationRequested();
                    if (DeleteFile(file.Path))
                    {
                        result.FilesDeleted++;
                        result.BytesFreed += file.Length;
                    }
                    else
                    {
                        result.Failed++;
                        result.BytesRemaining += file.Length;
                    }

                    Report();
                }
            }

            foreach (var root in roots)
            {
                PruneEmptyDirectories(root, ct);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        onProgress?.Invoke(result.BytesFreed, result.FilesDeleted, result.Failed);
        return result;
    }

    private static bool DeleteFile(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            try
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
                return true;
            }
            catch
            {
                return false;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Removes directories under <paramref name="root"/> that are empty after cleaning. The root itself is kept.</summary>
    private static void PruneEmptyDirectories(string root, CancellationToken ct)
    {
        List<string> dirs;
        try
        {
            dirs = Directory.EnumerateDirectories(root, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            }).ToList();
        }
        catch
        {
            return;
        }

        // Deepest first so parents become empty after their children are removed.
        dirs.Sort((a, b) => b.Length.CompareTo(a.Length));
        foreach (var dir in dirs)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir, false);
            }
            catch
            {
                // in use or protected; leave it
            }
        }
    }
}

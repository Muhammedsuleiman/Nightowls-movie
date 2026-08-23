using System.IO;
using LibVLCSharp.Shared;
using NightOwls.Models;
using NightOwls.Repositories;

namespace NightOwls.Services;

public class LibraryScanner
{
    private readonly LibraryRepository _repo;
    private readonly LibVlcHost _vlc;
    private static readonly string[] VideoExtensions =
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm",
        ".m4v", ".mpeg", ".mpg", ".3gp", ".ts"
    };
    private static readonly HashSet<string> SkipDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "$RECYCLE.BIN", "System Volume Information", "$WinREAgent", "WindowsApps",
        "AppData", "node_modules", ".git"
    };

    public event Action? LibraryChanged;

    public LibraryScanner(LibraryRepository repo, LibVlcHost vlc)
    {
        _repo = repo;
        _vlc = vlc;
    }

    public async Task ScanAllAsync(IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        await Task.Run(() => ScanCore(progress, ct, null), ct).ConfigureAwait(true);
        LibraryChanged?.Invoke();
    }

    public async Task ScanLocationAsync(int locationId, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        await Task.Run(() => ScanCore(progress, ct, locationId), ct).ConfigureAwait(true);
        LibraryChanged?.Invoke();
    }

    private void ScanCore(IProgress<ScanProgress>? progress, CancellationToken ct, int? onlyLocationId)
    {
        var locations = _repo.GetLocations();
        if (onlyLocationId.HasValue) locations = locations.Where(l => l.Id == onlyLocationId.Value).ToList();

        int addedTotal = 0, removedTotal = 0, foundTotal = 0;

        foreach (var loc in locations)
        {
            ct.ThrowIfCancellationRequested();
            string root = LibraryRepository.NormalizePath(loc.Path);

            if (!Directory.Exists(root))
            {
                if (!loc.IsUnavailable) _repo.SetLocationUnavailable(loc.Id, true);
                continue;
            }
            if (loc.IsUnavailable) _repo.SetLocationUnavailable(loc.Id, false);

            var filesOnDisk = new List<(string Path, long Size, DateTime ModifiedUtc)>();
            try
            {
                foreach (var file in EnumerateVideoFiles(root))
                {
                    ct.ThrowIfCancellationRequested();
                    FileInfo fi;
                    try { fi = new FileInfo(file.Path); } catch { continue; }
                    if (!fi.Exists) continue;
                    filesOnDisk.Add((fi.FullName.ToLowerInvariant(), fi.Length, fi.LastWriteTimeUtc));
                }
            }
            catch
            {
            }

            foundTotal += filesOnDisk.Count;
            progress?.Report(new ScanProgress
            {
                StatusText = $"Scanning {Path.GetFileName(root)}…",
                FilesFound = foundTotal,
                FilesAdded = addedTotal,
                FilesRemoved = removedTotal
            });

            var existing = _repo.GetAllMovies()
                .Where(m => m.Path.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                            || m.Path.Equals(root, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(m => m.Path.ToLowerInvariant(), m => m);

            var onDiskSet = filesOnDisk.Select(f => f.Path).ToHashSet();

            var toInsert = new List<Movie>();
            foreach (var f in filesOnDisk)
            {
                if (existing.TryGetValue(f.Path, out var known))
                {
                    var (freshTitle, freshYear) = TitleParser.Parse(System.IO.Path.GetFileName(f.Path));
                    bool metaChanged = known.FileSizeBytes != f.Size ||
                                       Math.Abs((known.LastModifiedUtc - f.ModifiedUtc).TotalSeconds) > 1;
                    bool titleChanged = !known.Title.Equals(freshTitle, StringComparison.Ordinal) ||
                                        known.Year != freshYear;
                    if (metaChanged || titleChanged)
                    {
                        known.FileSizeBytes = f.Size;
                        known.LastModifiedUtc = f.ModifiedUtc;
                        known.Title = freshTitle;
                        known.Year = freshYear;
                        _repo.UpdateMovieMetadata(known);
                        existing[f.Path] = known;
                    }
                }
                else
                {
                    var (title, year) = TitleParser.Parse(System.IO.Path.GetFileName(f.Path));
                    toInsert.Add(new Movie
                    {
                        Path = f.Path,
                        FileName = System.IO.Path.GetFileName(f.Path),
                        Title = title,
                        Year = year,
                        Extension = System.IO.Path.GetExtension(f.Path),
                        FileSizeBytes = f.Size,
                        LastModifiedUtc = f.ModifiedUtc,
                        DurationMs = 0,
                        LocationId = loc.Id,
                        DateAddedUtc = DateTime.UtcNow
                    });
                }
            }

            if (toInsert.Count > 0)
            {
                _repo.InsertMovies(toInsert);
                addedTotal += toInsert.Count;
            }

            var missing = existing.Values
                .Where(m => !onDiskSet.Contains(m.Path.ToLowerInvariant()))
                .Select(m => m.Path)
                .ToList();
            if (missing.Count > 0)
            {
                _repo.DeleteMoviesByPaths(missing);
                removedTotal += missing.Count;
            }

            progress?.Report(new ScanProgress
            {
                StatusText = $"Scanned {Path.GetFileName(root)}",
                FilesFound = foundTotal,
                FilesAdded = addedTotal,
                FilesRemoved = removedTotal
            });
        }

        progress?.Report(new ScanProgress
        {
            StatusText = "Reading durations…",
            FilesFound = foundTotal,
            FilesAdded = addedTotal,
            FilesRemoved = removedTotal,
            IsProbing = true
        });

        ProbeMissingDurationsAsync(progress, ct, addedTotal, removedTotal, foundTotal).GetAwaiter().GetResult();

        progress?.Report(new ScanProgress
        {
            StatusText = "Scan complete",
            FilesFound = foundTotal,
            FilesAdded = addedTotal,
            FilesRemoved = removedTotal
        });
    }

    private async Task ProbeMissingDurationsAsync(IProgress<ScanProgress>? progress, CancellationToken ct,
        int addedTotal, int removedTotal, int foundTotal)
    {
        var candidates = _repo.GetAllMovies().Where(m => m.DurationMs <= 0 && File.Exists(m.Path)).ToList();
        if (candidates.Count == 0) return;

        using var gate = new SemaphoreSlim(4);
        int done = 0;
        var tasks = candidates.Select(async movie =>
        {
            ct.ThrowIfCancellationRequested();
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                long durationMs = await GetDurationAsync(movie.Path, ct).ConfigureAwait(false);
                if (durationMs > 0) _repo.SetDurationIfEmpty(movie.Id, durationMs);
            }
            catch { }
            finally
            {
                gate.Release();
                Interlocked.Increment(ref done);
                progress?.Report(new ScanProgress
                {
                    StatusText = "Reading media information…",
                    FilesFound = foundTotal,
                    FilesAdded = addedTotal,
                    FilesRemoved = removedTotal,
                    IsProbing = true,
                    ProbedCount = Volatile.Read(ref done),
                    ProbeTotal = candidates.Count
                });
            }
        }).ToList();
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public async Task<long> GetDurationAsync(string path, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var media = _vlc.CreateMedia(path);
                var parsed = media.Parse(MediaParseOptions.ParseLocal, 15000, ct);
                parsed.Wait(TimeSpan.FromSeconds(16));
                if (media.Duration <= 0)
                {
                    // Retry without the cancellation token in case the token raced the parse.
                    using var retry = _vlc.CreateMedia(path);
                    var retryParse = retry.Parse(MediaParseOptions.ParseLocal, 15000, CancellationToken.None);
                    retryParse.Wait(TimeSpan.FromSeconds(16));
                    return retry.Duration;
                }
                return media.Duration;
            }
            catch { return 0; }
        }, ct).ConfigureAwait(false);
    }

    private static IEnumerable<(string Path, bool IsFile)> EnumerateVideoFiles(string root)
    {
        var pendingDirs = new Stack<string>();
        pendingDirs.Push(root);
        while (pendingDirs.Count > 0)
        {
            string dir = pendingDirs.Pop();
            FileSystemInfo[] entries;
            try
            {
                var info = new DirectoryInfo(dir);
                entries = info.Exists ? info.GetFileSystemInfos() : Array.Empty<FileSystemInfo>();
            }
            catch { continue; }

            var files = new List<string>();
            foreach (var entry in entries)
            {
                try
                {
                    if (entry is DirectoryInfo sub)
                    {
                        if (SkipDirectoryNames.Contains(sub.Name)) continue;
                        if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                        pendingDirs.Push(sub.FullName);
                    }
                    else
                    {
                        string ext = System.IO.Path.GetExtension(entry.Name);
                        if (VideoExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                            files.Add(entry.FullName);
                    }
                }
                catch { continue; }
            }

            foreach (var f in files)
                yield return (f, true);
        }
    }
}

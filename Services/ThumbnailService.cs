using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using LibVLCSharp.Shared;
using NightOwls.Repositories;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace NightOwls.Services;

public class ThumbnailService : IDisposable
{
    private const int ThumbWidth = 480;
    private const int ThumbHeight = 270;
    private static readonly string[] SubtitleExtensions = { ".srt", ".ass", ".ssa", ".sub", ".vtt" };

    private readonly LibVlcHost _vlc;
    private readonly LibraryRepository _repo;
    private readonly string _cacheDir;
    private readonly BlockingCollection<(int MovieId, string FilePath, string CacheKey)> _queue = new(boundedCapacity: 512);
    private readonly CancellationTokenSource _cts = new();
    private Thread? _worker;

    public event Action<int, string>? ThumbnailReady;

    public ThumbnailService(LibVlcHost vlc, LibraryRepository repo)
    {
        _vlc = vlc;
        _repo = repo;
        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NightOwls", "Thumbnails");
        Directory.CreateDirectory(_cacheDir);
    }

    public string CacheDirectory => _cacheDir;

    public static string ComputeCacheKey(string filePath, long sizeBytes, DateTime modifiedUtc)
    {
        string payload = $"{filePath.ToLowerInvariant()}|{sizeBytes}|{modifiedUtc.Ticks}";
        byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    public string? GetCachedThumbnailPath(string cacheKey)
    {
        string file = Path.Combine(_cacheDir, cacheKey + ".png");
        return File.Exists(file) ? file : null;
    }

    public void Request(int movieId, string filePath, long sizeBytes, DateTime modifiedUtc)
    {
        string key = ComputeCacheKey(filePath, sizeBytes, modifiedUtc);
        if (GetCachedThumbnailPath(key) is not null)
        {
            _repo.SetThumbnailFile(movieId, key + ".png");
            OnReady(movieId, key + ".png");
            return;
        }
        if (!_queue.IsAddingCompleted)
        {
            try { _queue.TryAdd((movieId, filePath, key)); } catch { }
            EnsureWorker();
        }
    }

    public void EnqueueRange(IEnumerable<(int MovieId, string Path, long Size, DateTime Modified)> items)
    {
        foreach (var it in items) Request(it.MovieId, it.Path, it.Size, it.Modified);
    }

    private readonly object _workerGate = new();

    private void EnsureWorker()
    {
        lock (_workerGate)
        {
            if (_worker is not null && _worker.IsAlive) return;
            _worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = "ThumbnailWorker"
            };
            _worker.Start();
        }
    }

    private void WorkerLoop()
    {
        try
        {
            foreach (var job in _queue.GetConsumingEnumerable(_cts.Token))
            {
                try
                {
                    string fileName = job.CacheKey + ".png";
                    string outFile = Path.Combine(_cacheDir, fileName);
                    bool ok = TryExtractSnapshot(job.FilePath, outFile);
                    if (ok)
                    {
                        _repo.SetThumbnailFile(job.MovieId, fileName);
                        OnReady(job.MovieId, fileName);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { }
                Thread.Sleep(50);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch { }
    }

    private void OnReady(int movieId, string fileName) =>
        ThumbnailReady?.Invoke(movieId, fileName);

    public bool TryExtractSnapshot(string videoPath, string outputPngPath)
    {
        IntPtr buffer = IntPtr.Zero;
        VlcMediaPlayer? player = null;
        Media? media = null;
        int frames = 0;
        try
        {
            media = _vlc.CreateMedia(videoPath);
            media.AddOption(":no-audio");
            media.AddOption(":avcodec-hw=none");
            media.AddOption(":no-snapshot-preview");

            player = new VlcMediaPlayer(media);

            var lockCb = new VlcMediaPlayer.LibVLCVideoLockCb((_, planes) =>
            {
                Marshal.WriteIntPtr(planes, buffer);
                return buffer;
            });
            var unlockCb = new VlcMediaPlayer.LibVLCVideoUnlockCb((_, _, _) => { });
            var displayCb = new VlcMediaPlayer.LibVLCVideoDisplayCb((_, _) => Interlocked.Increment(ref frames));
            var formatCb = new VlcMediaPlayer.LibVLCVideoFormatCb((ref IntPtr opaque, IntPtr chroma, ref uint w, ref uint h, ref uint pitches, ref uint lines) =>
            {
                byte[] fourCc = System.Text.Encoding.ASCII.GetBytes("RV32\0");
                Marshal.Copy(fourCc, 0, chroma, fourCc.Length);
                w = ThumbWidth;
                h = ThumbHeight;
                pitches = ThumbWidth * 4;
                lines = ThumbHeight;
                return 1u;
            });
            var cleanupCb = new VlcMediaPlayer.LibVLCVideoCleanupCb((ref IntPtr opaque) => { });

            player.SetVideoCallbacks(lockCb, unlockCb, displayCb);
            player.SetVideoFormatCallbacks(formatCb, cleanupCb);

            buffer = Marshal.AllocHGlobal(ThumbWidth * ThumbHeight * 4);

            if (!player.Play()) return false;

            int lastCount = 0;
            int stableRounds = 0;
            for (int i = 0; i < 160; i++)
            {
                Thread.Sleep(60);
                if (_cts.IsCancellationRequested) break;
                int current = Volatile.Read(ref frames);
                if (current >= 4) break;
                if (current == lastCount) stableRounds++; else stableRounds = 0;
                lastCount = current;
                if (stableRounds > 40 && i > 100) break;
            }

            if (Volatile.Read(ref frames) < 2) return false;

            byte[] pixels = new byte[ThumbWidth * ThumbHeight * 4];
            Marshal.Copy(buffer, pixels, 0, pixels.Length);

            for (int p = 0; p < pixels.Length; p += 4)
                (pixels[p], pixels[p + 2]) = (pixels[p + 2], pixels[p]);

            var source = BitmapSource.Create(ThumbWidth, ThumbHeight, 96, 96, PixelFormats.Bgra32, null, pixels, ThumbWidth * 4);
            source.Freeze();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new FileStream(outputPngPath + ".tmp", FileMode.Create, FileAccess.Write);
            encoder.Save(stream);
            stream.Close();
            File.Move(outputPngPath + ".tmp", outputPngPath, overwrite: true);
            return File.Exists(outputPngPath);
        }
        catch { return false; }
        finally
        {
            try { player?.Stop(); } catch { }
            player?.Dispose();
            media?.Dispose();
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        }
    }

    public static string? FindSidecarSubtitle(string videoPath)
    {
        try
        {
            string dir = Path.GetDirectoryName(videoPath) ?? "";
            string baseName = Path.GetFileNameWithoutExtension(videoPath);
            if (!Directory.Exists(dir)) return null;
            var candidates = Directory.EnumerateFiles(dir)
                .Where(f =>
                {
                    string ext = Path.GetExtension(f);
                    return SubtitleExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
                })
                .Where(f =>
                {
                    string n = Path.GetFileNameWithoutExtension(f);
                    return n.Equals(baseName, StringComparison.OrdinalIgnoreCase)
                        || n.StartsWith(baseName + ".", StringComparison.OrdinalIgnoreCase);
                })
                .OrderBy(n => Path.GetFileNameWithoutExtension(n).Length)
                .ToList();
            return candidates.FirstOrDefault();
        }
        catch { return null; }
    }

    public int ClearCache()
    {
        int removed = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(_cacheDir, "*.png"))
            {
                try { File.Delete(f); removed++; } catch { }
            }
        }
        catch { }
        return removed;
    }

    public void Dispose()
    {
        try
        {
            _queue.CompleteAdding();
            _cts.Cancel();
            _worker?.Join(3000);
        }
        catch { }
        _cts.Dispose();
        _queue.Dispose();
    }
}

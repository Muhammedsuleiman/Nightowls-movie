using System.IO;

namespace NightOwls.Models;

public class Movie
{
    public int Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int? Year { get; set; }
    public string Extension { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public long DurationMs { get; set; }
    public int? LocationId { get; set; }
    public DateTime DateAddedUtc { get; set; }
    public string? ThumbnailFile { get; set; }
    public bool IsFavorite { get; set; }

    public WatchRecord? Watch { get; set; }

    public bool FileExists
    {
        get
        {
            try { return File.Exists(Path); }
            catch { return false; }
        }
    }

    public double ResumeFraction => LengthMs > 0 ? Math.Clamp((double)PositionMs / LengthMs, 0.0, 1.0) : 0.0;

    public long PositionMs => Watch?.PositionMs ?? 0;
    private long LengthMs => Watch?.LengthMs > 0 ? Watch.LengthMs : DurationMs;

    public bool IsResumeCandidate => PositionMs > 30_000 && LengthMs > 0 && PositionMs < LengthMs * 0.95;
}

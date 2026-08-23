namespace NightOwls.Models;

public class WatchRecord
{
    public int MovieId { get; set; }
    public DateTime LastWatchedUtc { get; set; }
    public long PositionMs { get; set; }
    public long LengthMs { get; set; }
    public bool Completed { get; set; }
}

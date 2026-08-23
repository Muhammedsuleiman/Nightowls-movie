namespace NightOwls.Models;

public class ScanLocation
{
    public int Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public DateTime DateAddedUtc { get; set; }
    public bool IsUnavailable { get; set; }

    public override string ToString() => Path;
}

namespace NightOwls.Models;

public class ScanProgress
{
    public string StatusText { get; init; } = string.Empty;
    public int FilesFound { get; init; }
    public int FilesAdded { get; init; }
    public int FilesRemoved { get; init; }
    public bool IsProbing { get; init; }
    public int ProbedCount { get; init; }
    public int ProbeTotal { get; init; }
}

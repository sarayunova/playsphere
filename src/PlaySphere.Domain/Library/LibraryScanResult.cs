namespace PlaySphere.Domain.Library;

/// <summary>Outcome of scanning one library folder (PICKLE.md §22 "scanning").</summary>
public sealed record LibraryScanResult(int Added, int Updated, int Failed)
{
    public int TotalProcessed => Added + Updated;
}

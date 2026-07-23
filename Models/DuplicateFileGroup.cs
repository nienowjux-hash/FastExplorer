namespace FastExplorer.Models;

// One group of byte-for-byte identical files found by DuplicateFinderService.
public sealed class DuplicateFileGroup
{
    public required long SizeBytes { get; init; }
    public required IReadOnlyList<string> Paths { get; init; }
}

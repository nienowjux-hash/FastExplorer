namespace FastExplorer.Models;

// One row in DuplicatesView's flat list - a single duplicate file, annotated with which
// group it belongs to so same-group rows sort together and share a group label.
public sealed class DuplicateFileRow
{
    public required string FullPath { get; init; }
    public required string Name { get; init; }
    public required string FolderPath { get; init; }
    public required string SizeDisplay { get; init; }
    public required string GroupLabel { get; init; }
    public required int GroupIndex { get; init; }
    public required bool IsFirstInGroup { get; init; }
}

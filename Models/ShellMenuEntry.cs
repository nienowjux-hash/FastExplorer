namespace FastExplorer.Models;

public sealed class ShellMenuEntry
{
    public required string Label { get; init; }
    public int CommandId { get; init; }
    public bool IsEnabled { get; init; } = true;
    public bool IsSeparator { get; init; }
    public IReadOnlyList<ShellMenuEntry>? SubItems { get; init; }
}

namespace FastExplorer.Services;

public abstract class UndoAction
{
    public abstract string Description { get; }
    public abstract void Undo();
}

public sealed class CreateItemUndoAction : UndoAction
{
    private readonly string _path;
    private readonly bool _isDirectory;

    public CreateItemUndoAction(string path, bool isDirectory)
    {
        _path = path;
        _isDirectory = isDirectory;
    }

    public override string Description => $"Undid creating '{Path.GetFileName(_path)}'";

    public override void Undo()
    {
        if (_isDirectory) Directory.Delete(_path, recursive: true);
        else File.Delete(_path);
    }
}

public sealed class RenameUndoAction : UndoAction
{
    private readonly string _newPath;
    private readonly string _originalName;
    private readonly bool _isDirectory;

    public RenameUndoAction(string newPath, string originalName, bool isDirectory)
    {
        _newPath = newPath;
        _originalName = originalName;
        _isDirectory = isDirectory;
    }

    public override string Description => $"Undid renaming '{Path.GetFileName(_newPath)}'";

    public override void Undo()
    {
        var directory = Path.GetDirectoryName(_newPath)!;
        var target = Path.Combine(directory, _originalName);
        if (_isDirectory) Directory.Move(_newPath, target);
        else File.Move(_newPath, target);
    }
}

public sealed class MoveUndoAction : UndoAction
{
    private readonly IReadOnlyList<(string CurrentPath, string OriginalPath)> _moves;

    public MoveUndoAction(IReadOnlyList<(string CurrentPath, string OriginalPath)> moves)
    {
        _moves = moves;
    }

    public override string Description => _moves.Count == 1
        ? $"Undid moving '{Path.GetFileName(_moves[0].CurrentPath)}'"
        : $"Undid moving {_moves.Count} items";

    public override void Undo()
    {
        foreach (var (currentPath, originalPath) in _moves)
        {
            if (Directory.Exists(currentPath)) Directory.Move(currentPath, originalPath);
            else if (File.Exists(currentPath)) File.Move(currentPath, originalPath);
        }
    }
}

public sealed class CopyUndoAction : UndoAction
{
    private readonly IReadOnlyList<string> _createdPaths;

    public CopyUndoAction(IReadOnlyList<string> createdPaths)
    {
        _createdPaths = createdPaths;
    }

    public override string Description => _createdPaths.Count == 1
        ? $"Undid copying '{Path.GetFileName(_createdPaths[0])}'"
        : $"Undid copying {_createdPaths.Count} items";

    public override void Undo()
    {
        foreach (var path in _createdPaths)
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
        }
    }
}

public sealed class DeleteUndoAction : UndoAction
{
    private readonly IReadOnlyList<string> _originalPaths;

    public DeleteUndoAction(IReadOnlyList<string> originalPaths)
    {
        _originalPaths = originalPaths;
    }

    public override string Description => _originalPaths.Count == 1
        ? $"Restored '{Path.GetFileName(_originalPaths[0])}'"
        : $"Restored {_originalPaths.Count} items";

    public override void Undo()
    {
        foreach (var path in _originalPaths)
        {
            RecycleBinService.TryRestore(path);
        }
    }
}

// A single, app-wide undo stack (mirrors how Explorer's undo history is shared
// across the whole process, not scoped per window/tab).
public static class UndoService
{
    private static readonly Stack<UndoAction> s_stack = new();

    public static bool CanUndo => s_stack.Count > 0;

    public static void Push(UndoAction action) => s_stack.Push(action);

    public static bool TryUndo(out string? description)
    {
        description = null;
        if (s_stack.Count == 0) return false;

        var action = s_stack.Pop();
        description = action.Description;
        action.Undo();
        return true;
    }
}

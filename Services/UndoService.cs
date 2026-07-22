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

    public override string Description => $"Criação de '{Path.GetFileName(_path)}' desfeita";

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

    public override string Description => $"Renomeação de '{Path.GetFileName(_newPath)}' desfeita";

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
        ? $"Movimentação de '{Path.GetFileName(_moves[0].CurrentPath)}' desfeita"
        : $"Movimentação de {_moves.Count} itens desfeita";

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
        ? $"Cópia de '{Path.GetFileName(_createdPaths[0])}' desfeita"
        : $"Cópia de {_createdPaths.Count} itens desfeita";

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
        ? $"'{Path.GetFileName(_originalPaths[0])}' restaurado"
        : $"{_originalPaths.Count} itens restaurados";

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

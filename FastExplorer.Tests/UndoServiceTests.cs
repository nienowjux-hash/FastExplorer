using FastExplorer.Services;
using Xunit;

namespace FastExplorer.Tests;

public class UndoServiceTests : IDisposable
{
    private readonly string _root;

    public UndoServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FastExplorerUndoTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void CreateItemUndoAction_DeletesTheCreatedFile()
    {
        var path = Path.Combine(_root, "new.txt");
        File.WriteAllText(path, "content");

        new CreateItemUndoAction(path, isDirectory: false).Undo();

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void CreateItemUndoAction_DeletesTheCreatedFolder()
    {
        var path = Path.Combine(_root, "new-folder");
        Directory.CreateDirectory(path);

        new CreateItemUndoAction(path, isDirectory: true).Undo();

        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public void RenameUndoAction_RestoresTheOriginalName()
    {
        var oldPath = Path.Combine(_root, "old.txt");
        var newPath = Path.Combine(_root, "new.txt");
        File.WriteAllText(oldPath, "content");
        File.Move(oldPath, newPath);

        new RenameUndoAction(newPath, "old.txt", isDirectory: false).Undo();

        Assert.True(File.Exists(oldPath));
        Assert.False(File.Exists(newPath));
    }

    [Fact]
    public void MoveUndoAction_MovesFileBackToOriginalLocation()
    {
        var sourceDir = Path.Combine(_root, "source");
        var destDir = Path.Combine(_root, "dest");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(destDir);
        var originalPath = Path.Combine(sourceDir, "file.txt");
        var currentPath = Path.Combine(destDir, "file.txt");
        File.WriteAllText(originalPath, "content");
        File.Move(originalPath, currentPath);

        new MoveUndoAction(new[] { (currentPath, originalPath) }).Undo();

        Assert.True(File.Exists(originalPath));
        Assert.False(File.Exists(currentPath));
    }

    [Fact]
    public void CopyUndoAction_DeletesTheCreatedCopies()
    {
        var path1 = Path.Combine(_root, "copy1.txt");
        var path2 = Path.Combine(_root, "copy2.txt");
        File.WriteAllText(path1, "content");
        File.WriteAllText(path2, "content");

        new CopyUndoAction(new[] { path1, path2 }).Undo();

        Assert.False(File.Exists(path1));
        Assert.False(File.Exists(path2));
    }

    [Fact]
    public void TryUndo_WithEmptyStack_ReturnsFalse()
    {
        // Drain whatever earlier tests in this run may have pushed, then verify
        // an empty stack correctly reports nothing to undo.
        while (UndoService.CanUndo)
        {
            UndoService.TryUndo(out _);
        }

        var result = UndoService.TryUndo(out var description);

        Assert.False(result);
        Assert.Null(description);
    }
}

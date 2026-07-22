using System.Security.AccessControl;
using System.Security.Principal;
using FastExplorer.Models;

namespace FastExplorer.Services;

public static class PropertiesService
{
    // Recursive folder size is exactly the kind of thing that must never run on the
    // UI thread - it's opt-in (only when Properties is open) and fully cancellable,
    // so it never touches the speed of ordinary browsing.
    public static Task<long> CalculateFolderSizeAsync(string path, CancellationToken cancellationToken) =>
        Task.Run(() => CalculateFolderSize(path, cancellationToken), cancellationToken);

    private static long CalculateFolderSize(string path, CancellationToken cancellationToken)
    {
        long total = 0;
        var options = new EnumerationOptions { IgnoreInaccessible = true };

        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }

            foreach (var dir in Directory.EnumerateDirectories(path, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                total += CalculateFolderSize(dir, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return total;
    }

    public static (DateTime Created, DateTime LastAccessed) GetTimestamps(string path, bool isDirectory)
    {
        try
        {
            if (isDirectory)
            {
                var info = new DirectoryInfo(path);
                return (info.CreationTime, info.LastAccessTime);
            }
            else
            {
                var info = new FileInfo(path);
                return (info.CreationTime, info.LastAccessTime);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (default, default);
        }
    }

    public static (bool ReadOnly, bool Hidden) GetAttributes(string path)
    {
        try
        {
            var attr = File.GetAttributes(path);
            return (attr.HasFlag(FileAttributes.ReadOnly), attr.HasFlag(FileAttributes.Hidden));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, false);
        }
    }

    public static void SetAttributes(string path, bool readOnly, bool hidden)
    {
        var attr = File.GetAttributes(path);
        attr = readOnly ? attr | FileAttributes.ReadOnly : attr & ~FileAttributes.ReadOnly;
        attr = hidden ? attr | FileAttributes.Hidden : attr & ~FileAttributes.Hidden;
        File.SetAttributes(path, attr);
    }

    // Read-only: owner + access rules, mirroring the Security tab of Explorer's
    // Properties dialog without any ability to change permissions.
    public static (string Owner, IReadOnlyList<AccessRuleInfo> Rules) GetAccessInfo(string path, bool isDirectory)
    {
        try
        {
            FileSystemSecurity security = isDirectory
                ? new DirectoryInfo(path).GetAccessControl()
                : new FileInfo(path).GetAccessControl();

            var owner = security.GetOwner(typeof(NTAccount))?.Value ?? "Desconhecido";

            var rules = new List<AccessRuleInfo>();
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(NTAccount)))
            {
                rules.Add(new AccessRuleInfo(
                    rule.IdentityReference.Value,
                    TranslateAccessType(rule.AccessControlType),
                    TranslateRights(rule.FileSystemRights)));
            }

            return (owner, rules);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SystemException)
        {
            return ("Indisponível", Array.Empty<AccessRuleInfo>());
        }
    }

    private static string TranslateAccessType(AccessControlType type) => type switch
    {
        AccessControlType.Allow => "Permitir",
        AccessControlType.Deny => "Negar",
        _ => type.ToString(),
    };

    // FileSystemRights.ToString() (a [Flags] enum) returns a comma-separated list of
    // the *named* combination it resolves to (e.g. "Modify, Synchronize"), not a fixed
    // set of values - each token is translated independently and rejoined so unknown/
    // future .NET-added tokens degrade to their English name rather than disappearing.
    private static readonly Dictionary<string, string> RightsTokens = new()
    {
        ["FullControl"] = "Controle total",
        ["Modify"] = "Modificar",
        ["ReadAndExecute"] = "Leitura e execução",
        ["ListDirectory"] = "Listar conteúdo da pasta",
        ["ReadData"] = "Ler dados",
        ["Read"] = "Leitura",
        ["Write"] = "Gravação",
        ["Delete"] = "Excluir",
        ["DeleteSubdirectoriesAndFiles"] = "Excluir subpastas e arquivos",
        ["ReadPermissions"] = "Ler permissões",
        ["ChangePermissions"] = "Alterar permissões",
        ["TakeOwnership"] = "Tomar posse",
        ["Synchronize"] = "Sincronizar",
        ["ReadAttributes"] = "Ler atributos",
        ["WriteAttributes"] = "Gravar atributos",
        ["ReadExtendedAttributes"] = "Ler atributos estendidos",
        ["WriteExtendedAttributes"] = "Gravar atributos estendidos",
        ["ExecuteFile"] = "Executar arquivo",
        ["Traverse"] = "Percorrer pasta",
        ["CreateFiles"] = "Criar arquivos",
        ["WriteData"] = "Gravar dados",
        ["CreateDirectories"] = "Criar pastas",
        ["AppendData"] = "Acrescentar dados",
    };

    private static string TranslateRights(FileSystemRights rights)
    {
        var tokens = rights.ToString().Split(", ", StringSplitOptions.TrimEntries);
        var translated = tokens.Select(t => RightsTokens.TryGetValue(t, out var pt) ? pt : t);
        return string.Join(", ", translated);
    }
}

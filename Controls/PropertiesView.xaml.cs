using FastExplorer.Models;
using FastExplorer.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FastExplorer.Controls;

public sealed partial class PropertiesView : UserControl
{
    private readonly FileSystemItem _item;
    private CancellationTokenSource? _sizeCts;

    public bool ReadOnlyChecked => ReadOnlyCheck.IsChecked == true;
    public bool HiddenChecked => HiddenCheck.IsChecked == true;

    public PropertiesView(FileSystemItem item)
    {
        _item = item;
        InitializeComponent();

        HeaderIcon.Glyph = item.IconGlyph;
        HeaderName.Text = item.Name;
        LocationText.Text = DescribeLocation(item);

        if (item.Kind == FileSystemItemKind.Drive)
        {
            TypeText.Text = "Disco local";
            AttributesPanel.Visibility = Visibility.Collapsed;
            SizeText.Text = item.SizeDisplay;
        }
        else
        {
            TypeText.Text = item.IsDirectory ? "Pasta" : DescribeFileType(item.Extension);

            var (readOnly, hidden) = PropertiesService.GetAttributes(item.FullPath);
            ReadOnlyCheck.IsChecked = readOnly;
            HiddenCheck.IsChecked = hidden;

            var (created, accessed) = PropertiesService.GetTimestamps(item.FullPath, item.IsDirectory);
            CreatedText.Text = created == default ? string.Empty : created.ToString("g");
            ModifiedText.Text = item.DateModifiedDisplay;
            AccessedText.Text = accessed == default ? string.Empty : accessed.ToString("g");

            if (item.IsDirectory)
            {
                _ = CalculateSizeAsync(item.FullPath);
            }
            else
            {
                SizeText.Text = item.SizeDisplay;
            }
        }

        _ = LoadSecurityAsync(item.FullPath, item.IsDirectory);
    }

    private static string DescribeLocation(FileSystemItem item)
    {
        if (item.Kind == FileSystemItemKind.Drive) return item.FullPath;
        var parent = Path.GetDirectoryName(item.FullPath.TrimEnd(Path.DirectorySeparatorChar));
        return string.IsNullOrEmpty(parent) ? item.FullPath : parent;
    }

    private static string DescribeFileType(string extension) =>
        string.IsNullOrEmpty(extension) ? "Arquivo" : $"Arquivo {extension.TrimStart('.').ToUpperInvariant()}";

    private async Task CalculateSizeAsync(string path)
    {
        SizeText.Text = "Calculando...";
        SizeSpinner.IsActive = true;
        CancelSizeButton.Visibility = Visibility.Visible;
        _sizeCts = new CancellationTokenSource();

        try
        {
            var size = await PropertiesService.CalculateFolderSizeAsync(path, _sizeCts.Token);
            SizeText.Text = FileSystemItem.FormatSize(size);
        }
        catch (OperationCanceledException)
        {
            SizeText.Text = "Cancelado";
        }
        finally
        {
            SizeSpinner.IsActive = false;
            CancelSizeButton.Visibility = Visibility.Collapsed;
        }
    }

    private void CancelSizeButton_Click(object sender, RoutedEventArgs e) => _sizeCts?.Cancel();

    private async Task LoadSecurityAsync(string path, bool isDirectory)
    {
        var (owner, rules) = await Task.Run(() => PropertiesService.GetAccessInfo(path, isDirectory));
        OwnerText.Text = owner;
        foreach (var rule in rules)
        {
            AccessRulesList.Items.Add(rule);
        }
    }
}

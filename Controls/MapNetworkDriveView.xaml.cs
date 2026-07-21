using FastExplorer.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FastExplorer.Controls;

public sealed partial class MapNetworkDriveView : UserControl
{
    public MapNetworkDriveView()
    {
        InitializeComponent();

        foreach (var letter in NetworkDriveService.GetAvailableDriveLetters())
        {
            DriveLetterCombo.Items.Add(letter);
        }
        if (DriveLetterCombo.Items.Count > 0) DriveLetterCombo.SelectedIndex = 0;
    }

    // Returns true and performs the mapping if the form is valid; otherwise shows an
    // inline error and returns false so the caller (ContentDialog) can keep the dialog open.
    public bool TryMap()
    {
        ErrorText.Visibility = Visibility.Collapsed;

        var driveLetter = DriveLetterCombo.SelectedItem as string;
        var path = PathBox.Text.Trim();

        if (string.IsNullOrEmpty(driveLetter))
        {
            ShowError("No drive letters available.");
            return false;
        }
        if (string.IsNullOrEmpty(path))
        {
            ShowError("Enter a network path, e.g. \\\\server\\share.");
            return false;
        }

        try
        {
            var username = string.IsNullOrWhiteSpace(UsernameBox.Text) ? null : UsernameBox.Text.Trim();
            var password = string.IsNullOrEmpty(PasswordBox.Password) ? null : PasswordBox.Password;
            NetworkDriveService.MapDrive(driveLetter, path, username, password, ReconnectCheck.IsChecked == true);
            return true;
        }
        catch (IOException ex)
        {
            ShowError(ex.Message);
            return false;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}

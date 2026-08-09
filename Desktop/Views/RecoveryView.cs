using Avalonia.Controls;
using Avalonia.Layout;
using Trionine.TOST.Core.Integrations.SlsSteam;
using Trionine.TOST.Desktop.Services;

namespace Trionine.TOST.Desktop.Views;

internal sealed class RecoveryView : UserControl
{
    private readonly ComboBox installation = new() { ItemsSource = new[] { "Native", "Flatpak" }, SelectedIndex = 0, Width = 150 };
    private readonly ListBox entries = new() { MinHeight = 310 };
    private readonly TextBlock status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly CheckBox confirmation = new() { Content = "I reviewed the selected recovery destination" };
    private readonly Button restore = new() { Content = "Restore selected", IsEnabled = false };

    public RecoveryView()
    {
        installation.SelectedIndex = DesktopPaths.PreferredInstallationIndex;
        var refresh = new Button { Content = "Refresh" };
        refresh.Click += (_, _) => Refresh();
        installation.SelectionChanged += (_, _) => Refresh();
        entries.SelectionChanged += (_, _) => UpdateSelection();
        confirmation.IsCheckedChanged += (_, _) => UpdateSelection();
        restore.Click += (_, _) => RestoreSelected();
        Content = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                Card("Recovery archives", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { installation, refresh } }),
                Card("Available backups", entries),
                status,
                confirmation,
                restore
            }
        };
        if (OperatingSystem.IsLinux()) Refresh();
        else { status.Text = "Windows Game Manager recovery remains available in the existing frontend during migration."; installation.IsEnabled = refresh.IsEnabled = false; }
    }

    private bool Flatpak => installation.SelectedIndex == 1;
    private SlsSteamPaths Paths => Flatpak ? SlsSteamPaths.ForFlatpakUser() : SlsSteamPaths.ForCurrentUser();

    private void Refresh()
    {
        try
        {
            var kind = Flatpak ? "Flatpak" : "Native";
            var items = new List<RecoveryItem>();
            items.AddRange(new SlsSteamConfigService().FindBackups(ConfigBackupDirectory()).Select(item =>
                new RecoveryItem("Configuration", item.FileName, $"Configuration • {item.LastWriteUtc:u} • {item.SizeBytes} bytes")));
            items.AddRange(new SlsSteamRecoveryService().FindRecoveryEntries(LibraryRecoveryDirectory())
                .Where(item => item.InstallationKind.Equals(kind, StringComparison.OrdinalIgnoreCase))
                .Select(item => new RecoveryItem("Libraries", item.ArchiveId, $"Libraries • {item.RemovedUtc:u} • {item.FileNames.Count} files")));
            items.AddRange(new SlsSteamLaunchConfigurationService().FindRecoveryEntries(LaunchRecoveryDirectory())
                .Where(item => item.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase))
                .Select(item => new RecoveryItem("Launch hooks", item.ArchiveId, $"Launch hooks • {item.RemovedUtc:u} • {item.Paths.Count} files")));
            entries.ItemsSource = items;
            status.Text = items.Count == 0 ? $"No {kind} recovery entries found." : $"{items.Count} recoverable entries found.";
            confirmation.IsChecked = false;
        }
        catch (Exception ex) { status.Text = $"Could not list recovery entries: {ex.Message}"; }
    }

    private void UpdateSelection()
    {
        restore.IsEnabled = entries.SelectedItem is RecoveryItem && confirmation.IsChecked == true;
        if (entries.SelectedItem is RecoveryItem item) status.Text = $"Selected {item.Category}: {item.Id}";
    }

    private void RestoreSelected()
    {
        if (entries.SelectedItem is not RecoveryItem item || confirmation.IsChecked != true) return;
        try
        {
            var kind = Flatpak ? "Flatpak" : "Native";
            switch (item.Category)
            {
                case "Configuration":
                    new SlsSteamConfigService().RestoreBackup(Paths.ConfigPath, ConfigBackupDirectory(), item.Id);
                    break;
                case "Libraries":
                    new SlsSteamRecoveryService().Restore(Paths, kind, LibraryRecoveryDirectory(), item.Id);
                    break;
                case "Launch hooks":
                    var launchService = new SlsSteamLaunchConfigurationService();
                    launchService.Restore(SlsSteamLaunchPlanFactory.Create(Flatpak, Paths), LaunchRecoveryDirectory(), item.Id);
                    break;
            }
            status.Text = $"Restored {item.Category.ToLowerInvariant()} successfully.";
            Refresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            status.Text = $"Restore failed: {ex.Message}";
        }
    }

    private static string ConfigBackupDirectory() => Path.Combine(DataRoot(), "backups", "SLSsteam");
    private static string LibraryRecoveryDirectory() => Path.Combine(DataRoot(), "backups", "removed-slssteam");
    private static string LaunchRecoveryDirectory() => Path.Combine(DataRoot(), "backups", "launch-hooks");
    private static string DataRoot() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TOST");

    private static Border Card(string title, Control content) => new()
    {
        Classes = { "card" },
        Child = new StackPanel { Spacing = 10, Children = { new TextBlock { Text = title, FontSize = 17, FontWeight = Avalonia.Media.FontWeight.SemiBold }, content } }
    };

    private sealed record RecoveryItem(string Category, string Id, string Display)
    {
        public override string ToString() => Display;
    }
}

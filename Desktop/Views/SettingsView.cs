using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Trionine.TOST.Core.Steam;
using Trionine.TOST.Desktop.Services;

namespace Trionine.TOST.Desktop.Views;

internal sealed class SettingsView : UserControl
{
    private readonly TextBox steamRoot = new() { MinWidth = 390 };
    private readonly ComboBox preferredInstallation = new() { MinWidth = 390 };
    private readonly CheckBox overwrite = new() { Content = "Overwrite existing files" };
    private readonly CheckBox backup = new() { Content = "Backup files before overwrite" };
    private readonly CheckBox startWithDesktop = new();
    private readonly CheckBox floatingAlwaysOnTop = new() { Content = "Keep floating icon always on top" };
    private readonly CheckBox updateChecks = new() { Content = "Automatically check for TOST updates" };
    private readonly TextBlock status = new()
    {
        Foreground = Brush.Parse("#D79A42"),
        TextWrapping = TextWrapping.Wrap
    };

    public SettingsView()
    {
        startWithDesktop.Content = OperatingSystem.IsWindows()
            ? "Start floating installer with Windows"
            : "Start floating installer when I sign in";

        var save = Button("Save", primary: true);
        var cancel = Button("Cancel", primary: false);
        save.Click += (_, _) => Save();
        cancel.Click += (_, _) => Close();

        var fields = new StackPanel { Spacing = 12 };
        if (OperatingSystem.IsWindows())
        {
            var browse = Button("Browse", primary: false);
            browse.Width = 74;
            browse.Click += async (_, _) => await BrowseSteamFolderAsync();
            fields.Children.Add(new TextBlock { Text = "Steam folder" });
            fields.Children.Add(new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 10,
                Children = { steamRoot, browse }
            });
            Grid.SetColumn(browse, 1);
            fields.Children.Add(overwrite);
            fields.Children.Add(backup);
        }
        else
        {
            fields.Children.Add(new TextBlock { Text = "Steam installation" });
            fields.Children.Add(preferredInstallation);
        }

        fields.Children.Add(startWithDesktop);
        fields.Children.Add(floatingAlwaysOnTop);
        fields.Children.Add(updateChecks);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { save, cancel }
        };

        var scroller = new ScrollViewer
        {
            Content = fields,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto,Auto"),
            RowSpacing = 10,
            Children = { scroller, status, actions }
        };
        Grid.SetRow(status, 1);
        Grid.SetRow(actions, 2);
        Load();
    }

    private void Load()
    {
        var preferences = DesktopPaths.PreferencesStore.Load();
        steamRoot.Text = preferences.WindowsSteamRoot;
        if (string.IsNullOrWhiteSpace(steamRoot.Text))
        {
            steamRoot.Text = SteamDiscovery.FindInstallations().FirstOrDefault()?.RootPath ?? string.Empty;
        }

        overwrite.IsChecked = preferences.OverwriteExistingFiles;
        backup.IsChecked = preferences.BackupFilesBeforeOverwrite;
        startWithDesktop.IsChecked = preferences.StartWithDesktop;
        floatingAlwaysOnTop.IsChecked = preferences.FloatingIconAlwaysOnTop;
        updateChecks.IsChecked = preferences.AutomaticallyCheckForUpdates;

        var installations = SteamDiscovery.FindInstallations();
        preferredInstallation.ItemsSource = installations;
        preferredInstallation.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<SteamInstallation>((item, _) =>
            new TextBlock
            {
                Text = item is null ? string.Empty : $"{DisplayKind(item.Kind)} - {item.RootPath}",
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        preferredInstallation.SelectedIndex = installations.Count == 0
            ? -1
            : Math.Max(0, installations.ToList().FindIndex(item => item.Kind == preferences.PreferredSteamInstallation));

        if (!StartupRegistrationService.CanRegister(out var reason))
        {
            startWithDesktop.IsEnabled = false;
            status.Text = reason;
        }
    }

    private async Task BrowseSteamFolderAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            return;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Steam installation folder",
            AllowMultiple = false
        });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            steamRoot.Text = path;
        }
    }

    private void Save()
    {
        try
        {
            var current = DesktopPaths.PreferencesStore.Load();
            var selected = preferredInstallation.SelectedItem as SteamInstallation;
            var updated = current with
            {
                WindowsSteamRoot = steamRoot.Text?.Trim() ?? string.Empty,
                PreferredSteamInstallation = selected?.Kind ?? current.PreferredSteamInstallation,
                OverwriteExistingFiles = overwrite.IsChecked == true,
                BackupFilesBeforeOverwrite = backup.IsChecked == true,
                StartWithDesktop = startWithDesktop.IsEnabled && startWithDesktop.IsChecked == true,
                FloatingIconAlwaysOnTop = floatingAlwaysOnTop.IsChecked == true,
                AutomaticallyCheckForUpdates = updateChecks.IsChecked == true,
                ShowFloatingIcon = true
            };
            if (startWithDesktop.IsEnabled)
            {
                StartupRegistrationService.Apply(updated.StartWithDesktop);
            }

            DesktopPaths.PreferencesStore.Save(updated);
            (Application.Current as App)?.ApplyPreferences();
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            status.Text = $"Could not save settings: {ex.Message}";
        }
    }

    private void Close() => (TopLevel.GetTopLevel(this) as Window)?.Close();

    private static Button Button(string text, bool primary) => new()
    {
        Content = text,
        Width = 76,
        Height = 30,
        Background = primary ? Brush.Parse("#159B35") : null
    };

    private static string DisplayKind(SteamInstallationKind kind) => kind switch
    {
        SteamInstallationKind.Flatpak => "Flatpak Steam",
        SteamInstallationKind.Windows => "Steam",
        _ => "Native Steam"
    };
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Trionine.TOST.Core.Configuration;
using Trionine.TOST.Core.Steam;
using Trionine.TOST.Desktop.Services;

namespace Trionine.TOST.Desktop.Views;

internal sealed class SettingsView : UserControl
{
    private readonly ComboBox preferredInstallation = new()
    {
        ItemsSource = new[] { "Native Steam", "Flatpak Steam" },
        Width = 210
    };
    private readonly CheckBox updateChecks = new() { Content = "Automatically check for TOST updates" };
    private readonly CheckBox floatingIcon = new() { Content = "Show the floating TOST icon" };
    private readonly CheckBox floatingAlwaysOnTop = new() { Content = "Keep the floating icon always on top" };
    private readonly CheckBox startWithDesktop = new() { Content = "Start TOST when I sign in" };
    private readonly NumericUpDown logLines = new()
    {
        Minimum = 10,
        Maximum = 2_000,
        Increment = 10,
        Width = 110
    };
    private readonly TextBlock status = new()
    {
        Foreground = Brush.Parse("#AFC0B4"),
        TextWrapping = TextWrapping.Wrap
    };

    public SettingsView()
    {
        var save = new Button
        {
            Content = "Save",
            Width = 100,
            Height = 34,
            Background = Brush.Parse("#219638")
        };
        var cancel = new Button
        {
            Content = "Cancel",
            Width = 100,
            Height = 34
        };
        save.Click += (_, _) => Save();
        cancel.Click += (_, _) => (TopLevel.GetTopLevel(this) as Window)?.Close();

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto,Auto"),
            RowSpacing = 12,
            Children =
            {
                new StackPanel
                {
                    Spacing = 14,
                    Children =
                    {
                        Row("Preferred Steam installation", preferredInstallation),
                        new Separator(),
                        floatingIcon,
                        floatingAlwaysOnTop,
                        startWithDesktop,
                        updateChecks,
                        Row("Default diagnostic log lines", logLines)
                    }
                },
                status,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, save }
                }
            }
        };
        Grid.SetRow(status, 1);
        Grid.SetRow(((Grid)Content).Children[2], 2);
        Load();
    }

    private void Load()
    {
        var settings = DesktopPaths.PreferencesStore.Load();
        preferredInstallation.SelectedIndex = settings.PreferredSteamInstallation == SteamInstallationKind.Flatpak ? 1 : 0;
        updateChecks.IsChecked = settings.AutomaticallyCheckForUpdates;
        floatingIcon.IsChecked = settings.ShowFloatingIcon;
        floatingAlwaysOnTop.IsChecked = settings.FloatingIconAlwaysOnTop;
        startWithDesktop.IsChecked = settings.StartWithDesktop;
        if (!StartupRegistrationService.CanRegister(out var startupReason))
        {
            startWithDesktop.IsEnabled = false;
            startWithDesktop.Content = $"Start TOST when I sign in — {startupReason}";
        }
        logLines.Value = settings.DiagnosticTailLines;
    }

    private void Save()
    {
        try
        {
            var updated = new TostPreferences
            {
                PreferredSteamInstallation = preferredInstallation.SelectedIndex == 1
                    ? SteamInstallationKind.Flatpak
                    : SteamInstallationKind.Native,
                AutomaticallyCheckForUpdates = updateChecks.IsChecked == true,
                ShowFloatingIcon = floatingIcon.IsChecked == true,
                FloatingIconAlwaysOnTop = floatingAlwaysOnTop.IsChecked == true,
                StartWithDesktop = startWithDesktop.IsEnabled && startWithDesktop.IsChecked == true,
                DiagnosticTailLines = (int)(logLines.Value ?? 100)
            };
            if (startWithDesktop.IsEnabled)
            {
                StartupRegistrationService.Apply(updated.StartWithDesktop);
            }
            DesktopPaths.PreferencesStore.Save(updated);
            (Application.Current as App)?.ApplyPreferences();
            (TopLevel.GetTopLevel(this) as Window)?.Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            status.Text = $"Could not save settings: {ex.Message}";
        }
    }

    private static Grid Row(string label, Control control)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };
        row.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }
}

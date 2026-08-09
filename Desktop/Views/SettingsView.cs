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
        Width = 190
    };
    private readonly CheckBox updateChecks = new() { Content = "Automatically check for TOST updates" };
    private readonly CheckBox floatingIcon = new() { Content = "Show the floating TOST icon" };
    private readonly CheckBox floatingAlwaysOnTop = new() { Content = "Keep the floating icon always on top" };
    private readonly CheckBox startWithDesktop = new() { Content = "Start TOST when I sign in" };
    private readonly NumericUpDown logLines = new() { Minimum = 10, Maximum = 2_000, Increment = 10, Width = 110 };
    private readonly TextBlock status = new() { Foreground = Brush.Parse("#AFC0B4"), TextWrapping = TextWrapping.Wrap };

    public SettingsView()
    {
        var save = new Button { Content = "Save settings" };
        save.Click += (_, _) => Save();
        var openData = new Button { Content = "Open TOST data folder" };
        var openSteam = new Button { Content = "Open preferred Steam folder" };
        var restartSteam = new Button { Content = "Restart Steam safely", IsEnabled = OperatingSystem.IsLinux() };
        openData.Click += (_, _) => OpenFolder(DesktopPaths.DataRoot, create: true);
        openSteam.Click += (_, _) => OpenSteamFolder();
        restartSteam.Click += async (_, _) => await RestartSteamAsync(restartSteam);
        Content = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                Card("Steam", Row("Preferred installation", preferredInstallation)),
                Card("Desktop behavior", new StackPanel { Spacing = 12, Children = { floatingIcon, floatingAlwaysOnTop, startWithDesktop, updateChecks } }),
                Card("Diagnostics", Row("Default log lines", logLines)),
                Card("Platform actions", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { openData, openSteam, restartSteam } }),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { save, status } }
            }
        };
        Load();
    }

    private async Task RestartSteamAsync(Button button)
    {
        var kind = preferredInstallation.SelectedIndex == 1 ? SteamInstallationKind.Flatpak : SteamInstallationKind.Native;
        try
        {
            var plan = new SteamRestartService().CreatePlan(kind);
            if (!await TostDialog.ConfirmAsync(this, "Restart Steam",
                    $"TOST will ask {kind} Steam to shut down normally, wait briefly, then relaunch it. Running games should be closed first.",
                    "Restart")) return;
            button.IsEnabled = false;
            status.Text = "Requesting a normal Steam shutdown…";
            await new SteamLifecycleService().RestartAsync(plan);
            status.Text = "Steam relaunch requested. TOST did not terminate any game, Wine, or Proton processes.";
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            status.Text = $"Could not restart Steam safely: {ex.Message}";
        }
        finally { button.IsEnabled = OperatingSystem.IsLinux(); }
    }

    private void OpenSteamFolder()
    {
        var kind = preferredInstallation.SelectedIndex == 1 ? SteamInstallationKind.Flatpak : SteamInstallationKind.Native;
        var path = LinuxSteamDiscovery.FindInstallations().FirstOrDefault(item => item.Kind == kind)?.RootPath;
        if (path is null) { status.Text = $"No {kind} Steam installation was detected."; return; }
        OpenFolder(path, create: false);
    }

    private void OpenFolder(string path, bool create)
    {
        try
        {
            if (create) Directory.CreateDirectory(path);
            FolderLauncher.Open(path);
            status.Text = $"Opened {path}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            status.Text = $"Could not open folder: {ex.Message}";
        }
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
        status.Text = "Settings are stored locally and are not included in release files.";
    }

    private void Save()
    {
        try
        {
            var updated = new TostPreferences
            {
                PreferredSteamInstallation = preferredInstallation.SelectedIndex == 1 ? SteamInstallationKind.Flatpak : SteamInstallationKind.Native,
                AutomaticallyCheckForUpdates = updateChecks.IsChecked == true,
                ShowFloatingIcon = floatingIcon.IsChecked == true,
                FloatingIconAlwaysOnTop = floatingAlwaysOnTop.IsChecked == true,
                StartWithDesktop = startWithDesktop.IsEnabled && startWithDesktop.IsChecked == true,
                DiagnosticTailLines = (int)(logLines.Value ?? 100)
            };
            if (startWithDesktop.IsEnabled) StartupRegistrationService.Apply(updated.StartWithDesktop);
            DesktopPaths.PreferencesStore.Save(updated);
            (Avalonia.Application.Current as App)?.ApplyPreferences();
            status.Text = "Settings saved.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            status.Text = $"Could not save settings: {ex.Message}";
        }
    }

    private static StackPanel Row(string label, Control control) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 12,
        Children = { new TextBlock { Text = label, Width = 190, VerticalAlignment = VerticalAlignment.Center }, control }
    };

    private static Border Card(string title, Control content) => new()
    {
        Classes = { "card" },
        Child = new StackPanel { Spacing = 12, Children = { new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeight.SemiBold }, content } }
    };
}

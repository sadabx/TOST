using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Trionine.TOST.Desktop.Views;

namespace Trionine.TOST.Desktop;

public sealed partial class App : Application
{
    internal bool IsExiting { get; private set; }
    private FloatingIconWindow? floatingIcon;
    private Window? gameManagerWindow;
    private Window? settingsWindow;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            ApplyPreferences();
            if (floatingIcon is not null)
            {
                desktop.MainWindow = floatingIcon;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowFloatingIcon(object? sender, EventArgs e) => ShowFloatingIcon();

    internal void ShowFloatingIcon()
    {
        if (floatingIcon is null)
        {
            var preferences = DesktopPaths.PreferencesStore.Load();
            DesktopPaths.PreferencesStore.Save(preferences with { ShowFloatingIcon = true });
            ApplyPreferences();
        }

        floatingIcon?.Show();
        floatingIcon?.Activate();
    }

    internal void ShowGameManager()
    {
        if (gameManagerWindow is not null)
        {
            gameManagerWindow.Show();
            gameManagerWindow.Activate();
            return;
        }

        gameManagerWindow = CreateToolWindow(
            "TOST Game Manager",
            760,
            500,
            new GameManagerView());
        gameManagerWindow.Closed += (_, _) => gameManagerWindow = null;
        gameManagerWindow.Show();
    }

    internal void ShowSettings()
    {
        if (settingsWindow is not null)
        {
            settingsWindow.Show();
            settingsWindow.Activate();
            return;
        }

        settingsWindow = CreateToolWindow(
            "TOST Settings",
            520,
            350,
            new SettingsView());
        settingsWindow.Closed += (_, _) => settingsWindow = null;
        settingsWindow.Show();
    }

    internal void HideFloatingIcon()
    {
        var preferences = DesktopPaths.PreferencesStore.Load();
        DesktopPaths.PreferencesStore.Save(preferences with { ShowFloatingIcon = false });
        floatingIcon?.Close();
        floatingIcon = null;
    }

    internal void Exit()
    {
        IsExiting = true;
        floatingIcon?.Close();
        gameManagerWindow?.Close();
        settingsWindow?.Close();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    internal void ApplyPreferences()
    {
        var preferences = DesktopPaths.PreferencesStore.Load();
        if (!preferences.ShowFloatingIcon)
        {
            floatingIcon?.Close();
            floatingIcon = null;
            return;
        }

        if (floatingIcon is null)
        {
            floatingIcon = new FloatingIconWindow(preferences.FloatingIconAlwaysOnTop);
            floatingIcon.Closed += (_, _) => floatingIcon = null;
            floatingIcon.Show();
        }
        else
        {
            floatingIcon.Topmost = preferences.FloatingIconAlwaysOnTop;
            if (!floatingIcon.IsVisible)
            {
                floatingIcon.Show();
            }
        }
    }

    private static Window CreateToolWindow(string title, double width, double height, Control content) => new()
    {
        Title = title,
        Width = width,
        Height = height,
        MinWidth = width,
        MinHeight = height,
        MaxWidth = width,
        MaxHeight = height,
        CanResize = false,
        Background = Brush.Parse("#232426"),
        WindowStartupLocation = WindowStartupLocation.CenterScreen,
        Content = new Border
        {
            Padding = new Thickness(12),
            Child = content
        }
    };

    private async void InstallSlsSteam(object? sender, EventArgs e)
    {
        ShowFloatingIcon();
        if (floatingIcon is not null)
        {
            await floatingIcon.InstallOrRepairSlsSteamAsync();
        }
    }

    private void CheckForUpdates(object? sender, EventArgs e) =>
        FloatingIconWindow.OpenWebsite("https://github.com/sadabx/TOST/releases/latest");

    private void ExitApplication(object? sender, EventArgs e) => Exit();
}

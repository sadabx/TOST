using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Trionine.TOST.Desktop;

public sealed partial class App : Application
{
    internal bool IsExiting { get; private set; }
    private FloatingIconWindow? floatingIcon;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.MainWindow = new MainWindow();
            ApplyPreferences();
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void ShowMainWindow(object? sender, EventArgs e)
        => ShowMainWindow();

    internal void ShowMainWindow()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } window }) return;
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    internal void OpenPage(string page)
    {
        ShowMainWindow();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow window })
            window.OpenPage(page);
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
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
    }

    private void ShowGameManager(object? sender, EventArgs e) => OpenPage("Game Manager");
    private void ShowImports(object? sender, EventArgs e) => OpenPage("Import Files");
    private void ShowIntegration(object? sender, EventArgs e) => OpenPage("Integration");
    private void ShowRecovery(object? sender, EventArgs e) => OpenPage("Recovery");
    private void ShowLogs(object? sender, EventArgs e) => OpenPage("Logs");
    private void ShowSettings(object? sender, EventArgs e) => OpenPage("Settings");

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
            if (!floatingIcon.IsVisible) floatingIcon.Show();
        }
    }

    private void ExitApplication(object? sender, EventArgs e)
        => Exit();
}

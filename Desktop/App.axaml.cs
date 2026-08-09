using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Trionine.TOST.Desktop.Views;

namespace Trionine.TOST.Desktop;

public sealed partial class App : Application
{
    internal bool IsExiting { get; private set; }
    private FloatingIconWindow? floatingIcon;
    private readonly Dictionary<string, Window> toolWindows = new(StringComparer.Ordinal);

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            ApplyPreferences();
            if (floatingIcon is not null) desktop.MainWindow = floatingIcon;
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void ShowMainWindow(object? sender, EventArgs e)
        => ShowMainWindow();

    internal void ShowMainWindow()
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

    internal void OpenPage(string page)
    {
        if (toolWindows.TryGetValue(page, out var existing))
        {
            existing.Show();
            existing.Activate();
            return;
        }
        Control content = page switch
        {
            "Game Manager" => new GameManagerView(),
            "Import Files" => new ImportView(),
            "Integration" => new IntegrationView(),
            "Recovery" => new RecoveryView(),
            "Logs" => new LogsView(),
            "Settings" => new SettingsView(),
            _ => throw new ArgumentOutOfRangeException(nameof(page))
        };
        var window = new Window
        {
            Title = $"TOST — {page}",
            Width = page == "Logs" ? 900 : page == "Game Manager" ? 820 : 760,
            Height = page == "Game Manager" ? 650 : 620,
            MinWidth = 620,
            MinHeight = 480,
            Background = Avalonia.Media.Brush.Parse("#101311"),
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new ScrollViewer { Margin = new Thickness(22), Content = content }
        };
        window.Closed += (_, _) => toolWindows.Remove(page);
        toolWindows[page] = window;
        window.Show();
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
        foreach (var window in toolWindows.Values.ToArray()) window.Close();
        toolWindows.Clear();
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

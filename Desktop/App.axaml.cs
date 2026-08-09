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
            DesktopPaths.Initialize();
            ApplyPreferences();
            if (floatingIcon is not null)
            {
                desktop.MainWindow = floatingIcon;
            }
        }

        var preferences = DesktopPaths.PreferencesStore.Load();
        if (floatingIcon is not null &&
            preferences.AutomaticallyCheckForUpdates &&
            (!preferences.LastUpdateCheckUtc.HasValue || DateTime.UtcNow - preferences.LastUpdateCheckUtc.Value >= TimeSpan.FromHours(24)))
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                await floatingIcon.CheckForUpdatesAsync(silentWhenCurrent: true));
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

    internal void ActivateExistingInstance() => ShowFloatingIcon();

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
            OperatingSystem.IsWindows() ? 315 : 300,
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

    private static Window CreateToolWindow(string title, double width, double height, Control content)
    {
        var window = new Window
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
            WindowDecorations = Avalonia.Controls.WindowDecorations.None
        };
        var close = new Button
        {
            Content = "X",
            Width = 38,
            Height = 32,
            Padding = new Thickness(0),
            Background = Brushes.Transparent
        };
        close.Click += (_, _) => window.Close();
        var header = new Grid
        {
            Height = 38,
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Background = Brush.Parse("#232426"),
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    Margin = new Thickness(12, 0),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    FontSize = 13
                },
                close
            }
        };
        Grid.SetColumn(close, 1);
        header.PointerPressed += (_, args) =>
        {
            if (args.GetCurrentPoint(window).Properties.IsLeftButtonPressed)
            {
                window.BeginMoveDrag(args);
            }
        };
        var body = new Border
        {
            Padding = new Thickness(8, 0, 8, 8),
            BorderBrush = Brush.Parse("#414346"),
            BorderThickness = new Thickness(1),
            Child = content
        };
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children = { header, body }
        };
        Grid.SetRow(body, 1);
        window.Content = root;
        return window;
    }

    private async void InstallIntegration(object? sender, EventArgs e)
    {
        ShowFloatingIcon();
        if (floatingIcon is not null)
        {
            await floatingIcon.InstallOrRepairIntegrationAsync();
        }
    }

    private async void CheckForUpdates(object? sender, EventArgs e)
    {
        ShowFloatingIcon();
        if (floatingIcon is not null)
        {
            await floatingIcon.CheckForUpdatesAsync(silentWhenCurrent: false);
        }
    }

    private void ExitApplication(object? sender, EventArgs e) => Exit();
}

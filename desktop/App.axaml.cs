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

        try
        {
            gameManagerWindow = CreateToolWindow(
                "TOST Game Manager",
                840,
                540,
                new GameManagerView());
            gameManagerWindow.Closed += (_, _) => gameManagerWindow = null;
            gameManagerWindow.Show();
        }
        catch (Exception ex)
        {
            Services.DesktopLog.Error($"Game Manager could not open: {ex}");
            gameManagerWindow = null;
            if (floatingIcon is not null)
            {
                _ = TostDialog.ShowAsync(
                    floatingIcon,
                    "TOST Game Manager",
                    $"Game Manager could not open. No Steam files were changed.{Environment.NewLine}{Environment.NewLine}{ex.Message}");
            }
        }
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
            OperatingSystem.IsWindows() ? 390 : 340,
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
            Icon = LoadAppIcon(),
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
            Content = new TextBlock
            {
                Text = "\u00D7",
                FontSize = 18,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Foreground = Brush.Parse("#8A9A9F")
            },
            Width = 38,
            Height = 38,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        close.Click += (_, _) => window.Close();

        var titleStack = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(12, 0)
        };
        try
        {
            using var stream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://TOST.Desktop/Assets/TOST.png"));
            titleStack.Children.Add(new Image
            {
                Source = new Avalonia.Media.Imaging.Bitmap(stream),
                Width = 18,
                Height = 18,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            });
        }
        catch { }

        titleStack.Children.Add(new TextBlock
        {
            Text = title,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            FontSize = 13,
            Foreground = Brush.Parse("#B8C8C0"),
            FontWeight = Avalonia.Media.FontWeight.Medium
        });

        var header = new Grid
        {
            Height = 38,
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Background = Brush.Parse("#1C2022"),
            Children = { titleStack, close }
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

    private static WindowIcon? LoadAppIcon()
    {
        try
        {
            using var stream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://TOST.Desktop/Assets/TOST.png"));
            return new WindowIcon(stream);
        }
        catch
        {
            return null;
        }
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

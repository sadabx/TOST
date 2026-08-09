using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Trionine.TOST.Core.Integrations.SlsSteam;
using Trionine.TOST.Core.Steam;
using Trionine.TOST.Desktop.Services;
using Trionine.TOST.Desktop.Views;

namespace Trionine.TOST.Desktop;

internal sealed class FloatingIconWindow : Window
{
    private const string SlsSteamReleasesUrl = "https://github.com/AceSLS/SLSsteam/releases";
    private const string ManifestHubUrl = "https://manifesthub.trionine.com/";
    private const string TostReleasesUrl = "https://github.com/sadabx/TOST/releases/latest";

    public FloatingIconWindow(bool alwaysOnTop)
    {
        Width = Height = 52;
        MinWidth = MinHeight = 52;
        MaxWidth = MaxHeight = 52;
        CanResize = false;
        ShowInTaskbar = false;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Topmost = alwaysOnTop;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Opened += (_, _) =>
        {
            if (Screens.Primary?.WorkingArea is { } area)
            {
                Position = new PixelPoint(area.Right - (int)Width - 18, area.Y + 18);
            }
        };

        var surface = new Border
        {
            Width = 50,
            Height = 50,
            CornerRadius = new CornerRadius(25),
            Background = Brush.Parse("#24282A"),
            BorderBrush = Brush.Parse("#41474C"),
            BorderThickness = new Thickness(1),
            Child = new Image
            {
                Source = new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(
                    new Uri("avares://TOST.Desktop/Assets/TOST.png"))),
                Width = 42,
                Height = 42,
                Stretch = Stretch.Uniform
            },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        surface.PointerPressed += (_, args) =>
        {
            if (args.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(args);
            }
        };
        surface.DoubleTapped += async (_, _) => await RestartSteamAsync();
        ToolTip.SetTip(surface, "TOST — drag to move, right-click for menu");
        surface.ContextMenu = BuildMenu();
        Content = surface;
    }

    internal async Task InstallOrRepairSlsSteamAsync()
    {
        var steam = PreferredSteamInstallation();
        if (steam is null)
        {
            await TostDialog.ShowAsync(this, "Install SLSsteam", "No native or Flatpak Steam installation was detected.");
            return;
        }

        if (!await TostDialog.ConfirmAsync(
                this,
                "Install / Repair SLSsteam",
                $"Download and verify the latest official SLSsteam release for {steam.Kind} Steam?",
                "Install"))
        {
            return;
        }

        try
        {
            var paths = PathsFor(steam.Kind);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            var release = await new SlsSteamReleaseService(client).GetLatestAsync();
            var installer = new SlsSteamInstallerService(client);
            var preview = installer.Preview(release, paths);
            if (!preview.CanInstall)
            {
                await TostDialog.ShowAsync(this, "Install / Repair SLSsteam", preview.BlockReason ?? "SLSsteam cannot be installed safely.");
                return;
            }

            var result = await installer.InstallAsync(release, paths);
            await TostDialog.ShowAsync(
                this,
                "Install / Repair SLSsteam",
                $"Installed {result.Tag} successfully. Restart Steam to apply it.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await TostDialog.ShowAsync(this, "Install / Repair SLSsteam", $"Installation failed: {ex.Message}");
        }
    }

    internal static void OpenWebsite(string url)
    {
        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo(url) { UseShellExecute = true }
            : new ProcessStartInfo("xdg-open") { UseShellExecute = false, ArgumentList = { url } };
        Process.Start(startInfo);
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu
        {
            MinWidth = 292
        };
        menu.Items.Add(Item("Launch Steam", "▷", LaunchSteam));
        menu.Items.Add(Item("Restart Steam", "↻", async () => await RestartSteamAsync()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Install / Repair SLSsteam", "⇩", async () => await InstallOrRepairSlsSteamAsync()));
        menu.Items.Add(Item("View SLSsteam Releases", "◎", () => OpenWebsite(SlsSteamReleasesUrl)));
        menu.Items.Add(Item("Open ManifestHub", "◎", () => OpenWebsite(ManifestHubUrl)));
        menu.Items.Add(CreateFolderMenu());
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Manage Games", "▣", () => App()?.ShowGameManager()));
        menu.Items.Add(Item("TOST Settings", "⚙", () => App()?.ShowSettings()));
        menu.Items.Add(Item("Check for Updates", "↻", () => OpenWebsite(TostReleasesUrl)));
        menu.Items.Add(Item("Open Logs", "▧", OpenLogs));
        menu.Items.Add(Item("Hide Floating Icon", "◉", () => App()?.HideFloatingIcon()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Exit", "◯", () => App()?.Exit()));
        return menu;
    }

    private MenuItem CreateFolderMenu()
    {
        var folders = Item("Open Steam Folder", "□");
        var steam = PreferredSteamInstallation();
        if (steam is null)
        {
            folders.Items.Add(new MenuItem { Header = "Steam installation not found", IsEnabled = false });
            return folders;
        }

        folders.Items.Add(Item("Steam Folder", "□", () => OpenFolder(steam.RootPath)));
        folders.Items.Add(Item("Steam Config", "⚙", () => OpenFolder(steam.ConfigPath)));
        folders.Items.Add(Item("Steam Manifests", "□", () => OpenFolder(steam.DepotCachePath)));
        folders.Items.Add(Item("Steam Apps", "□", () => OpenFolder(steam.SteamAppsPath)));
        folders.Items.Add(Item("Steam User Data", "□", () => OpenFolder(Path.Combine(steam.RootPath, "userdata"))));
        return folders;
    }

    private void LaunchSteam()
    {
        try
        {
            var plan = CreateSteamPlan();
            Start(plan.Launch);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _ = TostDialog.ShowAsync(this, "Launch Steam", $"Could not launch Steam: {ex.Message}");
        }
    }

    private async Task RestartSteamAsync()
    {
        try
        {
            var plan = CreateSteamPlan();
            if (!await TostDialog.ConfirmAsync(
                    this,
                    "Restart Steam",
                    "Ask Steam to shut down normally, wait briefly, and relaunch it? Close any running games first.",
                    "Restart"))
            {
                return;
            }

            await new SteamLifecycleService().RestartAsync(plan);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            await TostDialog.ShowAsync(this, "Restart Steam", $"Could not restart Steam: {ex.Message}");
        }
    }

    private void OpenLogs()
    {
        try
        {
            var steam = PreferredSteamInstallation();
            var paths = PathsFor(steam?.Kind ?? SteamInstallationKind.Native);
            var log = paths.LogPaths.FirstOrDefault(File.Exists);
            OpenFolder(log is null ? DesktopPaths.DataRoot : Path.GetDirectoryName(log)!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _ = TostDialog.ShowAsync(this, "Open Logs", $"Could not open the logs folder: {ex.Message}");
        }
    }

    private void OpenFolder(string path)
    {
        try
        {
            FolderLauncher.Open(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _ = TostDialog.ShowAsync(this, "Open Steam Folder", $"Could not open the folder: {ex.Message}");
        }
    }

    private static SteamRestartPlan CreateSteamPlan()
    {
        var kind = PreferredSteamInstallation()?.Kind ?? DesktopPaths.PreferencesStore.Load().PreferredSteamInstallation;
        return new SteamRestartService().CreatePlan(kind);
    }

    private static SteamInstallation? PreferredSteamInstallation()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        var installations = LinuxSteamDiscovery.FindInstallations();
        var preferred = DesktopPaths.PreferencesStore.Load().PreferredSteamInstallation;
        return installations.FirstOrDefault(item => item.Kind == preferred) ?? installations.FirstOrDefault();
    }

    private static SlsSteamPaths PathsFor(SteamInstallationKind kind) =>
        kind == SteamInstallationKind.Flatpak ? SlsSteamPaths.ForFlatpakUser() : SlsSteamPaths.ForCurrentUser();

    private static void Start(SteamCommand command)
    {
        var startInfo = new ProcessStartInfo(command.Executable) { UseShellExecute = false };
        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process.Start(startInfo);
    }

    private static MenuItem Item(string header, string glyph, Action? action = null)
    {
        var layout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("30,*"),
            MinWidth = 240
        };
        layout.Children.Add(new TextBlock
        {
            Text = glyph,
            Width = 24,
            FontSize = 19,
            Foreground = Brush.Parse("#AEB4B8"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        var label = new TextBlock
        {
            Text = header,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 1);
        layout.Children.Add(label);

        var item = new MenuItem
        {
            Header = layout,
            Height = 38,
            Padding = new Thickness(8, 2)
        };
        if (action is not null)
        {
            item.Click += (_, _) => action();
        }

        return item;
    }

    private static App? App() => Application.Current as App;
}

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Trionine.TOST.Core.Integrations.OpenSteamTool;
using Trionine.TOST.Core.Integrations.SlsSteam;
using Trionine.TOST.Core.Steam;
using Trionine.TOST.Desktop.Services;
using Trionine.TOST.Desktop.Views;

namespace Trionine.TOST.Desktop;

internal sealed class FloatingIconWindow : Window
{
    private const string ManifestHubUrl = "https://manifesthub.trionine.com/";
    private readonly Border surface;
    private DropToastWindow? activeToast;

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
        Opened += (_, _) => PositionAtTopRight();

        surface = new Border
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
        ToolTip.SetTip(surface, "TOST - drag files to import, drag the icon to move, right-click for menu");
        surface.ContextMenu = BuildMenu();
        DragDrop.SetAllowDrop(surface, true);
        DragDrop.AddDragOverHandler(surface, OnDragOver);
        DragDrop.AddDragLeaveHandler(surface, (_, _) => SetDropHighlight(false));
        DragDrop.AddDropHandler(surface, OnDrop);
        Content = surface;
    }

    internal async Task InstallOrRepairIntegrationAsync()
    {
        var steam = DesktopPlatform.PreferredInstallation();
        if (steam is null)
        {
            await TostDialog.ShowAsync(this, $"Install {DesktopPlatform.IntegrationName}", "No Steam installation was detected. Check the Steam folder in TOST Settings.");
            return;
        }

        if (!await TostDialog.ConfirmAsync(
                this,
                $"Install / Repair {DesktopPlatform.IntegrationName}",
                $"Download and install the latest official {DesktopPlatform.IntegrationName} release?",
                "Install"))
        {
            return;
        }

        try
        {
            if (DesktopPlatform.UsesOpenSteamTool)
            {
                var preferences = DesktopPaths.PreferencesStore.Load();
                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                var result = await new OpenSteamToolInstallerService(client).InstallLatestAsync(
                    steam,
                    preferences.OverwriteExistingFiles,
                    preferences.BackupFilesBeforeOverwrite);
                DesktopLog.Info(result.ToMessage());
                await TostDialog.ShowAsync(this, "Install / Repair OpenSteamTool", result.ToMessage());
                return;
            }

            var paths = PathsFor(steam.Kind);
            using var slsClient = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            var release = await new SlsSteamReleaseService(slsClient).GetLatestAsync();
            var installer = new SlsSteamInstallerService(slsClient);
            var preview = installer.Preview(release, paths, allowRepair: true);
            if (!preview.CanInstall)
            {
                await TostDialog.ShowAsync(this, "Install / Repair SLSsteam", preview.BlockReason ?? "SLSsteam cannot be installed safely.");
                return;
            }

            var installed = await installer.InstallAsync(release, paths, repairExisting: true);
            var launchPlan = SlsSteamLaunchPlanFactory.Create(steam.Kind == SteamInstallationKind.Flatpak, paths);
            if (!launchPlan.CanApply)
            {
                throw new IOException("SLSsteam was installed, but an existing unmanaged Steam launch hook prevents automatic activation.");
            }

            if (launchPlan.HasChanges)
            {
                new SlsSteamLaunchConfigurationService().Apply(launchPlan);
            }

            DesktopLog.Info($"Installed SLSsteam {installed.Tag} successfully and configured Steam launch injection.");
            await TostDialog.ShowAsync(this, "Install / Repair SLSsteam", $"Installed {installed.Tag} successfully. Restart Steam to apply it.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            DesktopLog.Error($"{DesktopPlatform.IntegrationName} installation failed: {ex}");
            await TostDialog.ShowAsync(this, $"Install / Repair {DesktopPlatform.IntegrationName}", $"Installation failed: {ex.Message}");
        }
    }

    internal async Task CheckForUpdatesAsync(bool silentWhenCurrent)
    {
        try
        {
            var updater = new TostUpdateService();
            var result = await updater.CheckAsync();
            var preferences = DesktopPaths.PreferencesStore.Load();
            DesktopPaths.PreferencesStore.Save(preferences with { LastUpdateCheckUtc = DateTime.UtcNow });
            if (result.InstalledBuild)
            {
                if (result.State is null)
                {
                    if (!silentWhenCurrent)
                    {
                        await TostDialog.ShowAsync(this, "TOST Updates", "TOST is up to date.");
                    }

                    return;
                }

                if (await TostDialog.ConfirmAsync(
                        this,
                        "TOST Update Available",
                        $"TOST {result.Version} is available. Download it now and restart TOST?",
                        "Update"))
                {
                    await updater.DownloadAndApplyAsync(result);
                }

                return;
            }

            if (!silentWhenCurrent && OperatingSystem.IsWindows())
            {
                await TostDialog.ShowAsync(
                    this,
                    "TOST Updates",
                    "Automatic updates are available in the installed TOST build. Download TOST Setup from Releases to switch from a raw or portable build.");
            }
            else if (!silentWhenCurrent)
            {
                OpenWebsite("https://github.com/sadabx/TOST/releases/latest");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            DesktopLog.Error($"TOST update check failed: {ex}");
            if (!silentWhenCurrent)
            {
                await TostDialog.ShowAsync(this, "TOST Updates", $"Could not check for updates: {ex.Message}");
            }
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
        var menu = new ContextMenu { MinWidth = 292 };
        menu.Items.Add(Item("Launch Steam", "\u25B7", LaunchSteam));
        menu.Items.Add(Item("Restart Steam", "\u21BB", async () => await RestartSteamAsync()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item($"Install / Repair {DesktopPlatform.IntegrationName}", "\u21E9", async () => await InstallOrRepairIntegrationAsync()));
        menu.Items.Add(Item($"View {DesktopPlatform.IntegrationName} Releases", "\u25CE", () => OpenWebsite(DesktopPlatform.IntegrationReleasesUrl)));
        menu.Items.Add(Item("Open ManifestHub", "\u25CE", () => OpenWebsite(ManifestHubUrl)));
        menu.Items.Add(CreateFolderMenu());
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Manage Games", "\u25A3", () => App()?.ShowGameManager()));
        menu.Items.Add(Item("TOST Settings", "\u2699", () => App()?.ShowSettings()));
        menu.Items.Add(Item("Check for Updates", "\u21BB", async () => await CheckForUpdatesAsync(false)));
        menu.Items.Add(Item("Open Logs", "\u25A7", OpenLogs));
        menu.Items.Add(Item("Hide Floating Icon", "\u25C9", () => App()?.HideFloatingIcon()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Exit", "\u25EF", () => App()?.Exit()));
        return menu;
    }

    private MenuItem CreateFolderMenu()
    {
        var folders = Item("Open Steam Folder", "\u25A1");
        var steam = DesktopPlatform.PreferredInstallation();
        if (steam is null)
        {
            folders.Items.Add(new MenuItem { Header = "Steam installation not found", IsEnabled = false });
            return folders;
        }

        folders.Items.Add(Item("Steam Folder", "\u25A1", () => OpenFolder(steam.RootPath)));
        folders.Items.Add(Item("Steam Config", "\u2699", () => OpenFolder(steam.ConfigPath)));
        folders.Items.Add(Item("Steam Manifests", "\u25A1", () => OpenFolder(steam.ManagedManifestsPath)));
        folders.Items.Add(Item("Steam Apps", "\u25A1", () => OpenFolder(steam.CommonAppsPath)));
        folders.Items.Add(Item("Steam User Data", "\u25A1", () => OpenFolder(steam.UserDataPath)));
        return folders;
    }

    private void LaunchSteam()
    {
        try
        {
            Start(CreateSteamPlan().Launch);
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
            if (!await TostDialog.ConfirmAsync(
                    this,
                    "Restart Steam",
                    "Ask Steam to shut down normally, wait briefly, and relaunch it? Close any running games first.",
                    "Restart"))
            {
                return;
            }

            await new SteamLifecycleService().RestartAsync(CreateSteamPlan());
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
            Directory.CreateDirectory(DesktopPaths.LogDirectory);
            OpenFolder(DesktopPaths.LogDirectory);
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
        var steam = DesktopPlatform.PreferredInstallation()
            ?? throw new DirectoryNotFoundException("No Steam installation was detected.");
        var plan = new SteamRestartService().CreatePlan(steam.Kind, steamRoot: steam.RootPath);
        if (steam.Kind == SteamInstallationKind.Native)
        {
            var wrapper = Path.Combine(SlsSteamPaths.ForCurrentUser().DataDirectory, "path", "steam");
            if (File.Exists(wrapper))
            {
                return plan with
                {
                    Shutdown = new SteamCommand(wrapper, ["-shutdown"]),
                    Launch = new SteamCommand(wrapper, [])
                };
            }
        }

        return plan;
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
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("30,*"), MinWidth = 240 };
        layout.Children.Add(new TextBlock
        {
            Text = glyph,
            Width = 24,
            FontSize = 19,
            Foreground = Brush.Parse("#AEB4B8"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        var label = new TextBlock { Text = header, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(label, 1);
        layout.Children.Add(label);
        var item = new MenuItem { Header = layout, Height = 38, Padding = new Thickness(8, 2) };
        if (action is not null)
        {
            item.Click += (_, _) => action();
        }

        return item;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var hasFiles = e.DataTransfer.TryGetFiles()?.Any() == true;
        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        SetDropHighlight(hasFiles);
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        SetDropHighlight(false);
        var paths = e.DataTransfer.TryGetFiles()?
            .Select(item => item.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray() ?? [];
        if (paths.Length == 0)
        {
            return;
        }

        var steam = DesktopPlatform.PreferredInstallation();
        if (steam is null)
        {
            await TostDialog.ShowAsync(this, "Import Files", "No Steam installation was detected. Check TOST Settings.");
            return;
        }

        DesktopImportSummary summary;
        if (DesktopPlatform.UsesOpenSteamTool)
        {
            var settings = DesktopPaths.PreferencesStore.Load();
            using var client = new HttpClient();
            var result = new OpenSteamToolInstallerService(client).Import(
                steam,
                paths,
                settings.OverwriteExistingFiles,
                settings.BackupFilesBeforeOverwrite);
            summary = new DesktopImportSummary(
                result.ImportedCount,
                result.Files.Where(file => !file.Success).Select(file => $"{file.Name}: {file.Error}").ToArray());
        }
        else
        {
            summary = DesktopPlatform.ImportLinuxFiles(steam, paths);
        }

        DesktopLog.Info(summary.ToMessage());
        ShowDropToast(summary);
    }

    private void ShowDropToast(DesktopImportSummary summary)
    {
        activeToast?.Close();
        activeToast = new DropToastWindow(summary);
        activeToast.Closed += (_, _) => activeToast = null;
        activeToast.Show(this);
        activeToast.PositionNextTo(this);
    }

    private void SetDropHighlight(bool active)
    {
        surface.Background = Brush.Parse(active ? "#363E45" : "#24282A");
        surface.BorderBrush = Brush.Parse(active ? "#66C0F4" : "#41474C");
        surface.BorderThickness = new Thickness(active ? 2 : 1);
    }

    private void PositionAtTopRight()
    {
        if (Screens.Primary?.WorkingArea is { } area)
        {
            Position = new PixelPoint(area.Right - (int)Width - 18, area.Y + 18);
        }
    }

    private static App? App() => Application.Current as App;
}

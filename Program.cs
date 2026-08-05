using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.Win32;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace Trionine.TOST;

internal static class Program
{
    private const string InstanceMutexName = @"Local\Trionine.TOST.Instance";
    private const string ActivationEventName = @"Local\Trionine.TOST.Activate";

    [STAThread]
    private static void Main()
    {
        VelopackApp.Build().Run();

        using var activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            ActivationEventName);
        using var instanceMutex = new Mutex(
            initiallyOwned: true,
            InstanceMutexName,
            out var isFirstInstance);

        if (!isFirstInstance)
        {
            activationEvent.Set();
            return;
        }

        AppPaths.Initialize();
        ApplicationConfiguration.Initialize();
        using var form = new FloatingInstallerForm();
        _ = form.Handle;

        var activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            activationEvent,
            (_, _) => form.ActivateExistingInstance(),
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        Application.Run(form);
        activationRegistration.Unregister(null);
    }
}

internal sealed class FloatingInstallerForm : Form
{
    private const string UpstreamReleasesUrl = "https://github.com/OpenSteam001/OpenSteamTool/releases";
    private const string UpstreamLatestReleaseUrl = "https://github.com/OpenSteam001/OpenSteamTool/releases/latest";
    private const string TostUpdateUrl = "https://github.com/sadabx/TOST/releases/latest/download";
    private const string ManifestHubUrl = "https://manifesthub.trionine.com/";
    private const long MaxArchiveEntryBytes = 256L * 1024 * 1024;
    private const long MaxArchivePayloadBytes = 512L * 1024 * 1024;
    private const long MaxUpstreamDownloadBytes = 512L * 1024 * 1024;
    private const int MaxArchiveEntries = 10_000;
    private static readonly string? SymbolFontFamilyName = FindSymbolFontFamily();
    private static readonly HttpClient UpstreamHttpClient = CreateUpstreamHttpClient();
    private readonly InstallerSettings settings;
    private readonly InstallerLogger logger;
    private readonly FloatingIconSurface glyph = new();
    private readonly ToolTip toolTip = new();
    private readonly NotifyIcon trayIcon;
    private DropToastForm? activeToast;
    private bool isInstallingOpenSteamTool;

    public FloatingInstallerForm()
    {
        settings = InstallerSettings.Load();
        logger = new InstallerLogger(settings.LogPath);

        Text = "TOST";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(52, 52);
        MinimumSize = Size;
        MaximumSize = Size;
        TopMost = settings.AlwaysOnTop;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(43, 45, 48);
        AllowDrop = true;
        ContextMenuStrip = BuildMenu();
        Region = CreateRoundedRegion(ClientRectangle, 8);

        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Location = new Point(screen.Left + Math.Max(24, (int)(screen.Width * 0.24)), screen.Top + 116);

        glyph.Dock = DockStyle.Fill;
        glyph.Logo = LoadLogo();
        glyph.AllowDrop = true;
        glyph.ContextMenuStrip = ContextMenuStrip;
        Controls.Add(glyph);

        toolTip.SetToolTip(glyph, "TOST");

        DragEnter += OnDragEnter;
        DragLeave += OnDragLeave;
        DragDrop += OnDragDrop;
        glyph.DragEnter += OnDragEnter;
        glyph.DragLeave += OnDragLeave;
        glyph.DragDrop += OnDragDrop;
        glyph.DoubleClick += (_, _) => RestartSteam();

        EnableWindowDrag(this);
        EnableWindowDrag(glyph);

        trayIcon = new NotifyIcon
        {
            Icon = Icon ?? SystemIcons.Application,
            Text = "TOST",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        trayIcon.DoubleClick += (_, _) => ShowFloatingWindow();

        Shown += async (_, _) =>
        {
            EnsureVisibleOnScreen();
            BringToFront();
            Activate();
            SetStartupRegistration(settings.StartWithWindows);

            if (settings.ShouldCheckForUpdates())
            {
                await CheckForUpdatesAsync(silentWhenCurrent: true);
            }
        };

        logger.Info($"TOST started in {(AppPaths.IsPortable ? "portable" : "installed")} mode.");
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = CreateDarkMenu();
        menu.Items.Add(CreateMenuItem("Launch Steam", "\uE768", (_, _) => LaunchSteam()));
        menu.Items.Add(CreateMenuItem("Restart Steam", "\uE72C", (_, _) => RestartSteam()));
        menu.Items.Add(CreateSeparator());
        menu.Items.Add(CreateMenuItem("Install / Repair OpenSteamTool", "\uE896", async (_, _) => await InstallOrRepairAsync()));
        menu.Items.Add(CreateMenuItem("View OpenSteamTool Releases", "\uE774", (_, _) => OpenOfficialReleases()));
        menu.Items.Add(CreateMenuItem("Open ManifestHub", "\uE774", (_, _) => OpenManifestHub()));

        var folders = CreateMenuItem("Open Steam Folder", "\uE8B7");
        folders.DropDownItems.Add(CreateMenuItem("Steam Folder", "\uE8B7", (_, _) => OpenFolder(settings.SteamRoot), 184));
        folders.DropDownItems.Add(CreateMenuItem("Steam Config", "\uE713", (_, _) => OpenFolder(settings.SteamConfigPath), 184));
        folders.DropDownItems.Add(CreateMenuItem("Steam Manifests", "\uE8B7", (_, _) => OpenFolder(settings.SteamAppsPath), 184));
        folders.DropDownItems.Add(CreateMenuItem("Steam Apps", "\uE8B7", (_, _) => OpenFolder(settings.SteamCommonPath), 184));
        folders.DropDownItems.Add(CreateMenuItem("Steam User Data", "\uE8B7", (_, _) => OpenFolder(settings.SteamUserDataPath), 184));
        menu.Items.Add(folders);

        menu.Items.Add(CreateSeparator());
        menu.Items.Add(CreateMenuItem("TOST Settings", "\uE713", (_, _) => OpenSettings()));
        menu.Items.Add(CreateMenuItem("Check for Updates", "\uE895", async (_, _) => await CheckForUpdatesAsync(false)));
        menu.Items.Add(CreateMenuItem("Open Logs", "\uE9D9", (_, _) => OpenFolder(settings.LogDirectory)));
        menu.Items.Add(CreateMenuItem("Hide Floating Icon", "\uED1A", (_, _) => Hide()));
        menu.Items.Add(CreateSeparator());
        menu.Items.Add(CreateMenuItem("Exit", "\uE7E8", (_, _) => Close()));
        StyleDropDowns(menu.Items);
        return menu;
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = CreateDarkMenu();
        menu.Items.Add(CreateMenuItem("Show Floating Icon", "\uE890", (_, _) => ShowFloatingWindow()));
        menu.Items.Add(CreateMenuItem("Install / Repair OpenSteamTool", "\uE896", async (_, _) => await InstallOrRepairAsync()));
        menu.Items.Add(CreateMenuItem("Check for Updates", "\uE895", async (_, _) => await CheckForUpdatesAsync(false)));
        menu.Items.Add(CreateSeparator());
        menu.Items.Add(CreateMenuItem("Exit", "\uE7E8", (_, _) => Close()));
        return menu;
    }

    private static ContextMenuStrip CreateDarkMenu()
    {
        return new ContextMenuStrip
        {
            AutoSize = true,
            MinimumSize = new Size(244, 0),
            BackColor = Color.FromArgb(36, 36, 36),
            ForeColor = Color.FromArgb(226, 229, 232),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            ImageScalingSize = new Size(18, 18),
            Padding = new Padding(5, 7, 5, 7),
            ShowImageMargin = true,
            Renderer = new SteamStyleMenuRenderer()
        };
    }

    private static ToolStripMenuItem CreateMenuItem(string text, string glyphText, EventHandler? click = null, int width = 232)
    {
        var item = new ToolStripMenuItem(text)
        {
            AutoSize = false,
            Size = new Size(width, 38),
            ForeColor = Color.FromArgb(226, 229, 232),
            Image = CreateMenuIcon(glyphText),
            ImageScaling = ToolStripItemImageScaling.None,
            Padding = new Padding(2, 0, 4, 0)
        };

        if (click is not null)
        {
            item.Click += click;
        }

        return item;
    }

    private static ToolStripSeparator CreateSeparator()
    {
        return new ToolStripSeparator
        {
            AutoSize = false,
            Size = new Size(232, 9)
        };
    }

    private static Bitmap CreateMenuIcon(string glyphText)
    {
        // Keep a one-pixel safety edge so symbol-font overhangs are not clipped.
        var bitmap = new Bitmap(22, 22);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        using var font = CreateSymbolFont(14f);
        using var brush = new SolidBrush(Color.FromArgb(151, 157, 164));
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        graphics.DrawString(glyphText, font, brush, new RectangleF(1, 1, 20, 20), format);
        return bitmap;
    }

    private static Font CreateSymbolFont(float size)
    {
        if (SymbolFontFamilyName is not null)
        {
            return new Font(SymbolFontFamilyName, size, FontStyle.Regular, GraphicsUnit.Point);
        }

        return new Font(FontFamily.GenericSansSerif, size, FontStyle.Regular, GraphicsUnit.Point);
    }

    private static string? FindSymbolFontFamily()
    {
        var installedFamilies = FontFamily.Families
            .Select(family => family.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new[] { "Segoe MDL2 Assets", "Segoe Fluent Icons", "Segoe UI Symbol" }
            .FirstOrDefault(installedFamilies.Contains);
    }

    private static void StyleDropDowns(ToolStripItemCollection items)
    {
        foreach (ToolStripItem toolStripItem in items)
        {
            if (toolStripItem is not ToolStripMenuItem item || item.DropDownItems.Count == 0)
            {
                continue;
            }

            item.DropDown.AutoSize = true;
            item.DropDown.MinimumSize = new Size(196, 0);
            item.DropDown.BackColor = Color.FromArgb(36, 36, 36);
            item.DropDown.ForeColor = Color.FromArgb(226, 229, 232);
            item.DropDown.Padding = new Padding(5, 7, 5, 7);
            item.DropDown.Renderer = new SteamStyleMenuRenderer();
            StyleDropDowns(item.DropDownItems);
        }
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        glyph.IsDropTarget = e.Effect == DragDropEffects.Copy;
    }

    private void OnDragLeave(object? sender, EventArgs e)
    {
        glyph.IsDropTarget = false;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        glyph.IsDropTarget = false;
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return;
        }

        var report = new CopyReport();
        foreach (var path in paths)
        {
            CopyExpectedPath(path, report);
        }

        ShowDropToast(report);
    }

    private void ShowDropToast(CopyReport report)
    {
        logger.Info(report.ToLogMessage());
        activeToast?.Close();
        activeToast = new DropToastForm(report);
        activeToast.FormClosed += (_, _) => activeToast = null;

        var workingArea = Screen.FromControl(this).WorkingArea;
        var toastX = Right + 8;
        if (toastX + activeToast.Width > workingArea.Right)
        {
            toastX = Left - activeToast.Width - 8;
        }

        var toastY = Top + ((Height - activeToast.Height) / 2);
        toastX = Math.Clamp(toastX, workingArea.Left, workingArea.Right - activeToast.Width);
        toastY = Math.Clamp(toastY, workingArea.Top, workingArea.Bottom - activeToast.Height);
        activeToast.Location = new Point(toastX, toastY);
        activeToast.Show(this);
    }

    private async Task InstallOrRepairAsync()
    {
        if (isInstallingOpenSteamTool)
        {
            return;
        }

        isInstallingOpenSteamTool = true;
        UseWaitCursor = true;
        string? temporaryArchivePath = null;
        var report = new CopyReport();

        try
        {
            var release = await ResolveLatestOpenSteamToolReleaseAsync();
            temporaryArchivePath = Path.Combine(
                Path.GetTempPath(),
                $"TOST-{release.AssetName}-{Guid.NewGuid():N}.zip");

            logger.Info($"Downloading OpenSteamTool {release.Tag} from {release.DownloadUri}");
            await DownloadFileAsync(release.DownloadUri, temporaryArchivePath, MaxUpstreamDownloadBytes);

            EnsureSteamFolders(report);
            InstallFromZip(temporaryArchivePath, report);
            logger.Info($"Finished OpenSteamTool {release.Tag} install/repair.");
        }
        catch (Exception ex)
        {
            report.AddFailure("OpenSteamTool download", ex.Message);
            logger.Error($"Automatic OpenSteamTool install/repair failed: {ex}");
        }
        finally
        {
            UseWaitCursor = false;
            isInstallingOpenSteamTool = false;

            if (temporaryArchivePath is not null)
            {
                try
                {
                    File.Delete(temporaryArchivePath);
                }
                catch (Exception ex)
                {
                    logger.Error($"Could not remove temporary download {temporaryArchivePath}: {ex}");
                }
            }
        }

        ShowReport(report);
    }

    private static async Task<OpenSteamToolRelease> ResolveLatestOpenSteamToolReleaseAsync()
    {
        using var latestResponse = await UpstreamHttpClient.GetAsync(
            UpstreamLatestReleaseUrl,
            HttpCompletionOption.ResponseHeadersRead);
        latestResponse.EnsureSuccessStatusCode();

        var releaseUri = latestResponse.RequestMessage?.RequestUri
            ?? throw new InvalidDataException("GitHub did not return the latest OpenSteamTool release URL.");
        var pathSegments = releaseUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var tagMarkerIndex = Array.FindIndex(
            pathSegments,
            segment => segment.Equals("tag", StringComparison.OrdinalIgnoreCase));
        if (tagMarkerIndex < 0 || tagMarkerIndex + 1 >= pathSegments.Length)
        {
            throw new InvalidDataException("Could not determine the latest OpenSteamTool release tag.");
        }

        var tag = Uri.UnescapeDataString(pathSegments[tagMarkerIndex + 1]);
        var assetsUri = new Uri(
            $"https://github.com/OpenSteam001/OpenSteamTool/releases/expanded_assets/{Uri.EscapeDataString(tag)}");
        var assetsHtml = await UpstreamHttpClient.GetStringAsync(assetsUri);
        var assetPaths = Regex.Matches(
                assetsHtml,
                "href=\"(?<path>/OpenSteam001/OpenSteamTool/releases/download/[^\"]+\\.zip)\"",
                RegexOptions.IgnoreCase)
            .Select(match => WebUtility.HtmlDecode(match.Groups["path"].Value))
            .Where(path =>
                Path.GetFileName(path).StartsWith("OpenSteamTool-", StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(path).EndsWith("-Release.zip", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (assetPaths.Count != 1)
        {
            throw new InvalidDataException(
                assetPaths.Count == 0
                    ? "The latest OpenSteamTool release does not contain a release ZIP."
                    : "The latest OpenSteamTool release contains multiple matching release ZIPs.");
        }

        var downloadUri = new Uri(new Uri("https://github.com"), assetPaths[0]);
        return new OpenSteamToolRelease(tag, Path.GetFileName(downloadUri.LocalPath), downloadUri);
    }

    private static async Task DownloadFileAsync(Uri sourceUri, string destinationPath, long maximumBytes)
    {
        using var response = await UpstreamHttpClient.GetAsync(
            sourceUri,
            HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is long contentLength &&
            contentLength > maximumBytes)
        {
            throw new InvalidDataException("The OpenSteamTool download is larger than the supported limit.");
        }

        await using var source = await response.Content.ReadAsStreamAsync();
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        var buffer = new byte[81920];
        long bytesCopied = 0;
        int bytesRead;
        while ((bytesRead = await source.ReadAsync(buffer)) > 0)
        {
            if (bytesCopied > maximumBytes - bytesRead)
            {
                throw new InvalidDataException("The OpenSteamTool download is larger than the supported limit.");
            }

            bytesCopied += bytesRead;
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead));
        }
    }

    private static HttpClient CreateUpstreamHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TOST/1.1 (+https://github.com/sadabx/TOST)");
        return client;
    }

    private void InstallFromZip(string archivePath, CopyReport report)
    {
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            if (archive.Entries.Count > MaxArchiveEntries)
            {
                report.AddFailure(
                    Path.GetFileName(archivePath),
                    $"Archive contains too many entries (maximum {MaxArchiveEntries:N0}).");
                return;
            }

            var recognizedEntries = archive.Entries
                .Where(entry => !string.IsNullOrEmpty(entry.Name) && ResolveDestination(entry.Name) is not null)
                .ToList();

            if (recognizedEntries.Count == 0)
            {
                report.AddFailure(Path.GetFileName(archivePath), "Archive contains no supported files.");
                return;
            }

            var duplicateNames = recognizedEntries
                .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();
            if (duplicateNames.Count > 0)
            {
                report.AddFailure(
                    Path.GetFileName(archivePath),
                    $"Archive contains duplicate supported files: {string.Join(", ", duplicateNames)}");
                return;
            }

            if (recognizedEntries.Any(entry => entry.Length > MaxArchiveEntryBytes) ||
                recognizedEntries.Sum(entry => entry.Length) > MaxArchivePayloadBytes)
            {
                report.AddFailure(Path.GetFileName(archivePath), "Archive payload is larger than the supported limit.");
                return;
            }

            long actualPayloadBytes = 0;
            foreach (var entry in recognizedEntries)
            {
                var destinationDirectory = ResolveDestination(entry.Name)!;
                CopyArchiveEntry(entry, destinationDirectory, report, ref actualPayloadBytes);
            }
        }
        catch (InvalidDataException ex)
        {
            report.AddFailure(Path.GetFileName(archivePath), $"Invalid ZIP archive: {ex.Message}");
            logger.Error($"Could not read ZIP archive {archivePath}: {ex}");
        }
        catch (Exception ex)
        {
            report.AddFailure(Path.GetFileName(archivePath), ex.Message);
            logger.Error($"Could not install ZIP archive {archivePath}: {ex}");
        }
    }

    private void CopyArchiveEntry(
        ZipArchiveEntry entry,
        string destinationDirectory,
        CopyReport report,
        ref long actualPayloadBytes)
    {
        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(destinationDirectory);
            var destinationPath = Path.Combine(destinationDirectory, entry.Name);

            // Decompress into a same-directory temporary file first. Besides enforcing
            // the limit against the actual stream (not only the ZIP header), this keeps
            // a failed ZIP import from leaving a partially written Steam file behind.
            temporaryPath = Path.Combine(
                destinationDirectory,
                $".{entry.Name}.{Guid.NewGuid():N}.tost-tmp");
            long bytesCopied = 0;
            using (var source = entry.Open())
            using (var destination = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                options: FileOptions.SequentialScan))
            {
                var buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (bytesCopied > MaxArchiveEntryBytes - bytesRead ||
                        actualPayloadBytes > MaxArchivePayloadBytes - bytesRead)
                    {
                        throw new InvalidDataException("Archive payload exceeds the supported size limit.");
                    }

                    bytesCopied += bytesRead;
                    actualPayloadBytes += bytesRead;
                    destination.Write(buffer, 0, bytesRead);
                }
            }

            if (settings.BackupBeforeOverwrite && File.Exists(destinationPath))
            {
                var backupPath = destinationPath + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(destinationPath, backupPath, overwrite: false);
                logger.Info($"Backed up {destinationPath} -> {backupPath}");
            }

            File.Move(temporaryPath, destinationPath, overwrite: settings.OverwriteExisting);
            temporaryPath = null;

            report.AddSuccess(entry.Name, destinationDirectory);
            logger.Info($"Copied ZIP entry {entry.FullName} -> {destinationPath}");
        }
        catch (Exception ex)
        {
            report.AddFailure(entry.Name, ex.Message);
            logger.Error($"Failed to copy ZIP entry {entry.FullName}: {ex}");
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // Best-effort cleanup; the import failure is already reported.
                }
            }
        }
    }

    private static void OpenOfficialReleases()
    {
        OpenWebsite(UpstreamReleasesUrl);
    }

    private static void OpenManifestHub()
    {
        OpenWebsite(ManifestHubUrl);
    }

    private async Task CheckForUpdatesAsync(bool silentWhenCurrent)
    {
        try
        {
            var source = new SimpleWebSource(TostUpdateUrl);
            var manager = new UpdateManager(source);
            settings.LastUpdateCheckUtc = DateTime.UtcNow;
            settings.Save();

            if (!manager.IsInstalled)
            {
                if (!silentWhenCurrent)
                {
                    TostDialog.Show(
                        this,
                        "Automatic updates are available in the installed TOST build.\n\nDownload TOST Setup from the Releases page to switch from a raw or portable build.",
                        "TOST Updates",
                        TostDialogButtons.Ok,
                        TostDialogIcon.Information);
                }

                return;
            }

            Cursor = Cursors.WaitCursor;
            trayIcon.Text = "TOST - checking for updates";
            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                if (!silentWhenCurrent)
                {
                    TostDialog.Show(
                        this,
                        "TOST is up to date.",
                        "TOST Updates",
                        TostDialogButtons.Ok,
                        TostDialogIcon.Information);
                }

                return;
            }

            var result = TostDialog.Show(
                this,
                $"TOST {update.TargetFullRelease.Version} is available.\n\nDownload it now and restart TOST?",
                "TOST Update Available",
                TostDialogButtons.YesNo,
                TostDialogIcon.Information);
            if (result != DialogResult.Yes)
            {
                return;
            }

            trayIcon.Text = "TOST - downloading update";
            await manager.DownloadUpdatesAsync(
                update,
                progress => BeginInvoke(() => trayIcon.Text = $"TOST - downloading {progress}%"),
                CancellationToken.None);
            logger.Info($"Downloaded TOST update {update.TargetFullRelease.Version}.");
            manager.ApplyUpdatesAndRestart(update.TargetFullRelease, null);
        }
        catch (Exception ex)
        {
            logger.Error($"TOST update check failed: {ex}");
            if (!silentWhenCurrent)
            {
                TostDialog.Show(
                    this,
                    $"Could not check for TOST updates.\n\n{ex.Message}",
                    "TOST Updates",
                    TostDialogButtons.Ok,
                    TostDialogIcon.Warning);
            }
        }
        finally
        {
            Cursor = Cursors.Default;
            trayIcon.Text = "TOST";
        }
    }

    private static void OpenWebsite(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private void EnsureSteamFolders(CopyReport report)
    {
        foreach (var directory in new[] { settings.SteamRoot, settings.SteamConfigPath, settings.LuaPath, settings.SteamAppsPath })
        {
            try
            {
                Directory.CreateDirectory(directory);
                logger.Info($"Ensured folder: {directory}");
            }
            catch (Exception ex)
            {
                report.AddFailure(directory, $"Could not create folder: {ex.Message}");
                logger.Error($"Could not create folder {directory}: {ex}");
            }
        }
    }

    private void CopyExpectedPath(string sourcePath, CopyReport report)
    {
        if (Directory.Exists(sourcePath))
        {
            CopyExpectedDirectory(sourcePath, report);
            return;
        }

        if (!File.Exists(sourcePath))
        {
            report.AddFailure(sourcePath, "Path does not exist.");
            logger.Error($"Skipped missing path: {sourcePath}");
            return;
        }

        if (Path.GetExtension(sourcePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            InstallFromZip(sourcePath, report);
            return;
        }

        var fileName = Path.GetFileName(sourcePath);
        var destinationDirectory = ResolveDestination(fileName);
        if (destinationDirectory is null)
        {
            report.AddFailure(fileName, "Unexpected file type or name.");
            logger.Info($"Skipped unexpected file: {sourcePath}");
            return;
        }

        CopyFile(sourcePath, destinationDirectory, report);
    }

    private void CopyExpectedDirectory(string sourceDirectory, CopyReport report)
    {
        var copiedAny = false;
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var before = report.Successes;
            CopyExpectedPath(file, report);
            copiedAny = copiedAny || report.Successes > before;
        }

        if (!copiedAny)
        {
            report.AddFailure(sourceDirectory, "Folder did not contain expected OpenSteamTool files.");
        }
    }

    private void CopyFile(string sourcePath, string destinationDirectory, CopyReport report)
    {
        var fileName = Path.GetFileName(sourcePath);

        try
        {
            Directory.CreateDirectory(destinationDirectory);
            var destinationPath = Path.Combine(destinationDirectory, fileName);
            if (settings.BackupBeforeOverwrite && File.Exists(destinationPath))
            {
                var backupPath = destinationPath + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(destinationPath, backupPath, overwrite: false);
                logger.Info($"Backed up {destinationPath} -> {backupPath}");
            }

            File.Copy(sourcePath, destinationPath, overwrite: settings.OverwriteExisting);
            report.AddSuccess(fileName, destinationDirectory);
            logger.Info($"Copied {sourcePath} -> {destinationPath}");
        }
        catch (Exception ex)
        {
            report.AddFailure(fileName, ex.Message);
            logger.Error($"Failed to copy {sourcePath}: {ex}");
        }
    }

    private string? ResolveDestination(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (fileName.Equals("opensteamtool.toml", StringComparison.OrdinalIgnoreCase))
        {
            return settings.SteamRoot;
        }

        if (fileName.Equals("OpenSteamTool.dll", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("dwmapi.dll", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("xinput1_4.dll", StringComparison.OrdinalIgnoreCase))
        {
            return settings.SteamRoot;
        }

        if (extension == ".lua")
        {
            return settings.LuaPath;
        }

        if ((extension == ".acf" && fileName.StartsWith("appmanifest_", StringComparison.OrdinalIgnoreCase)) ||
            extension == ".manifest")
        {
            return settings.SteamAppsPath;
        }

        return null;
    }

    private void OpenSettings()
    {
        using var dialog = new SettingsForm(settings);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        settings.Save();
        TopMost = settings.AlwaysOnTop;
        SetStartupRegistration(settings.StartWithWindows);
        logger.Info("Settings saved.");
        TostDialog.Show(
            this,
            "Settings saved.",
            "TOST",
            TostDialogButtons.Ok,
            TostDialogIcon.Success);
    }

    private void ShowReport(CopyReport report)
    {
        logger.Info(report.ToLogMessage());
        TostDialog.Show(
            this,
            report.ToMessage(),
            "TOST",
            TostDialogButtons.Ok,
            report.Failures == 0 ? TostDialogIcon.Success : TostDialogIcon.Warning);
    }

    private static Image? LoadLogo()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "logo-128.png"),
            Path.Combine(AppContext.BaseDirectory, "Assets", "logo-512.png"),
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                using var source = Image.FromFile(path);
                return new Bitmap(source);
            }
        }

        using var embeddedLogo = typeof(FloatingInstallerForm).Assembly
            .GetManifestResourceStream("TOST.Assets.logo-128.png");
        if (embeddedLogo is not null)
        {
            using var source = Image.FromStream(embeddedLogo);
            return new Bitmap(source);
        }

        return null;
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private void RestartSteam()
    {
        logger.Info("Restart Steam requested.");
        KillProcess("steam");
        var steamExe = Path.Combine(settings.SteamRoot, "steam.exe");
        if (File.Exists(steamExe))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = steamExe,
                WorkingDirectory = settings.SteamRoot,
                UseShellExecute = true
            });
        }
    }

    private void LaunchSteam()
    {
        var steamExe = Path.Combine(settings.SteamRoot, "steam.exe");
        if (!File.Exists(steamExe))
        {
            TostDialog.Show(
                this,
                "Steam was not found. Check the Steam folder in Settings.",
                "TOST",
                TostDialogButtons.Ok,
                TostDialogIcon.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = steamExe,
            WorkingDirectory = settings.SteamRoot,
            UseShellExecute = true
        });
    }

    internal void ActivateExistingInstance()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(ActivateExistingInstance));
            }
            catch (InvalidOperationException)
            {
                // The primary instance is already shutting down.
            }

            return;
        }

        ShowFloatingWindow();
    }

    private void ShowFloatingWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        EnsureVisibleOnScreen();
        TopMost = settings.AlwaysOnTop;
        BringToFront();
        Activate();
    }

    private void EnsureVisibleOnScreen()
    {
        var visibleScreen = Screen.AllScreens.FirstOrDefault(screen => screen.WorkingArea.IntersectsWith(Bounds))
            ?? Screen.PrimaryScreen;
        var workingArea = visibleScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);

        Left = Math.Clamp(Left, workingArea.Left, workingArea.Right - Width);
        Top = Math.Clamp(Top, workingArea.Top, workingArea.Bottom - Height);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        trayIcon.Visible = false;
        trayIcon.Dispose();
        glyph.Logo?.Dispose();
        base.OnFormClosed(e);
    }

    private static Region CreateRoundedRegion(Rectangle bounds, int radius)
    {
        using var path = new GraphicsPath();
        var diameter = radius * 2;
        var arc = new Rectangle(bounds.X, bounds.Y, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.X;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return new Region(path);
    }

    private static void KillProcess(string processName)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Steam may reject termination while elevated or already exiting.
            }
        }
    }

    private static void SetStartupRegistration(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (key is null)
        {
            return;
        }

        const string valueName = "TOST";
        const string oldValueName = "OpenSteamToolFloatingInstaller";
        key.DeleteValue(oldValueName, throwOnMissingValue: false);
        if (enabled)
        {
            key.SetValue(valueName, $"\"{AppPaths.LauncherPath}\"");
        }
        else
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
        }
    }

    private static void EnableWindowDrag(Control control)
    {
        var dragging = false;
        var startCursor = Point.Empty;
        var startForm = Point.Empty;

        control.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            dragging = true;
            startCursor = Cursor.Position;
            startForm = control.FindForm()?.Location ?? Point.Empty;
        };

        control.MouseMove += (_, _) =>
        {
            if (!dragging)
            {
                return;
            }

            var form = control.FindForm();
            if (form is null)
            {
                return;
            }

            var delta = Point.Subtract(Cursor.Position, new Size(startCursor));
            form.Location = Point.Add(startForm, new Size(delta));
        };

        control.MouseUp += (_, _) => dragging = false;
    }
}

internal sealed class FloatingIconSurface : Control
{
    private bool isDropTarget;

    public Image? Logo { get; set; }

    public bool IsDropTarget
    {
        get => isDropTarget;
        set
        {
            if (isDropTarget == value)
            {
                return;
            }

            isDropTarget = value;
            Invalidate();
        }
    }

    public FloatingIconSurface()
    {
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(IsDropTarget ? Color.FromArgb(54, 62, 69) : Color.FromArgb(43, 45, 48));

        if (Logo is not null)
        {
            var logoBounds = new Rectangle(8, 8, Width - 16, Height - 16);
            e.Graphics.DrawImage(Logo, logoBounds);
        }
        else
        {
            using var font = new Font("Segoe UI", 11f, FontStyle.Bold);
            using var brush = new SolidBrush(Color.White);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            e.Graphics.DrawString("TOST", font, brush, ClientRectangle, format);
        }

        using var border = new Pen(
            IsDropTarget ? Color.FromArgb(102, 192, 244) : Color.FromArgb(72, 75, 79),
            IsDropTarget ? 2f : 1f);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }
}

internal sealed class SteamStyleMenuRenderer : ToolStripProfessionalRenderer
{
    private static readonly Color MenuColor = Color.FromArgb(36, 36, 36);
    private static readonly Color HoverColor = Color.FromArgb(52, 53, 55);
    private static readonly Color BorderColor = Color.FromArgb(49, 50, 52);
    private static readonly Color SeparatorColor = Color.FromArgb(67, 68, 70);

    public SteamStyleMenuRenderer()
        : base(new SteamStyleColorTable())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        e.Graphics.Clear(MenuColor);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(MenuColor);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var color = e.Item.Selected ? HoverColor : MenuColor;
        using var brush = new SolidBrush(color);
        e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
    }

    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
        if (e.Image is null)
        {
            return;
        }

        // Draw the full glyph ourselves instead of using the narrow image slot
        // calculated by the standard menu renderer.
        var imageY = (e.Item.Height - e.Image.Height) / 2;
        e.Graphics.DrawImageUnscaled(e.Image, 6, imageY);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        // Use a compact icon-to-label gap and reclaim the shortcut/arrow column
        // for ordinary items. Submenus still keep room for their arrow.
        var rightPadding = e.Item is ToolStripMenuItem { HasDropDownItems: true } ? 28 : 6;
        e.TextRectangle = new Rectangle(
            34,
            e.TextRectangle.Top,
            Math.Max(0, e.Item.Width - 34 - rightPadding),
            e.TextRectangle.Height);

        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(SeparatorColor);
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.FromArgb(151, 157, 164), 1.4f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        var centerX = e.ArrowRectangle.Left + (e.ArrowRectangle.Width / 2f);
        var centerY = e.ArrowRectangle.Top + (e.ArrowRectangle.Height / 2f);
        e.Graphics.DrawLines(pen,
        [
            new PointF(centerX - 2f, centerY - 4f),
            new PointF(centerX + 2f, centerY),
            new PointF(centerX - 2f, centerY + 4f)
        ]);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(BorderColor);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }
}

internal sealed class SteamStyleColorTable : ProfessionalColorTable
{
    private static readonly Color Dark = Color.FromArgb(36, 36, 36);

    public override Color ToolStripDropDownBackground => Dark;
    public override Color ImageMarginGradientBegin => Dark;
    public override Color ImageMarginGradientMiddle => Dark;
    public override Color ImageMarginGradientEnd => Dark;
    public override Color MenuBorder => Color.FromArgb(49, 50, 52);
    public override Color MenuItemBorder => Color.Transparent;
    public override Color MenuItemSelected => Color.FromArgb(52, 53, 55);
}

internal sealed class DropToastForm : Form
{
    private readonly System.Windows.Forms.Timer dismissTimer = new();
    private readonly System.Windows.Forms.Timer fadeTimer = new();

    public DropToastForm(CopyReport report)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(42, 42, 43);
        ForeColor = Color.FromArgb(232, 234, 236);
        ClientSize = new Size(326, report.Failures > 0 ? 128 : 116);
        Region = CreateRoundedRegion(ClientRectangle, 7);
        Padding = new Padding(18, 12, 18, 12);

        var status = new ToastStatusIcon
        {
            Success = report.Successes > 0,
            Location = new Point((ClientSize.Width - 24) / 2, 11),
            Size = new Size(24, 24)
        };

        var message = new Label
        {
            AutoSize = false,
            Location = new Point(14, 42),
            Size = new Size(ClientSize.Width - 28, ClientSize.Height - 50),
            Text = report.ToToastMessage(),
            TextAlign = ContentAlignment.TopCenter,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Regular),
            ForeColor = ForeColor,
            BackColor = Color.Transparent
        };

        Controls.Add(status);
        Controls.Add(message);

        dismissTimer.Interval = report.Failures > 0 ? 5200 : 3800;
        dismissTimer.Tick += (_, _) =>
        {
            dismissTimer.Stop();
            fadeTimer.Start();
        };

        fadeTimer.Interval = 30;
        fadeTimer.Tick += (_, _) =>
        {
            Opacity -= 0.08;
            if (Opacity > 0.05)
            {
                return;
            }

            fadeTimer.Stop();
            Close();
        };

        Click += (_, _) => Close();
        status.Click += (_, _) => Close();
        message.Click += (_, _) => Close();
        Shown += (_, _) => dismissTimer.Start();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int CsDropShadow = 0x00020000;
            const int WsExToolWindow = 0x00000080;
            const int WsExNoActivate = 0x08000000;

            var parameters = base.CreateParams;
            parameters.ClassStyle |= CsDropShadow;
            parameters.ExStyle |= WsExToolWindow | WsExNoActivate;
            return parameters;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        dismissTimer.Dispose();
        fadeTimer.Dispose();
        base.OnFormClosed(e);
    }

    private static Region CreateRoundedRegion(Rectangle bounds, int radius)
    {
        using var path = new GraphicsPath();
        var diameter = radius * 2;
        var arc = new Rectangle(bounds.X, bounds.Y, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.X;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return new Region(path);
    }
}

internal sealed class ToastStatusIcon : Control
{
    public bool Success { get; set; }

    public ToastStatusIcon()
    {
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var circlePen = new Pen(
            Success ? Color.FromArgb(224, 230, 234) : Color.FromArgb(231, 177, 83),
            2.2f);
        e.Graphics.DrawEllipse(circlePen, 3, 3, Width - 7, Height - 7);

        if (Success)
        {
            using var checkPen = new Pen(Color.FromArgb(224, 230, 234), 2.2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            e.Graphics.DrawLines(checkPen,
            [
                new PointF(7f, 12f),
                new PointF(10.5f, 15.5f),
                new PointF(17f, 8.5f)
            ]);
        }
        else
        {
            using var warningPen = new Pen(Color.FromArgb(231, 177, 83), 2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            e.Graphics.DrawLine(warningPen, Width / 2f, 7f, Width / 2f, 13f);
            e.Graphics.DrawEllipse(warningPen, (Width / 2f) - 0.5f, 16f, 1f, 1f);
        }
    }
}

internal enum TostDialogButtons
{
    Ok,
    YesNo
}

internal enum TostDialogIcon
{
    Information,
    Success,
    Warning,
    Error
}

internal sealed class TostDialog : Form
{
    private const int DialogWidth = 468;
    private readonly Button primaryButton;

    private TostDialog(string message, string title, TostDialogButtons buttons, TostDialogIcon icon)
    {
        var messageFont = new Font("Segoe UI", 10f, FontStyle.Regular);
        var measuredMessage = TextRenderer.MeasureText(
            message,
            messageFont,
            new Size(352, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        var messageHeight = Math.Clamp(measuredMessage.Height + 8, 54, 230);
        var dialogHeight = 64 + messageHeight + 68;

        Text = title;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = Color.FromArgb(35, 36, 38);
        ForeColor = Color.FromArgb(232, 234, 236);
        ClientSize = new Size(DialogWidth, dialogHeight);
        Font = messageFont;
        KeyPreview = true;

        var titleBar = new Panel
        {
            Location = Point.Empty,
            Size = new Size(DialogWidth, 44),
            BackColor = Color.FromArgb(29, 30, 32)
        };
        var accent = new Panel
        {
            Location = new Point(0, 43),
            Size = new Size(DialogWidth, 2),
            BackColor = Color.FromArgb(42, 185, 71)
        };
        var titleLabel = new Label
        {
            AutoEllipsis = true,
            AutoSize = false,
            Location = new Point(16, 0),
            Size = new Size(DialogWidth - 64, 43),
            Text = title,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Regular),
            ForeColor = Color.FromArgb(235, 237, 239)
        };
        var closeButton = new Button
        {
            FlatStyle = FlatStyle.Flat,
            Location = new Point(DialogWidth - 44, 0),
            Size = new Size(44, 43),
            Text = "X",
            Font = new Font("Segoe UI Semibold", 9f),
            ForeColor = Color.FromArgb(174, 179, 184),
            BackColor = Color.Transparent,
            DialogResult = DialogResult.Cancel,
            TabStop = false
        };
        closeButton.FlatAppearance.BorderSize = 0;
        closeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(190, 52, 52);
        closeButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(155, 42, 42);

        titleBar.Controls.Add(titleLabel);
        titleBar.Controls.Add(closeButton);
        titleBar.Controls.Add(accent);
        EnableWindowDrag(titleBar);
        EnableWindowDrag(titleLabel);

        var statusIcon = new TostDialogStatusIcon
        {
            Icon = icon,
            Location = new Point(22, 64),
            Size = new Size(30, 30)
        };
        var messageBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            ScrollBars = measuredMessage.Height + 8 > messageHeight
                ? ScrollBars.Vertical
                : ScrollBars.None,
            Location = new Point(70, 62),
            Size = new Size(374, messageHeight),
            Text = message,
            Font = messageFont,
            ForeColor = ForeColor,
            BackColor = BackColor,
            TabStop = true
        };

        var buttonBarTop = dialogHeight - 56;
        var buttonBar = new Panel
        {
            Location = new Point(0, buttonBarTop),
            Size = new Size(DialogWidth, 56),
            BackColor = Color.FromArgb(31, 32, 34)
        };
        var divider = new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = Color.FromArgb(57, 59, 62)
        };
        buttonBar.Controls.Add(divider);

        if (buttons == TostDialogButtons.YesNo)
        {
            var noButton = CreateButton("No", DialogResult.No, primary: false);
            noButton.Location = new Point(DialogWidth - 196, 12);
            buttonBar.Controls.Add(noButton);

            primaryButton = CreateButton("Yes", DialogResult.Yes, primary: true);
            primaryButton.Location = new Point(DialogWidth - 98, 12);
            CancelButton = noButton;
        }
        else
        {
            primaryButton = CreateButton("OK", DialogResult.OK, primary: true);
            primaryButton.Location = new Point(DialogWidth - 98, 12);
            CancelButton = closeButton;
        }

        buttonBar.Controls.Add(primaryButton);
        AcceptButton = primaryButton;

        Controls.Add(titleBar);
        Controls.Add(statusIcon);
        Controls.Add(messageBox);
        Controls.Add(buttonBar);

        Shown += (_, _) => primaryButton.Focus();
    }

    public static DialogResult Show(
        IWin32Window? owner,
        string message,
        string title,
        TostDialogButtons buttons,
        TostDialogIcon icon)
    {
        using var dialog = new TostDialog(message, title, buttons, icon);
        if (owner is Form ownerForm)
        {
            dialog.TopMost = ownerForm.TopMost;
        }

        return owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int CsDropShadow = 0x00020000;
            var parameters = base.CreateParams;
            parameters.ClassStyle |= CsDropShadow;
            return parameters;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var borderPen = new Pen(Color.FromArgb(68, 70, 73));
        e.Graphics.DrawRectangle(borderPen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    private static Button CreateButton(string text, DialogResult result, bool primary)
    {
        var button = new Button
        {
            AutoSize = false,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(86, 32),
            Text = text,
            DialogResult = result,
            Font = new Font("Segoe UI Semibold", 9.5f),
            ForeColor = Color.White,
            BackColor = primary
                ? Color.FromArgb(33, 150, 57)
                : Color.FromArgb(63, 65, 68),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = primary
            ? Color.FromArgb(48, 183, 73)
            : Color.FromArgb(82, 84, 87);
        button.FlatAppearance.MouseOverBackColor = primary
            ? Color.FromArgb(39, 166, 64)
            : Color.FromArgb(75, 77, 80);
        button.FlatAppearance.MouseDownBackColor = primary
            ? Color.FromArgb(27, 130, 49)
            : Color.FromArgb(52, 54, 57);
        return button;
    }

    private void EnableWindowDrag(Control control)
    {
        var dragging = false;
        var cursorStart = Point.Empty;
        var formStart = Point.Empty;

        control.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            dragging = true;
            cursorStart = Cursor.Position;
            formStart = Location;
        };
        control.MouseMove += (_, _) =>
        {
            if (!dragging)
            {
                return;
            }

            var delta = Point.Subtract(Cursor.Position, new Size(cursorStart));
            Location = Point.Add(formStart, new Size(delta));
        };
        control.MouseUp += (_, _) => dragging = false;
    }
}

internal sealed class TostDialogStatusIcon : Control
{
    public TostDialogIcon Icon { get; set; }

    public TostDialogStatusIcon()
    {
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var color = Icon switch
        {
            TostDialogIcon.Success => Color.FromArgb(52, 190, 80),
            TostDialogIcon.Warning => Color.FromArgb(231, 177, 83),
            TostDialogIcon.Error => Color.FromArgb(224, 92, 92),
            _ => Color.FromArgb(102, 192, 244)
        };
        using var pen = new Pen(color, 2.2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        e.Graphics.DrawEllipse(pen, 3, 3, Width - 7, Height - 7);

        switch (Icon)
        {
            case TostDialogIcon.Success:
                e.Graphics.DrawLines(pen,
                [
                    new PointF(8f, 15f),
                    new PointF(13f, 20f),
                    new PointF(22f, 10f)
                ]);
                break;
            case TostDialogIcon.Error:
                e.Graphics.DrawLine(pen, 10f, 10f, 20f, 20f);
                e.Graphics.DrawLine(pen, 20f, 10f, 10f, 20f);
                break;
            case TostDialogIcon.Warning:
                e.Graphics.DrawLine(pen, 15f, 9f, 15f, 17f);
                e.Graphics.DrawEllipse(pen, 14.5f, 21f, 1f, 1f);
                break;
            default:
                e.Graphics.DrawLine(pen, 15f, 13f, 15f, 21f);
                e.Graphics.DrawEllipse(pen, 14.5f, 8f, 1f, 1f);
                break;
        }
    }
}

internal sealed class SettingsForm : Form
{
    private readonly InstallerSettings settings;
    private readonly TextBox steamRootTextBox = new();
    private readonly CheckBox overwriteCheckBox = new();
    private readonly CheckBox backupCheckBox = new();
    private readonly CheckBox startupCheckBox = new();
    private readonly CheckBox alwaysOnTopCheckBox = new();
    private readonly CheckBox updateCheckBox = new();

    public SettingsForm(InstallerSettings settings)
    {
        this.settings = settings;

        Text = "TOST Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 270);

        var steamRootLabel = new Label
        {
            Text = "Steam folder",
            AutoSize = true,
            Location = new Point(16, 20)
        };

        steamRootTextBox.Location = new Point(16, 44);
        steamRootTextBox.Size = new Size(402, 27);
        steamRootTextBox.Text = settings.SteamRoot;

        var browseButton = new Button
        {
            Text = "Browse",
            Location = new Point(428, 43),
            Size = new Size(76, 29)
        };
        browseButton.Click += (_, _) => BrowseSteamFolder();

        overwriteCheckBox.Text = "Overwrite existing files";
        overwriteCheckBox.AutoSize = true;
        overwriteCheckBox.Location = new Point(16, 88);
        overwriteCheckBox.Checked = settings.OverwriteExisting;

        backupCheckBox.Text = "Backup files before overwrite";
        backupCheckBox.AutoSize = true;
        backupCheckBox.Location = new Point(16, 118);
        backupCheckBox.Checked = settings.BackupBeforeOverwrite;

        startupCheckBox.Text = "Start floating installer with Windows";
        startupCheckBox.AutoSize = true;
        startupCheckBox.Location = new Point(16, 148);
        startupCheckBox.Checked = settings.StartWithWindows;

        alwaysOnTopCheckBox.Text = "Keep floating icon always on top";
        alwaysOnTopCheckBox.AutoSize = true;
        alwaysOnTopCheckBox.Location = new Point(16, 178);
        alwaysOnTopCheckBox.Checked = settings.AlwaysOnTop;

        updateCheckBox.Text = "Automatically check for TOST updates";
        updateCheckBox.AutoSize = true;
        updateCheckBox.Location = new Point(16, 208);
        updateCheckBox.Checked = settings.AutoCheckForUpdates;

        var saveButton = new Button
        {
            Text = "Save",
            DialogResult = DialogResult.OK,
            Location = new Point(348, 228),
            Size = new Size(75, 29)
        };
        saveButton.Click += (_, _) => ApplySettings();

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(429, 228),
            Size = new Size(75, 29)
        };

        Controls.AddRange([
            steamRootLabel,
            steamRootTextBox,
            browseButton,
            overwriteCheckBox,
            backupCheckBox,
            startupCheckBox,
            alwaysOnTopCheckBox,
            updateCheckBox,
            saveButton,
            cancelButton
        ]);

        AcceptButton = saveButton;
        CancelButton = cancelButton;
    }

    private void BrowseSteamFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select Steam installation folder",
            InitialDirectory = Directory.Exists(steamRootTextBox.Text) ? steamRootTextBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            steamRootTextBox.Text = dialog.SelectedPath;
        }
    }

    private void ApplySettings()
    {
        settings.SteamRoot = steamRootTextBox.Text.Trim();
        settings.OverwriteExisting = overwriteCheckBox.Checked;
        settings.BackupBeforeOverwrite = backupCheckBox.Checked;
        settings.StartWithWindows = startupCheckBox.Checked;
        settings.AlwaysOnTop = alwaysOnTopCheckBox.Checked;
        settings.AutoCheckForUpdates = updateCheckBox.Checked;
    }
}

internal static class AppPaths
{
    public static bool IsPortable { get; private set; } = true;
    public static string DataDirectory { get; private set; } = AppContext.BaseDirectory;
    public static string LauncherPath { get; private set; } = Application.ExecutablePath;
    public static string SettingsPath => Path.Combine(DataDirectory, "installer-settings.json");
    public static string LogDirectory => Path.Combine(DataDirectory, "logs");
    public static string LogPath => Path.Combine(LogDirectory, "install.log");

    public static void Initialize()
    {
        var locator = VelopackLocator.Current;
        IsPortable = locator.IsPortable;

        if (IsPortable)
        {
            DataDirectory = AppContext.BaseDirectory;
            LauncherPath = Application.ExecutablePath;
        }
        else
        {
            var root = string.IsNullOrWhiteSpace(locator.RootAppDir)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TOST")
                : locator.RootAppDir;
            DataDirectory = Path.Combine(root, "data");
            LauncherPath = Path.Combine(root, "TOST.exe");
        }

        Directory.CreateDirectory(DataDirectory);
        MigrateLegacyData();
    }

    private static void MigrateLegacyData()
    {
        var oldLocalData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OST",
            "data");
        var settingsCandidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "installer-settings.json"),
            Path.Combine(oldLocalData, "installer-settings.json")
        };
        var logCandidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "logs", "install.log"),
            Path.Combine(oldLocalData, "logs", "install.log")
        };

        MigrateFirstExistingFile(settingsCandidates, SettingsPath);
        MigrateFirstExistingFile(logCandidates, LogPath);
    }

    private static void MigrateFirstExistingFile(IEnumerable<string> candidates, string destination)
    {
        if (File.Exists(destination))
        {
            return;
        }

        var source = candidates.FirstOrDefault(path =>
            !Path.GetFullPath(path).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase) &&
            File.Exists(path));
        if (source is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: false);
        }
        catch
        {
            // A migration failure must not prevent TOST from starting.
        }
    }
}

internal sealed class InstallerSettings
{
    public string SteamRoot { get; set; } = DetectSteamRoot();
    public bool OverwriteExisting { get; set; } = true;
    public bool BackupBeforeOverwrite { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool AlwaysOnTop { get; set; } = true;
    public bool AutoCheckForUpdates { get; set; } = true;
    public DateTime? LastUpdateCheckUtc { get; set; }

    public string SteamConfigPath => Path.Combine(SteamRoot, "config");
    public string LuaPath => Path.Combine(SteamConfigPath, "lua");
    public string SteamAppsPath => Path.Combine(SteamRoot, "steamapps");
    public string SteamCommonPath => Path.Combine(SteamAppsPath, "common");
    public string SteamUserDataPath => Path.Combine(SteamRoot, "userdata");
    public string LogDirectory => AppPaths.LogDirectory;
    public string LogPath => AppPaths.LogPath;

    public bool ShouldCheckForUpdates()
    {
        return AutoCheckForUpdates &&
            (!LastUpdateCheckUtc.HasValue || DateTime.UtcNow - LastUpdateCheckUtc.Value >= TimeSpan.FromHours(24));
    }

    public static InstallerSettings Load()
    {
        var path = SettingsPath;
        if (!File.Exists(path))
        {
            return new InstallerSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<InstallerSettings>(File.ReadAllText(path)) ?? new InstallerSettings();
        }
        catch
        {
            return new InstallerSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(AppPaths.SettingsPath, json);
    }

    private static string SettingsPath => AppPaths.SettingsPath;

    private static string DetectSteamRoot()
    {
        var registryPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (!string.IsNullOrWhiteSpace(registryPath))
        {
            return registryPath.Replace('/', '\\');
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return Path.Combine(programFilesX86, "Steam");
    }
}

internal sealed class InstallerLogger
{
    private readonly string path;

    public InstallerLogger(string path)
    {
        this.path = path;
    }

    public void Info(string message)
    {
        Write("INFO", message);
    }

    public void Error(string message)
    {
        Write("ERROR", message);
    }

    private void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {level} {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never block installation.
        }
    }
}

internal sealed record OpenSteamToolRelease(string Tag, string AssetName, Uri DownloadUri);

internal sealed class CopyReport
{
    private readonly List<string> lines = [];
    private readonly Dictionary<CopyCategory, int> categories = [];
    public int Successes { get; private set; }
    public int Failures { get; private set; }

    public void AddSuccess(string fileName, string destinationDirectory)
    {
        Successes++;
        var category = Categorize(fileName);
        categories[category] = categories.GetValueOrDefault(category) + 1;
        lines.Add($"Copied {fileName} -> {destinationDirectory}");
    }

    public void AddFailure(string fileName, string reason)
    {
        Failures++;
        lines.Add($"Skipped {fileName}: {reason}");
    }

    public string ToMessage()
    {
        return lines.Count == 0 ? "No files copied." : string.Join(Environment.NewLine, lines);
    }

    public string ToLogMessage()
    {
        return $"Copy report: {Successes} copied, {Failures} skipped. {string.Join(" | ", lines)}";
    }

    public string ToToastMessage()
    {
        if (Successes == 0)
        {
            return Failures == 1
                ? "No supported file was imported\nCheck Logs for details"
                : $"No supported files were imported\nSkipped {Failures} files\nCheck Logs for details";
        }

        var summary = new List<string>();
        AddCategoryLine(summary, CopyCategory.Lua, "Lua script", "Lua scripts");
        AddCategoryLine(summary, CopyCategory.Manifest, "manifest file", "manifest files");
        AddCategoryLine(summary, CopyCategory.OpenSteamTool, "OpenSteamTool file", "OpenSteamTool files");

        if (Failures > 0)
        {
            summary.Add($"Skipped {Failures} unsupported {(Failures == 1 ? "file" : "files")}");
        }

        summary.Add("Will take effect after Steam restarts");
        return string.Join(Environment.NewLine, summary);
    }

    private void AddCategoryLine(List<string> summary, CopyCategory category, string singular, string plural)
    {
        var count = categories.GetValueOrDefault(category);
        if (count > 0)
        {
            summary.Add($"Imported {count} {(count == 1 ? singular : plural)}");
        }
    }

    private static CopyCategory Categorize(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (extension.Equals(".lua", StringComparison.OrdinalIgnoreCase))
        {
            return CopyCategory.Lua;
        }

        if (extension.Equals(".manifest", StringComparison.OrdinalIgnoreCase) ||
            (extension.Equals(".acf", StringComparison.OrdinalIgnoreCase) &&
             fileName.StartsWith("appmanifest_", StringComparison.OrdinalIgnoreCase)))
        {
            return CopyCategory.Manifest;
        }

        return CopyCategory.OpenSteamTool;
    }

    private enum CopyCategory
    {
        Lua,
        Manifest,
        OpenSteamTool
    }
}

using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
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
        Region = CreateRoundedRegion(ClientRectangle, ClientSize.Width / 2);

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
        menu.Items.Add(CreateMenuItem("Manage Games", "\uE7FC", (_, _) => OpenGameManager()));
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
        var menu = new ContextMenuStrip
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

        menu.Opening += (_, _) => FillMenuDisplayWidth(menu);
        return menu;
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

    private static void FillMenuDisplayWidth(ToolStripDropDown menu)
    {
        var width = menu.DisplayRectangle.Width;
        if (width <= 0)
        {
            return;
        }

        foreach (ToolStripItem item in menu.Items)
        {
            item.Size = new Size(width, item.Height);
        }
    }

    private static Bitmap CreateMenuIcon(string glyphText)
    {
        // Symbol-font glyphs can overhang their nominal bounds at high DPI.
        // Keep a three-pixel safety edge around the original 20px drawing area.
        var bitmap = new Bitmap(26, 26);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        using var font = CreateSymbolFont(14f);
        using var brush = new SolidBrush(Color.FromArgb(151, 157, 164));
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoClip | StringFormatFlags.NoWrap
        };
        graphics.DrawString(glyphText, font, brush, new RectangleF(0, 0, 26, 26), format);
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
            item.DropDown.Opening += (_, _) => FillMenuDisplayWidth(item.DropDown);
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
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TOST/1.2 (+https://github.com/sadabx/TOST)");
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

    private void OpenGameManager()
    {
        using var dialog = new GameManagerForm(settings, logger, RestartSteam);
        dialog.ShowDialog(this);
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
        e.Graphics.DrawEllipse(border, 1, 1, Width - 3, Height - 3);
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
        e.Graphics.DrawImageUnscaled(e.Image, 4, imageY);
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

internal sealed record ManagedGame(
    string AppId,
    string? Name,
    string LuaPath,
    IReadOnlyList<string> DepotIds,
    IReadOnlyList<string> ManifestPaths)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"App {AppId}" : Name;
}

internal sealed class RemovedGameArchive
{
    public string ArchiveId { get; set; } = string.Empty;
    public DateTime RemovedUtc { get; set; }
    public List<RemovedGameEntry> Games { get; set; } = [];
    public List<RemovedFileEntry> Files { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public string ArchiveDirectory { get; set; } = string.Empty;
}

internal sealed class RemovedGameEntry
{
    public string AppId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string LuaFileName { get; set; } = string.Empty;
}

internal sealed class RemovedFileEntry
{
    public string Kind { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ArchiveRelativePath { get; set; } = string.Empty;
}

internal sealed record GameManagementResult(bool Success, string Message);

internal static class GameManagementService
{
    private const long MaxLuaBytes = 8L * 1024 * 1024;
    private static readonly Regex AddAppIdRegex = new(
        @"(?im)\baddappid\s*\(\s*(\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex AppManifestNameRegex = new(
        "(?im)^\\s*\"name\"\\s+\"(?<name>[^\"]+)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static List<ManagedGame> FindManagedGames(InstallerSettings settings, InstallerLogger logger)
    {
        var games = new List<ManagedGame>();
        if (!Directory.Exists(settings.LuaPath))
        {
            return games;
        }

        var manifestsByDepot = FindManifestsByDepot(settings.SteamAppsPath, logger);
        IEnumerable<string> luaFiles;
        try
        {
            luaFiles = Directory.EnumerateFiles(settings.LuaPath, "*.lua", SearchOption.TopDirectoryOnly).ToList();
        }
        catch (Exception ex)
        {
            logger.Error($"Could not scan managed game Lua files: {ex}");
            throw;
        }

        foreach (var luaPath in luaFiles.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var fileInfo = new FileInfo(luaPath);
                var content = fileInfo.Length <= MaxLuaBytes ? File.ReadAllText(luaPath) : string.Empty;
                var ids = AddAppIdRegex.Matches(content)
                    .Select(match => match.Groups[1].Value)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var fileStem = Path.GetFileNameWithoutExtension(luaPath);
                var appId = fileStem.All(char.IsDigit) && fileStem.Length > 0
                    ? fileStem
                    : ids.FirstOrDefault() ?? fileStem;
                if (ids.Count == 0 && appId.All(char.IsDigit))
                {
                    ids.Add(appId);
                }

                var manifestPaths = ids
                    .Where(manifestsByDepot.ContainsKey)
                    .SelectMany(id => manifestsByDepot[id])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                games.Add(new ManagedGame(
                    appId,
                    TryReadGameName(settings.SteamAppsPath, appId, logger),
                    luaPath,
                    ids,
                    manifestPaths));
            }
            catch (Exception ex)
            {
                logger.Error($"Could not inspect managed game Lua file {luaPath}: {ex}");
            }
        }

        return games;
    }

    public static List<RemovedGameArchive> FindRemovedGames(InstallerLogger logger)
    {
        var archives = new List<RemovedGameArchive>();
        if (!Directory.Exists(AppPaths.RemovedGamesDirectory))
        {
            return archives;
        }

        foreach (var directory in Directory.EnumerateDirectories(AppPaths.RemovedGamesDirectory))
        {
            var metadataPath = Path.Combine(directory, "removal.json");
            if (!File.Exists(metadataPath))
            {
                continue;
            }

            try
            {
                var archive = JsonSerializer.Deserialize<RemovedGameArchive>(File.ReadAllText(metadataPath));
                if (archive is null || archive.Files.Count == 0)
                {
                    continue;
                }

                archive.ArchiveDirectory = directory;
                archives.Add(archive);
            }
            catch (Exception ex)
            {
                logger.Error($"Could not read removed game archive {directory}: {ex}");
            }
        }

        return archives.OrderByDescending(archive => archive.RemovedUtc).ToList();
    }

    public static GameManagementResult RemoveGames(
        IReadOnlyCollection<ManagedGame> selectedGames,
        IReadOnlyCollection<ManagedGame> allGames,
        InstallerSettings settings,
        InstallerLogger logger)
    {
        if (selectedGames.Count == 0)
        {
            return new GameManagementResult(false, "Select at least one game to remove.");
        }

        var selectedLuaPaths = selectedGames
            .Select(game => Path.GetFullPath(game.LuaPath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var manifestsUsedByOtherGames = allGames
            .Where(game => !selectedLuaPaths.Contains(Path.GetFullPath(game.LuaPath)))
            .SelectMany(game => game.ManifestPaths)
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedManifests = selectedGames
            .SelectMany(game => game.ManifestPaths)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var manifestsToMove = selectedManifests
            .Where(path => !manifestsUsedByOtherGames.Contains(path))
            .ToList();
        var sharedManifestCount = selectedManifests.Count - manifestsToMove.Count;

        var archiveId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var archiveDirectory = Path.Combine(AppPaths.RemovedGamesDirectory, archiveId);
        var archive = new RemovedGameArchive
        {
            ArchiveId = archiveId,
            RemovedUtc = DateTime.UtcNow,
            ArchiveDirectory = archiveDirectory,
            Games = selectedGames.Select(game => new RemovedGameEntry
            {
                AppId = game.AppId,
                DisplayName = game.DisplayName,
                LuaFileName = Path.GetFileName(game.LuaPath)
            }).ToList()
        };

        var movedFiles = new List<(string Source, string ArchivePath)>();
        try
        {
            foreach (var game in selectedGames)
            {
                AddArchiveFile(archive, "Lua", game.LuaPath, settings.LuaPath);
            }

            foreach (var manifestPath in manifestsToMove)
            {
                AddArchiveFile(archive, "Manifest", manifestPath, settings.SteamAppsPath);
            }

            Directory.CreateDirectory(archiveDirectory);
            foreach (var file in archive.Files)
            {
                var sourceRoot = file.Kind == "Lua" ? settings.LuaPath : settings.SteamAppsPath;
                var source = Path.Combine(sourceRoot, file.FileName);
                var archivePath = Path.Combine(archiveDirectory, file.ArchiveRelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
                File.Move(source, archivePath);
                movedFiles.Add((source, archivePath));
                logger.Info($"Archived managed game file {source} -> {archivePath}");
            }

            var metadataPath = Path.Combine(archiveDirectory, "removal.json");
            File.WriteAllText(
                metadataPath,
                JsonSerializer.Serialize(archive, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            RollBackRemoval(movedFiles, logger);
            TryDeleteArchiveDirectory(archiveDirectory, logger);
            logger.Error($"Managed game removal failed: {ex}");
            return new GameManagementResult(false, $"Could not remove the selected games.\n\n{ex.Message}");
        }

        var message = $"Moved {archive.Files.Count} file{(archive.Files.Count == 1 ? string.Empty : "s")} to TOST's recovery folder.";
        if (sharedManifestCount > 0)
        {
            message += $"\n\nKept {sharedManifestCount} shared manifest{(sharedManifestCount == 1 ? string.Empty : "s")} still used by another Lua file.";
        }

        message += "\n\nRestart Steam for the change to take effect.";
        return new GameManagementResult(true, message);
    }

    public static GameManagementResult RestoreArchive(
        RemovedGameArchive archive,
        InstallerSettings settings,
        InstallerLogger logger)
    {
        if (!IsPathInside(archive.ArchiveDirectory, AppPaths.RemovedGamesDirectory))
        {
            return new GameManagementResult(false, "The recovery archive path is invalid.");
        }

        var moves = new List<(string ArchivePath, string Destination)>();
        foreach (var file in archive.Files)
        {
            if (!IsValidArchiveFile(file))
            {
                return new GameManagementResult(false, $"The recovery entry for {file.FileName} is invalid.");
            }

            var archivePath = Path.GetFullPath(Path.Combine(archive.ArchiveDirectory, file.ArchiveRelativePath));
            var destinationRoot = file.Kind == "Lua" ? settings.LuaPath : settings.SteamAppsPath;
            var destination = Path.GetFullPath(Path.Combine(destinationRoot, file.FileName));
            if (!IsPathInside(archivePath, archive.ArchiveDirectory) ||
                !IsPathInside(destination, destinationRoot) ||
                !File.Exists(archivePath))
            {
                return new GameManagementResult(false, $"The recovery file {file.FileName} is missing or invalid.");
            }

            if (File.Exists(destination))
            {
                return new GameManagementResult(false, $"Cannot restore {file.FileName} because a file with that name already exists.");
            }

            moves.Add((archivePath, destination));
        }

        var completedMoves = new List<(string ArchivePath, string Destination)>();
        try
        {
            foreach (var move in moves)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(move.Destination)!);
                File.Move(move.ArchivePath, move.Destination);
                completedMoves.Add(move);
                logger.Info($"Restored managed game file {move.ArchivePath} -> {move.Destination}");
            }

            Directory.Delete(archive.ArchiveDirectory, recursive: true);
        }
        catch (Exception ex)
        {
            foreach (var move in completedMoves.AsEnumerable().Reverse())
            {
                try
                {
                    if (File.Exists(move.Destination) && !File.Exists(move.ArchivePath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(move.ArchivePath)!);
                        File.Move(move.Destination, move.ArchivePath);
                    }
                }
                catch (Exception rollbackException)
                {
                    logger.Error($"Could not roll back restored file {move.Destination}: {rollbackException}");
                }
            }

            logger.Error($"Managed game restore failed: {ex}");
            return new GameManagementResult(false, $"Could not restore the selected games.\n\n{ex.Message}");
        }

        return new GameManagementResult(
            true,
            $"Restored {moves.Count} file{(moves.Count == 1 ? string.Empty : "s")}.\n\nRestart Steam for the change to take effect.");
    }

    private static Dictionary<string, List<string>> FindManifestsByDepot(string steamAppsPath, InstallerLogger logger)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(steamAppsPath))
        {
            return result;
        }

        try
        {
            foreach (var manifestPath in Directory.EnumerateFiles(steamAppsPath, "*.manifest", SearchOption.TopDirectoryOnly))
            {
                var fileStem = Path.GetFileNameWithoutExtension(manifestPath);
                var separatorIndex = fileStem.IndexOf('_');
                var depotId = separatorIndex >= 0 ? fileStem[..separatorIndex] : fileStem;
                if (depotId.Length == 0 || !depotId.All(char.IsDigit))
                {
                    continue;
                }

                if (!result.TryGetValue(depotId, out var paths))
                {
                    paths = [];
                    result[depotId] = paths;
                }

                paths.Add(manifestPath);
            }
        }
        catch (Exception ex)
        {
            logger.Error($"Could not scan Steam manifest files: {ex}");
            throw;
        }

        return result;
    }

    private static string? TryReadGameName(string steamAppsPath, string appId, InstallerLogger logger)
    {
        if (appId.Length == 0 || !appId.All(char.IsDigit))
        {
            return null;
        }

        var appManifestPath = Path.Combine(steamAppsPath, $"appmanifest_{appId}.acf");
        if (!File.Exists(appManifestPath))
        {
            return null;
        }

        try
        {
            var match = AppManifestNameRegex.Match(File.ReadAllText(appManifestPath));
            return match.Success ? match.Groups["name"].Value : null;
        }
        catch (Exception ex)
        {
            logger.Error($"Could not read game name from {appManifestPath}: {ex}");
            return null;
        }
    }

    private static void AddArchiveFile(RemovedGameArchive archive, string kind, string sourcePath, string allowedRoot)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!IsPathInside(fullSourcePath, allowedRoot) || !File.Exists(fullSourcePath))
        {
            throw new InvalidDataException($"The managed file path is invalid: {sourcePath}");
        }

        var fileName = Path.GetFileName(fullSourcePath);
        if (archive.Files.Any(file =>
                file.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase) &&
                file.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var folder = kind == "Lua" ? "lua" : "steamapps";
        archive.Files.Add(new RemovedFileEntry
        {
            Kind = kind,
            FileName = fileName,
            ArchiveRelativePath = Path.Combine("files", folder, fileName)
        });
    }

    private static bool IsValidArchiveFile(RemovedFileEntry file)
    {
        if (string.IsNullOrWhiteSpace(file.FileName) ||
            !file.FileName.Equals(Path.GetFileName(file.FileName), StringComparison.Ordinal))
        {
            return false;
        }

        return file.Kind switch
        {
            "Lua" => Path.GetExtension(file.FileName).Equals(".lua", StringComparison.OrdinalIgnoreCase),
            "Manifest" => Path.GetExtension(file.FileName).Equals(".manifest", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool IsPathInside(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static void RollBackRemoval(
        IEnumerable<(string Source, string ArchivePath)> movedFiles,
        InstallerLogger logger)
    {
        foreach (var movedFile in movedFiles.Reverse())
        {
            try
            {
                if (File.Exists(movedFile.ArchivePath) && !File.Exists(movedFile.Source))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(movedFile.Source)!);
                    File.Move(movedFile.ArchivePath, movedFile.Source);
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Could not roll back removed file {movedFile.Source}: {ex}");
            }
        }
    }

    private static void TryDeleteArchiveDirectory(string archiveDirectory, InstallerLogger logger)
    {
        try
        {
            if (Directory.Exists(archiveDirectory) && IsPathInside(archiveDirectory, AppPaths.RemovedGamesDirectory))
            {
                Directory.Delete(archiveDirectory, recursive: true);
            }
        }
        catch (Exception ex)
        {
            logger.Error($"Could not clean failed removal archive {archiveDirectory}: {ex}");
        }
    }
}

internal static class SteamGameNameResolver
{
    private const long MaxResponseBytes = 1024 * 1024;
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public static async Task<IReadOnlyDictionary<string, string>> ResolveAsync(
        IEnumerable<string> appIds,
        InstallerLogger logger,
        CancellationToken cancellationToken)
    {
        var cache = LoadCache(logger);
        var requestedIds = appIds
            .Where(appId => !string.IsNullOrWhiteSpace(appId) && appId.All(char.IsDigit))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var missingIds = requestedIds.Where(appId => !cache.ContainsKey(appId)).ToList();
        var cacheChanged = false;

        foreach (var appId in missingIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var name = await FetchNameAsync(appId, cancellationToken);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    cache[appId] = name;
                    cacheChanged = true;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.Error($"Steam name lookup timed out for App {appId}.");
            }
            catch (Exception ex)
            {
                logger.Error($"Could not look up the Steam name for App {appId}: {ex.Message}");
            }
        }

        if (cacheChanged)
        {
            SaveCache(cache, logger);
        }

        return cache
            .Where(pair => requestedIds.Contains(pair.Key, StringComparer.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static async Task<string?> FetchNameAsync(string appId, CancellationToken cancellationToken)
    {
        var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&filters=basic&l=english";
        using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > MaxResponseBytes)
        {
            throw new InvalidDataException("The Steam Store response was larger than expected.");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var limitedStream = new LimitedReadStream(responseStream, MaxResponseBytes);
        using var document = await JsonDocument.ParseAsync(limitedStream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty(appId, out var app) ||
            !app.TryGetProperty("success", out var success) ||
            !success.GetBoolean() ||
            !app.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("name", out var nameElement))
        {
            return null;
        }

        return nameElement.GetString()?.Trim();
    }

    private static Dictionary<string, string> LoadCache(InstallerLogger logger)
    {
        try
        {
            if (!File.Exists(AppPaths.GameNamesCachePath))
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            var cachedNames = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(AppPaths.GameNamesCachePath));
            return cachedNames is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(cachedNames, StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            logger.Error($"Could not read the Steam game-name cache: {ex.Message}");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static void SaveCache(Dictionary<string, string> cache, InstallerLogger logger)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.WriteAllText(
                AppPaths.GameNamesCachePath,
                JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            logger.Error($"Could not save the Steam game-name cache: {ex.Message}");
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TOST/1.2 (+https://github.com/sadabx/TOST)");
        return client;
    }

    private sealed class LimitedReadStream(Stream inner, long maximumBytes) : Stream
    {
        private long bytesRead;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => bytesRead;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            TrackBytes(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            TrackBytes(read);
            return read;
        }

        private void TrackBytes(int count)
        {
            bytesRead += count;
            if (bytesRead > maximumBytes)
            {
                throw new InvalidDataException("The Steam Store response was larger than expected.");
            }
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

internal static class WindowTheme
{
    private const int UseImmersiveDarkMode = 20;
    private const int UseImmersiveDarkModeBefore20H1 = 19;
    private const int CaptionColor = 35;
    private const int TextColor = 36;

    public static void ApplyDarkTitleBar(Form form)
    {
        form.HandleCreated += (_, _) => ApplyDarkTitleBar(form.Handle);
        if (form.IsHandleCreated)
        {
            ApplyDarkTitleBar(form.Handle);
        }
    }

    private static void ApplyDarkTitleBar(IntPtr handle)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return;
        }

        var enabled = 1;
        var result = DwmSetWindowAttribute(
            handle,
            UseImmersiveDarkMode,
            ref enabled,
            sizeof(int));
        if (result != 0)
        {
            DwmSetWindowAttribute(
                handle,
                UseImmersiveDarkModeBefore20H1,
                ref enabled,
                sizeof(int));
        }

        var captionColor = ToColorRef(Color.FromArgb(35, 36, 38));
        var textColor = ToColorRef(Color.FromArgb(232, 234, 236));
        DwmSetWindowAttribute(handle, CaptionColor, ref captionColor, sizeof(int));
        DwmSetWindowAttribute(handle, TextColor, ref textColor, sizeof(int));
    }

    private static int ToColorRef(Color color) =>
        color.R | color.G << 8 | color.B << 16;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}

internal sealed class DarkTabControl : TabControl
{
    private static readonly Color SurfaceColor = Color.FromArgb(35, 36, 38);
    private static readonly Color SelectedColor = Color.FromArgb(29, 30, 32);
    private static readonly Color BorderColor = Color.FromArgb(73, 75, 78);
    private static readonly Color TextColor = Color.FromArgb(232, 234, 236);
    private static readonly Color MutedTextColor = Color.FromArgb(174, 179, 184);

    public DarkTabControl()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer,
            true);
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(142, 34);
        BackColor = SurfaceColor;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(SurfaceColor);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(SurfaceColor);

        var pageBounds = DisplayRectangle;
        using (var borderPen = new Pen(BorderColor))
        {
            e.Graphics.DrawRectangle(
                borderPen,
                pageBounds.X - 1,
                pageBounds.Y - 1,
                pageBounds.Width + 1,
                pageBounds.Height + 1);
        }

        for (var index = 0; index < TabCount; index++)
        {
            var tabBounds = GetTabRect(index);
            var selected = index == SelectedIndex;
            using var backgroundBrush = new SolidBrush(selected ? SelectedColor : SurfaceColor);
            e.Graphics.FillRectangle(backgroundBrush, tabBounds);

            if (selected)
            {
                using var accentBrush = new SolidBrush(Color.FromArgb(47, 184, 75));
                e.Graphics.FillRectangle(accentBrush, tabBounds.Left, tabBounds.Bottom - 2, tabBounds.Width, 2);
            }

            TextRenderer.DrawText(
                e.Graphics,
                TabPages[index].Text,
                Font,
                tabBounds,
                selected ? TextColor : MutedTextColor,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        Invalidate();
    }
}

internal sealed class GameManagerForm : Form
{
    private readonly InstallerSettings settings;
    private readonly InstallerLogger logger;
    private readonly Action restartSteam;
    private readonly CancellationTokenSource closingCancellation = new();
    private readonly ListView managedGamesList = CreateListView();
    private readonly ListView removedGamesList = CreateListView();
    private List<ManagedGame> managedGames = [];
    private List<RemovedGameArchive> removedArchives = [];

    public GameManagerForm(InstallerSettings settings, InstallerLogger logger, Action restartSteam)
    {
        this.settings = settings;
        this.logger = logger;
        this.restartSteam = restartSteam;

        Text = "TOST Game Manager";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(760, 500);
        BackColor = Color.FromArgb(35, 36, 38);
        ForeColor = Color.FromArgb(232, 234, 236);
        Font = new Font("Segoe UI", 9.5f);
        WindowTheme.ApplyDarkTitleBar(this);

        managedGamesList.Columns.Add("Game", 250);
        managedGamesList.Columns.Add("App ID", 100);
        managedGamesList.Columns.Add("Lua file", 220);
        managedGamesList.Columns.Add("Manifests", 90);

        removedGamesList.Columns.Add("Removed", 150);
        removedGamesList.Columns.Add("Games", 430);
        removedGamesList.Columns.Add("Files", 80);

        var tabs = new DarkTabControl
        {
            Dock = DockStyle.Fill
        };
        tabs.TabPages.Add(CreateManagedGamesPage());
        tabs.TabPages.Add(CreateRemovedGamesPage());

        Controls.Add(tabs);
        Shown += async (_, _) => await RefreshListsAsync();
        FormClosed += (_, _) => closingCancellation.Cancel();
    }

    private TabPage CreateManagedGamesPage()
    {
        var page = CreateTabPage("Managed Games");
        var description = CreateDescriptionLabel(
            "Games detected from Steam's config\\lua folder. Removal moves only the Lua file and its unshared depot manifests into TOST's recovery folder.");
        var removeButton = CreateActionButton("Remove Selected", primary: true);
        removeButton.Location = new Point(14, 10);
        removeButton.Click += (_, _) => RemoveSelectedGames();
        var refreshButton = CreateActionButton("Refresh", primary: false);
        refreshButton.Location = new Point(removeButton.Right + 8, 10);
        refreshButton.Click += async (_, _) => await RefreshListsAsync();

        var actionBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            BackColor = Color.FromArgb(31, 32, 34)
        };
        actionBar.Controls.Add(removeButton);
        actionBar.Controls.Add(refreshButton);

        managedGamesList.Dock = DockStyle.Fill;
        page.Controls.Add(managedGamesList);
        page.Controls.Add(actionBar);
        page.Controls.Add(description);
        return page;
    }

    private TabPage CreateRemovedGamesPage()
    {
        var page = CreateTabPage("Recovery");
        var description = CreateDescriptionLabel(
            "Files removed through TOST remain recoverable here. Restore returns them to the current Steam folder without overwriting existing files.");
        var restoreButton = CreateActionButton("Restore Selected", primary: true);
        restoreButton.Location = new Point(14, 10);
        restoreButton.Click += (_, _) => RestoreSelectedArchive();

        var actionBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            BackColor = Color.FromArgb(31, 32, 34)
        };
        actionBar.Controls.Add(restoreButton);

        removedGamesList.Dock = DockStyle.Fill;
        page.Controls.Add(removedGamesList);
        page.Controls.Add(actionBar);
        page.Controls.Add(description);
        return page;
    }

    private async Task RefreshListsAsync()
    {
        UseWaitCursor = true;
        try
        {
            managedGames = GameManagementService.FindManagedGames(settings, logger);
            removedArchives = GameManagementService.FindRemovedGames(logger);
            PopulateManagedGames();
            PopulateRemovedGames();

            var missingIds = managedGames
                .Where(game => string.IsNullOrWhiteSpace(game.Name))
                .Select(game => game.AppId)
                .ToList();
            if (missingIds.Count == 0)
            {
                return;
            }

            var resolvedNames = await SteamGameNameResolver.ResolveAsync(
                missingIds,
                logger,
                closingCancellation.Token);
            managedGames = managedGames
                .Select(game => resolvedNames.TryGetValue(game.AppId, out var name)
                    ? game with { Name = name }
                    : game)
                .ToList();
            PopulateManagedGames();
        }
        catch (OperationCanceledException) when (closingCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.Error($"Could not refresh Game Manager: {ex}");
            TostDialog.Show(
                this,
                $"Could not scan the Steam folders.\n\n{ex.Message}",
                "TOST Game Manager",
                TostDialogButtons.Ok,
                TostDialogIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void PopulateManagedGames()
    {
        managedGamesList.BeginUpdate();
        managedGamesList.Items.Clear();
        foreach (var game in managedGames)
        {
            var item = new ListViewItem(game.DisplayName)
            {
                Tag = game
            };
            item.SubItems.Add(game.AppId);
            item.SubItems.Add(Path.GetFileName(game.LuaPath));
            item.SubItems.Add(game.ManifestPaths.Count.ToString());
            managedGamesList.Items.Add(item);
        }

        if (managedGamesList.Items.Count == 0)
        {
            managedGamesList.Items.Add(new ListViewItem("No managed game Lua files were found.")
            {
                ForeColor = Color.FromArgb(151, 157, 164)
            });
        }

        managedGamesList.EndUpdate();
    }

    private void PopulateRemovedGames()
    {
        removedGamesList.BeginUpdate();
        removedGamesList.Items.Clear();
        foreach (var archive in removedArchives)
        {
            var gameNames = string.Join(", ", archive.Games.Select(game => game.DisplayName));
            var item = new ListViewItem(archive.RemovedUtc.ToLocalTime().ToString("g"))
            {
                Tag = archive
            };
            item.SubItems.Add(gameNames);
            item.SubItems.Add(archive.Files.Count.ToString());
            removedGamesList.Items.Add(item);
        }

        if (removedGamesList.Items.Count == 0)
        {
            removedGamesList.Items.Add(new ListViewItem("No removed games are available to restore.")
            {
                ForeColor = Color.FromArgb(151, 157, 164)
            });
        }

        removedGamesList.EndUpdate();
    }

    private void RemoveSelectedGames()
    {
        var selectedGames = managedGamesList.CheckedItems
            .Cast<ListViewItem>()
            .Select(item => item.Tag)
            .OfType<ManagedGame>()
            .ToList();
        if (selectedGames.Count == 0)
        {
            ShowSelectionRequired("Select one or more managed games first.");
            return;
        }

        var names = string.Join("\n", selectedGames.Select(game => $"• {game.DisplayName} ({game.AppId})"));
        var confirmation = TostDialog.Show(
            this,
            $"Remove the following games from OpenSteamTool?\n\n{names}\n\nFiles will be moved to TOST's recovery folder and can be restored.",
            "Remove Managed Games",
            TostDialogButtons.YesNo,
            TostDialogIcon.Warning);
        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        var result = GameManagementService.RemoveGames(selectedGames, managedGames, settings, logger);
        TostDialog.Show(
            this,
            result.Message,
            "TOST Game Manager",
            TostDialogButtons.Ok,
            result.Success ? TostDialogIcon.Success : TostDialogIcon.Warning);
        if (!result.Success)
        {
            return;
        }

        _ = RefreshListsAsync();
        OfferSteamRestart();
    }

    private void RestoreSelectedArchive()
    {
        var selectedArchives = removedGamesList.CheckedItems
            .Cast<ListViewItem>()
            .Select(item => item.Tag)
            .OfType<RemovedGameArchive>()
            .ToList();
        if (selectedArchives.Count != 1)
        {
            ShowSelectionRequired("Select exactly one recovery entry to restore.");
            return;
        }

        var archive = selectedArchives[0];
        var names = string.Join(", ", archive.Games.Select(game => game.DisplayName));
        var confirmation = TostDialog.Show(
            this,
            $"Restore {names}?\n\nExisting files will not be overwritten.",
            "Restore Managed Games",
            TostDialogButtons.YesNo,
            TostDialogIcon.Information);
        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        var result = GameManagementService.RestoreArchive(archive, settings, logger);
        TostDialog.Show(
            this,
            result.Message,
            "TOST Game Manager",
            TostDialogButtons.Ok,
            result.Success ? TostDialogIcon.Success : TostDialogIcon.Warning);
        if (!result.Success)
        {
            return;
        }

        _ = RefreshListsAsync();
        OfferSteamRestart();
    }

    private void OfferSteamRestart()
    {
        var restart = TostDialog.Show(
            this,
            "Restart Steam now to apply the change?",
            "TOST Game Manager",
            TostDialogButtons.YesNo,
            TostDialogIcon.Information);
        if (restart == DialogResult.Yes)
        {
            restartSteam();
        }
    }

    private void ShowSelectionRequired(string message)
    {
        TostDialog.Show(
            this,
            message,
            "TOST Game Manager",
            TostDialogButtons.Ok,
            TostDialogIcon.Information);
    }

    private static ListView CreateListView()
    {
        return new ListView
        {
            View = View.Details,
            CheckBoxes = true,
            FullRowSelect = true,
            GridLines = false,
            HideSelection = false,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(29, 30, 32),
            ForeColor = Color.FromArgb(232, 234, 236),
            Font = new Font("Segoe UI", 9.5f)
        };
    }

    private static TabPage CreateTabPage(string text)
    {
        return new TabPage(text)
        {
            BackColor = Color.FromArgb(35, 36, 38),
            ForeColor = Color.FromArgb(232, 234, 236),
            Padding = new Padding(10)
        };
    }

    private static Label CreateDescriptionLabel(string text)
    {
        return new Label
        {
            Dock = DockStyle.Top,
            Height = 54,
            Padding = new Padding(2, 6, 2, 6),
            Text = text,
            ForeColor = Color.FromArgb(174, 179, 184),
            BackColor = Color.FromArgb(35, 36, 38)
        };
    }

    private static Button CreateActionButton(string text, bool primary)
    {
        var button = new Button
        {
            AutoSize = false,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(132, 32),
            Text = text,
            Font = new Font("Segoe UI Semibold", 9.5f),
            ForeColor = Color.White,
            BackColor = primary ? Color.FromArgb(33, 150, 57) : Color.FromArgb(63, 65, 68),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = primary
            ? Color.FromArgb(48, 183, 73)
            : Color.FromArgb(82, 84, 87);
        button.FlatAppearance.MouseOverBackColor = primary
            ? Color.FromArgb(39, 166, 64)
            : Color.FromArgb(75, 77, 80);
        return button;
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
        WindowTheme.ApplyDarkTitleBar(this);

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
    public static string RemovedGamesDirectory => Path.Combine(DataDirectory, "removed-games");
    public static string GameNamesCachePath => Path.Combine(DataDirectory, "steam-game-names.json");

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

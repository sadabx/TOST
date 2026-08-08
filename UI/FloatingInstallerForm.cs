using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Velopack;
using Velopack.Sources;

namespace Trionine.TOST;

internal sealed partial class FloatingInstallerForm : Form
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
            Path.Combine(AppContext.BaseDirectory, "Assets", "TOST.png"),
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
            .GetManifestResourceStream("TOST.Assets.TOST.png");
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

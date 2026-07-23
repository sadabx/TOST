using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;

namespace OpenSteamTool.FloatingInstaller;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new FloatingInstallerForm());
    }
}

internal sealed class FloatingInstallerForm : Form
{
    private readonly InstallerSettings settings;
    private readonly InstallerLogger logger;
    private readonly Label glyph = new();
    private readonly ToolTip toolTip = new();

    public FloatingInstallerForm()
    {
        settings = InstallerSettings.Load();
        logger = new InstallerLogger(settings.LogPath);

        Text = "OpenSteamTool Installer";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(76, 76);
        MinimumSize = Size;
        MaximumSize = Size;
        TopMost = settings.AlwaysOnTop;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(28, 31, 36);
        AllowDrop = true;
        ContextMenuStrip = BuildMenu();

        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Location = new Point(screen.Right - Width - 32, screen.Top + 120);

        glyph.Dock = DockStyle.Fill;
        glyph.Image = LoadLogo();
        glyph.ImageAlign = ContentAlignment.MiddleCenter;
        glyph.AllowDrop = true;
        glyph.ContextMenuStrip = ContextMenuStrip;
        Controls.Add(glyph);

        toolTip.SetToolTip(glyph, "Drop OpenSteamTool files or folders here");

        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        glyph.DragEnter += OnDragEnter;
        glyph.DragDrop += OnDragDrop;

        EnableWindowDrag(this);
        EnableWindowDrag(glyph);

        logger.Info("Floating installer started.");
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Install / Repair OpenSteamTool", null, (_, _) => InstallBundledFiles());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open Steam Folder", null, (_, _) => OpenFolder(settings.SteamRoot));
        menu.Items.Add("Open Steam Config", null, (_, _) => OpenFolder(settings.SteamConfigPath));
        menu.Items.Add("Open Steam Lua", null, (_, _) => OpenFolder(settings.LuaPath));
        menu.Items.Add("Open Steam Apps", null, (_, _) => OpenFolder(settings.SteamAppsPath));
        menu.Items.Add("Open Logs", null, (_, _) => OpenFolder(settings.LogDirectory));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings", null, (_, _) => OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Restart Steam", null, (_, _) => RestartSteam());
        menu.Items.Add("Exit Steam", null, (_, _) => KillProcess("steam"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit Installer", null, (_, _) => Close());
        return menu;
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return;
        }

        var report = new CopyReport();
        foreach (var path in paths)
        {
            CopyExpectedPath(path, report);
        }

        ShowReport(report);
    }

    private void InstallBundledFiles()
    {
        var bundledDirectory = settings.BundledFilesPath;
        var report = new CopyReport();

        EnsureSteamFolders(report);
        if (!Directory.Exists(bundledDirectory))
        {
            report.AddFailure(bundledDirectory, "Bundled files folder was not found.");
            ShowReport(report);
            return;
        }

        foreach (var file in Directory.EnumerateFiles(bundledDirectory, "*", SearchOption.AllDirectories))
        {
            if (ResolveDestination(Path.GetFileName(file)) is null)
            {
                logger.Info($"Ignored bundled file with no route: {file}");
                continue;
            }

            CopyExpectedPath(file, report);
        }

        ShowReport(report);
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
        MessageBox.Show("Settings saved.", "OpenSteamTool Installer", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowReport(CopyReport report)
    {
        logger.Info(report.ToLogMessage());
        MessageBox.Show(
            report.ToMessage(),
            "OpenSteamTool Installer",
            MessageBoxButtons.OK,
            report.Failures == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
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
                return Image.FromFile(path);
            }
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

        const string valueName = "OpenSteamToolFloatingInstaller";
        if (enabled)
        {
            key.SetValue(valueName, $"\"{Application.ExecutablePath}\"");
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

internal sealed class SettingsForm : Form
{
    private readonly InstallerSettings settings;
    private readonly TextBox steamRootTextBox = new();
    private readonly CheckBox overwriteCheckBox = new();
    private readonly CheckBox backupCheckBox = new();
    private readonly CheckBox startupCheckBox = new();
    private readonly CheckBox alwaysOnTopCheckBox = new();

    public SettingsForm(InstallerSettings settings)
    {
        this.settings = settings;

        Text = "OpenSteamTool Installer Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 238);

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

        var saveButton = new Button
        {
            Text = "Save",
            DialogResult = DialogResult.OK,
            Location = new Point(348, 196),
            Size = new Size(75, 29)
        };
        saveButton.Click += (_, _) => ApplySettings();

        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(429, 196),
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
    }
}

internal sealed class InstallerSettings
{
    public string SteamRoot { get; set; } = DetectSteamRoot();
    public bool OverwriteExisting { get; set; } = true;
    public bool BackupBeforeOverwrite { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool AlwaysOnTop { get; set; } = true;

    public string SteamConfigPath => Path.Combine(SteamRoot, "config");
    public string LuaPath => Path.Combine(SteamConfigPath, "lua");
    public string SteamAppsPath => Path.Combine(SteamRoot, "steamapps");
    public string BundledFilesPath => Path.Combine(AppContext.BaseDirectory, "files");
    public string LogDirectory => Path.Combine(AppContext.BaseDirectory, "logs");
    public string LogPath => Path.Combine(LogDirectory, "install.log");

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
        Directory.CreateDirectory(AppContext.BaseDirectory);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }

    private static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "installer-settings.json");

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

internal sealed class CopyReport
{
    private readonly List<string> lines = [];
    public int Successes { get; private set; }
    public int Failures { get; private set; }

    public void AddSuccess(string fileName, string destinationDirectory)
    {
        Successes++;
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
}

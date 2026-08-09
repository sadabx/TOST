using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Trionine.TOST.Core.Imports;
using Trionine.TOST.Core.Integrations.SlsSteam;
using Trionine.TOST.Core.Steam;

namespace Trionine.TOST.Desktop.Views;

internal sealed class ImportView : UserControl
{
    private readonly ComboBox installation = new() { ItemsSource = new[] { "Native Steam", "Flatpak Steam" }, SelectedIndex = 0, Width = 180 };
    private readonly TextBox output = new() { AcceptsReturn = true, IsReadOnly = true, MinHeight = 260, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly Button previewButton = new() { Content = "Preview", IsEnabled = false };
    private readonly Button applyButton = new() { Content = "Apply Import", IsEnabled = false };
    private readonly CheckBox confirmation = new() { Content = "I reviewed the destinations and backups", IsEnabled = false };
    private string[] selectedPaths = [];
    private SteamInstallation? selectedSteam;
    private SteamImportPlan? importPlan;
    private SlsSteamImportConversionPlan? conversionPlan;

    public ImportView()
    {
        installation.SelectedIndex = DesktopPaths.PreferredInstallationIndex;
        var chooseButton = new Button { Content = "Choose files" };
        chooseButton.Click += ChooseFiles;
        previewButton.Click += Preview;
        applyButton.Click += Apply;
        confirmation.IsCheckedChanged += (_, _) => applyButton.IsEnabled = confirmation.IsChecked == true && importPlan?.CanApply == true;
        if (OperatingSystem.IsLinux())
        {
            DragDrop.SetAllowDrop(output, true);
            DragDrop.AddDragOverHandler(output, DragOver);
            DragDrop.AddDropHandler(output, DropFiles);
        }
        Content = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                Card("Import target", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { installation, chooseButton, previewButton } }),
                Card("Preview", output),
                confirmation,
                applyButton
            }
        };
        if (!OperatingSystem.IsLinux())
        {
            output.Text = "The Avalonia importer is currently connected to the Linux backend. Windows imports remain available in the existing TOST WinForms application.";
            chooseButton.IsEnabled = false;
        }
    }

    private static void DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Copy;
    }

    private void DropFiles(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        SetSelectedPaths(files?.Select(file => file.TryGetLocalPath()));
    }

    private async void ChooseFiles(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose Steam import files",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("TOST import files") { Patterns = ["*.lua", "*.manifest", "appmanifest_*.acf"] }
            ]
        });
        SetSelectedPaths(files.Select(file => file.TryGetLocalPath()));
    }

    private void SetSelectedPaths(IEnumerable<string?>? paths)
    {
        selectedPaths = paths?.Where(path => path is not null).Cast<string>().Distinct(StringComparer.Ordinal).ToArray() ?? [];
        previewButton.IsEnabled = selectedPaths.Length > 0;
        applyButton.IsEnabled = false;
        confirmation.IsEnabled = false;
        confirmation.IsChecked = false;
        output.Text = selectedPaths.Length == 0
            ? "Drop Lua or manifest files here, or choose them above."
            : string.Join(Environment.NewLine, selectedPaths);
    }

    private void Preview(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var expectedKind = installation.SelectedIndex == 1 ? SteamInstallationKind.Flatpak : SteamInstallationKind.Native;
            selectedSteam = LinuxSteamDiscovery.FindInstallations().FirstOrDefault(item => item.Kind == expectedKind)
                ?? throw new DirectoryNotFoundException($"No {expectedKind} Steam installation was found.");
            importPlan = new SteamImportService().CreatePlan(selectedSteam, selectedPaths);
            conversionPlan = new SlsSteamImportConversionService().CreatePlan(importPlan.Items.Select(item => item.Inspection));
            var lines = new List<string> { $"Steam: {selectedSteam.RootPath}", string.Empty };
            foreach (var item in importPlan.Items)
            {
                lines.Add($"{item.State}: {Path.GetFileName(item.Inspection.Path)}");
                lines.Add($"  → {item.DestinationPath}");
                if (item.Message is not null) lines.Add($"  {item.Message}");
            }
            lines.Add(string.Empty);
            lines.Add($"SLSsteam: {conversionPlan.AdditionalApps.Count} apps, {conversionPlan.AppTokens.Count} tokens, {conversionPlan.ManifestIds.Count} manifests");
            lines.Add($"Steam VDF: {conversionPlan.DepotKeys.Count} depot keys");
            foreach (var warning in conversionPlan.Warnings) lines.Add($"Warning: {warning}");
            output.Text = string.Join(Environment.NewLine, lines);
            confirmation.IsEnabled = importPlan.CanApply;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            output.Text = $"Preview failed: {ex.Message}";
            confirmation.IsEnabled = false;
        }
    }

    private void Apply(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (selectedSteam is null || importPlan?.CanApply != true || conversionPlan is null || confirmation.IsChecked != true) return;
        try
        {
            var result = new SteamImportService().ApplyNewFiles(selectedSteam, selectedPaths);
            if (!result.Success) throw new IOException(result.Message);
            var slsPaths = selectedSteam.Kind == SteamInstallationKind.Flatpak ? SlsSteamPaths.ForFlatpakUser() : SlsSteamPaths.ForCurrentUser();
            var backupRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TOST", "backups");
            if (conversionPlan.AdditionalApps.Count > 0)
                new SlsSteamImportConfigService().Apply(slsPaths.ConfigPath, conversionPlan, Path.Combine(backupRoot, "SLSsteam"));
            if (conversionPlan.DepotKeys.Count > 0)
                new SteamDepotKeyService().Apply(Path.Combine(selectedSteam.ConfigPath, "config.vdf"), conversionPlan.DepotKeys, Path.Combine(backupRoot, "Steam-config"));
            output.Text += $"{Environment.NewLine}{Environment.NewLine}Import complete. Restart Steam through the configured SLSsteam launch path.";
            applyButton.IsEnabled = false;
            confirmation.IsChecked = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            output.Text += $"{Environment.NewLine}{Environment.NewLine}Import failed: {ex.Message}";
        }
    }

    private static Border Card(string title, Control content) => new()
    {
        Classes = { "card" },
        Child = new StackPanel { Spacing = 10, Children = { new TextBlock { Text = title, FontSize = 17, FontWeight = Avalonia.Media.FontWeight.SemiBold }, content } }
    };
}

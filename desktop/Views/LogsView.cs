using Avalonia.Controls;
using Avalonia.Layout;
using Trionine.TOST.Core.Integrations.SlsSteam;

namespace Trionine.TOST.Desktop.Views;

internal sealed class LogsView : UserControl
{
    private readonly ComboBox installation = new() { ItemsSource = new[] { "Native SLSsteam", "Flatpak SLSsteam" }, SelectedIndex = 0, Width = 190 };
    private readonly NumericUpDown lineCount = new() { Minimum = 10, Maximum = SlsSteamDiagnosticsService.MaximumTailLines, Value = 100, Increment = 10, Width = 100 };
    private readonly TextBox output = new() { AcceptsReturn = true, IsReadOnly = true, FontFamily = "monospace", MinHeight = 440, TextWrapping = Avalonia.Media.TextWrapping.NoWrap };

    public LogsView()
    {
        var preferences = DesktopPaths.PreferencesStore.Load();
        installation.SelectedIndex = preferences.PreferredSteamInstallation == Trionine.TOST.Core.Steam.SteamInstallationKind.Flatpak ? 1 : 0;
        lineCount.Value = preferences.DiagnosticTailLines;
        var refresh = new Button { Content = "Refresh" };
        refresh.Click += (_, _) => Refresh();
        installation.SelectionChanged += (_, _) => Refresh();
        Content = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                Card("Log source", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { installation, new TextBlock { Text = "Lines", VerticalAlignment = VerticalAlignment.Center }, lineCount, refresh } }),
                Card("Latest bounded log", output)
            }
        };
        if (OperatingSystem.IsLinux()) Refresh();
        else { output.Text = "Windows OpenSteamTool log integration will be connected after its services move into TOST.Core."; installation.IsEnabled = refresh.IsEnabled = false; }
    }

    private void Refresh()
    {
        try
        {
            var paths = installation.SelectedIndex == 1 ? SlsSteamPaths.ForFlatpakUser() : SlsSteamPaths.ForCurrentUser();
            var diagnostics = new SlsSteamDiagnosticsService();
            var binary = diagnostics.InspectBinary(paths.MainLibraryPath);
            var log = diagnostics.ReadLatestLog(paths.LogPaths, (int)(lineCount.Value ?? 100));
            var header = binary is null ? "SLSsteam binary not found." : $"Binary: {binary.Path}{Environment.NewLine}SHA-256: {binary.Sha256}";
            output.Text = log is null
                ? $"{header}{Environment.NewLine}{Environment.NewLine}No SLSsteam log was found."
                : $"{header}{Environment.NewLine}{Environment.NewLine}Log: {log.Path}{Environment.NewLine}{(log.Truncated ? "[Earlier content omitted]" + Environment.NewLine : "")}{string.Join(Environment.NewLine, log.Lines)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            output.Text = $"Could not read diagnostics: {ex.Message}";
        }
    }

    private static Border Card(string title, Control content) => new()
    {
        Classes = { "card" },
        Child = new StackPanel { Spacing = 10, Children = { new TextBlock { Text = title, FontSize = 17, FontWeight = Avalonia.Media.FontWeight.SemiBold }, content } }
    };
}

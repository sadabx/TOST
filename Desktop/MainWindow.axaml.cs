using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Trionine.TOST.Core.Integrations.SlsSteam;
using Trionine.TOST.Core.Steam;
using Trionine.TOST.Desktop.Views;

namespace Trionine.TOST.Desktop;

public sealed partial class MainWindow : Window
{
    private string currentPage = "Overview";
    private readonly TextBlock pageTitle;
    private readonly TextBlock pageSubtitle;
    private readonly ContentControl pageContent;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        pageTitle = this.FindControl<TextBlock>("PageTitle") ?? throw new InvalidOperationException("Page title control is missing.");
        pageSubtitle = this.FindControl<TextBlock>("PageSubtitle") ?? throw new InvalidOperationException("Page subtitle control is missing.");
        pageContent = this.FindControl<ContentControl>("PageContent") ?? throw new InvalidOperationException("Page content control is missing.");
        Opened += (_, _) => RenderCurrentPage();
        Closing += (_, args) =>
        {
            if (Application.Current is App { IsExiting: false } && DesktopPaths.PreferencesStore.Load().CloseToTray)
            {
                args.Cancel = true;
                Hide();
            }
        };
    }

    private void ShowOverview(object? sender, RoutedEventArgs e) => Navigate("Overview", "Cross-platform Steam integration status");
    private void ShowGameManager(object? sender, RoutedEventArgs e) => Navigate("Game Manager", "Remove and restore imported games");
    private void ShowImports(object? sender, RoutedEventArgs e) => Navigate("Import Files", "Preview Lua, manifests, and AppState files before applying");
    private void ShowIntegration(object? sender, RoutedEventArgs e) => Navigate("Integration", "Manage OpenSteamTool on Windows and SLSsteam on Linux");
    private void ShowRecovery(object? sender, RoutedEventArgs e) => Navigate("Recovery", "Configuration, library, game, and launch-hook archives");
    private void ShowLogs(object? sender, RoutedEventArgs e) => Navigate("Logs", "Inspect bounded integration diagnostics");
    private void ShowSettings(object? sender, RoutedEventArgs e) => Navigate("Settings", "TOST behavior and platform options");
    private void RefreshCurrentPage(object? sender, RoutedEventArgs e) => RenderCurrentPage();

    private void Navigate(string page, string subtitle)
    {
        currentPage = page;
        pageTitle.Text = page;
        pageSubtitle.Text = subtitle;
        RenderCurrentPage();
    }

    internal void OpenPage(string page)
    {
        var subtitle = page switch
        {
            "Game Manager" => "Remove and restore imported games",
            "Import Files" => "Preview Lua, manifests, and AppState files before applying",
            "Integration" => "Manage OpenSteamTool on Windows and SLSsteam on Linux",
            "Recovery" => "Configuration, library, game, and launch-hook archives",
            "Logs" => "Inspect bounded integration diagnostics",
            "Settings" => "TOST behavior and platform options",
            _ => "Cross-platform Steam integration status"
        };
        Navigate(page, subtitle);
    }

    private void RenderCurrentPage()
    {
        pageContent.Content = currentPage switch
        {
            "Overview" => BuildOverview(),
            "Game Manager" => new GameManagerView(),
            "Import Files" => new ImportView(),
            "Integration" => new IntegrationView(),
            "Recovery" => new RecoveryView(),
            "Logs" => new LogsView(),
            "Settings" => new SettingsView(),
            _ => BuildPlaceholder(currentPage)
        };
    }

    private Control BuildOverview()
    {
        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(Card("Platform", OperatingSystem.IsLinux() ? "Linux" : OperatingSystem.IsWindows() ? "Windows" : "Unsupported platform"));
        if (OperatingSystem.IsLinux())
        {
            var installations = LinuxSteamDiscovery.FindInstallations();
            panel.Children.Add(Card("Steam installations", installations.Count == 0
                ? "No native or Flatpak Steam installation detected"
                : string.Join(Environment.NewLine, installations.Select(item => $"{item.Kind}: {item.RootPath}"))));
            var native = new SlsSteamProvider(SlsSteamPaths.ForCurrentUser()).GetStatusAsync().GetAwaiter().GetResult();
            var flatpak = new SlsSteamProvider(SlsSteamPaths.ForFlatpakUser()).GetStatusAsync().GetAwaiter().GetResult();
            panel.Children.Add(Card("Native SLSsteam", $"{native.Health} — {native.Summary}"));
            panel.Children.Add(Card("Flatpak SLSsteam", $"{flatpak.Health} — {flatpak.Summary}"));
        }
        else
        {
            panel.Children.Add(Card("Windows frontend", "The existing WinForms application remains active while feature screens migrate to Avalonia."));
        }
        return panel;
    }

    private static Control BuildPlaceholder(string name) => Card(name, "This screen shell is ready. Backend actions will be connected in the next migration batch.");

    private static Border Card(string title, string body) => new()
    {
        Classes = { "card" },
        Child = new StackPanel
        {
            Spacing = 7,
            Children =
            {
                new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = body, Foreground = new SolidColorBrush(Color.Parse("#B7C1BA")), TextWrapping = TextWrapping.Wrap }
            }
        }
    };
}

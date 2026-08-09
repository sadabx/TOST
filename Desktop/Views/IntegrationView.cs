using Avalonia.Controls;
using Avalonia.Layout;
using Trionine.TOST.Core.Integrations.SlsSteam;
using Trionine.TOST.Desktop.Services;

namespace Trionine.TOST.Desktop.Views;

internal sealed class IntegrationView : UserControl
{
    private readonly ComboBox installation = new() { ItemsSource = new[] { "Native SLSsteam", "Flatpak SLSsteam" }, SelectedIndex = 0, Width = 190 };
    private readonly TextBox output = new() { AcceptsReturn = true, IsReadOnly = true, MinHeight = 280, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly Button installButton = new() { Content = "Install verified release" };
    private readonly Button configureButton = new() { Content = "Configure launch" };
    private readonly Button removeButton = new() { Content = "Archive launch hooks" };

    public IntegrationView()
    {
        installation.SelectedIndex = DesktopPaths.PreferredInstallationIndex;
        var refresh = new Button { Content = "Refresh status" };
        refresh.Click += async (_, _) => await RefreshAsync();
        installButton.Click += async (_, _) => await InstallAsync();
        configureButton.Click += async (_, _) => await ConfigureLaunchAsync();
        removeButton.Click += async (_, _) => await ArchiveLaunchAsync();
        installation.SelectionChanged += async (_, _) => await RefreshAsync();
        Content = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                Card("Integration", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { installation, refresh, installButton, configureButton, removeButton } }),
                Card("Status and activity", output)
            }
        };
        if (!OperatingSystem.IsLinux())
        {
            output.Text = "OpenSteamTool management remains in the existing Windows frontend while its platform service is extracted into TOST.Core.";
            installation.IsEnabled = refresh.IsEnabled = installButton.IsEnabled = configureButton.IsEnabled = removeButton.IsEnabled = false;
        }
        else _ = RefreshAsync();
    }

    private SlsSteamPaths Paths => installation.SelectedIndex == 1 ? SlsSteamPaths.ForFlatpakUser() : SlsSteamPaths.ForCurrentUser();
    private bool Flatpak => installation.SelectedIndex == 1;

    private async Task RefreshAsync()
    {
        try
        {
            var status = await new SlsSteamProvider(Paths).GetStatusAsync();
            output.Text = $"{status.DisplayName}: {status.Health}{Environment.NewLine}{status.Summary}{Environment.NewLine}{Environment.NewLine}" +
                          string.Join(Environment.NewLine, status.Components.Select(item => $"{(item.Exists ? "Found" : "Missing")}: {item.Name}{Environment.NewLine}  {item.Path}"));
        }
        catch (Exception ex) { output.Text = $"Status failed: {ex.Message}"; }
    }

    private async Task InstallAsync()
    {
        if (!await TostDialog.ConfirmAsync(this, "Install SLSsteam",
                "TOST will download the official portable release, verify its published SHA-256 digest, and replace the managed SLSsteam libraries.",
                "Install")) return;
        installButton.IsEnabled = false;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            output.Text = "Checking the official SLSsteam release…";
            var release = await new SlsSteamReleaseService(client).GetLatestAsync();
            var installer = new SlsSteamInstallerService(client);
            var preview = installer.Preview(release, Paths);
            output.Text = $"Pinned release: {preview.Tag}{Environment.NewLine}Asset: {preview.Asset.Name}{Environment.NewLine}SHA-256: {preview.Asset.Sha256}";
            if (!preview.CanInstall) { output.Text += $"{Environment.NewLine}{preview.BlockReason}"; return; }
            output.Text += $"{Environment.NewLine}{Environment.NewLine}Downloading and verifying…";
            var result = await installer.InstallAsync(release, Paths);
            output.Text += $"{Environment.NewLine}Installed {result.Tag}.{Environment.NewLine}Configure launch injection before starting Steam.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            output.Text += $"{Environment.NewLine}Installation failed: {ex.Message}";
        }
        finally { installButton.IsEnabled = true; }
    }

    private async Task ConfigureLaunchAsync()
    {
        try
        {
            var plan = SlsSteamLaunchPlanFactory.Create(Flatpak, Paths);
            output.Text = FormatPlan(plan);
            if (!plan.CanApply) return;
            if (!plan.HasChanges) { output.Text += $"{Environment.NewLine}{Environment.NewLine}Launch injection is already configured."; return; }
            if (!await TostDialog.ConfirmAsync(this, "Configure Steam launch",
                    $"Create {plan.Items.Count(item => item.State == SlsSteamLaunchItemState.Ready)} TOST-managed {plan.Kind} launch hook files? Close Steam first.",
                    "Configure")) return;
            var created = new SlsSteamLaunchConfigurationService().Apply(plan);
            output.Text += $"{Environment.NewLine}{Environment.NewLine}Created {created.Count} launch hooks. Start Steam normally to use SLSsteam.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            output.Text = $"Could not configure launch injection: {ex.Message}";
        }
    }

    private async Task ArchiveLaunchAsync()
    {
        try
        {
            var plan = SlsSteamLaunchPlanFactory.Create(Flatpak, Paths);
            output.Text = FormatPlan(plan);
            var present = plan.Items.Count(item => item.State == SlsSteamLaunchItemState.AlreadyConfigured);
            if (present == 0) { output.Text += $"{Environment.NewLine}{Environment.NewLine}No unmodified TOST-managed launch hooks were found."; return; }
            if (!plan.CanApply) return;
            if (!await TostDialog.ConfirmAsync(this, "Archive launch hooks",
                    $"Move {present} unmodified {plan.Kind} launch hook files into TOST Recovery? Close Steam first.",
                    "Archive")) return;
            var recovery = Path.Combine(DesktopPaths.DataRoot, "backups", "launch-hooks");
            var archived = new SlsSteamLaunchConfigurationService().ArchiveManaged(plan, recovery);
            output.Text += $"{Environment.NewLine}{Environment.NewLine}Archived {archived.Paths.Count} hooks as {archived.ArchiveId}. Restore them from Recovery.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            output.Text = $"Could not archive launch injection: {ex.Message}";
        }
    }

    private static string FormatPlan(SlsSteamLaunchPlan plan) =>
        $"{plan.Kind} launch injection:{Environment.NewLine}" + string.Join(Environment.NewLine,
            plan.Items.Select(item => $"{item.State}: {item.Path}{(item.Message is null ? string.Empty : $" — {item.Message}")}"));

    private static Border Card(string title, Control content) => new()
    {
        Classes = { "card" },
        Child = new StackPanel { Spacing = 10, Children = { new TextBlock { Text = title, FontSize = 17, FontWeight = Avalonia.Media.FontWeight.SemiBold }, content } }
    };
}

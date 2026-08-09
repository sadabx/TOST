using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Trionine.TOST.Core.GameManagement;
using Trionine.TOST.Core.Steam;

namespace Trionine.TOST.Desktop.Views;

internal sealed class GameManagerView : UserControl
{
    private readonly ManagedGameService service = new();
    private readonly ComboBox installation = new() { Width = 170 };
    private readonly ListBox games = new() { MinHeight = 250, SelectionMode = SelectionMode.Multiple };
    private readonly ListBox recovery = new() { MinHeight = 150 };
    private readonly CheckBox confirmation = new() { Content = "I reviewed the selected files and recovery destination" };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button remove = new() { Content = "Archive selected games", IsEnabled = false };
    private readonly Button restore = new() { Content = "Restore selected archive", IsEnabled = false };
    private IReadOnlyList<ManagedGame> managedGames = [];

    public GameManagerView()
    {
        var refresh = new Button { Content = "Refresh" };
        refresh.Click += (_, _) => Refresh();
        installation.SelectionChanged += (_, _) => Refresh();
        games.SelectionChanged += (_, _) => UpdateActions();
        recovery.SelectionChanged += (_, _) => UpdateActions();
        confirmation.IsCheckedChanged += (_, _) => UpdateActions();
        remove.Click += (_, _) => RemoveSelected();
        restore.Click += (_, _) => RestoreSelected();

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { remove, restore } };
        Content = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                Card("Steam installation", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { installation, refresh } }),
                Card("Managed games", new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = "Games detected from SLSsteam's plugin directory. Removal archives Lua files and only unshared depot manifests.", Foreground = Brush.Parse("#B7C1BA"), TextWrapping = TextWrapping.Wrap },
                        games
                    }
                }),
                Card("Game recovery", recovery),
                confirmation,
                actions,
                status
            }
        };

        if (OperatingSystem.IsLinux()) LoadInstallations();
        else
        {
            status.Text = "Windows Game Manager remains available in the WinForms frontend during migration.";
            installation.IsEnabled = refresh.IsEnabled = confirmation.IsEnabled = false;
        }
    }

    private SteamInstallation? SelectedInstallation => installation.SelectedItem as SteamInstallation;
    private static string RecoveryRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TOST", "backups", "removed-games");

    private void LoadInstallations()
    {
        var found = LinuxSteamDiscovery.FindInstallations();
        installation.ItemsSource = found;
        installation.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<SteamInstallation>((item, _) =>
            new TextBlock { Text = $"{item.Kind} — {item.RootPath}" });
        var preferred = DesktopPaths.PreferencesStore.Load().PreferredSteamInstallation;
        installation.SelectedIndex = found.Count == 0 ? -1 : Math.Max(0, found.ToList().FindIndex(item => item.Kind == preferred));
        if (found.Count == 0) status.Text = "No native or Flatpak Steam installation was detected.";
    }

    private void Refresh()
    {
        var steam = SelectedInstallation;
        if (steam is null) return;
        try
        {
            managedGames = service.FindManagedGames(steam);
            games.ItemsSource = managedGames.Select(game => new GameItem(game)).ToArray();
            recovery.ItemsSource = service.FindRemovedGames(RecoveryRoot).Select(item => new ArchiveItem(item)).ToArray();
            confirmation.IsChecked = false;
            status.Text = $"Found {managedGames.Count} managed game{(managedGames.Count == 1 ? "" : "s")}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            status.Text = $"Could not scan the Steam installation: {ex.Message}";
        }
        UpdateActions();
    }

    private void UpdateActions()
    {
        var confirmed = confirmation.IsChecked == true;
        remove.IsEnabled = confirmed && games.SelectedItems is { Count: > 0 } && SelectedInstallation is not null;
        restore.IsEnabled = confirmed && recovery.SelectedItem is ArchiveItem && SelectedInstallation is not null;
    }

    private void RemoveSelected()
    {
        if (SelectedInstallation is not { } steam || confirmation.IsChecked != true) return;
        var selected = (games.SelectedItems?.OfType<GameItem>() ?? []).Select(item => item.Game).ToArray();
        var result = service.RemoveGames(selected, managedGames, steam, RecoveryRoot);
        status.Text = result.Message;
        if (result.Success) Refresh();
    }

    private void RestoreSelected()
    {
        if (SelectedInstallation is not { } steam || recovery.SelectedItem is not ArchiveItem item || confirmation.IsChecked != true) return;
        var result = service.RestoreArchive(item.Archive, steam, RecoveryRoot);
        status.Text = result.Message;
        if (result.Success) Refresh();
    }

    private static Border Card(string title, Control content) => new()
    {
        Classes = { "card" },
        Child = new StackPanel { Spacing = 10, Children = { new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeight.SemiBold }, content } }
    };

    private sealed record GameItem(ManagedGame Game)
    {
        public override string ToString() => $"{Game.DisplayName}  •  App {Game.AppId}  •  {Game.ManifestPaths.Count} manifests";
    }

    private sealed record ArchiveItem(RemovedGameArchive Archive)
    {
        public override string ToString() => $"{Archive.RemovedUtc:u}  •  {string.Join(", ", Archive.Games.Select(game => game.DisplayName))}  •  {Archive.Files.Count} files";
    }
}

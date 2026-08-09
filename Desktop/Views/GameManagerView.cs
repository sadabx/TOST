using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Trionine.TOST.Core.GameManagement;
using Trionine.TOST.Core.Steam;
using Trionine.TOST.Desktop.Services;

namespace Trionine.TOST.Desktop.Views;

internal sealed class GameManagerView : UserControl
{
    private readonly ManagedGameService service = new();
    private readonly ComboBox installation = new() { Width = 245 };
    private readonly Grid targetBar;
    private readonly ListBox games = new();
    private readonly ListBox recovery = new();
    private readonly TextBlock status = new()
    {
        Foreground = Brush.Parse("#AFC0B4"),
        TextWrapping = TextWrapping.Wrap
    };
    private readonly Button remove = PrimaryButton("Remove Selected");
    private readonly Button restore = PrimaryButton("Restore Selected");
    private IReadOnlyList<ManagedGame> managedGames = [];

    public GameManagerView()
    {
        remove.IsEnabled = false;
        restore.IsEnabled = false;

        var refresh = SecondaryButton("Refresh");
        refresh.Click += async (_, _) => await RefreshAsync();
        installation.SelectionChanged += async (_, _) => await RefreshAsync();
        recovery.SelectionChanged += (_, _) => UpdateActions();
        remove.Click += async (_, _) => await RemoveSelectedAsync();
        restore.Click += async (_, _) => await RestoreSelectedAsync();

        games.ItemTemplate = new FuncDataTemplate<GameItem>((item, _) => CreateGameRow(item));

        targetBar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
            ColumnSpacing = 10
        };
        targetBar.Children.Add(new TextBlock
        {
            Text = "Steam installation",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush.Parse("#B7C1BA")
        });
        Grid.SetColumn(installation, 1);
        targetBar.Children.Add(installation);

        var tabs = new TabControl
        {
            ItemsSource = new TabItem[]
            {
                new TabItem { Header = "Managed Games", Content = CreateManagedPage(refresh) },
                new TabItem { Header = "Recovery", Content = CreateRecoveryPage() }
            }
        };

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 10,
            Margin = new Thickness(4)
        };
        root.Children.Add(targetBar);
        Grid.SetRow(tabs, 1);
        root.Children.Add(tabs);
        Grid.SetRow(status, 2);
        root.Children.Add(status);
        Content = root;

        LoadInstallations();
    }

    private SteamInstallation? SelectedInstallation => installation.SelectedItem as SteamInstallation;

    private static string RecoveryRoot => DesktopPaths.RecoveryRoot;

    private Control CreateManagedPage(Button refresh)
    {
        var description = new TextBlock
        {
            Text = DesktopPlatform.UsesOpenSteamTool
                ? "Games detected from Steam's config\\lua folder. Removal moves only the Lua file and its unshared depot manifests into TOST's recovery folder."
                : "Games detected from SLSsteam's plugin folder. Removal moves the Lua file and its unshared depot manifests into TOST's recovery folder.",
            Foreground = Brush.Parse("#B7C1BA"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 10, 8, 8)
        };
        var header = CreateGameHeader();
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(8, 8, 8, 6),
            Children = { remove, refresh }
        };
        var page = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto")
        };
        page.Children.Add(description);
        Grid.SetRow(header, 1);
        page.Children.Add(header);
        Grid.SetRow(games, 2);
        page.Children.Add(games);
        Grid.SetRow(actions, 3);
        page.Children.Add(actions);
        return page;
    }

    private Control CreateRecoveryPage()
    {
        var description = new TextBlock
        {
            Text = "Files removed through TOST remain recoverable here.",
            Foreground = Brush.Parse("#B7C1BA"),
            Margin = new Thickness(8, 10, 8, 8)
        };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 8, 8, 6),
            Children = { restore }
        };
        var page = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto")
        };
        page.Children.Add(description);
        Grid.SetRow(recovery, 1);
        page.Children.Add(recovery);
        Grid.SetRow(actions, 2);
        page.Children.Add(actions);
        return page;
    }

    private static Grid CreateGameHeader()
    {
        var header = CreateGameGrid();
        header.Background = Brush.Parse("#F1F1F1");
        header.Children.Add(HeaderText("Game", 0));
        header.Children.Add(HeaderText("App ID", 1));
        header.Children.Add(HeaderText("Lua file", 2));
        header.Children.Add(HeaderText("Manifests", 3));
        return header;
    }

    private Control CreateGameRow(GameItem item)
    {
        var row = CreateGameGrid();
        var selected = new CheckBox
        {
            Content = item.Game.DisplayName,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 2)
        };
        selected.IsCheckedChanged += (_, _) =>
        {
            item.Selected = selected.IsChecked == true;
            UpdateActions();
        };
        row.Children.Add(selected);
        row.Children.Add(CellText(item.Game.AppId, 1));
        row.Children.Add(CellText(Path.GetFileName(item.Game.LuaPath), 2));
        row.Children.Add(CellText(item.Game.ManifestPaths.Count.ToString(), 3));
        return row;
    }

    private static Grid CreateGameGrid() => new()
    {
        ColumnDefinitions = new ColumnDefinitions("3*,1.15*,2*,0.9*"),
        MinHeight = 28
    };

    private static TextBlock HeaderText(string text, int column)
    {
        var label = new TextBlock
        {
            Text = text,
            Foreground = Brushes.Black,
            Margin = new Thickness(8, 5),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, column);
        return label;
    }

    private static TextBlock CellText(string text, int column)
    {
        var label = new TextBlock
        {
            Text = text,
            Margin = new Thickness(8, 4),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(label, column);
        return label;
    }

    private void LoadInstallations()
    {
        var found = DesktopPlatform.FindInstallations();
        targetBar.IsVisible = found.Count > 1;
        installation.ItemsSource = found;
        installation.ItemTemplate = new FuncDataTemplate<SteamInstallation>((item, _) =>
            new TextBlock { Text = $"{item.Kind} - {item.RootPath}", TextTrimming = TextTrimming.CharacterEllipsis });
        var preferred = DesktopPaths.PreferencesStore.Load().PreferredSteamInstallation;
        installation.SelectedIndex = found.Count == 0
            ? -1
            : Math.Max(0, found.ToList().FindIndex(item => item.Kind == preferred));
        if (found.Count == 0)
        {
            status.Text = "No Steam installation was detected. Check TOST Settings.";
        }
    }

    private async Task RefreshAsync()
    {
        var steam = SelectedInstallation;
        if (steam is null)
        {
            managedGames = [];
            games.ItemsSource = Array.Empty<GameItem>();
            recovery.ItemsSource = Array.Empty<ArchiveItem>();
            UpdateActions();
            return;
        }

        try
        {
            managedGames = service.FindManagedGames(steam);
            games.ItemsSource = managedGames.Select(game => new GameItem(game)).ToArray();
            recovery.ItemsSource = service.FindRemovedGames(RecoveryRoot)
                .Select(item => new ArchiveItem(item))
                .ToArray();
            status.Text = $"Found {managedGames.Count} managed game{(managedGames.Count == 1 ? "" : "s")}.";

            var missingNames = managedGames.Where(game => string.IsNullOrWhiteSpace(game.Name)).Select(game => game.AppId).ToArray();
            if (missingNames.Length > 0)
            {
                var resolved = await SteamGameNameResolver.ResolveAsync(missingNames);
                managedGames = managedGames
                    .Select(game => resolved.TryGetValue(game.AppId, out var name) ? game with { Name = name } : game)
                    .ToArray();
                games.ItemsSource = managedGames.Select(game => new GameItem(game)).ToArray();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            status.Text = $"Could not scan the Steam installation: {ex.Message}";
        }

        UpdateActions();
    }

    private void UpdateActions()
    {
        remove.IsEnabled = games.ItemsSource?.OfType<GameItem>().Any(item => item.Selected) == true &&
                           SelectedInstallation is not null;
        restore.IsEnabled = recovery.SelectedItem is ArchiveItem && SelectedInstallation is not null;
    }

    private async Task RemoveSelectedAsync()
    {
        if (SelectedInstallation is not { } steam)
        {
            return;
        }

        var selected = games.ItemsSource?.OfType<GameItem>()
            .Where(item => item.Selected)
            .Select(item => item.Game)
            .ToArray() ?? [];
        if (selected.Length == 0)
        {
            return;
        }

        var names = string.Join(Environment.NewLine, selected.Select(game => $"- {game.DisplayName} ({game.AppId})"));
        if (!await TostDialog.ConfirmAsync(
                this,
                "Remove Managed Games",
                $"Archive the following games?{Environment.NewLine}{Environment.NewLine}{names}{Environment.NewLine}{Environment.NewLine}They can be restored from Recovery.",
                "Remove"))
        {
            return;
        }

        var result = service.RemoveGames(selected, managedGames, steam, RecoveryRoot);
        status.Text = result.Message;
        if (result.Success)
        {
            await RefreshAsync();
        }
    }

    private async Task RestoreSelectedAsync()
    {
        if (SelectedInstallation is not { } steam || recovery.SelectedItem is not ArchiveItem item)
        {
            return;
        }

        var names = string.Join(", ", item.Archive.Games.Select(game => game.DisplayName));
        if (!await TostDialog.ConfirmAsync(
                this,
                "Restore Managed Games",
                $"Restore {names}? Existing files will not be overwritten.",
                "Restore"))
        {
            return;
        }

        var result = service.RestoreArchive(item.Archive, steam, RecoveryRoot);
        status.Text = result.Message;
        if (result.Success)
        {
            await RefreshAsync();
        }
    }

    private static Button PrimaryButton(string text) => new()
    {
        Content = text,
        MinWidth = 132,
        Height = 34,
        Background = Brush.Parse("#219638")
    };

    private static Button SecondaryButton(string text) => new()
    {
        Content = text,
        MinWidth = 132,
        Height = 34
    };

    private sealed class GameItem(ManagedGame game)
    {
        public ManagedGame Game { get; } = game;
        public bool Selected { get; set; }
    }

    private sealed record ArchiveItem(RemovedGameArchive Archive)
    {
        public override string ToString() =>
            $"{Archive.RemovedUtc.ToLocalTime():g}  -  {string.Join(", ", Archive.Games.Select(game => game.DisplayName))}  -  {Archive.Files.Count} files";
    }
}

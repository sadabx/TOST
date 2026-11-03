using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Trionine.TOST.Core.Downloads;
using Trionine.TOST.Core.GameManagement;
using Trionine.TOST.Core.Steam;
using Trionine.TOST.Desktop.Services;

namespace Trionine.TOST.Desktop.Views;

internal sealed class GameManagerView : UserControl
{
    private readonly ManagedGameService service = new();
    private readonly ComboBox installation = new() { Width = 230 };
    private readonly ItemsControl games = new()
    {
        Background = Brushes.Transparent
    };
    private readonly TextBlock summaryText = new()
    {
        Foreground = Brush.Parse("#8F98A0"),
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock status = new()
    {
        Foreground = Brush.Parse("#AFC0B4"),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(6, 4, 6, 4)
    };
    private readonly Button downloadButton = PrimaryButton("Download");
    private readonly Button removeButton = DangerButton("Remove");
    private readonly CheckBox selectAll = new()
    {
        Content = "Select All",
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(4, 0, 12, 0)
    };

    private readonly Button libraryTabButton;
    private readonly Button downloadsTabButton;
    private readonly Border libraryTabIndicator;
    private readonly Border downloadsTabIndicator;
    private readonly Panel pageContainer = new();
    private readonly Control libraryPage;
    private readonly DownloadsView downloadsPage;

    private IReadOnlyList<ManagedGame> managedGames = [];
    private int refreshGeneration;
    private bool isDownloadsTabActive;

    public GameManagerView()
    {
        downloadButton.IsEnabled = false;
        removeButton.IsEnabled = false;

        var refresh = SecondaryButton("Refresh");
        refresh.Click += async (_, _) => await RefreshAsync();
        installation.SelectionChanged += async (_, _) => await RefreshAsync();
        downloadButton.Click += async (_, _) => await DownloadSelectedAsync();
        removeButton.Click += async (_, _) => await RemoveSelectedAsync();

        selectAll.IsCheckedChanged += (_, _) =>
        {
            var isChecked = selectAll.IsChecked == true;
            foreach (var item in games.ItemsSource?.OfType<GameItem>() ?? [])
            {
                item.Selected = isChecked;
                item.UpdateSelectionUi?.Invoke(isChecked);
            }
            UpdateActions();
        };

        games.ItemTemplate = new FuncDataTemplate<GameItem>((item, _) => CreateGameCard(item));

        // Create Navigation Tabs (Steam Style)
        libraryTabIndicator = new Border { Height = 2, Background = Brush.Parse("#1A9FFF"), HorizontalAlignment = HorizontalAlignment.Stretch };
        downloadsTabIndicator = new Border { Height = 2, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Stretch };

        libraryTabButton = CreateTabButton("LIBRARY", true);
        downloadsTabButton = CreateTabButton("DOWNLOADS", false);

        libraryTabButton.Click += (_, _) => SwitchToTab(false);
        downloadsTabButton.Click += (_, _) => SwitchToTab(true);

        var tabStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        tabStack.Children.Add(new StackPanel { Children = { libraryTabButton, libraryTabIndicator } });
        tabStack.Children.Add(new StackPanel { Children = { downloadsTabButton, downloadsTabIndicator } });

        var headerBar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(12, 10, 12, 6)
        };
        headerBar.Children.Add(tabStack);

        var installStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        installStack.Children.Add(new TextBlock
        {
            Text = "Steam:",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush.Parse("#8F98A0"),
            FontSize = 11
        });
        installStack.Children.Add(installation);
        Grid.SetColumn(installStack, 2);
        headerBar.Children.Add(installStack);

        // Build Pages
        libraryPage = CreateLibraryPage(refresh);
        downloadsPage = new DownloadsView(() => SwitchToTab(false));

        pageContainer.Children.Add(libraryPage);
        pageContainer.Children.Add(downloadsPage);
        downloadsPage.IsVisible = false;

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Background = Brush.Parse("#101418")
        };
        root.Children.Add(headerBar);
        Grid.SetRow(pageContainer, 1);
        root.Children.Add(pageContainer);
        Grid.SetRow(status, 2);
        root.Children.Add(status);

        Content = root;

        DownloadManager.Instance.Changed += OnDownloadManagerChanged;
        DownloadManager.Instance.DownloadFinished += OnDownloadFinished;

        LoadInstallations();
    }

    private SteamInstallation? SelectedInstallation => installation.SelectedItem as SteamInstallation;

    private void SwitchToTab(bool downloadsTab)
    {
        isDownloadsTabActive = downloadsTab;
        libraryPage.IsVisible = !downloadsTab;
        downloadsPage.IsVisible = downloadsTab;

        libraryTabIndicator.Background = downloadsTab ? Brushes.Transparent : Brush.Parse("#1A9FFF");
        downloadsTabIndicator.Background = downloadsTab ? Brush.Parse("#1A9FFF") : Brushes.Transparent;

        libraryTabButton.Foreground = downloadsTab ? Brush.Parse("#8F98A0") : Brushes.White;
        downloadsTabButton.Foreground = downloadsTab ? Brushes.White : Brush.Parse("#8F98A0");
    }

    private void OnDownloadManagerChanged()
    {
        var active = DownloadManager.Instance.ActiveItem;
        var count = DownloadManager.Instance.Queue.Count + (active != null ? 1 : 0);
        downloadsTabButton.Content = count > 0 ? $"DOWNLOADS ({count})" : "DOWNLOADS";

        // Update card action buttons if download status changed
        foreach (var item in games.ItemsSource?.OfType<GameItem>() ?? [])
        {
            item.UpdateUi?.Invoke();
        }
    }

    private void OnDownloadFinished(DownloadItem finished)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var item in games.ItemsSource?.OfType<GameItem>() ?? [])
            {
                if (item.Game.AppId == finished.AppId)
                {
                    item.IsInstalled = finished.IsCompleted;
                    item.UpdateUi?.Invoke();
                }
            }
            UpdateSummary();
            UpdateActions();
            if (finished.IsCompleted)
            {
                status.Text = $"Downloaded {finished.DisplayName} successfully! Restart Steam to play.";
            }
        });
    }

    private Control CreateLibraryPage(Button refresh)
    {
        var toolbar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(12, 4, 12, 8)
        };
        toolbar.Children.Add(selectAll);
        Grid.SetColumn(summaryText, 1);
        toolbar.Children.Add(summaryText);
        Grid.SetColumn(refresh, 2);
        toolbar.Children.Add(refresh);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(12, 10, 12, 10),
            Children = { downloadButton, removeButton }
        };

        var gamesScroll = new ScrollViewer
        {
            Content = games,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };

        var page = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto")
        };
        page.Children.Add(toolbar);
        Grid.SetRow(gamesScroll, 1);
        page.Children.Add(gamesScroll);
        Grid.SetRow(actions, 2);
        page.Children.Add(actions);
        return page;
    }

    private Control CreateGameCard(GameItem? item)
    {
        if (item?.Game is not { } game)
        {
            return new Border { MinHeight = 40 };
        }

        var card = new Border
        {
            Background = Brush.Parse("#1A2026"),
            BorderBrush = Brush.Parse("#2B333D"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(10, 3),
            Padding = new Thickness(12, 10)
        };

        card.PointerEntered += (_, _) =>
        {
            card.Background = Brush.Parse("#222931");
            card.BorderBrush = Brush.Parse("#3D4855");
        };
        card.PointerExited += (_, _) =>
        {
            card.Background = Brush.Parse("#1A2026");
            card.BorderBrush = Brush.Parse("#2B333D");
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
            VerticalAlignment = VerticalAlignment.Center
        };

        // 1. CheckBox
        var checkbox = new CheckBox
        {
            IsChecked = item.Selected,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        checkbox.IsCheckedChanged += (_, _) =>
        {
            item.Selected = checkbox.IsChecked == true;
            UpdateActions();
        };
        item.UpdateSelectionUi = val => checkbox.IsChecked = val;
        grid.Children.Add(checkbox);

        // 2. Artwork Thumbnail
        var thumbnailImage = new Image
        {
            Stretch = Stretch.UniformToFill
        };
        var thumbnailBorder = new Border
        {
            Width = 110,
            Height = 52,
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Background = Brush.Parse("#111518"),
            BorderBrush = Brush.Parse("#232930"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 14, 0),
            Child = thumbnailImage
        };
        Grid.SetColumn(thumbnailBorder, 1);
        grid.Children.Add(thumbnailBorder);

        if (item.Thumbnail != null)
        {
            thumbnailImage.Source = item.Thumbnail;
        }
        else
        {
            _ = Task.Run(async () =>
            {
                var thumb = await SteamThumbnailService.GetThumbnailAsync(game.AppId);
                if (thumb != null)
                {
                    item.Thumbnail = thumb;
                    Dispatcher.UIThread.Post(() => thumbnailImage.Source = thumb);
                }
            });
        }

        // 3. Center Info
        var titleBlock = new TextBlock
        {
            Text = game.DisplayName,
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var subtitleBlock = new TextBlock
        {
            Text = $"App ID: {game.AppId}  •  {game.ManifestPaths.Count} Manifest{(game.ManifestPaths.Count == 1 ? "" : "s")}",
            FontSize = 11,
            Foreground = Brush.Parse("#8F98A0"),
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var folderInfo = new TextBlock
        {
            Text = $"Folder: {item.InstallDirName}",
            FontSize = 10,
            Foreground = Brush.Parse("#68727D"),
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var infoPanel = new StackPanel
        {
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { titleBlock, subtitleBlock, folderInfo }
        };
        Grid.SetColumn(infoPanel, 2);
        grid.Children.Add(infoPanel);

        // 4. Right Controls
        var actionPanel = new Panel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        item.UpdateUi = () => UpdateActionControl(actionPanel, item);
        item.UpdateUi();

        Grid.SetColumn(actionPanel, 3);
        grid.Children.Add(actionPanel);

        card.Child = grid;
        return card;
    }

    private void UpdateActionControl(Panel container, GameItem item)
    {
        container.Children.Clear();

        var isQueuedOrDownloading = DownloadManager.Instance.IsActiveOrQueued(item.Game.AppId);

        if (isQueuedOrDownloading)
        {
            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center
            };

            var pill = new Border
            {
                Background = Brush.Parse("#152331"),
                BorderBrush = Brush.Parse("#1D4566"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(9, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "In Progress",
                    Foreground = Brush.Parse("#3EA6FF"),
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold
                }
            };

            var viewBtn = new Button
            {
                Content = "View Download",
                Height = 28,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Background = Brush.Parse("#2080DF"),
                Foreground = Brushes.White,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 2)
            };
            viewBtn.Click += (_, _) => SwitchToTab(true);

            stack.Children.Add(pill);
            stack.Children.Add(viewBtn);
            container.Children.Add(stack);
        }
        else if (item.IsInstalled)
        {
            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Status Pill
            var pill = new Border
            {
                Background = Brush.Parse("#10281C"),
                BorderBrush = Brush.Parse("#1D5235"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(9, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "✔ Installed",
                    Foreground = Brush.Parse("#4EC9B0"),
                    FontSize = 11,
                    FontWeight = FontWeight.SemiBold
                }
            };

            // Play button
            var playBtn = new Button
            {
                Content = "▶ Play",
                Height = 28,
                MinWidth = 68,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Background = Brush.Parse("#1E9E40"),
                Foreground = Brushes.White,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 2)
            };
            playBtn.Click += (_, _) => LaunchGame(item.Game.AppId);

            // Vector Folder button (never renders as box glyph)
            var folderBtn = new Button
            {
                Content = new PathIcon
                {
                    Data = StreamGeometry.Parse("M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z"),
                    Width = 14,
                    Height = 14,
                    Foreground = Brush.Parse("#BAC5D1")
                },
                Height = 28,
                Width = 32,
                Background = Brush.Parse("#272E36"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(folderBtn, "Open game installation folder");
            folderBtn.Click += (_, _) => OpenGameFolder(item);

            // Re-download button
            var redownloadBtn = new Button
            {
                Content = "Re-download",
                Height = 28,
                FontSize = 10,
                Background = Brush.Parse("#242B32"),
                Foreground = Brush.Parse("#8F98A0"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2)
            };
            redownloadBtn.Click += (_, _) =>
            {
                if (SelectedInstallation is { } steam)
                {
                    DownloadManager.Instance.Enqueue(steam, item.Game, item.Thumbnail);
                    SwitchToTab(true);
                }
            };

            stack.Children.Add(pill);
            stack.Children.Add(playBtn);
            stack.Children.Add(folderBtn);
            stack.Children.Add(redownloadBtn);
            container.Children.Add(stack);
        }
        else
        {
            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Not Installed Pill
            var pill = new Border
            {
                Background = Brush.Parse("#1D2228"),
                BorderBrush = Brush.Parse("#353C46"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(9, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "Ready to Download",
                    Foreground = Brush.Parse("#9AA4AF"),
                    FontSize = 11
                }
            };

            // Download button
            var downloadSingleBtn = new Button
            {
                Content = "Download",
                Height = 30,
                MinWidth = 105,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Background = Brush.Parse("#159B35"),
                Foreground = Brushes.White,
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(12, 2)
            };
            downloadSingleBtn.Click += (_, _) =>
            {
                if (SelectedInstallation is { } steam)
                {
                    DownloadManager.Instance.Enqueue(steam, item.Game, item.Thumbnail);
                    item.UpdateUi?.Invoke();
                    SwitchToTab(true);
                }
            };

            stack.Children.Add(pill);
            stack.Children.Add(downloadSingleBtn);
            container.Children.Add(stack);
        }
    }

    private void LaunchGame(string appId)
    {
        try
        {
            Process.Start(new ProcessStartInfo("xdg-open")
            {
                ArgumentList = { $"steam://rungameid/{appId}" },
                UseShellExecute = false
            });
            status.Text = $"Launched game {appId} via Steam.";
        }
        catch (Exception ex)
        {
            status.Text = $"Could not launch game: {ex.Message}";
        }
    }

    private void OpenGameFolder(GameItem item)
    {
        var steam = SelectedInstallation;
        if (steam is null) return;
        var path = Path.Combine(steam.CommonAppsPath, item.InstallDirName);
        if (Directory.Exists(path))
        {
            try { FolderLauncher.Open(path); }
            catch (Exception ex) { status.Text = $"Could not open folder: {ex.Message}"; }
        }
        else
        {
            status.Text = $"Install folder not found: {path}";
        }
    }

    private Task DownloadSelectedAsync()
    {
        var steam = SelectedInstallation;
        if (steam is null)
        {
            status.Text = "No Steam installation selected.";
            return Task.CompletedTask;
        }

        var toDownload = games.ItemsSource?.OfType<GameItem>()
            .Where(item => item.Selected && !item.IsInstalled && !DownloadManager.Instance.IsActiveOrQueued(item.Game.AppId))
            .ToList() ?? [];

        if (toDownload.Count == 0) return Task.CompletedTask;

        foreach (var item in toDownload)
        {
            item.Selected = false;
            item.UpdateSelectionUi?.Invoke(false);
            DownloadManager.Instance.Enqueue(steam, item.Game, item.Thumbnail);
        }

        UpdateActions();
        SwitchToTab(true);
        return Task.CompletedTask;
    }

    private void LoadInstallations()
    {
        var found = DesktopPlatform.FindInstallations();
        installation.ItemsSource = found;
        installation.ItemTemplate = new FuncDataTemplate<SteamInstallation>((item, _) =>
            new TextBlock
            {
                Text = item is null ? string.Empty : $"{item.Kind} - {item.RootPath}",
                TextTrimming = TextTrimming.CharacterEllipsis
            });
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
        var generation = ++refreshGeneration;
        var steam = SelectedInstallation;
        if (steam is null)
        {
            managedGames = [];
            games.ItemsSource = Array.Empty<GameItem>();
            UpdateActions();
            UpdateSummary();
            return;
        }

        try
        {
            var refreshedGames = service.FindManagedGames(steam);
            managedGames = refreshedGames;

            var items = refreshedGames.Select(game => new GameItem(game)
            {
                IsInstalled = GameInstallStatus.IsGameInstalled(steam, game),
                InstallDirName = GameDownloadPlanner.ResolveInstallDirName(steam, game)
            }).ToArray();

            games.ItemsSource = items;
            UpdateSummary();
            UpdateActions();

            var missingNames = refreshedGames.Where(game => string.IsNullOrWhiteSpace(game.Name)).Select(game => game.AppId).ToArray();
            if (missingNames.Length > 0)
            {
                var resolved = await SteamGameNameResolver.ResolveAsync(missingNames);
                if (generation != refreshGeneration)
                {
                    return;
                }

                managedGames = refreshedGames
                    .Select(game => resolved.TryGetValue(game.AppId, out var name) ? game with { Name = name } : game)
                    .ToArray();

                var updatedItems = managedGames.Select(game => new GameItem(game)
                {
                    IsInstalled = GameInstallStatus.IsGameInstalled(steam, game),
                    InstallDirName = GameDownloadPlanner.ResolveInstallDirName(steam, game)
                }).ToArray();

                games.ItemsSource = updatedItems;
                UpdateSummary();
            }
        }
        catch (Exception ex)
        {
            DesktopLog.Error($"Game Manager refresh failed: {ex}");
            status.Text = "Game Manager could not read the Steam files. " +
                          $"Details: {ex.Message}";
        }

        UpdateActions();
    }

    private void UpdateSummary()
    {
        var items = games.ItemsSource?.OfType<GameItem>().ToList() ?? [];
        var installedCount = items.Count(i => i.IsInstalled);
        var readyCount = items.Count - installedCount;
        summaryText.Text = $"{items.Count} Managed Game{(items.Count == 1 ? "" : "s")}  •  {installedCount} Installed  •  {readyCount} Ready to Download";
    }

    private void UpdateActions()
    {
        var items = games.ItemsSource?.OfType<GameItem>().ToList() ?? [];
        var selectedCount = items.Count(i => i.Selected);
        selectAll.IsChecked = items.Count > 0 && items.All(i => i.Selected);

        var downloadCount = items.Count(item => item.Selected && !item.IsInstalled && !DownloadManager.Instance.IsActiveOrQueued(item.Game.AppId));
        downloadButton.IsEnabled = downloadCount > 0 && SelectedInstallation is not null;
        removeButton.IsEnabled = selectedCount > 0 && SelectedInstallation is not null;

        removeButton.Content = selectedCount > 0 ? $"Remove ({selectedCount})" : "Remove";
        downloadButton.Content = downloadCount > 0 ? $"Download ({downloadCount})" : "Download";
    }

    private async Task RemoveSelectedAsync()
    {
        if (SelectedInstallation is not { } steam)
        {
            return;
        }

        var selectedItems = games.ItemsSource?.OfType<GameItem>()
            .Where(item => item.Selected)
            .ToList() ?? [];
        if (selectedItems.Count == 0)
        {
            return;
        }

        var selectedGames = selectedItems.Select(item => item.Game).ToArray();
        var names = string.Join(Environment.NewLine, selectedGames.Select(game => $"- {game.DisplayName} ({game.AppId})"));
        if (!await TostDialog.ConfirmAsync(
                this,
                "Remove Managed Games",
                $"Permanently delete the following games, manifests, and downloaded game files?{Environment.NewLine}{Environment.NewLine}{names}{Environment.NewLine}{Environment.NewLine}This action cannot be undone.",
                "Remove"))
        {
            return;
        }

        var result = service.RemoveGames(selectedGames, managedGames, steam);

        // Also clean up common directory if present
        foreach (var item in selectedItems)
        {
            var commonDir = Path.Combine(steam.CommonAppsPath, item.InstallDirName);
            if (Directory.Exists(commonDir))
            {
                try { Directory.Delete(commonDir, recursive: true); } catch { }
            }
        }

        status.Text = result.Message;
        if (result.Success)
        {
            await RefreshAsync();
        }
    }

    private static Button CreateTabButton(string header, bool isActive) => new()
    {
        Content = header,
        Height = 32,
        Padding = new Thickness(14, 4),
        FontSize = 13,
        FontWeight = FontWeight.Bold,
        Background = Brushes.Transparent,
        Foreground = isActive ? Brushes.White : Brush.Parse("#8F98A0"),
        CornerRadius = new CornerRadius(0)
    };

    private static Button PrimaryButton(string text) => new()
    {
        Content = text,
        MinWidth = 120,
        Height = 34,
        Background = Brush.Parse("#219638"),
        Foreground = Brushes.White,
        FontWeight = FontWeight.SemiBold,
        CornerRadius = new CornerRadius(5)
    };

    private static Button DangerButton(string text) => new()
    {
        Content = text,
        MinWidth = 110,
        Height = 34,
        Background = Brush.Parse("#382326"),
        Foreground = Brush.Parse("#E0808A"),
        CornerRadius = new CornerRadius(5)
    };

    private static Button SecondaryButton(string text) => new()
    {
        Content = text,
        MinWidth = 85,
        Height = 32,
        Background = Brush.Parse("#252C34"),
        Foreground = Brush.Parse("#D0D7DE"),
        CornerRadius = new CornerRadius(5)
    };

    private sealed class GameItem(ManagedGame game)
    {
        public ManagedGame Game { get; } = game;
        public bool Selected { get; set; }
        public bool IsInstalled { get; set; }
        public string InstallDirName { get; set; } = string.Empty;
        public Bitmap? Thumbnail { get; set; }
        public Action? UpdateUi { get; set; }
        public Action<bool>? UpdateSelectionUi { get; set; }
    }
}

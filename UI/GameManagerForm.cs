namespace Trionine.TOST;

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

        var names = string.Join("\n", selectedGames.Select(game => $"â€¢ {game.DisplayName} ({game.AppId})"));
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



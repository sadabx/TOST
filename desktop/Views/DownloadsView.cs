using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Trionine.TOST.Desktop.Services;

namespace Trionine.TOST.Desktop.Views;

internal sealed class DownloadsView : UserControl
{
    private readonly Action onNavigateToLibrary;
    private readonly StackPanel contentPanel = new() { Spacing = 16, Margin = new Thickness(16) };
    private readonly ScrollViewer scrollViewer;

    public DownloadsView(Action onNavigateToLibrary)
    {
        this.onNavigateToLibrary = onNavigateToLibrary;
        Background = Brush.Parse("#101418");

        scrollViewer = new ScrollViewer
        {
            Content = contentPanel,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };

        Content = scrollViewer;

        DownloadManager.Instance.Changed += OnManagerChanged;
        RebuildUi();
    }

    private void OnManagerChanged()
    {
        Dispatcher.UIThread.Post(RebuildUi);
    }

    private void RebuildUi()
    {
        contentPanel.Children.Clear();

        var manager = DownloadManager.Instance;
        var active = manager.ActiveItem;
        var queue = manager.Queue;
        var completed = manager.Completed;

        if (active == null && queue.Count == 0 && completed.Count == 0)
        {
            contentPanel.Children.Add(CreateEmptyState());
            return;
        }

        // 1. Active Download Hero Section
        if (active != null)
        {
            contentPanel.Children.Add(CreateActiveHeroCard(active));
        }

        // 2. Up Next Section
        contentPanel.Children.Add(CreateQueueSection(queue));

        // 3. Completed Section
        if (completed.Count > 0)
        {
            contentPanel.Children.Add(CreateCompletedSection(completed));
        }
    }

    private Control CreateEmptyState()
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 12,
            Margin = new Thickness(0, 60, 0, 0)
        };

        panel.Children.Add(new TextBlock
        {
            Text = "No Active Downloads",
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = Brush.Parse("#C6D4DF"),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        panel.Children.Add(new TextBlock
        {
            Text = "Select games from your Library to download files directly from Steam CDN.",
            FontSize = 12,
            Foreground = Brush.Parse("#8F98A0"),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        var browseBtn = new Button
        {
            Content = "Browse Library",
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0),
            Height = 32,
            Padding = new Thickness(16, 4),
            Background = Brush.Parse("#1A9FFF"),
            Foreground = Brushes.White,
            FontWeight = FontWeight.SemiBold,
            CornerRadius = new CornerRadius(5)
        };
        browseBtn.Click += (_, _) => onNavigateToLibrary();
        panel.Children.Add(browseBtn);

        return panel;
    }

    private Control CreateActiveHeroCard(DownloadItem item)
    {
        var container = new Border
        {
            Background = Brush.Parse("#161E27"),
            BorderBrush = Brush.Parse("#233140"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16)
        };

        var rootGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
            RowSpacing = 10
        };

        // Top Row: Banner/Title on left, Network/Disk Stats on right
        var topRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 16
        };

        // Left: Thumbnail
        var img = new Image
        {
            Source = item.Thumbnail,
            Stretch = Stretch.UniformToFill
        };
        var imgBorder = new Border
        {
            Width = 160,
            Height = 75,
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Background = Brush.Parse("#0F141A"),
            Child = img
        };
        topRow.Children.Add(imgBorder);

        // Center: Title + AppId
        var titleStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 4
        };
        titleStack.Children.Add(new TextBlock
        {
            Text = item.DisplayName,
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = $"App ID: {item.AppId}  •  Folder: {item.InstallDirName}",
            FontSize = 11,
            Foreground = Brush.Parse("#8F98A0"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(titleStack, 1);
        topRow.Children.Add(titleStack);

        // Right: Stats (Network, Peak, Disk Usage)
        var statsStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
            VerticalAlignment = VerticalAlignment.Top
        };

        statsStack.Children.Add(CreateStatBadge("NETWORK", FormatSpeed(item.CurrentSpeedBytesPerSec), "#1A9FFF"));
        statsStack.Children.Add(CreateStatBadge("PEAK", FormatSpeed(item.PeakSpeedBytesPerSec), "#8F98A0"));
        statsStack.Children.Add(CreateStatBadge("DISK USAGE", FormatSpeed(item.CurrentSpeedBytesPerSec), "#5CBE32"));

        Grid.SetColumn(statsStack, 2);
        topRow.Children.Add(statsStack);

        rootGrid.Children.Add(topRow);

        // Middle 1: Network Progress Bar
        var dlLabelGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 4, 0, 2)
        };
        dlLabelGrid.Children.Add(new TextBlock
        {
            Text = "Downloading data",
            FontSize = 11,
            Foreground = Brush.Parse("#8F98A0")
        });
        var dlBytes = item.TotalBytes > 0
            ? $"{FormatBytes(item.DownloadedBytes)} / {FormatBytes(item.TotalBytes)}"
            : $"{FormatBytes(item.DownloadedBytes)} downloaded";
        var dlBytesBlock = new TextBlock
        {
            Text = dlBytes,
            FontSize = 11,
            Foreground = Brush.Parse("#C6D4DF")
        };
        Grid.SetColumn(dlBytesBlock, 1);
        dlLabelGrid.Children.Add(dlBytesBlock);

        var netProgress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = item.DownloadPercent,
            Height = 7,
            CornerRadius = new CornerRadius(3.5),
            Background = Brush.Parse("#0F151B"),
            Foreground = Brush.Parse("#1A9FFF")
        };

        var netStack = new StackPanel { Spacing = 3 };
        netStack.Children.Add(dlLabelGrid);
        netStack.Children.Add(netProgress);
        Grid.SetRow(netStack, 1);
        rootGrid.Children.Add(netStack);

        // Middle 2: Disk / Install Progress Bar
        var diskLabelGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 2, 0, 2)
        };
        diskLabelGrid.Children.Add(new TextBlock
        {
            Text = "Installing files",
            FontSize = 11,
            Foreground = Brush.Parse("#8F98A0")
        });
        var diskPercentBlock = new TextBlock
        {
            Text = $"{item.InstallPercent:F1}%",
            FontSize = 11,
            Foreground = Brush.Parse("#C6D4DF")
        };
        Grid.SetColumn(diskPercentBlock, 1);
        diskLabelGrid.Children.Add(diskPercentBlock);

        var diskProgress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = item.InstallPercent,
            Height = 7,
            CornerRadius = new CornerRadius(3.5),
            Background = Brush.Parse("#0F151B"),
            Foreground = Brush.Parse("#5CBE32")
        };

        var diskStack = new StackPanel { Spacing = 3 };
        diskStack.Children.Add(diskLabelGrid);
        diskStack.Children.Add(diskProgress);
        Grid.SetRow(diskStack, 2);
        rootGrid.Children.Add(diskStack);

        // Bottom Row: ETA + Status on left, Cancel button on right
        var bottomGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        };

        var etaText = item.EstimatedRemaining.HasValue
            ? $"Estimated {FormatTime(item.EstimatedRemaining.Value)} remaining  •  {item.StatusMessage}"
            : item.StatusMessage;

        var statusBlock = new TextBlock
        {
            Text = etaText,
            FontSize = 11,
            Foreground = Brush.Parse("#8F98A0"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        bottomGrid.Children.Add(statusBlock);

        var cancelBtn = new Button
        {
            Content = "✕ Cancel",
            Height = 26,
            Padding = new Thickness(12, 2),
            FontSize = 11,
            Background = Brush.Parse("#382326"),
            Foreground = Brush.Parse("#E0808A"),
            CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Center
        };
        cancelBtn.Click += (_, _) =>
        {
            DownloadManager.Instance.CancelCurrent();
            cancelBtn.IsEnabled = false;
        };
        Grid.SetColumn(cancelBtn, 1);
        bottomGrid.Children.Add(cancelBtn);

        Grid.SetRow(bottomGrid, 3);
        rootGrid.Children.Add(bottomGrid);

        container.Child = rootGrid;
        return container;
    }

    private Control CreateQueueSection(IReadOnlyList<DownloadItem> queue)
    {
        var section = new StackPanel { Spacing = 8 };

        // Header
        var header = new TextBlock
        {
            Text = $"Up Next ({queue.Count})",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#C6D4DF")
        };
        section.Children.Add(header);

        if (queue.Count == 0)
        {
            section.Children.Add(new TextBlock
            {
                Text = "No games in queue.",
                FontSize = 12,
                Foreground = Brush.Parse("#55606B"),
                Margin = new Thickness(4, 2)
            });
            return section;
        }

        foreach (var item in queue)
        {
            var card = new Border
            {
                Background = Brush.Parse("#161B21"),
                BorderBrush = Brush.Parse("#232932"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8)
            };

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
                ColumnSpacing = 12,
                VerticalAlignment = VerticalAlignment.Center
            };

            var thumb = new Border
            {
                Width = 80,
                Height = 38,
                CornerRadius = new CornerRadius(4),
                ClipToBounds = true,
                Background = Brush.Parse("#0F141A"),
                Child = new Image { Source = item.Thumbnail, Stretch = Stretch.UniformToFill }
            };
            grid.Children.Add(thumb);

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
            info.Children.Add(new TextBlock
            {
                Text = item.DisplayName,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            info.Children.Add(new TextBlock
            {
                Text = $"App ID: {item.AppId}",
                FontSize = 11,
                Foreground = Brush.Parse("#8F98A0")
            });
            Grid.SetColumn(info, 1);
            grid.Children.Add(info);

            var nextBadge = new Border
            {
                Background = Brush.Parse("#1A232D"),
                BorderBrush = Brush.Parse("#293848"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "QUEUED",
                    FontSize = 10,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brush.Parse("#1A9FFF")
                }
            };
            Grid.SetColumn(nextBadge, 2);
            grid.Children.Add(nextBadge);

            var removeBtn = new Button
            {
                Content = "✕",
                Width = 26,
                Height = 26,
                FontSize = 11,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                Foreground = Brush.Parse("#8F98A0"),
                CornerRadius = new CornerRadius(4)
            };
            ToolTip.SetTip(removeBtn, "Remove from queue");
            removeBtn.Click += (_, _) => DownloadManager.Instance.CancelQueued(item.AppId);
            Grid.SetColumn(removeBtn, 3);
            grid.Children.Add(removeBtn);

            card.Child = grid;
            section.Children.Add(card);
        }

        return section;
    }

    private Control CreateCompletedSection(IReadOnlyList<DownloadItem> completed)
    {
        var section = new StackPanel { Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };

        var headerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };
        headerGrid.Children.Add(new TextBlock
        {
            Text = $"Completed ({completed.Count})",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#C6D4DF"),
            VerticalAlignment = VerticalAlignment.Center
        });

        var clearBtn = new Button
        {
            Content = "Clear All",
            FontSize = 11,
            Height = 26,
            Padding = new Thickness(10, 2),
            Background = Brush.Parse("#222830"),
            Foreground = Brush.Parse("#8F98A0"),
            CornerRadius = new CornerRadius(4)
        };
        clearBtn.Click += (_, _) => DownloadManager.Instance.ClearCompleted();
        Grid.SetColumn(clearBtn, 1);
        headerGrid.Children.Add(clearBtn);
        section.Children.Add(headerGrid);

        foreach (var item in completed)
        {
            var card = new Border
            {
                Background = Brush.Parse("#14181D"),
                BorderBrush = Brush.Parse("#20252D"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8)
            };

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"),
                ColumnSpacing = 10,
                VerticalAlignment = VerticalAlignment.Center
            };

            var thumb = new Border
            {
                Width = 80,
                Height = 38,
                CornerRadius = new CornerRadius(4),
                ClipToBounds = true,
                Background = Brush.Parse("#0F141A"),
                Child = new Image { Source = item.Thumbnail, Stretch = Stretch.UniformToFill }
            };
            grid.Children.Add(thumb);

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
            info.Children.Add(new TextBlock
            {
                Text = item.DisplayName,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var timeText = item.CompletedTime.HasValue ? item.CompletedTime.Value.ToString("t") : "RECENT";
            info.Children.Add(new TextBlock
            {
                Text = $"{FormatBytes(item.DownloadedBytes)} DOWNLOADED  •  COMPLETED: {timeText}",
                FontSize = 10,
                Foreground = Brush.Parse("#76828E")
            });
            Grid.SetColumn(info, 1);
            grid.Children.Add(info);

            // Play button
            var playBtn = new Button
            {
                Content = "▶ Play",
                Height = 26,
                Padding = new Thickness(10, 2),
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Background = Brush.Parse("#1E9E40"),
                Foreground = Brushes.White,
                CornerRadius = new CornerRadius(4)
            };
            playBtn.Click += (_, _) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo("xdg-open")
                    {
                        ArgumentList = { $"steam://rungameid/{item.AppId}" },
                        UseShellExecute = false
                    });
                }
                catch { }
            };
            Grid.SetColumn(playBtn, 2);
            grid.Children.Add(playBtn);

            // Folder button
            var folderBtn = new Button
            {
                Content = new PathIcon
                {
                    Data = StreamGeometry.Parse("M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z"),
                    Width = 13,
                    Height = 13,
                    Foreground = Brush.Parse("#BAC5D1")
                },
                Height = 26,
                Width = 28,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = Brush.Parse("#222830"),
                CornerRadius = new CornerRadius(4)
            };
            ToolTip.SetTip(folderBtn, "Open game folder");
            folderBtn.Click += (_, _) =>
            {
                var full = Path.Combine(item.Installation.CommonAppsPath, item.InstallDirName);
                if (Directory.Exists(full)) FolderLauncher.Open(full);
            };
            Grid.SetColumn(folderBtn, 3);
            grid.Children.Add(folderBtn);

            // Dismiss button
            var dismissBtn = new Button
            {
                Content = "✕",
                Width = 26,
                Height = 26,
                FontSize = 11,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                Foreground = Brush.Parse("#6F7B87"),
                CornerRadius = new CornerRadius(4)
            };
            dismissBtn.Click += (_, _) => DownloadManager.Instance.RemoveCompleted(item.AppId);
            Grid.SetColumn(dismissBtn, 4);
            grid.Children.Add(dismissBtn);

            card.Child = grid;
            section.Children.Add(card);
        }

        return section;
    }

    private static Control CreateStatBadge(string label, string value, string colorHex)
    {
        var panel = new StackPanel { Spacing = 1 };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 9,
            FontWeight = FontWeight.Bold,
            Foreground = Brush.Parse(colorHex)
        });
        panel.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White
        });
        return panel;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        int digit = (int)Math.Floor(Math.Log(bytes, 1024));
        digit = Math.Clamp(digit, 0, units.Length - 1);
        return $"{bytes / Math.Pow(1024, digit):F1} {units[digit]}";
    }

    private static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec <= 0) return "0.0 MB/s";
        var mbps = bytesPerSec / (1024 * 1024);
        return $"{mbps:F1} MB/s";
    }

    private static string FormatTime(TimeSpan span)
    {
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours:D2}:{span.Minutes:D2}:{span.Seconds:D2}"
            : $"{span.Minutes:D2}:{span.Seconds:D2}";
    }
}


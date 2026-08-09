using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Trionine.TOST.Desktop;

internal sealed class FloatingIconWindow : Window
{
    public FloatingIconWindow(bool alwaysOnTop)
    {
        Width = Height = 60;
        MinWidth = MinHeight = 60;
        MaxWidth = MaxHeight = 60;
        CanResize = false;
        ShowInTaskbar = false;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Topmost = alwaysOnTop;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Opened += (_, _) =>
        {
            if (Screens.Primary?.WorkingArea is { } area)
                Position = new PixelPoint(area.Right - (int)Width - 18, area.Y + 18);
        };

        var surface = new Border
        {
            Width = 54,
            Height = 54,
            CornerRadius = new CornerRadius(27),
            Background = Brush.Parse("#202421"),
            BorderBrush = Brush.Parse("#3B453E"),
            BorderThickness = new Thickness(1),
            Child = new Image
            {
                Source = new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(
                    new Uri("avares://TOST.Desktop/Assets/TOST.png"))),
                Width = 42,
                Height = 42,
                Stretch = Stretch.Uniform
            },
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        surface.PointerPressed += (_, args) =>
        {
            if (args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(args);
        };
        surface.Tapped += (_, _) => (Application.Current as App)?.ShowMainWindow();
        ToolTip.SetTip(surface, "TOST — drag to move, click to open");
        surface.ContextMenu = BuildMenu();
        Content = surface;
    }

    private static ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Open TOST", () => App()?.ShowMainWindow()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Game Manager", () => App()?.OpenPage("Game Manager")));
        menu.Items.Add(Item("Import Files", () => App()?.OpenPage("Import Files")));
        menu.Items.Add(Item("SLSsteam / OST", () => App()?.OpenPage("Integration")));
        menu.Items.Add(Item("Recovery", () => App()?.OpenPage("Recovery")));
        menu.Items.Add(Item("Logs", () => App()?.OpenPage("Logs")));
        menu.Items.Add(Item("Settings", () => App()?.OpenPage("Settings")));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Hide floating icon", () => App()?.HideFloatingIcon()));
        menu.Items.Add(Item("Exit", () => App()?.Exit()));
        return menu;
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static App? App() => Application.Current as App;
}

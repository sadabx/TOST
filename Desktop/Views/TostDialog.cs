using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Trionine.TOST.Desktop.Views;

internal static class TostDialog
{
    public static async Task<bool> ConfirmAsync(Control owner, string title, string message, string acceptText = "Continue")
    {
        var accepted = false;
        var dialog = Create(title, message);
        var cancel = DialogButton("Cancel", primary: false);
        var accept = DialogButton(acceptText, primary: true);
        cancel.Click += (_, _) => dialog.Close();
        accept.Click += (_, _) =>
        {
            accepted = true;
            dialog.Close();
        };
        AddButtons(dialog, cancel, accept);
        if (TopLevel.GetTopLevel(owner) is Window parent)
        {
            await dialog.ShowDialog(parent);
        }

        return accepted;
    }

    public static async Task ShowAsync(Control owner, string title, string message)
    {
        var dialog = Create(title, message);
        var close = DialogButton("OK", primary: true);
        close.Click += (_, _) => dialog.Close();
        AddButtons(dialog, close);
        if (TopLevel.GetTopLevel(owner) is Window parent)
        {
            await dialog.ShowDialog(parent);
        }
    }

    private static Window Create(string title, string message)
    {
        var dialog = new Window
        {
            Width = 470,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            WindowDecorations = Avalonia.Controls.WindowDecorations.None,
            Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent]
        };

        var close = new Button
        {
            Content = "X",
            Width = 34,
            Height = 30,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        close.Click += (_, _) => dialog.Close();

        var header = new Grid
        {
            Height = 42,
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    Margin = new Thickness(18, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 14,
                    FontWeight = FontWeight.SemiBold
                },
                close
            }
        };
        Grid.SetColumn(close, 1);

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("44,*"),
            Margin = new Thickness(18, 18, 18, 20),
            Children =
            {
                new Border
                {
                    Width = 25,
                    Height = 25,
                    CornerRadius = new CornerRadius(13),
                    BorderBrush = Brush.Parse("#56B9E8"),
                    BorderThickness = new Thickness(2),
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = new TextBlock
                    {
                        Text = "i",
                        FontWeight = FontWeight.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = Brush.Parse("#56B9E8")
                    }
                },
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brush.Parse("#E6E9EB"),
                    MaxWidth = 380,
                    LineHeight = 19
                }
            }
        };
        Grid.SetColumn(body.Children[1], 1);

        var buttons = new StackPanel
        {
            Name = "DialogButtons",
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(18, 11, 18, 13)
        };

        dialog.Content = new Border
        {
            Background = Brush.Parse("#202123"),
            BorderBrush = Brush.Parse("#34373A"),
            BorderThickness = new Thickness(1),
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 18, OffsetY = 5, Color = Color.FromArgb(110, 0, 0, 0) }),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,1,Auto,1,Auto"),
                Children =
                {
                    header,
                    new Border { Background = Brush.Parse("#16A13A") },
                    body,
                    new Border { Background = Brush.Parse("#34373A") },
                    buttons
                }
            }
        };
        var root = (Grid)((Border)dialog.Content).Child!;
        Grid.SetRow(root.Children[1], 1);
        Grid.SetRow(body, 2);
        Grid.SetRow(root.Children[3], 3);
        Grid.SetRow(buttons, 4);
        return dialog;
    }

    private static Button DialogButton(string text, bool primary) => new()
    {
        Content = text,
        MinWidth = 86,
        Height = 32,
        Background = primary ? Brush.Parse("#159B35") : Brush.Parse("#424448")
    };

    private static void AddButtons(Window dialog, params Button[] buttons)
    {
        var panel = ((Grid)((Border)dialog.Content!).Child!).Children.OfType<StackPanel>().Last();
        foreach (var button in buttons)
        {
            panel.Children.Add(button);
        }
    }
}

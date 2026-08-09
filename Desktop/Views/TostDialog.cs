using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Trionine.TOST.Desktop.Views;

internal static class TostDialog
{
    public static async Task<bool> ConfirmAsync(Control owner, string title, string message, string acceptText = "Continue")
    {
        var result = false;
        var dialog = Create(title, message);
        var cancel = new Button { Content = "Cancel", MinWidth = 90 };
        var accept = new Button { Content = acceptText, MinWidth = 100, Background = Brush.Parse("#219638") };
        cancel.Click += (_, _) => dialog.Close();
        accept.Click += (_, _) => { result = true; dialog.Close(); };
        AddButtons(dialog, cancel, accept);
        var parent = TopLevel.GetTopLevel(owner) as Window;
        if (parent is null) return false;
        await dialog.ShowDialog(parent);
        return result;
    }

    public static async Task ShowAsync(Control owner, string title, string message)
    {
        var dialog = Create(title, message);
        var close = new Button { Content = "OK", MinWidth = 90, Background = Brush.Parse("#219638") };
        close.Click += (_, _) => dialog.Close();
        AddButtons(dialog, close);
        if (TopLevel.GetTopLevel(owner) is Window parent) await dialog.ShowDialog(parent);
    }

    private static Window Create(string title, string message) => new()
    {
        Title = title,
        Width = 470,
        SizeToContent = SizeToContent.Height,
        CanResize = false,
        ShowInTaskbar = false,
        WindowStartupLocation = WindowStartupLocation.CenterScreen,
        Background = Brush.Parse("#171A18"),
        Content = new StackPanel
        {
            Spacing = 18,
            Margin = new Avalonia.Thickness(22),
            Children =
            {
                new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#C7D0CA"), MaxWidth = 420 },
                new StackPanel { Name = "DialogButtons", Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10 }
            }
        }
    };

    private static void AddButtons(Window dialog, params Button[] buttons)
    {
        var panel = ((StackPanel)dialog.Content!).Children.OfType<StackPanel>().Last();
        foreach (var button in buttons) panel.Children.Add(button);
    }
}

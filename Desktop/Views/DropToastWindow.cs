using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Trionine.TOST.Desktop.Services;

namespace Trionine.TOST.Desktop.Views;

internal sealed class DropToastWindow : Window
{
    public DropToastWindow(DesktopImportSummary summary)
    {
        Width = 326;
        SizeToContent = SizeToContent.Height;
        MinHeight = 100;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Content = new Border
        {
            Padding = new Thickness(18, 14),
            CornerRadius = new CornerRadius(8),
            Background = Brush.Parse("#27282B"),
            BorderBrush = Brush.Parse("#34373A"),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = summary.Failures.Count == 0 ? "\u2713" : "!",
                        FontSize = 20,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Foreground = Brush.Parse(summary.Failures.Count == 0 ? "#D8E2DC" : "#E0A33E")
                    },
                    new TextBlock
                    {
                        Text = summary.ToMessage(),
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 285
                    }
                }
            }
        };
        var timer = new System.Timers.Timer(4500) { AutoReset = false };
        timer.Elapsed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(Close);
        Closed += (_, _) => timer.Dispose();
        timer.Start();
    }

    public void PositionNextTo(Window owner)
    {
        var screen = owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var scale = screen.Scaling;
        var width = (int)Math.Ceiling(Width * scale);
        var height = (int)Math.Ceiling(Math.Max(120, Height) * scale);
        var x = owner.Position.X + (int)Math.Ceiling(owner.Width * scale) + 8;
        if (x + width > screen.WorkingArea.Right)
        {
            x = owner.Position.X - width - 8;
        }

        var y = owner.Position.Y - (height - (int)Math.Ceiling(owner.Height * scale)) / 2;
        Position = new PixelPoint(
            Math.Clamp(x, screen.WorkingArea.X, screen.WorkingArea.Right - width),
            Math.Clamp(y, screen.WorkingArea.Y, screen.WorkingArea.Bottom - height));
    }
}

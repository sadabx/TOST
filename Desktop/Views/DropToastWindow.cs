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
            Padding = new Thickness(22, 16),
            CornerRadius = new CornerRadius(10),
            Background = Brush.Parse("#1E2023"),
            BorderBrush = Brush.Parse("#32353A"),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new Border
                    {
                        Width = 24,
                        Height = 24,
                        CornerRadius = new CornerRadius(12),
                        BorderBrush = Brush.Parse(summary.Failures.Count == 0 ? "#C8D1CC" : "#E0A33E"),
                        BorderThickness = new Thickness(1.8),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Child = new TextBlock
                        {
                            Text = summary.Failures.Count == 0 ? "✓" : "!",
                            FontSize = 13,
                            FontWeight = FontWeight.SemiBold,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            Foreground = Brush.Parse(summary.Failures.Count == 0 ? "#C8D1CC" : "#E0A33E"),
                            Margin = new Thickness(0, -1, 0, 0)
                        }
                    },
                    new TextBlock
                    {
                        Text = summary.ToMessage(),
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 285,
                        FontSize = 13,
                        LineHeight = 19,
                        Foreground = Brush.Parse("#D6DFDA")
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

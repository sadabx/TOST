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
        var actualWidth = double.IsNaN(Width) ? (Bounds.Width > 0 ? Bounds.Width : 350) : Width;
        var actualHeight = double.IsNaN(Height) ? (Bounds.Height > 0 ? Bounds.Height : 150) : Height;
        var ownerWidth = double.IsNaN(owner.Width) ? owner.Bounds.Width : owner.Width;
        var ownerHeight = double.IsNaN(owner.Height) ? owner.Bounds.Height : owner.Height;

        var width = (int)Math.Ceiling(actualWidth * scale);
        var height = (int)Math.Ceiling(Math.Max(120, actualHeight) * scale);
        var x = owner.Position.X + (int)Math.Ceiling(ownerWidth * scale) + 8;
        
        if (x + width > screen.WorkingArea.Right)
        {
            x = owner.Position.X - width - 8;
        }

        var y = owner.Position.Y - (height - (int)Math.Ceiling(ownerHeight * scale)) / 2;
        
        var maxX = Math.Max(screen.WorkingArea.X, screen.WorkingArea.Right - width);
        var maxY = Math.Max(screen.WorkingArea.Y, screen.WorkingArea.Bottom - height);

        Position = new PixelPoint(
            Math.Clamp(x, screen.WorkingArea.X, maxX),
            Math.Clamp(y, screen.WorkingArea.Y, maxY));
    }
}

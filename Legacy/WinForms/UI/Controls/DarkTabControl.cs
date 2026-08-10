namespace Trionine.TOST;

internal sealed class DarkTabControl : TabControl
{
    private static readonly Color SurfaceColor = Color.FromArgb(35, 36, 38);
    private static readonly Color SelectedColor = Color.FromArgb(29, 30, 32);
    private static readonly Color BorderColor = Color.FromArgb(73, 75, 78);
    private static readonly Color TextColor = Color.FromArgb(232, 234, 236);
    private static readonly Color MutedTextColor = Color.FromArgb(174, 179, 184);

    public DarkTabControl()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer,
            true);
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(142, 34);
        BackColor = SurfaceColor;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(SurfaceColor);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(SurfaceColor);

        var pageBounds = DisplayRectangle;
        using (var borderPen = new Pen(BorderColor))
        {
            e.Graphics.DrawRectangle(
                borderPen,
                pageBounds.X - 1,
                pageBounds.Y - 1,
                pageBounds.Width + 1,
                pageBounds.Height + 1);
        }

        for (var index = 0; index < TabCount; index++)
        {
            var tabBounds = GetTabRect(index);
            var selected = index == SelectedIndex;
            using var backgroundBrush = new SolidBrush(selected ? SelectedColor : SurfaceColor);
            e.Graphics.FillRectangle(backgroundBrush, tabBounds);

            if (selected)
            {
                using var accentBrush = new SolidBrush(Color.FromArgb(47, 184, 75));
                e.Graphics.FillRectangle(accentBrush, tabBounds.Left, tabBounds.Bottom - 2, tabBounds.Width, 2);
            }

            TextRenderer.DrawText(
                e.Graphics,
                TabPages[index].Text,
                Font,
                tabBounds,
                selected ? TextColor : MutedTextColor,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        Invalidate();
    }
}


using System.Drawing.Drawing2D;

namespace Trionine.TOST;

internal sealed class SteamStyleMenuRenderer : ToolStripProfessionalRenderer
{
    private static readonly Color MenuColor = Color.FromArgb(36, 36, 36);
    private static readonly Color HoverColor = Color.FromArgb(52, 53, 55);
    private static readonly Color BorderColor = Color.FromArgb(49, 50, 52);
    private static readonly Color SeparatorColor = Color.FromArgb(67, 68, 70);

    public SteamStyleMenuRenderer()
        : base(new SteamStyleColorTable())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        e.Graphics.Clear(MenuColor);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(MenuColor);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var color = e.Item.Selected ? HoverColor : MenuColor;
        using var brush = new SolidBrush(color);
        e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
    }

    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
        if (e.Image is null)
        {
            return;
        }

        // Draw the full glyph ourselves instead of using the narrow image slot
        // calculated by the standard menu renderer.
        var imageY = (e.Item.Height - e.Image.Height) / 2;
        e.Graphics.DrawImageUnscaled(e.Image, 4, imageY);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        // Use a compact icon-to-label gap and reclaim the shortcut/arrow column
        // for ordinary items. Submenus still keep room for their arrow.
        var rightPadding = e.Item is ToolStripMenuItem { HasDropDownItems: true } ? 28 : 6;
        e.TextRectangle = new Rectangle(
            34,
            e.TextRectangle.Top,
            Math.Max(0, e.Item.Width - 34 - rightPadding),
            e.TextRectangle.Height);

        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(SeparatorColor);
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.FromArgb(151, 157, 164), 1.4f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        var centerX = e.ArrowRectangle.Left + (e.ArrowRectangle.Width / 2f);
        var centerY = e.ArrowRectangle.Top + (e.ArrowRectangle.Height / 2f);
        e.Graphics.DrawLines(pen,
        [
            new PointF(centerX - 2f, centerY - 4f),
            new PointF(centerX + 2f, centerY),
            new PointF(centerX - 2f, centerY + 4f)
        ]);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(BorderColor);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }
}

internal sealed class SteamStyleColorTable : ProfessionalColorTable
{
    private static readonly Color Dark = Color.FromArgb(36, 36, 36);

    public override Color ToolStripDropDownBackground => Dark;
    public override Color ImageMarginGradientBegin => Dark;
    public override Color ImageMarginGradientMiddle => Dark;
    public override Color ImageMarginGradientEnd => Dark;
    public override Color MenuBorder => Color.FromArgb(49, 50, 52);
    public override Color MenuItemBorder => Color.Transparent;
    public override Color MenuItemSelected => Color.FromArgb(52, 53, 55);
}


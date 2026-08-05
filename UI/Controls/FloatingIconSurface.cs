using System.Drawing.Drawing2D;

namespace Trionine.TOST;

internal sealed class FloatingIconSurface : Control
{
    private bool isDropTarget;

    public Image? Logo { get; set; }

    public bool IsDropTarget
    {
        get => isDropTarget;
        set
        {
            if (isDropTarget == value)
            {
                return;
            }

            isDropTarget = value;
            Invalidate();
        }
    }

    public FloatingIconSurface()
    {
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(IsDropTarget ? Color.FromArgb(54, 62, 69) : Color.FromArgb(43, 45, 48));

        if (Logo is not null)
        {
            var logoBounds = new Rectangle(8, 8, Width - 16, Height - 16);
            e.Graphics.DrawImage(Logo, logoBounds);
        }
        else
        {
            using var font = new Font("Segoe UI", 11f, FontStyle.Bold);
            using var brush = new SolidBrush(Color.White);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            e.Graphics.DrawString("TOST", font, brush, ClientRectangle, format);
        }

        using var border = new Pen(
            IsDropTarget ? Color.FromArgb(102, 192, 244) : Color.FromArgb(72, 75, 79),
            IsDropTarget ? 2f : 1f);
        e.Graphics.DrawEllipse(border, 1, 1, Width - 3, Height - 3);
    }
}


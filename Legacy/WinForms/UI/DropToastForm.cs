using System.Drawing.Drawing2D;

namespace Trionine.TOST;

internal sealed class DropToastForm : Form
{
    private readonly System.Windows.Forms.Timer dismissTimer = new();
    private readonly System.Windows.Forms.Timer fadeTimer = new();

    public DropToastForm(CopyReport report)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(42, 42, 43);
        ForeColor = Color.FromArgb(232, 234, 236);
        ClientSize = new Size(326, report.Failures > 0 ? 128 : 116);
        Region = CreateRoundedRegion(ClientRectangle, 7);
        Padding = new Padding(18, 12, 18, 12);

        var status = new ToastStatusIcon
        {
            Success = report.Successes > 0,
            Location = new Point((ClientSize.Width - 24) / 2, 11),
            Size = new Size(24, 24)
        };

        var message = new Label
        {
            AutoSize = false,
            Location = new Point(14, 42),
            Size = new Size(ClientSize.Width - 28, ClientSize.Height - 50),
            Text = report.ToToastMessage(),
            TextAlign = ContentAlignment.TopCenter,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Regular),
            ForeColor = ForeColor,
            BackColor = Color.Transparent
        };

        Controls.Add(status);
        Controls.Add(message);

        dismissTimer.Interval = report.Failures > 0 ? 5200 : 3800;
        dismissTimer.Tick += (_, _) =>
        {
            dismissTimer.Stop();
            fadeTimer.Start();
        };

        fadeTimer.Interval = 30;
        fadeTimer.Tick += (_, _) =>
        {
            Opacity -= 0.08;
            if (Opacity > 0.05)
            {
                return;
            }

            fadeTimer.Stop();
            Close();
        };

        Click += (_, _) => Close();
        status.Click += (_, _) => Close();
        message.Click += (_, _) => Close();
        Shown += (_, _) => dismissTimer.Start();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int CsDropShadow = 0x00020000;
            const int WsExToolWindow = 0x00000080;
            const int WsExNoActivate = 0x08000000;

            var parameters = base.CreateParams;
            parameters.ClassStyle |= CsDropShadow;
            parameters.ExStyle |= WsExToolWindow | WsExNoActivate;
            return parameters;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        dismissTimer.Dispose();
        fadeTimer.Dispose();
        base.OnFormClosed(e);
    }

    private static Region CreateRoundedRegion(Rectangle bounds, int radius)
    {
        using var path = new GraphicsPath();
        var diameter = radius * 2;
        var arc = new Rectangle(bounds.X, bounds.Y, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.X;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return new Region(path);
    }
}

internal sealed class ToastStatusIcon : Control
{
    public bool Success { get; set; }

    public ToastStatusIcon()
    {
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var circlePen = new Pen(
            Success ? Color.FromArgb(224, 230, 234) : Color.FromArgb(231, 177, 83),
            2.2f);
        e.Graphics.DrawEllipse(circlePen, 3, 3, Width - 7, Height - 7);

        if (Success)
        {
            using var checkPen = new Pen(Color.FromArgb(224, 230, 234), 2.2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            e.Graphics.DrawLines(checkPen,
            [
                new PointF(7f, 12f),
                new PointF(10.5f, 15.5f),
                new PointF(17f, 8.5f)
            ]);
        }
        else
        {
            using var warningPen = new Pen(Color.FromArgb(231, 177, 83), 2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            e.Graphics.DrawLine(warningPen, Width / 2f, 7f, Width / 2f, 13f);
            e.Graphics.DrawEllipse(warningPen, (Width / 2f) - 0.5f, 16f, 1f, 1f);
        }
    }
}


using System.Drawing.Drawing2D;

namespace Trionine.TOST;

internal enum TostDialogButtons
{
    Ok,
    YesNo
}

internal enum TostDialogIcon
{
    Information,
    Success,
    Warning,
    Error
}

internal sealed class TostDialog : Form
{
    private const int DialogWidth = 468;
    private readonly Button primaryButton;

    private TostDialog(string message, string title, TostDialogButtons buttons, TostDialogIcon icon)
    {
        var messageFont = new Font("Segoe UI", 10f, FontStyle.Regular);
        var measuredMessage = TextRenderer.MeasureText(
            message,
            messageFont,
            new Size(352, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        var messageHeight = Math.Clamp(measuredMessage.Height + 8, 54, 230);
        var dialogHeight = 64 + messageHeight + 68;

        Text = title;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = Color.FromArgb(35, 36, 38);
        ForeColor = Color.FromArgb(232, 234, 236);
        ClientSize = new Size(DialogWidth, dialogHeight);
        Font = messageFont;
        KeyPreview = true;

        var titleBar = new Panel
        {
            Location = Point.Empty,
            Size = new Size(DialogWidth, 44),
            BackColor = Color.FromArgb(29, 30, 32)
        };
        var accent = new Panel
        {
            Location = new Point(0, 43),
            Size = new Size(DialogWidth, 2),
            BackColor = Color.FromArgb(42, 185, 71)
        };
        var titleLabel = new Label
        {
            AutoEllipsis = true,
            AutoSize = false,
            Location = new Point(16, 0),
            Size = new Size(DialogWidth - 64, 43),
            Text = title,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Regular),
            ForeColor = Color.FromArgb(235, 237, 239)
        };
        var closeButton = new Button
        {
            FlatStyle = FlatStyle.Flat,
            Location = new Point(DialogWidth - 44, 0),
            Size = new Size(44, 43),
            Text = "X",
            Font = new Font("Segoe UI Semibold", 9f),
            ForeColor = Color.FromArgb(174, 179, 184),
            BackColor = Color.Transparent,
            DialogResult = DialogResult.Cancel,
            TabStop = false
        };
        closeButton.FlatAppearance.BorderSize = 0;
        closeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(190, 52, 52);
        closeButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(155, 42, 42);

        titleBar.Controls.Add(titleLabel);
        titleBar.Controls.Add(closeButton);
        titleBar.Controls.Add(accent);
        EnableWindowDrag(titleBar);
        EnableWindowDrag(titleLabel);

        var statusIcon = new TostDialogStatusIcon
        {
            Icon = icon,
            Location = new Point(22, 64),
            Size = new Size(30, 30)
        };
        var messageBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            ScrollBars = measuredMessage.Height + 8 > messageHeight
                ? ScrollBars.Vertical
                : ScrollBars.None,
            Location = new Point(70, 62),
            Size = new Size(374, messageHeight),
            Text = message,
            Font = messageFont,
            ForeColor = ForeColor,
            BackColor = BackColor,
            TabStop = true
        };

        var buttonBarTop = dialogHeight - 56;
        var buttonBar = new Panel
        {
            Location = new Point(0, buttonBarTop),
            Size = new Size(DialogWidth, 56),
            BackColor = Color.FromArgb(31, 32, 34)
        };
        var divider = new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = Color.FromArgb(57, 59, 62)
        };
        buttonBar.Controls.Add(divider);

        if (buttons == TostDialogButtons.YesNo)
        {
            var noButton = CreateButton("No", DialogResult.No, primary: false);
            noButton.Location = new Point(DialogWidth - 196, 12);
            buttonBar.Controls.Add(noButton);

            primaryButton = CreateButton("Yes", DialogResult.Yes, primary: true);
            primaryButton.Location = new Point(DialogWidth - 98, 12);
            CancelButton = noButton;
        }
        else
        {
            primaryButton = CreateButton("OK", DialogResult.OK, primary: true);
            primaryButton.Location = new Point(DialogWidth - 98, 12);
            CancelButton = closeButton;
        }

        buttonBar.Controls.Add(primaryButton);
        AcceptButton = primaryButton;

        Controls.Add(titleBar);
        Controls.Add(statusIcon);
        Controls.Add(messageBox);
        Controls.Add(buttonBar);

        Shown += (_, _) => primaryButton.Focus();
    }

    public static DialogResult Show(
        IWin32Window? owner,
        string message,
        string title,
        TostDialogButtons buttons,
        TostDialogIcon icon)
    {
        using var dialog = new TostDialog(message, title, buttons, icon);
        if (owner is Form ownerForm)
        {
            dialog.TopMost = ownerForm.TopMost;
        }

        return owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int CsDropShadow = 0x00020000;
            var parameters = base.CreateParams;
            parameters.ClassStyle |= CsDropShadow;
            return parameters;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var borderPen = new Pen(Color.FromArgb(68, 70, 73));
        e.Graphics.DrawRectangle(borderPen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    private static Button CreateButton(string text, DialogResult result, bool primary)
    {
        var button = new Button
        {
            AutoSize = false,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(86, 32),
            Text = text,
            DialogResult = result,
            Font = new Font("Segoe UI Semibold", 9.5f),
            ForeColor = Color.White,
            BackColor = primary
                ? Color.FromArgb(33, 150, 57)
                : Color.FromArgb(63, 65, 68),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = primary
            ? Color.FromArgb(48, 183, 73)
            : Color.FromArgb(82, 84, 87);
        button.FlatAppearance.MouseOverBackColor = primary
            ? Color.FromArgb(39, 166, 64)
            : Color.FromArgb(75, 77, 80);
        button.FlatAppearance.MouseDownBackColor = primary
            ? Color.FromArgb(27, 130, 49)
            : Color.FromArgb(52, 54, 57);
        return button;
    }

    private void EnableWindowDrag(Control control)
    {
        var dragging = false;
        var cursorStart = Point.Empty;
        var formStart = Point.Empty;

        control.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            dragging = true;
            cursorStart = Cursor.Position;
            formStart = Location;
        };
        control.MouseMove += (_, _) =>
        {
            if (!dragging)
            {
                return;
            }

            var delta = Point.Subtract(Cursor.Position, new Size(cursorStart));
            Location = Point.Add(formStart, new Size(delta));
        };
        control.MouseUp += (_, _) => dragging = false;
    }
}

internal sealed class TostDialogStatusIcon : Control
{
    public TostDialogIcon Icon { get; set; }

    public TostDialogStatusIcon()
    {
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var color = Icon switch
        {
            TostDialogIcon.Success => Color.FromArgb(52, 190, 80),
            TostDialogIcon.Warning => Color.FromArgb(231, 177, 83),
            TostDialogIcon.Error => Color.FromArgb(224, 92, 92),
            _ => Color.FromArgb(102, 192, 244)
        };
        using var pen = new Pen(color, 2.2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        e.Graphics.DrawEllipse(pen, 3, 3, Width - 7, Height - 7);

        switch (Icon)
        {
            case TostDialogIcon.Success:
                e.Graphics.DrawLines(pen,
                [
                    new PointF(8f, 15f),
                    new PointF(13f, 20f),
                    new PointF(22f, 10f)
                ]);
                break;
            case TostDialogIcon.Error:
                e.Graphics.DrawLine(pen, 10f, 10f, 20f, 20f);
                e.Graphics.DrawLine(pen, 20f, 10f, 10f, 20f);
                break;
            case TostDialogIcon.Warning:
                e.Graphics.DrawLine(pen, 15f, 9f, 15f, 17f);
                e.Graphics.DrawEllipse(pen, 14.5f, 21f, 1f, 1f);
                break;
            default:
                e.Graphics.DrawLine(pen, 15f, 13f, 15f, 21f);
                e.Graphics.DrawEllipse(pen, 14.5f, 8f, 1f, 1f);
                break;
        }
    }
}


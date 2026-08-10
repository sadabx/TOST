using System.Runtime.InteropServices;

namespace Trionine.TOST;

internal static class WindowTheme
{
    private const int UseImmersiveDarkMode = 20;
    private const int UseImmersiveDarkModeBefore20H1 = 19;
    private const int CaptionColor = 35;
    private const int TextColor = 36;

    public static void ApplyDarkTitleBar(Form form)
    {
        form.HandleCreated += (_, _) => ApplyDarkTitleBar(form.Handle);
        if (form.IsHandleCreated)
        {
            ApplyDarkTitleBar(form.Handle);
        }
    }

    private static void ApplyDarkTitleBar(IntPtr handle)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return;
        }

        var enabled = 1;
        var result = DwmSetWindowAttribute(
            handle,
            UseImmersiveDarkMode,
            ref enabled,
            sizeof(int));
        if (result != 0)
        {
            DwmSetWindowAttribute(
                handle,
                UseImmersiveDarkModeBefore20H1,
                ref enabled,
                sizeof(int));
        }

        var captionColor = ToColorRef(Color.FromArgb(35, 36, 38));
        var textColor = ToColorRef(Color.FromArgb(232, 234, 236));
        DwmSetWindowAttribute(handle, CaptionColor, ref captionColor, sizeof(int));
        DwmSetWindowAttribute(handle, TextColor, ref textColor, sizeof(int));
    }

    private static int ToColorRef(Color color) =>
        color.R | color.G << 8 | color.B << 16;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}


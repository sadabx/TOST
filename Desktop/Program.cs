using Avalonia;
using Velopack;

namespace Trionine.TOST.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (OperatingSystem.IsWindows())
        {
            VelopackApp.Build().Run();
        }

        var mutexName = OperatingSystem.IsWindows()
            ? @"Local\Trionine.TOST.Avalonia.Instance"
            : "Trionine.TOST.Avalonia.Instance";
        using var instanceMutex = new Mutex(initiallyOwned: true, mutexName, out var firstInstance);
        EventWaitHandle? activationEvent = null;
        if (OperatingSystem.IsWindows())
        {
            activationEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                @"Local\Trionine.TOST.Avalonia.Activate");
        }

        if (!firstInstance)
        {
            activationEvent?.Set();
            activationEvent?.Dispose();
            return;
        }

        RegisteredWaitHandle? activationRegistration = null;
        if (activationEvent is not null)
        {
            activationRegistration = ThreadPool.RegisterWaitForSingleObject(
                activationEvent,
                (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    (Application.Current as App)?.ActivateExistingInstance()),
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        activationRegistration?.Unregister(null);
        activationEvent?.Dispose();
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}

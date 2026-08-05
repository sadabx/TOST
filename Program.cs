using Velopack;

namespace Trionine.TOST;

internal static class Program
{
    private const string InstanceMutexName = @"Local\Trionine.TOST.Instance";
    private const string ActivationEventName = @"Local\Trionine.TOST.Activate";

    [STAThread]
    private static void Main()
    {
        VelopackApp.Build().Run();

        using var activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            ActivationEventName);
        using var instanceMutex = new Mutex(
            initiallyOwned: true,
            InstanceMutexName,
            out var isFirstInstance);

        if (!isFirstInstance)
        {
            activationEvent.Set();
            return;
        }

        AppPaths.Initialize();
        ApplicationConfiguration.Initialize();
        using var form = new FloatingInstallerForm();
        _ = form.Handle;

        var activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            activationEvent,
            (_, _) => form.ActivateExistingInstance(),
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        Application.Run(form);
        activationRegistration.Unregister(null);
    }
}

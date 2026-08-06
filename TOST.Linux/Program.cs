using Trionine.TOST.Core.Steam;

if (!OperatingSystem.IsLinux())
{
    Console.Error.WriteLine("TOST Linux CLI must be run on Linux.");
    return 2;
}

var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "status";
return command switch
{
    "status" => ShowStatus(),
    "help" or "--help" or "-h" => ShowHelp(),
    _ => UnknownCommand(command)
};

static int ShowStatus()
{
    var installations = LinuxSteamDiscovery.FindInstallations();
    Console.WriteLine("TOST Linux status");
    if (installations.Count == 0)
    {
        Console.WriteLine("No Steam installation was found.");
        Console.WriteLine("Set STEAM_DIR to use a custom Steam root.");
        return 1;
    }

    foreach (var installation in installations)
    {
        Console.WriteLine();
        Console.WriteLine($"Steam ({installation.Kind}): {installation.RootPath}");
        Console.WriteLine($"  steamapps: {(installation.HasSteamApps ? "found" : "missing")}");
        Console.WriteLine($"  config:    {(installation.HasConfig ? "found" : "missing")}");
    }

    Console.WriteLine();
    Console.WriteLine("SLSsteam integration and file importing are not enabled yet.");
    return 0;
}

static int ShowHelp()
{
    Console.WriteLine("TOST Linux CLI");
    Console.WriteLine("Usage: tost [status|help]");
    Console.WriteLine("  status  Detect Steam installations without changing files.");
    return 0;
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    ShowHelp();
    return 2;
}

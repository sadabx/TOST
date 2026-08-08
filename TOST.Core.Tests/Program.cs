using Trionine.TOST.Core.Imports;
using Trionine.TOST.Core.Integrations.SlsSteam;
using Trionine.TOST.Core.Steam;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;

var tests = new (string Name, Action Run)[]
{
    ("Lua parsing ignores comments and extracts declarations", TestLuaParsing),
    ("App manifests expose bounded AppState metadata", TestAppManifestParsing),
    ("Lua metadata produces a non-writing SLSsteam conversion plan", TestConversionPlan),
    ("SLSsteam import config merges known sections and restores its backup", TestImportConfigMerge),
    ("Steam depot keys merge into VDF without overwriting conflicts", TestDepotKeyMerge),
    ("SLSsteam installer verifies and extracts only managed libraries", TestSlsSteamInstaller),
    ("Native and Flatpak launch hooks are guarded and removable", TestLaunchConfiguration),
    ("Imports route into a fake Steam installation and reject conflicts", TestImportRouting),
    ("Configuration changes back up and restore exact bytes", TestConfigBackupRestore),
    ("Linux Steam discovery uses only the supplied fake home", TestSteamDiscovery),
    ("SLSsteam libraries archive and restore in a fake installation", TestSlsRecovery)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} checks passed.");
return failures == 0 ? 0 : 1;

static void TestLuaParsing()
{
    using var fixture = new TemporaryDirectory();
    var path = Path.Combine(fixture.Path, "123.lua");
    File.WriteAllText(path, "-- addappid(999)\n--[[\naddappid(888)\n]]\naddappid(123, 1, \"aabbcc\")\naddtoken(123, \"987654321\")\nsetManifestid(456, \"789\", 42)\n");
    var result = new SteamImportInspector().Inspect(path);
    Equal(["123"], result.AppIds);
    Equal(["456"], result.DepotIds);
    Equal(["789"], result.ManifestIds);
    True(result.AppDeclarations.Single().DepotKey == "aabbcc", "Depot key was not parsed.");
    True(result.Tokens.Single().Token == "987654321", "App token was not parsed.");
    True(result.Manifests.Single().Size == 42, "Manifest size was not parsed.");
}

static void TestAppManifestParsing()
{
    using var fixture = new TemporaryDirectory();
    var path = Path.Combine(fixture.Path, "appmanifest_10.acf");
    File.WriteAllText(path, "\"AppState\"\n{\n\"appid\" \"10\"\n\"name\" \"Test Game\"\n\"installdir\" \"Test\"\n\"StateFlags\" \"4\"\n}");
    var result = new SteamAppManifestParser().Parse(path);
    True(result.AppId == "10" && result.Name == "Test Game" && result.StateFlags == 4, "ACF metadata did not parse.");
}

static void TestConversionPlan()
{
    using var fixture = new TemporaryDirectory();
    var path = Path.Combine(fixture.Path, "10.lua");
    File.WriteAllText(path, "addappid(10)\naddappid(20, 1, \"aabb\")\naddtoken(10, \"99\")\nsetManifestid(20, \"30\")\n");
    var inspection = new SteamImportInspector().Inspect(path);
    var plan = new SlsSteamImportConversionService().CreatePlan([inspection]);
    Equal(["10", "20"], plan.AdditionalApps);
    True(plan.AppTokens["10"] == "99" && plan.DepotKeys["20"] == "aabb", "Conversion metadata was lost.");
    True(plan.ManifestIds.Single().ManifestId == "30" && plan.Warnings.Count == 2, "Conversion plan is incomplete.");
}

static void TestImportConfigMerge()
{
    using var fixture = new TemporaryDirectory();
    var config = Path.Combine(fixture.Path, "config.yaml");
    const string original = "SafeMode: no\nAdditionalApps:\n  - 5\nDlcData:\nAppTokens:\n  8: 9\nManifestIds:\nOther: yes\n";
    File.WriteAllText(config, original);
    var plan = new SlsSteamImportConversionPlan(["10", "5"],
        new Dictionary<string, string> { ["10"] = "99" },
        [new SlsSteamManifestOverride("20", "30", null)],
        new Dictionary<string, string>(), []);
    var service = new SlsSteamImportConfigService();
    var preview = service.Preview(config, plan);
    True(preview.ChangesFile && preview.ChangedSections.Count == 3, "Expected three changed YAML sections.");
    True(preview.UpdatedText.Contains("  - 5\n  - 10\n") && preview.UpdatedText.Contains("  10: 99\n") &&
         preview.UpdatedText.Contains("  20: 30\n"), "Official YAML shapes were not generated.");
    var result = service.Apply(config, plan, Path.Combine(fixture.Path, "backups"));
    True(result.Changed && result.Backup is not null, "Config merge did not create a backup.");
    new SlsSteamConfigService().RestoreBackup(config, Path.Combine(fixture.Path, "backups"), Path.GetFileName(result.Backup!.BackupPath));
    True(File.ReadAllText(config) == original, "Import config backup was not restorable.");
}

static void TestDepotKeyMerge()
{
    using var fixture = new TemporaryDirectory();
    var config = Path.Combine(fixture.Path, "config.vdf");
    const string original = "\"InstallConfigStore\"\n{\n\"Software\"\n{\n\"Valve\"\n{\n\"Steam\"\n{\n\"depots\"\n{\n\"5\"\n{\n\"DecryptionKey\" \"aabb\"\n}\n}\n}\n}\n}\n}\n";
    File.WriteAllText(config, original);
    var service = new SteamDepotKeyService();
    var preview = service.Preview(config, new Dictionary<string, string> { ["5"] = "AABB", ["10"] = "ccdd" });
    True(preview.ChangesFile && preview.AddedDepotIds.SequenceEqual(["10"]) && preview.Conflicts.Count == 0,
        "Depot-key preview did not preserve the existing key.");
    var result = service.Apply(config, new Dictionary<string, string> { ["10"] = "ccdd" }, Path.Combine(fixture.Path, "backups"));
    True(result.Changed && result.BackupPath is not null && File.Exists(result.BackupPath), "Depot-key backup was not created.");
    True(File.ReadAllText(config).Contains("\"DecryptionKey\"\t\t\"ccdd\""), "Depot key was not written.");
    var conflict = service.Preview(config, new Dictionary<string, string> { ["5"] = "eeff" });
    True(conflict.Conflicts.Count == 1 && !conflict.ChangesFile, "Conflicting depot key was not rejected.");
}

static void TestSlsSteamInstaller()
{
    using var fixture = new TemporaryDirectory();
    byte[] archiveBytes;
    using (var buffer = new MemoryStream())
    {
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in new[] { "bin/SLSsteam.so", "bin/library-inject.so", "ignored.txt" })
            {
                var entry = archive.CreateEntry(name);
                using var output = entry.Open();
                output.Write(name == "ignored.txt" ? new byte[] { 9 } : new byte[] { 1, 2, 3 });
            }
        }
        archiveBytes = buffer.ToArray();
    }
    using var client = new HttpClient(new StaticHttpHandler(archiveBytes));
    var asset = new SlsSteamReleaseAsset("SLSsteam-Any.7z", archiveBytes.Length,
        new Uri("https://github.com/AceSLS/SLSsteam/releases/download/test/SLSsteam-Any.7z"),
        Convert.ToHexString(SHA256.HashData(archiveBytes)));
    var release = new SlsSteamRelease("test", DateTimeOffset.UtcNow, new Uri("https://github.com/AceSLS/SLSsteam"), [asset]);
    var paths = new SlsSteamPaths(fixture.Path, fixture.Path, Path.Combine(fixture.Path, "SLSsteam.so"),
        Path.Combine(fixture.Path, "library-inject.so"), Path.Combine(fixture.Path, "config.yaml"), []);
    var result = new SlsSteamInstallerService(client).InstallAsync(release, paths).GetAwaiter().GetResult();
    True(result.InstalledFiles.Count == 2 && File.Exists(paths.MainLibraryPath) && File.Exists(paths.InjectorLibraryPath),
        "Verified libraries were not installed.");
    True(!File.Exists(Path.Combine(fixture.Path, "ignored.txt")), "Unexpected archive content was extracted.");
}

static void TestLaunchConfiguration()
{
    using var fixture = new TemporaryDirectory();
    var data = Path.Combine(fixture.Path, "SLSsteam");
    Directory.CreateDirectory(data);
    File.WriteAllBytes(Path.Combine(data, "SLSsteam.so"), [1]);
    File.WriteAllBytes(Path.Combine(data, "library-inject.so"), [2]);
    var steam = Path.Combine(fixture.Path, "usr", "bin", "steam");
    Directory.CreateDirectory(Path.GetDirectoryName(steam)!);
    File.WriteAllText(steam, "steam");
    var paths = new SlsSteamPaths(data, Path.Combine(fixture.Path, "config"), Path.Combine(data, "SLSsteam.so"),
        Path.Combine(data, "library-inject.so"), Path.Combine(fixture.Path, "config", "config.yaml"), []);
    var service = new SlsSteamLaunchConfigurationService();
    var native = service.PreviewNative(paths, fixture.Path, new Dictionary<string, string> { ["steam"] = steam });
    True(native.CanApply && native.HasChanges, "Native hook was not ready.");
    var created = service.Apply(native);
    True(created.Count == 2 && created.All(File.Exists), "Native hook files were not created.");
    var configured = service.PreviewNative(paths, fixture.Path, new Dictionary<string, string> { ["steam"] = steam });
    True(configured.CanApply && !configured.HasChanges, "Existing managed hooks were not recognized.");
    var recoveryRoot = Path.Combine(fixture.Path, "recovery");
    var archived = service.ArchiveManaged(configured, recoveryRoot);
    True(archived.Paths.Count == 2 && archived.Paths.All(path => !File.Exists(path)), "Managed native hooks were not archived.");
    True(service.FindRecoveryEntries(recoveryRoot).Count == 1, "Launch-hook recovery entry was not found.");
    True(service.Restore(native, recoveryRoot, archived.ArchiveId).Count == 2 && archived.Paths.All(File.Exists),
        "Native launch hooks were not restored.");
    service.RemoveManaged(configured);

    var flatpak = service.PreviewFlatpak(paths, fixture.Path);
    service.Apply(flatpak);
    File.AppendAllText(flatpak.Items.Single().Path, "changed=yes\n");
    var conflict = service.PreviewFlatpak(paths, fixture.Path);
    True(!conflict.CanApply, "Modified Flatpak override was not protected.");
}

static void TestImportRouting()
{
    using var fixture = new TemporaryDirectory();
    var steamRoot = Path.Combine(fixture.Path, "Steam");
    Directory.CreateDirectory(steamRoot);
    var lua = Path.Combine(fixture.Path, "10.lua");
    var manifest = Path.Combine(fixture.Path, "20_30.manifest");
    var appManifest = Path.Combine(fixture.Path, "appmanifest_10.acf");
    File.WriteAllText(lua, "addappid(10)\n");
    File.WriteAllBytes(manifest, [1, 2, 3]);
    File.WriteAllText(appManifest, "\"AppState\"\n{\n\"appid\" \"10\"\n}");
    var steam = new SteamInstallation(steamRoot, SteamInstallationKind.Native, false, false);
    var service = new SteamImportService();
    var result = service.ApplyNewFiles(steam, [lua, manifest, appManifest]);
    True(result.Success, result.Message);
    True(File.Exists(Path.Combine(steam.SlsPluginPath, "10.lua")), "Lua destination missing.");
    True(File.Exists(Path.Combine(steam.DepotCachePath, "20_30.manifest")), "Depot destination missing.");
    True(File.Exists(Path.Combine(steam.SteamAppsPath, "appmanifest_10.acf")), "App manifest destination missing.");
    True(!service.CreatePlan(steam, [lua]).CanApply, "Existing destination was not rejected.");
}

static void TestConfigBackupRestore()
{
    using var fixture = new TemporaryDirectory();
    var config = Path.Combine(fixture.Path, "config.yaml");
    var backups = Path.Combine(fixture.Path, "backups");
    const string original = "SafeMode: no\nNotifications: yes\n";
    File.WriteAllText(config, original);
    var service = new SlsSteamConfigService();
    var changed = service.SetBooleanSetting(config, "SafeMode", true, backups);
    True(changed.Changed && changed.Backup is not null, "Config change did not create a backup.");
    True(File.ReadAllText(config).Contains("SafeMode: yes"), "SafeMode was not updated.");
    var restored = service.RestoreBackup(config, backups, Path.GetFileName(changed.Backup!.BackupPath));
    True(restored.Changed, "Config restoration reported no change.");
    True(File.ReadAllText(config) == original, "Config restoration was not byte-for-byte.");
}

static void TestSteamDiscovery()
{
    using var fixture = new TemporaryDirectory();
    var root = Path.Combine(fixture.Path, ".local", "share", "Steam");
    Directory.CreateDirectory(Path.Combine(root, "steamapps"));
    var found = LinuxSteamDiscovery.FindInstallations(
        fixture.Path,
        new Dictionary<string, string?> { ["STEAM_DIR"] = null, ["STEAM_ROOT"] = null });
    True(found.Count == 1, $"Expected one Steam installation, found {found.Count}.");
    True(found[0].RootPath == root, "Steam discovery returned the wrong root.");
}

static void TestSlsRecovery()
{
    using var fixture = new TemporaryDirectory();
    var data = Path.Combine(fixture.Path, "SLSsteam");
    Directory.CreateDirectory(data);
    File.WriteAllBytes(Path.Combine(data, "SLSsteam.so"), [1]);
    File.WriteAllBytes(Path.Combine(data, "library-inject.so"), [2]);
    var paths = new SlsSteamPaths(
        data,
        Path.Combine(fixture.Path, "config"),
        Path.Combine(data, "SLSsteam.so"),
        Path.Combine(data, "library-inject.so"),
        Path.Combine(fixture.Path, "config", "config.yaml"),
        []);
    var recovery = Path.Combine(fixture.Path, "recovery");
    var service = new SlsSteamRecoveryService();
    var removed = service.Remove(paths, "Native", recovery);
    True(removed.Changed && removed.ArchiveId is not null, "Libraries were not archived.");
    True(!File.Exists(paths.MainLibraryPath), "Main library remained after archival.");
    service.Restore(paths, "Native", recovery, removed.ArchiveId!);
    True(File.Exists(paths.MainLibraryPath) && File.Exists(paths.InjectorLibraryPath), "Libraries were not restored.");
}

static void True(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Equal(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException($"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
}

sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tost-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}

sealed class StaticHttpHandler(byte[] content) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
        response.Content.Headers.ContentLength = content.Length;
        return Task.FromResult(response);
    }
}

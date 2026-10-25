using OpenLaunchCore;
using System.Xml.Linq;

var tests = new (string Name, Action Test)[] {
    ("preview is read-only; preferences and appearance import without changing source games", HappyPath),
    ("existing data wins by default; repeat imports are idempotent", PreserveExisting),
    ("explicit overwrite keeps recoverable file and registry backups", OverwriteAndBackup),
    ("registry failure rolls back all file and registry changes", FailureRollsBack),
    ("cancellation rolls back files and removes incomplete copies", CancellationRollsBack),
    ("settings changed since preview are protected", ConcurrentEdits),
    ("invalid values, unknown keys, credentials, and launcher identity are excluded", FilterSettings),
    ("PCL data-folder selection works; self-import is harmless", DataFolderAndSelfImport),
    ("invalid source and nested destinations fail before mutation", RejectInvalidPaths),
    ("symbolic links cannot redirect import writes", RejectLinks),
    ("category selection controls which data is imported", Categories),
    ("automatic discovery selects the most recently used installation", DetectLatest),
    ("registry paths reveal portable installations outside common folders", DetectFromRegistry),
    ("registry-only discovery imports preferences and registers games without file selection", RegistryOnlyImport),
    ("discovery excludes OpenLaunch and per-instance metadata", DetectionExclusions),
    ("discovery is bounded, cancellable, and ignores drive/network roots", BoundedDetection),
    ("detected data imports end to end with no source-folder selection", AutomaticImport),
    ("PCL2 dollar-prefixed game-folder selections resolve against the original launcher", RelativeGameSelection)
};
int failed = 0;
foreach (var test in tests) {
    try { test.Test(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + test.Name + "\n" + ex); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} import integration tests passed.");
return failed == 0 ? 0 : 1;

static void HappyPath() {
    using var f = new Fixture();
    f.SourceFile("Setup.ini", "LaunchArgumentTitle:My game\nLaunchArgumentInfo:PCL\nLaunchRamCustom:12\n");
    f.SourceFile("Custom.xaml", "<StackPanel />");
    f.SourceFile("Pictures/a.png", "image");
    f.SourceFile("Musics/nested/a.ogg", "music");
    f.SourceFile("Log1.txt", "not an appearance asset");
    f.SourceRegistry["ToolDownloadSpeed"] = "18";
    var game = f.Game(".minecraft");
    File.WriteAllText(Path.Combine(game, "save.dat"), "world");
    var before = Snapshot(f.Source);
    var plan = f.Scan();
    Equal(3, plan.SettingCount);
    Equal(3, plan.AppearanceFileCount);
    Equal(1, plan.GameFolderCount);
    Equal(1, plan.InstanceCount);
    Check(!Directory.Exists(f.Destination), "Preview created the destination.");
    var result = Pcl2Importer.Import(plan, f.Registry);
    Equal(3, result.CopiedFiles);
    Check(File.ReadAllText(f.Target("Setup.ini")).Contains("LaunchRamCustom:12"));
    Check(!File.ReadAllText(f.Target("Setup.ini")).Contains("LaunchArgumentInfo:PCL"));
    Equal("18", f.Registry.Read("ToolDownloadSpeed"));
    Check(f.Registry.Read("LaunchFolders")!.Contains(game));
    Check(!File.Exists(f.Target("Log1.txt")));
    Check(!Directory.Exists(Path.Combine(f.Destination, ".minecraft")), "Games were unexpectedly copied.");
    SameSnapshot(before, Snapshot(f.Source));
}

static void PreserveExisting() {
    using var f = new Fixture();
    f.SourceFile("Setup.ini", "LaunchArgumentTitle:incoming\nLaunchRamCustom:12\n");
    f.SourceFile("Logo.png", "incoming image");
    f.TargetFile("Setup.ini", "LaunchArgumentTitle:keep\nFutureUnknownKey:preserve\n");
    f.TargetFile("Logo.png", "keep image");
    f.SourceRegistry["ToolDownloadSpeed"] = "18";
    f.Registry.Write("ToolDownloadSpeed", "42");
    var game = f.Game(".minecraft");
    f.Registry.Write("LaunchFolders", "Already here>" + game + Path.DirectorySeparatorChar);
    var plan = f.Scan();
    Equal(1, plan.SettingCount);
    Equal(0, plan.GameFolderCount);
    Equal(0, plan.AppearanceFileCount);
    Pcl2Importer.Import(plan, f.Registry);
    var config = File.ReadAllText(f.Target("Setup.ini"));
    Check(config.Contains("LaunchArgumentTitle:keep") && config.Contains("FutureUnknownKey:preserve"));
    Equal("keep image", File.ReadAllText(f.Target("Logo.png")));
    Equal("42", f.Registry.Read("ToolDownloadSpeed"));
    Check(!f.Scan().HasChanges, "Repeat import was not idempotent.");
}

static void OverwriteAndBackup() {
    using var f = new Fixture();
    f.SourceFile("Setup.ini", "LaunchArgumentTitle:incoming\n");
    f.SourceFile("Logo.png", "incoming image");
    f.TargetFile("Setup.ini", "LaunchArgumentTitle:previous\nUnknown:preserve\n");
    f.TargetFile("Logo.png", "previous image");
    f.SourceRegistry["ToolDownloadSpeed"] = "18";
    f.Registry.Write("ToolDownloadSpeed", "42");
    var result = Pcl2Importer.Import(f.Scan(new Pcl2ImportOptions { Overwrite = true }), f.Registry);
    Equal("incoming image", File.ReadAllText(f.Target("Logo.png")));
    Equal("previous image", File.ReadAllText(Path.Combine(result.BackupFolder, "Files/Logo.png")));
    Check(File.ReadAllText(Path.Combine(result.BackupFolder, "Files/Setup.ini")).Contains("LaunchArgumentTitle:previous"));
    var registry = XDocument.Load(Path.Combine(result.BackupFolder, "Registry.xml"));
    Equal("42", registry.Root!.Elements("Entry").Single(e => (string?)e.Attribute("Key") == "ToolDownloadSpeed").Value);
    Equal("18", f.Registry.Read("ToolDownloadSpeed"));
}

static void FailureRollsBack() {
    using var f = new Fixture();
    f.SourceFile("Setup.ini", "LaunchArgumentTitle:incoming\n");
    f.SourceFile("Custom.xaml", "new homepage");
    f.SourceFile("Logo.png", "new image");
    f.TargetFile("Setup.ini", "LaunchArgumentTitle:previous\n");
    f.TargetFile("Logo.png", "previous image");
    f.Registry.Write("ToolDownloadSource", "0");
    f.SourceRegistry["ToolDownloadSource"] = "1";
    f.SourceRegistry["ToolDownloadSpeed"] = "18";
    var before = Snapshot(f.Destination);
    f.Registry.FailOnceOnKey = "ToolDownloadSpeed";
    Throws<IOException>(() => Pcl2Importer.Import(f.Scan(new Pcl2ImportOptions { Overwrite = true }), f.Registry));
    SameSnapshot(before, Snapshot(f.Destination));
    Equal("0", f.Registry.Read("ToolDownloadSource"));
    Equal(null, f.Registry.Read("ToolDownloadSpeed"));
    Check(Directory.EnumerateDirectories(f.Target("ImportBackups")).Any(), "Recovery backup is missing.");
}

static void CancellationRollsBack() {
    using var f = new Fixture();
    f.SourceFile("Logo.png", "new image");
    f.SourceFile("Custom.xaml", "new homepage");
    f.TargetFile("Logo.png", "previous image");
    var before = Snapshot(f.Destination);
    using var cancellation = new CancellationTokenSource();
    int reports = 0;
    var progress = new ImmediateProgress(_ => { if (++reports == 2) cancellation.Cancel(); });
    Throws<OperationCanceledException>(() => Pcl2Importer.Import(f.Scan(new Pcl2ImportOptions { Overwrite = true }), f.Registry, cancellation.Token, progress));
    Equal(2, reports);
    SameSnapshot(before, Snapshot(f.Destination));
    Check(!Directory.EnumerateFiles(f.Destination, "*.import-*", SearchOption.AllDirectories).Any());
}

static void ConcurrentEdits() {
    using var f = new Fixture();
    f.SourceFile("Setup.ini", "LaunchArgumentTitle:incoming\n");
    var plan = f.Scan();
    f.TargetFile("Setup.ini", "LaunchArgumentTitle:edited after preview\n");
    Throws<IOException>(() => Pcl2Importer.Import(plan, f.Registry));
    Check(File.ReadAllText(f.Target("Setup.ini")).Contains("edited after preview"));
    Check(!Directory.Exists(f.Target("ImportBackups")));
    f.SourceRegistry["ToolDownloadSpeed"] = "18";
    plan = f.Scan();
    f.Registry.Write("ToolDownloadSpeed", "42");
    Throws<IOException>(() => Pcl2Importer.Import(plan, f.Registry));
    Equal("42", f.Registry.Read("ToolDownloadSpeed"));
}

static void FilterSettings() {
    using var f = new Fixture();
    f.SourceFile("Setup.ini", "LoginMsJson:synthetic token\nIdentify:synthetic identity\nUiBackgroundColorful:invalid\nLaunchRamCustom:many\nUnknown:unknown\nLaunchArgumentTitle:valid\n");
    f.SourceRegistry["ToolDownloadSpeed"] = "NaN";
    f.SourceRegistry["LoginMsJson"] = "synthetic token";
    var plan = f.Scan();
    Equal(1, plan.SettingCount);
    Pcl2Importer.Import(plan, f.Registry);
    Equal("LaunchArgumentTitle:valid\r\n", File.ReadAllText(f.Target("Setup.ini")));
    Equal(null, f.Registry.Read("LoginMsJson"));
    Equal(null, f.Registry.Read("ToolDownloadSpeed"));
}

static void DataFolderAndSelfImport() {
    using var f = new Fixture();
    f.SourceFile("Setup.ini", "LaunchArgumentTitle:game\n");
    f.SourceFile("Logo.png", "image");
    var plan = Pcl2Importer.Scan(f.SourceData, f.Destination, f.SourceRegistry, Fixture.Definitions, f.Registry, new Pcl2ImportOptions());
    Equal(f.Source, plan.SourceFolder);
    Pcl2Importer.Import(plan, f.Registry);
    plan = Pcl2Importer.Scan(f.Source, f.SourceData, f.SourceRegistry, Fixture.Definitions, f.Registry,
        new Pcl2ImportOptions { GameFolders = false, Overwrite = true });
    Check(!plan.HasChanges);
    Pcl2Importer.Import(plan, f.Registry);
    Check(!Directory.Exists(Path.Combine(f.SourceData, "ImportBackups")));
}

static void RejectInvalidPaths() {
    using var f = new Fixture();
    var absent = Path.Combine(f.Source, "absent");
    Throws<DirectoryNotFoundException>(() => Pcl2Importer.Scan(absent, f.Destination, f.SourceRegistry, Fixture.Definitions, f.Registry, new Pcl2ImportOptions()));
    Directory.CreateDirectory(absent);
    Throws<InvalidDataException>(() => Pcl2Importer.Scan(absent, f.Destination, f.SourceRegistry, Fixture.Definitions, f.Registry, new Pcl2ImportOptions()));
    Throws<InvalidDataException>(() => Pcl2Importer.Scan(f.Source, Path.Combine(f.SourceData, "Pictures/nested"), f.SourceRegistry, Fixture.Definitions, f.Registry, new Pcl2ImportOptions()));
    Check(!Directory.Exists(f.Destination));
}

static void RejectLinks() {
    using var f = new Fixture();
    f.SourceFile("Logo.png", "image");
    Directory.CreateDirectory(f.Destination);
    try { Directory.CreateSymbolicLink(f.Target("ImportBackups"), f.SourceData); }
    catch (Exception ex) when (ex is UnauthorizedAccessException || ex is PlatformNotSupportedException || ex is IOException) {
        Console.WriteLine("  symbolic-link check skipped: OS does not permit creating links");
        return;
    }
    var before = Snapshot(f.Source);
    Throws<IOException>(() => Pcl2Importer.Import(f.Scan(), f.Registry));
    SameSnapshot(before, Snapshot(f.Source));
    Directory.Delete(f.Target("ImportBackups"));
    var external = Path.Combine(f.Root, "external");
    Directory.CreateDirectory(external);
    Directory.CreateSymbolicLink(Path.Combine(f.SourceData, "Pictures"), external);
    File.WriteAllText(Path.Combine(external, "do-not-import.png"), "external");
    Equal(1, f.Scan().AppearanceFileCount);
}

static void Categories() {
    using var f = new Fixture();
    f.SourceFile("Setup.ini", "LaunchArgumentTitle:game\n");
    f.SourceFile("Logo.png", "image");
    f.Game(".minecraft");
    f.SourceRegistry["LaunchFolders"] = "broken|missing>/missing/location";
    var plan = f.Scan(new Pcl2ImportOptions { Settings = false, Appearance = false });
    Equal(0, plan.SettingCount);
    Equal(0, plan.AppearanceFileCount);
    Equal(1, plan.GameFolderCount);
    Pcl2Importer.Import(plan, f.Registry);
    Check(!File.Exists(f.Target("Setup.ini")));
    Check(!File.Exists(f.Target("Logo.png")));
    Check(!f.Scan(new Pcl2ImportOptions { Settings = false, Appearance = false }).HasChanges);
}

static void DetectLatest() {
    using var f = new Fixture();
    var common = Path.Combine(f.Root, "Downloads");
    var older = Path.Combine(common, "PCL2-old");
    var newer = Path.Combine(common, "Minecraft", "PCL2-new");
    PutSetup(older, "LaunchArgumentTitle:old\n");
    PutSetup(newer, "LaunchArgumentTitle:new\n");
    File.SetLastWriteTimeUtc(Path.Combine(older, "PCL/Setup.ini"), DateTime.UtcNow.AddDays(-2));
    File.SetLastWriteTimeUtc(Path.Combine(newer, "PCL/Setup.ini"), DateTime.UtcNow.AddDays(-1));
    var detection = Pcl2Detector.Detect([common], f.Destination, f.SourceRegistry);
    Equal(newer, detection.SourceFolder);
    Equal(2, detection.InstallationCount);
    Check(!detection.RegistryOnly);
    Check(!Directory.Exists(f.Destination));
}

static void DetectFromRegistry() {
    using var f = new Fixture();
    f.SourceFile("Setup.ini", "LaunchArgumentTitle:game\n");
    var game = f.Game(".minecraft");
    f.SourceRegistry["LaunchFolders"] = "My games>" + game + Path.DirectorySeparatorChar;
    var detection = Pcl2Detector.Detect([], f.Destination, f.SourceRegistry);
    Equal(f.Source, detection.SourceFolder);
    Check(detection.RegistryDetected);
}

static void RegistryOnlyImport() {
    using var f = new Fixture();
    var game = f.Game(".minecraft");
    f.TargetFile("Setup.ini", "LaunchArgumentTitle:OpenLaunch only\n");
    f.SourceRegistry["LaunchFolders"] = "Original>" + game + Path.DirectorySeparatorChar;
    f.SourceRegistry["ToolDownloadSpeed"] = "18";
    var detection = Pcl2Detector.Detect([], f.Destination, f.SourceRegistry);
    Check(detection.RegistryOnly);
    var plan = Pcl2Importer.Scan(f.Root, f.Destination, f.SourceRegistry, Fixture.Definitions,
        f.Registry, new Pcl2ImportOptions(), registryOnly: true);
    Equal(1, plan.SettingCount);
    Equal(1, plan.GameFolderCount);
    Equal(0, plan.AppearanceFileCount);
    Pcl2Importer.Import(plan, f.Registry);
    Equal("18", f.Registry.Read("ToolDownloadSpeed"));
    Equal("LaunchArgumentTitle:OpenLaunch only\n", File.ReadAllText(f.Target("Setup.ini")));
    Check(f.Registry.Read("LaunchFolders")!.Contains(game));
}

static void DetectionExclusions() {
    using var f = new Fixture();
    f.TargetFile("Setup.ini", "LaunchArgumentTitle:own install\n");
    var instance = Path.Combine(f.Root, "Games/versions/1.21");
    PutSetup(instance, "VersionArgumentIndie:1\n");
    var unrelated = Path.Combine(f.Root, "unrelated");
    PutSetup(unrelated, "VersionRamCustom:8\n");
    var detection = Pcl2Detector.Detect([f.Root], f.Destination, f.SourceRegistry);
    Check(!detection.Found);
    Equal(0, detection.InstallationCount);
}

static void BoundedDetection() {
    using var f = new Fixture();
    for (int i = 0; i < 30; i++) Directory.CreateDirectory(Path.Combine(f.Root, "common", "directory" + i));
    var detection = Pcl2Detector.Detect([Path.Combine(f.Root, "common")], f.Destination, f.SourceRegistry, maxDirectories: 5);
    Check(detection.ScannedDirectories <= 5);
    detection = Pcl2Detector.Detect([Path.GetPathRoot(f.Root)!, @"\\unavailable-server\share"], f.Destination, f.SourceRegistry);
    Equal(0, detection.ScannedDirectories);
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    Throws<OperationCanceledException>(() => Pcl2Detector.Detect([f.Root], f.Destination, f.SourceRegistry, cancelled.Token));
}

static void AutomaticImport() {
    using var f = new Fixture();
    f.SourceFile("Setup.ini", "LaunchArgumentTitle:original game\n");
    f.SourceFile("Pictures/background.png", "background");
    f.Game(".minecraft");
    var before = Snapshot(f.Source);
    var detection = Pcl2Detector.Detect([f.Root], f.Destination, f.SourceRegistry);
    Check(detection.Found && !detection.RegistryOnly);
    var plan = Pcl2Importer.Scan(detection.SourceFolder!, f.Destination, f.SourceRegistry,
        Fixture.Definitions, f.Registry, new Pcl2ImportOptions());
    Pcl2Importer.Import(plan, f.Registry);
    Equal(1, plan.SettingCount);
    Equal(1, plan.GameFolderCount);
    Equal("background", File.ReadAllText(f.Target("Pictures/background.png")));
    SameSnapshot(before, Snapshot(f.Source));
}

static void PutSetup(string launcher, string content) {
    Directory.CreateDirectory(Path.Combine(launcher, "PCL"));
    File.WriteAllText(Path.Combine(launcher, "PCL/Setup.ini"), content);
}

static void RelativeGameSelection() {
    using var f = new Fixture();
    var game = f.Game("Games/nested");
    f.SourceFile("Setup.ini", "LaunchFolderSelect:$Games" + Path.DirectorySeparatorChar + "nested\n");
    var plan = f.Scan();
    Equal(1, plan.GameFolderCount);
    Pcl2Importer.Import(plan, f.Registry);
    Check(f.Registry.Read("LaunchFolders")!.Contains(game));
}

static Dictionary<string, string> Snapshot(string folder) => Directory.Exists(folder)
    ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
        .Where(f => !Path.GetRelativePath(folder, f).StartsWith("ImportBackups" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        .ToDictionary(f => Path.GetRelativePath(folder, f), f => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f))))
    : [];
static void SameSnapshot(Dictionary<string, string> expected, Dictionary<string, string> actual) {
    Equal(expected.Count, actual.Count);
    foreach (var entry in expected) Equal(entry.Value, actual.GetValueOrDefault(entry.Key));
}
static void Check(bool condition, string message = "Assertion failed.") { if (!condition) throw new Exception(message); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected '{expected}', got '{actual}'."); }
static void Throws<T>(Action action) where T : Exception {
    try { action(); } catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}

sealed class Fixture : IDisposable {
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "OpenLaunch-ImportTest-" + Guid.NewGuid().ToString("N"));
    public string Source => Path.Combine(Root, "source");
    public string SourceData => Path.Combine(Source, "PCL");
    public string Destination => Path.Combine(Root, "destination/PCL");
    public Dictionary<string, string> SourceRegistry { get; } = new(StringComparer.Ordinal);
    public MemoryRegistry Registry { get; } = new();
    public static readonly Pcl2ImportSetting[] Definitions = [
        new("LaunchArgumentTitle", typeof(string), false), new("LaunchArgumentInfo", typeof(string), false),
        new("LaunchRamCustom", typeof(int), false), new("UiBackgroundColorful", typeof(bool), false),
        new("ToolDownloadSource", typeof(int), true), new("ToolDownloadSpeed", typeof(int), true),
        new("LoginMsJson", typeof(string), false), new("Identify", typeof(string), false)
    ];
    public Fixture() { Directory.CreateDirectory(SourceData); }
    public string Target(string relative) => Path.Combine(Destination, relative);
    public void SourceFile(string relative, string text) => Put(Path.Combine(SourceData, relative), text);
    public void TargetFile(string relative, string text) => Put(Target(relative), text);
    public string Game(string relative) {
        string game = Path.GetFullPath(Path.Combine(Source, relative));
        Put(Path.Combine(game, "versions/1.21/1.21.json"), "{}");
        return game;
    }
    public Pcl2ImportPlan Scan(Pcl2ImportOptions? options = null) => Pcl2Importer.Scan(Source, Destination, SourceRegistry, Definitions, Registry, options ?? new Pcl2ImportOptions());
    public void Dispose() { Directory.Delete(Root, true); }
    private static void Put(string file, string text) { Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, text); }
}

sealed class MemoryRegistry : IPcl2ImportRegistry {
    private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);
    public string? FailOnceOnKey { get; set; }
    public string? Read(string key) => values.GetValueOrDefault(key);
    public void Write(string key, string? value) {
        if (key == FailOnceOnKey) { FailOnceOnKey = null; throw new IOException("Simulated registry failure."); }
        if (value == null) values.Remove(key); else values[key] = value;
    }
}

sealed class ImmediateProgress(Action<string> report) : IProgress<string> {
    public void Report(string value) => report(value);
}

using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml.Linq;

namespace OpenLaunchCore;

public interface IPcl2ImportRegistry {
    string? Read(string key);
    void Write(string key, string? value);
}

public sealed class Pcl2ImportSetting {
    public string Key { get; }
    public Type ValueType { get; }
    public bool InRegistry { get; }

    public Pcl2ImportSetting(string key, Type valueType, bool inRegistry) {
        Key = key;
        ValueType = valueType;
        InRegistry = inRegistry;
    }
}

public sealed class Pcl2ImportOptions {
    public bool Settings { get; set; } = true;
    public bool Appearance { get; set; } = true;
    public bool GameFolders { get; set; } = true;
    public bool Overwrite { get; set; }
}

public sealed class Pcl2ImportPlan {
    public string SourceFolder { get; internal set; } = "";
    public string DestinationFolder { get; internal set; } = "";
    public int SettingCount => FileSettings.Count + RegistrySettings.Count(r => r.Key != "LaunchFolders");
    public int AppearanceFileCount => Copies.Count;
    public int SkippedCount { get; internal set; }
    public int GameFolderCount { get; internal set; }
    public int InstanceCount { get; internal set; }
    public bool HasChanges => FileSettings.Count + RegistrySettings.Count + Copies.Count > 0;
    public IEnumerable<string> ImportedSettingKeys => FileSettings.Keys.Concat(RegistrySettings.Keys);
    internal bool Overwrite;
    internal string? SetupBefore;
    internal Dictionary<string, string> Setup = new(StringComparer.Ordinal);
    internal Dictionary<string, string> FileSettings = new(StringComparer.Ordinal);
    internal Dictionary<string, string> RegistrySettings = new(StringComparer.Ordinal);
    internal Dictionary<string, string?> RegistryBefore = new(StringComparer.Ordinal);
    internal List<KeyValuePair<string, string>> Copies = [];
}

public sealed class Pcl2ImportResult {
    public string BackupFolder { get; internal set; } = "";
    public int CopiedFiles { get; internal set; }
    public int SkippedFiles { get; internal set; }
}

/// <summary>
/// Imports only recognized preferences and appearance files. Game folders are registered
/// in place, so importing does not copy or change saves, mods, or instance settings.
/// This class has no WPF or Windows registry dependency; the UI supplies a registry adapter.
/// </summary>
public static class Pcl2Importer {
    private static readonly string[] AppearanceFiles = ["Custom.xaml", "Logo.png", "hints.txt"];
    private static readonly string[] AppearanceFolders = ["Pictures", "Musics", "Help"];

    // Account tokens, identity, sponsorship, usage counters and caches are not preferences.
    public static bool IsPreferenceKey(string key) =>
        key.StartsWith("Ui", StringComparison.Ordinal) || key.StartsWith("Tool", StringComparison.Ordinal) ||
        key.StartsWith("Launch", StringComparison.Ordinal) || key == "SystemSystemTelemetry";

    public static Pcl2ImportPlan Scan(string selectedFolder, string destinationDataFolder,
        IReadOnlyDictionary<string, string> sourceRegistry, IEnumerable<Pcl2ImportSetting> definitions,
        IPcl2ImportRegistry destinationRegistry, Pcl2ImportOptions options,
        CancellationToken cancellation = default, bool registryOnly = false) {
        string selected = FullPath(selectedFolder);
        if (!registryOnly && !Directory.Exists(selected)) throw new DirectoryNotFoundException("找不到所选的 PCL2 文件夹。");
        string data = Path.Combine(selected, "PCL");
        string launcher = selected;
        if (!registryOnly && !Directory.Exists(data)) {
            if (!Path.GetFileName(selected).Equals("PCL", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(Path.Combine(selected, "Setup.ini")))
                throw new InvalidDataException("请选择 PCL2 启动器所在文件夹，或其中包含 Setup.ini 的 PCL 数据文件夹。");
            data = selected;
            launcher = Directory.GetParent(selected)!.FullName;
        }
        if (!registryOnly) RejectLink(data);
        var plan = new Pcl2ImportPlan {
            SourceFolder = launcher,
            DestinationFolder = FullPath(destinationDataFolder),
            Overwrite = options.Overwrite
        };
        // Do not recursively import a destination nested in an appearance directory.
        foreach (string folder in AppearanceFolders)
            if (IsWithin(plan.DestinationFolder, Path.Combine(data, folder)))
                throw new InvalidDataException("OpenLaunch 数据文件夹不能位于待导入的个性化文件夹内。");
        plan.SetupBefore = ReadText(Path.Combine(plan.DestinationFolder, "Setup.ini"));
        plan.Setup = ReadIni(plan.SetupBefore);
        var source = ReadIni(registryOnly ? null : ReadText(Path.Combine(data, "Setup.ini")));

        if (options.Settings) {
            foreach (var definition in definitions) {
                cancellation.ThrowIfCancellationRequested();
                if (!IsPreferenceKey(definition.Key)) continue;
                // These keys are merged separately or belong to this launcher/session.
                if (definition.Key == "LaunchFolders" || definition.Key == "LaunchFolderSelect" ||
                    definition.Key == "WindowWidth" || definition.Key == "WindowHeight") continue;
                string? value;
                bool present = definition.InRegistry
                    ? sourceRegistry.TryGetValue(definition.Key, out value)
                    : source.TryGetValue(definition.Key, out value);
                if (!present) continue;
                string? before = definition.InRegistry ? destinationRegistry.Read(definition.Key)
                    : (plan.Setup.TryGetValue(definition.Key, out string? saved) ? saved : null);
                if (before == value || (!options.Overwrite && before != null)) { plan.SkippedCount++; continue; }
                // Retain OpenLaunch's launcher brand when PCL2 used its default game brand.
                if (definition.Key == "LaunchArgumentInfo" && (value == "PCL" || value == "PCL2" || value == "Plain Craft Launcher")) continue;
                if (!ValidValue(value!, definition.ValueType)) { plan.SkippedCount++; continue; }
                if (definition.InRegistry) {
                    plan.RegistrySettings[definition.Key] = value!;
                    plan.RegistryBefore[definition.Key] = before;
                } else {
                    plan.FileSettings[definition.Key] = value!;
                    plan.Setup[definition.Key] = value!;
                }
            }
        }

        if (options.Appearance && !registryOnly && !SamePath(data, plan.DestinationFolder)) {
            foreach (string name in AppearanceFiles) AddCopy(plan, Path.Combine(data, name), name);
            foreach (string folder in AppearanceFolders) {
                string directory = Path.Combine(data, folder);
                if (!Directory.Exists(directory)) continue;
                foreach (string file in WalkFiles(directory, cancellation))
                    AddCopy(plan, file, folder + Path.DirectorySeparatorChar + file.Substring(directory.Length + 1));
            }
        }

        if (options.GameFolders) {
            var candidates = new List<KeyValuePair<string, string>>();
            if (!registryOnly) {
                AddGameCandidate(candidates, launcher, "PCL2");
                foreach (string child in Directory.EnumerateDirectories(launcher)) {
                    cancellation.ThrowIfCancellationRequested();
                    AddGameCandidate(candidates, child, "PCL2 - " + Path.GetFileName(child));
                }
            }
            if (source.TryGetValue("LaunchFolderSelect", out string? selectedGame))
                AddGameCandidate(candidates, ResolveGamePath(selectedGame, launcher), "PCL2");
            if (sourceRegistry.TryGetValue("LaunchFolders", out string? folders)) {
                foreach (string entry in folders.Split('|')) {
                    int delimiter = entry.IndexOf('>');
                    if (delimiter <= 0) continue;
                    if (registryOnly && !Path.IsPathRooted(entry.Substring(delimiter + 1))) continue;
                    AddGameCandidate(candidates, ResolveGamePath(entry.Substring(delimiter + 1), launcher), entry.Substring(0, delimiter));
                }
            }
            string? original = destinationRegistry.Read("LaunchFolders");
            var merged = new List<string>((original ?? "").Split('|').Where(e => e.Length > 0));
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string entry in merged) {
                int delimiter = entry.IndexOf('>');
                if (delimiter >= 0) {
                    string? path = ResolveGamePath(entry.Substring(delimiter + 1), launcher);
                    if (path != null) known.Add(path);
                }
            }
            foreach (var folder in candidates) {
                cancellation.ThrowIfCancellationRequested();
                if (!known.Add(folder.Key)) continue;
                string name = new string(folder.Value.Where(c => c != '>' && c != '|' && !char.IsControl(c)).ToArray());
                if (string.IsNullOrWhiteSpace(name)) name = "PCL2";
                if (name.Length > 30) name = name.Substring(0, 30);
                merged.Add(name + ">" + folder.Key + Path.DirectorySeparatorChar);
                plan.GameFolderCount++;
                plan.InstanceCount += Directory.EnumerateDirectories(Path.Combine(folder.Key, "versions"))
                    .Count(d => File.Exists(Path.Combine(d, Path.GetFileName(d) + ".json")));
            }
            if (plan.GameFolderCount > 0) {
                plan.RegistrySettings["LaunchFolders"] = string.Join("|", merged);
                plan.RegistryBefore["LaunchFolders"] = original;
            }
        }
        return plan;
    }

    public static Pcl2ImportResult Import(Pcl2ImportPlan plan, IPcl2ImportRegistry registry,
        CancellationToken cancellation = default, IProgress<string>? progress = null) {
        var result = new Pcl2ImportResult();
        if (!plan.HasChanges) return result;
        cancellation.ThrowIfCancellationRequested();
        EnsureUnchanged(plan, registry);
        RejectLink(plan.DestinationFolder);
        string backup = Path.Combine(plan.DestinationFolder, "ImportBackups", "PCL2-" +
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        RejectLink(backup);
        Directory.CreateDirectory(backup);
        result.BackupFolder = backup;
        new XElement("Registry", plan.RegistryBefore.Select(p => new XElement("Entry",
            new XAttribute("Key", p.Key), new XAttribute("Exists", p.Value != null), p.Value))).Save(Path.Combine(backup, "Registry.xml"));
        File.WriteAllText(Path.Combine(backup, "README.txt"),
            "PCL2 import backup\r\nSource: " + plan.SourceFolder + "\r\nDestination: " + plan.DestinationFolder +
            "\r\nFiles contains previous destination files; Registry.xml contains previous destination registry values.\r\n", Encoding.UTF8);
        var changedFiles = new List<KeyValuePair<string, string?>>();
        var changedRegistry = new List<string>();
        try {
            foreach (var copy in plan.Copies) {
                cancellation.ThrowIfCancellationRequested();
                string target = Path.Combine(plan.DestinationFolder, copy.Value);
                if (File.Exists(target) && !plan.Overwrite) { result.SkippedFiles++; continue; }
                progress?.Report(copy.Value);
                SaveBackup(target, copy.Value, backup, changedFiles);
                WriteAtomic(target, copy.Key, null, cancellation);
                result.CopiedFiles++;
            }
            cancellation.ThrowIfCancellationRequested();
            // Preferences may have changed while large appearance files were being copied.
            EnsureUnchanged(plan, registry);
            if (plan.FileSettings.Count > 0) {
                string target = Path.Combine(plan.DestinationFolder, "Setup.ini");
                SaveBackup(target, "Setup.ini", backup, changedFiles);
                WriteAtomic(target, null, string.Join("\r\n", plan.Setup.Select(p => p.Key + ":" + p.Value)) + "\r\n", cancellation);
            }
            foreach (var pair in plan.RegistrySettings) {
                cancellation.ThrowIfCancellationRequested();
                changedRegistry.Add(pair.Key);
                registry.Write(pair.Key, pair.Value);
            }
            return result;
        } catch (Exception failure) {
            var failures = new List<Exception> { failure };
            foreach (string key in changedRegistry.AsEnumerable().Reverse()) {
                try { registry.Write(key, plan.RegistryBefore[key]); }
                catch (Exception ex) { failures.Add(ex); }
            }
            foreach (var entry in changedFiles.AsEnumerable().Reverse()) {
                try {
                    if (entry.Value == null) { if (File.Exists(entry.Key)) File.Delete(entry.Key); }
                    else File.Copy(entry.Value, entry.Key, true);
                } catch (Exception ex) { failures.Add(ex); }
            }
            if (failures.Count > 1) throw new AggregateException("导入失败，部分数据需要从备份恢复：" + backup, failures);
            throw;
        }
    }

    private static void EnsureUnchanged(Pcl2ImportPlan plan, IPcl2ImportRegistry registry) {
        if (plan.FileSettings.Count > 0 && ReadText(Path.Combine(plan.DestinationFolder, "Setup.ini")) != plan.SetupBefore)
            throw new IOException("OpenLaunch 设置已发生变化，请重新扫描后导入。");
        foreach (var entry in plan.RegistryBefore)
            if (registry.Read(entry.Key) != entry.Value)
                throw new IOException("OpenLaunch 设置已发生变化，请重新扫描后导入。");
    }

    private static void SaveBackup(string target, string relative, string backup,
        List<KeyValuePair<string, string?>> changed) {
        RejectLink(target);
        string? saved = null;
        if (File.Exists(target)) {
            saved = Path.Combine(backup, "Files", relative);
            Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
            File.Copy(target, saved, false);
        }
        changed.Add(new KeyValuePair<string, string?>(target, saved));
    }

    private static void WriteAtomic(string target, string? source, string? text, CancellationToken cancellation) {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary = target + ".import-" + Guid.NewGuid().ToString("N");
        try {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write)) {
                if (source != null) {
                    RejectLink(source);
                    using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var buffer = new byte[81920];
                    int length;
                    while ((length = input.Read(buffer, 0, buffer.Length)) > 0) {
                        cancellation.ThrowIfCancellationRequested();
                        output.Write(buffer, 0, length);
                    }
                } else {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(text!);
                    output.Write(bytes, 0, bytes.Length);
                }
            }
            cancellation.ThrowIfCancellationRequested();
            if (File.Exists(target)) File.Replace(temporary, target, null);
            else File.Move(temporary, target);
        } finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void AddCopy(Pcl2ImportPlan plan, string source, string relative) {
        if (!File.Exists(source)) return;
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) { plan.SkippedCount++; return; }
        if (File.Exists(Path.Combine(plan.DestinationFolder, relative)) && !plan.Overwrite) { plan.SkippedCount++; return; }
        plan.Copies.Add(new KeyValuePair<string, string>(source, relative));
    }

    private static IEnumerable<string> WalkFiles(string folder, CancellationToken cancellation) {
        cancellation.ThrowIfCancellationRequested();
        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) yield break;
        foreach (string file in Directory.EnumerateFiles(folder)) { cancellation.ThrowIfCancellationRequested(); yield return file; }
        foreach (string child in Directory.EnumerateDirectories(folder))
            foreach (string file in WalkFiles(child, cancellation)) yield return file;
    }

    private static Dictionary<string, string> ReadIni(string? content) {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in (content ?? "").Split('\n')) {
            string text = line.TrimEnd('\r');
            int separator = text.IndexOf(':');
            if (separator > 0) result[text.Substring(0, separator)] = text.Substring(separator + 1);
        }
        return result;
    }

    private static bool ValidValue(string value, Type type) {
        if (value.IndexOfAny(['\r', '\n', '\0']) >= 0) return false;
        try {
            if (type.IsEnum) Enum.Parse(type, value);
            else Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
            return true;
        } catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is ArgumentException || ex is InvalidCastException) { return false; }
    }

    private static string? ReadText(string file) => File.Exists(file) ? File.ReadAllText(file, Encoding.UTF8) : null;
    private static string FullPath(string path) {
        string full = Path.GetFullPath(path);
        string root = Path.GetPathRoot(full)!;
        return full.Length > root.Length ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
    }
    private static bool SamePath(string first, string second) => string.Equals(FullPath(first), FullPath(second), StringComparison.OrdinalIgnoreCase);
    private static bool IsWithin(string path, string parent) => SamePath(path, parent) ||
        FullPath(path).StartsWith(FullPath(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void RejectLink(string path) {
        for (string? current = path; current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("导入路径不能包含符号链接或目录联接：" + current);
    }

    private static string? ResolveGamePath(string? path, string launcher) {
        if (path == null || string.IsNullOrWhiteSpace(path) || path.IndexOfAny(['!', ';', '|', '>', '\r', '\n']) >= 0) return null;
        try {
            // PCL2 stores folders below its executable with a "$" base-directory prefix.
            if (path.StartsWith("$", StringComparison.Ordinal))
                path = Path.Combine(launcher, path.Substring(1).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return FullPath(Path.IsPathRooted(path) ? path : Path.Combine(launcher, path));
        }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) { return null; }
    }
    private static void AddGameCandidate(List<KeyValuePair<string, string>> folders, string? path, string name) {
        if (path == null || !Directory.Exists(Path.Combine(path, "versions"))) return;
        string? normalized = ResolveGamePath(path, path);
        if (normalized != null) folders.Add(new KeyValuePair<string, string>(normalized, name));
    }
}

using System.IO;
using System.Text;
using System.Threading;

namespace OpenLaunchCore;

public sealed class Pcl2DetectionResult {
    public string? SourceFolder { get; internal set; }
    public bool RegistryOnly => SourceFolder == null && RegistryDetected;
    public bool RegistryDetected { get; internal set; }
    public bool Found => SourceFolder != null || RegistryDetected;
    public int InstallationCount { get; internal set; }
    public int ScannedDirectories { get; internal set; }
}

/// <summary>Bounded discovery near known launcher locations. Never searches an entire drive.</summary>
public static class Pcl2Detector {
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase) {
        ".git", "node_modules", "versions", "libraries", "assets", "runtime", "resourcepacks",
        "mods", "saves", "ImportBackups", "Windows", "Program Files", "Program Files (x86)", "AppData"
    };

    public static Pcl2DetectionResult Detect(IEnumerable<string> searchRoots, string destinationDataFolder,
        IReadOnlyDictionary<string, string> sourceRegistry, CancellationToken cancellation = default,
        int maxDirectories = 1500, int maxDepth = 3) {
        if (maxDirectories < 1) throw new ArgumentOutOfRangeException(nameof(maxDirectories));
        if (maxDepth < 0) throw new ArgumentOutOfRangeException(nameof(maxDepth));
        var result = new Pcl2DetectionResult {
            RegistryDetected = sourceRegistry.Any(p => Pcl2Importer.IsPreferenceKey(p.Key) && !string.IsNullOrWhiteSpace(p.Value))
        };
        string ownRoot = Normalize(Directory.GetParent(Normalize(destinationDataFolder))!.FullName);
        var roots = new List<string>();
        // Registry game paths often sit next to the portable launcher, including on another drive.
        if (sourceRegistry.TryGetValue("LaunchFolders", out string? folders)) {
            foreach (string entry in folders.Split('|')) {
                int separator = entry.IndexOf('>');
                string path = separator < 0 ? "" : entry.Substring(separator + 1);
                if (!Path.IsPathRooted(path)) continue;
                try {
                    roots.Add(path);
                    var parent = Directory.GetParent(Normalize(path));
                    if (parent != null && parent.Parent != null) roots.Add(parent.FullName);
                } catch (Exception ex) when (IsPathError(ex)) { }
            }
        }
        roots.AddRange(searchRoots);
        var queue = new Queue<KeyValuePair<string, int>>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots) {
            try {
                if (string.IsNullOrWhiteSpace(root)) continue;
                if (root.StartsWith("\\\\", StringComparison.Ordinal) || root.StartsWith("//", StringComparison.Ordinal)) continue;
                string normalized = Normalize(root);
                // Explicit drive roots would turn a quick migration into a disk scan.
                if (normalized == Path.GetPathRoot(normalized) || Within(normalized, ownRoot)) continue;
                if (seen.Add(normalized) && Directory.Exists(normalized)) queue.Enqueue(new(normalized, 0));
            } catch (Exception ex) when (IsPathError(ex)) { }
        }
        var found = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        while (queue.Count > 0 && result.ScannedDirectories < maxDirectories) {
            cancellation.ThrowIfCancellationRequested();
            var next = queue.Dequeue();
            result.ScannedDirectories++;
            try {
                if ((File.GetAttributes(next.Key) & FileAttributes.ReparsePoint) != 0) continue;
                string launcher = next.Key;
                string setup = Path.Combine(launcher, "PCL", "Setup.ini");
                if (Path.GetFileName(launcher).Equals("PCL", StringComparison.OrdinalIgnoreCase)) {
                    setup = Path.Combine(launcher, "Setup.ini");
                    launcher = Directory.GetParent(launcher)!.FullName;
                }
                if (!Within(launcher, ownRoot) && IsLauncherSetup(setup)) found[launcher] = File.GetLastWriteTimeUtc(setup);
                if (next.Value >= maxDepth) continue;
                foreach (string child in Directory.EnumerateDirectories(next.Key)) {
                    cancellation.ThrowIfCancellationRequested();
                    if (seen.Count >= maxDirectories) break;
                    if (Excluded.Contains(Path.GetFileName(child)) || Within(child, ownRoot)) continue;
                    if (seen.Add(child)) queue.Enqueue(new(child, next.Value + 1));
                }
            } catch (Exception ex) when (IsPathError(ex)) { }
        }
        result.InstallationCount = found.Count;
        // The most recently used installation supplies preferences and appearance files.
        result.SourceFolder = found.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => p.Key).FirstOrDefault();
        return result;
    }

    private static bool IsLauncherSetup(string path) {
        if (!File.Exists(path)) return false;
        using var reader = new StreamReader(path, Encoding.UTF8, true);
        int characters = 0;
        string? line;
        while (characters < 65536 && (line = reader.ReadLine()) != null) {
            characters += line.Length;
            int separator = line.IndexOf(':');
            if (separator < 1) continue;
            string key = line.Substring(0, separator);
            if (Pcl2Importer.IsPreferenceKey(key) || key == "WindowWidth" || key == "WindowHeight") return true;
        }
        return false;
    }

    private static bool IsPathError(Exception ex) => ex is IOException || ex is UnauthorizedAccessException ||
        ex is ArgumentException || ex is NotSupportedException || ex is System.Security.SecurityException;
    private static string Normalize(string path) {
        string full = Path.GetFullPath(path);
        return full.Length > Path.GetPathRoot(full)!.Length ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
    }
    private static bool Within(string path, string parent) => string.Equals(path, parent, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}

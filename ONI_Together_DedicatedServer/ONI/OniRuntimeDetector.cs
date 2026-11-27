using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ONI_Together_DedicatedServer.Oni
{
    /// <summary>
    /// Locates a normal ONI installation / game runtime on disk. Does NOT provide a license;
    /// does NOT verify Klei DLC ownership; does NOT make the server headless-authoritative.
    /// Used to honestly report whether headless ONI simulation could ever be attempted.
    /// </summary>
    public sealed class OniRuntimeDetector
    {
        public string? Root { get; }
        public bool HasAssemblyCSharp { get; }
        public bool HasGameExecutable { get; }
        public string? GameExecutable { get; }
        public bool IsOniInstallation => HasAssemblyCSharp && HasGameExecutable;

        public OniRuntimeDetector(string? root)
        {
            Root = string.IsNullOrWhiteSpace(root) ? null : Path.GetFullPath(root);
            HasAssemblyCSharp = Root != null && File.Exists(Path.Combine(Root, "OxygenNotIncluded_Data", "Managed", "Assembly-CSharp.dll"))
                                || Root != null && File.Exists(Path.Combine(Root, "Assembly-CSharp.dll"));
            GameExecutable = FindGameExecutable(Root);
            HasGameExecutable = GameExecutable != null;
        }

        private static string? FindGameExecutable(string? root)
        {
            if (string.IsNullOrWhiteSpace(root)) return null;
            string[] names = { "OxygenNotIncluded.exe", "Oxygen Not Included.exe", "OxygenNotIncluded", "Oxygen Not Included" };
            foreach (var n in names)
            {
                try { string p = Path.Combine(root, n); if (File.Exists(p)) return p; } catch { }
            }
            return null;
        }

        /// <summary>Find ONI root from env (ONI_PATH/ONI_GAME_DIR/ONI_ROOT/ONI_GAME_ROOT) + common installs.</summary>
        public static OniRuntimeDetector Detect()
        {
            foreach (var candidate in CandidateRoots())
            {
                var d = new OniRuntimeDetector(candidate);
                if (d.IsOniInstallation) return d;
            }
            // Return best-effort env root even if incomplete, for reporting.
            return new OniRuntimeDetector(FirstEnv() ?? "");
        }

        public string Report()
        {
            if (Root == null || Root.Length == 0) return "ONI runtime not found (no ONI_PATH/ONI_GAME_DIR; no common install).";
            if (IsOniInstallation) return $"ONI runtime found at '{Root}'. NOTE: does not verify Klei ownership; server is not headless-authoritative.";
            return $"ONI root '{Root}' but Assembly-CSharp.dll={HasAssemblyCSharp}, executable={HasGameExecutable}.";
        }

        private static string? FirstEnv()
        {
            string?[] vars = { Env("ONI_PATH"), Env("ONI_GAME_DIR"), Env("ONI_ROOT"), Env("ONI_GAME_ROOT"), Env("ONI_HOME") };
            return vars.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        }

        private static string? Env(string name)
        {
            try { return Environment.GetEnvironmentVariable(name); } catch { return null; }
        }

        private static IEnumerable<string> CandidateRoots()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string? p, List<string> list)
            {
                if (string.IsNullOrWhiteSpace(p)) return;
                try { p = Path.GetFullPath(p); } catch { return; }
                if (seen.Add(p)) list.Add(p);
            }

            var list = new List<string>();
            Add(Env("ONI_PATH"), list);
            Add(Env("ONI_GAME_DIR"), list);
            Add(Env("ONI_ROOT"), list);
            Add(Env("ONI_GAME_ROOT"), list);
            Add(Env("ONI_HOME"), list);

            // ONI game libs folder: <root>/OxygenNotIncluded_Data/Managed  (e.g. Directory.Build.props GameLibsFolder).
            string? gameLibs = Env("ONI_GAME_LIBS_FOLDER") ?? Env("GAME_LIBS_FOLDER") ?? Env("ONI_MANAGED_DIR");
            AddGameLibsFolder(gameLibs, list);
            AddGameLibsFolder(ReadGameLibsFolderFromProps("Directory.Build.props.user"), list);
            AddGameLibsFolder(ReadGameLibsFolderFromProps("Directory.Build.props.default"), list);

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            // Steam common (Windows/Linux)
            Add(Path.Combine(programFilesX86, "Steam", "steamapps", "common", "OxygenNotIncluded"), list);
            Add(Path.Combine(programFiles, "Steam", "steamapps", "common", "OxygenNotIncluded"), list);
            Add(Path.Combine(home, ".steam", "steam", "steamapps", "common", "OxygenNotIncluded"), list);
            Add(Path.Combine(home, ".local", "share", "Steam", "steamapps", "common", "OxygenNotIncluded"), list);
            // Klei
            Add(Path.Combine(programFilesX86, "Klei", "Oxygen Not Included"), list);
            Add(Path.Combine(programFiles, "Klei", "Oxygen Not Included"), list);
            // macOS
            Add(Path.Combine(home, "Library", "Application Support", "Steam", "steamapps", "common", "OxygenNotIncluded"), list);

            return list;
        }

        private static void AddGameLibsFolder(string? libs, List<string> list)
        {
            if (string.IsNullOrWhiteSpace(libs)) return;
            try
            {
                string full = Path.GetFullPath(libs);
                list.Add(full);
                // <root>/OxygenNotIncluded_Data/Managed -> <root>
                string? managedParent = Path.GetDirectoryName(full);                 // OxygenNotIncluded_Data
                string? dataParent = managedParent == null ? null : Path.GetDirectoryName(managedParent); // <root>
                if (!string.IsNullOrWhiteSpace(managedParent) &&
                    Path.GetFileName(full).Equals("Managed", StringComparison.OrdinalIgnoreCase) &&
                    managedParent.EndsWith("OxygenNotIncluded_Data", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(dataParent))
                {
                    list.Add(dataParent);
                }
            }
            catch { /* ignore */ }
        }

        private static string? ReadGameLibsFolderFromProps(string fileName)
        {
            try
            {
                if (!File.Exists(fileName)) return null;
                string text = File.ReadAllText(fileName);
                var m = Regex.Match(text, "<GameLibsFolder>(.*?)</GameLibsFolder>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                return m.Success ? m.Groups[1].Value.Trim() : null;
            }
            catch { return null; }
        }
    }
}

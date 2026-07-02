// ModReady clean-room implementation. MIT, copyright 2026 Maxfield Management Group.
//
// SettingsProfileStore -- the file engine behind ModReady v2.0 framework
// primitive #4: "mod presets" (whole-loadout settings profiles).
//
// The existing per-mod preset layer (SettingsStorage.SavePreset/...) snapshots
// ONE mod's settings file. A *profile* snapshots EVERY mod's live settings at
// once into a single named bundle, so a player can keep "Hardcore", "Casual",
// and "Streaming" loadouts and switch the whole stack in one click.
//
// This class is deliberately MCM-agnostic: it is just "named snapshots of a
// directory of <id>.json files". The caller supplies:
//   liveRoot     -- the directory whose <id>.json files are the live config
//   profilesRoot -- where named profile bundles are stored
// MCM's ProfileManager passes the Global ModSettings dir + a _Profiles sibling;
// the off-engine self-test passes two temp dirs. Pure System.IO + System.Text,
// so it runs and is tested without the game.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using ModReady.Foundation;   // DiagLog

namespace ModReady.Framework
{
    public sealed class SettingsProfileStore
    {
        private const string Tag = "ModReady.SettingsProfileStore";
        // Manifest file dropped in each profile dir. Underscore-prefixed so it
        // never collides with a settings id and is easy to skip when listing.
        private const string ManifestName = "_profile.txt";

        private readonly string _liveRoot;
        private readonly string _profilesRoot;
        private readonly string _pattern;

        public SettingsProfileStore(string liveRoot, string profilesRoot, string filePattern = "*.json")
        {
            _liveRoot = liveRoot ?? throw new ArgumentNullException(nameof(liveRoot));
            _profilesRoot = profilesRoot ?? throw new ArgumentNullException(nameof(profilesRoot));
            _pattern = string.IsNullOrEmpty(filePattern) ? "*.json" : filePattern;
        }

        public string ProfilesRoot => _profilesRoot;

        /// <summary>Full path of a named profile's directory (not created here).</summary>
        public string ProfileDir(string profileName)
            => Path.Combine(_profilesRoot, Sanitize(profileName));

        /// <summary>True if a profile with this name exists on disk.</summary>
        public bool Exists(string profileName)
            => Directory.Exists(ProfileDir(profileName));

        /// <summary>Names of all stored profiles, alphabetical.</summary>
        public IReadOnlyList<string> List()
        {
            var list = new List<string>();
            try
            {
                if (!Directory.Exists(_profilesRoot)) return list;
                foreach (var d in Directory.GetDirectories(_profilesRoot))
                    list.Add(Path.GetFileName(d));
                list.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) { DiagLog.LogCaught(Tag, "List", ex); }
            return list;
        }

        /// <summary>
        /// Snapshot the live settings files into a named profile. When
        /// <paramref name="ids"/> is null, every file matching the pattern in
        /// liveRoot is captured; otherwise only "<id>.json" for each given id
        /// (missing ones are skipped). Overwrites an existing profile of the
        /// same name. Returns the ids actually captured.
        /// </summary>
        public IReadOnlyList<string> Capture(string profileName, IEnumerable<string>? ids = null)
        {
            var captured = new List<string>();
            try
            {
                var dir = ProfileDir(profileName);
                Directory.CreateDirectory(dir);

                // Clear any stale files from a previous capture of the same name
                // so a removed mod doesn't linger in the profile.
                foreach (var old in SafeFiles(dir, _pattern)) TryDelete(old);

                IEnumerable<string> sources;
                if (ids == null)
                {
                    sources = SafeFiles(_liveRoot, _pattern);
                }
                else
                {
                    sources = ids
                        .Where(i => !string.IsNullOrEmpty(i))
                        .Select(i => Path.Combine(_liveRoot, i + ".json"))
                        .Where(File.Exists);
                }

                foreach (var src in sources)
                {
                    var name = Path.GetFileName(src);
                    if (name.Equals(ManifestName, StringComparison.OrdinalIgnoreCase)) continue;
                    File.Copy(src, Path.Combine(dir, name), overwrite: true);
                    captured.Add(Path.GetFileNameWithoutExtension(name));
                }

                WriteManifest(dir, captured);
                DiagLog.Log(Tag, $"captured profile '{profileName}' ({captured.Count} settings file(s))");
            }
            catch (Exception ex) { DiagLog.LogCaught(Tag, $"Capture({profileName})", ex); }
            return captured;
        }

        /// <summary>
        /// Copy a profile's settings files back over the live config. Returns
        /// the ids applied. The caller is responsible for reloading in-memory
        /// settings instances afterward (the MCM ProfileManager does this).
        /// </summary>
        public IReadOnlyList<string> Apply(string profileName)
        {
            var applied = new List<string>();
            try
            {
                var dir = ProfileDir(profileName);
                if (!Directory.Exists(dir))
                {
                    DiagLog.Log(Tag, $"Apply: profile '{profileName}' not found at {dir}");
                    return applied;
                }
                Directory.CreateDirectory(_liveRoot);
                foreach (var src in SafeFiles(dir, _pattern))
                {
                    var name = Path.GetFileName(src);
                    if (name.Equals(ManifestName, StringComparison.OrdinalIgnoreCase)) continue;
                    File.Copy(src, Path.Combine(_liveRoot, name), overwrite: true);
                    applied.Add(Path.GetFileNameWithoutExtension(name));
                }
                DiagLog.Log(Tag, $"applied profile '{profileName}' ({applied.Count} settings file(s))");
            }
            catch (Exception ex) { DiagLog.LogCaught(Tag, $"Apply({profileName})", ex); }
            return applied;
        }

        /// <summary>The ids stored in a profile (filenames, manifest excluded).</summary>
        public IReadOnlyList<string> GetIds(string profileName)
        {
            var ids = new List<string>();
            try
            {
                var dir = ProfileDir(profileName);
                if (!Directory.Exists(dir)) return ids;
                foreach (var f in SafeFiles(dir, _pattern))
                {
                    var name = Path.GetFileName(f);
                    if (name.Equals(ManifestName, StringComparison.OrdinalIgnoreCase)) continue;
                    ids.Add(Path.GetFileNameWithoutExtension(name));
                }
                ids.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) { DiagLog.LogCaught(Tag, $"GetIds({profileName})", ex); }
            return ids;
        }

        /// <summary>Delete a profile bundle. Returns true if it existed.</summary>
        public bool Delete(string profileName)
        {
            try
            {
                var dir = ProfileDir(profileName);
                if (!Directory.Exists(dir)) return false;
                Directory.Delete(dir, recursive: true);
                DiagLog.Log(Tag, $"deleted profile '{profileName}'");
                return true;
            }
            catch (Exception ex) { DiagLog.LogCaught(Tag, $"Delete({profileName})", ex); return false; }
        }

        // ---- helpers ----

        private static IEnumerable<string> SafeFiles(string dir, string pattern)
        {
            try
            {
                if (!Directory.Exists(dir)) return Array.Empty<string>();
                return Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly);
            }
            catch { return Array.Empty<string>(); }
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch { }
        }

        private void WriteManifest(string dir, List<string> ids)
        {
            try
            {
                var sb = new StringBuilder();
                // No Date.Now in some sandboxes is fine here -- this runs on the
                // user's machine at capture time; a timestamp is informational.
                sb.AppendLine("# ModReady settings profile");
                sb.AppendLine($"# captured {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"# {ids.Count} settings file(s)");
                foreach (var id in ids) sb.AppendLine(id);
                File.WriteAllText(Path.Combine(dir, ManifestName), sb.ToString());
            }
            catch (Exception ex) { DiagLog.LogCaught(Tag, "WriteManifest", ex); }
        }

        private static string Sanitize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "Unnamed";
            var bad = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(raw.Length);
            bool lastUnderscore = false;
            foreach (var c in raw)
            {
                if (Array.IndexOf(bad, c) >= 0)
                {
                    if (!lastUnderscore) { sb.Append('_'); lastUnderscore = true; }
                }
                else { sb.Append(c); lastUnderscore = false; }
            }
            var s = sb.ToString().Trim('_', ' ', '.');
            return string.IsNullOrEmpty(s) ? "Unnamed" : s;
        }
    }
}

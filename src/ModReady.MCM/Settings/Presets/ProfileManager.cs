// ModReady clean-room implementation. MIT, copyright 2026 Maxfield Management Group.
//
// ProfileManager -- the MCM-integration half of ModReady v2.0 framework
// primitive #4 (whole-loadout settings profiles). The engine-free file work
// lives in ModReady.Framework.SettingsProfileStore (Foundation); this class
// ties it to MCM's live settings:
//
//   CaptureAll("Hardcore")  -> flush every registered GLOBAL settings object to
//                              its Global\<id>.json, then snapshot the whole set
//                              into the named profile.
//   Apply("Hardcore")       -> copy the profile's files back over Global\, then
//                              reload each live settings instance so the in-game
//                              values change immediately (no relaunch).
//
// Per-save / per-campaign settings are intentionally excluded: those are
// already save-scoped (they live under PerSave\<campaignId>\ etc.), so bundling
// them into a global profile would cross campaigns. Profiles are for the
// user-profile-level Global config a player wants to keep multiple loadouts of.
//
// Lives in MCMv5.dll but is exposed under the ModReady.Framework namespace so
// the entire v2.0 framework surface is one `using ModReady.Framework;` for
// consumers, regardless of which ModReady assembly a given primitive ships in.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using ModReady.Foundation;   // DiagLog

using MCM.Abstractions;
using MCM.Abstractions.Base.PerCampaign;
using MCM.Abstractions.Base.PerSave;
using MCM.Internal;          // SettingsRegistry, SettingsStorage

namespace ModReady.Framework
{
    public static class ProfileManager
    {
        private const string Tag = "ModReady.ProfileManager";

        /// <summary>Directory holding the live Global\&lt;id&gt;.json files.</summary>
        private static string GlobalDir()
            => Path.GetDirectoryName(SettingsStorage.ResolvePath("placeholder"))!;

        /// <summary>Sibling _Profiles directory under Configs\ModSettings\.</summary>
        private static string ProfilesDir()
            => Path.Combine(Path.GetDirectoryName(GlobalDir())!, "_Profiles");

        private static SettingsProfileStore Store()
            => new SettingsProfileStore(GlobalDir(), ProfilesDir());

        /// <summary>All saved profile names, alphabetical.</summary>
        public static IReadOnlyList<string> List()
        {
            try { return Store().List(); }
            catch (Exception ex) { DiagLog.LogCaught(Tag, "List", ex); return Array.Empty<string>(); }
        }

        /// <summary>The settings ids stored in a profile.</summary>
        public static IReadOnlyList<string> GetIds(string profileName)
        {
            try { return Store().GetIds(profileName); }
            catch (Exception ex) { DiagLog.LogCaught(Tag, $"GetIds({profileName})", ex); return Array.Empty<string>(); }
        }

        /// <summary>Delete a profile. Returns true if it existed.</summary>
        public static bool Delete(string profileName)
        {
            try { return Store().Delete(profileName); }
            catch (Exception ex) { DiagLog.LogCaught(Tag, $"Delete({profileName})", ex); return false; }
        }

        /// <summary>
        /// Snapshot every registered GLOBAL-scope settings object into a named
        /// profile. Returns the number of settings files captured.
        /// </summary>
        public static int CaptureAll(string profileName)
        {
            try
            {
                var ids = new List<string>();
                foreach (var rs in SettingsRegistry.All)
                {
                    if (rs?.Instance == null) continue;
                    if (IsScoped(rs.Instance)) continue;   // skip per-save/per-campaign
                    if (string.IsNullOrEmpty(rs.Id)) continue;
                    try
                    {
                        // Flush current in-memory values so the snapshot is fresh.
                        SettingsStorage.Save(rs.Instance, rs.Id);
                        ids.Add(rs.Id);
                    }
                    catch (Exception ex) { DiagLog.LogCaught(Tag, $"CaptureAll/flush({rs.Id})", ex); }
                }
                var captured = Store().Capture(profileName, ids);
                DiagLog.Log(Tag, $"CaptureAll('{profileName}'): {captured.Count} of {ids.Count} global settings snapshotted");
                return captured.Count;
            }
            catch (Exception ex) { DiagLog.LogCaught(Tag, $"CaptureAll({profileName})", ex); return 0; }
        }

        /// <summary>
        /// Apply a profile: copy its files over the live Global config, then
        /// reload each live settings instance so the change takes effect without
        /// a relaunch. Returns the number of settings files applied.
        /// </summary>
        public static int Apply(string profileName)
        {
            try
            {
                var store = Store();
                var applied = store.Apply(profileName);
                int reloaded = 0;
                foreach (var id in applied)
                {
                    var rs = SettingsRegistry.TryGet(id);
                    if (rs?.Instance == null) continue;   // file applied; instance not loaded this session
                    try
                    {
                        SettingsStorage.Load(rs.Instance, id);
                        reloaded++;
                    }
                    catch (Exception ex) { DiagLog.LogCaught(Tag, $"Apply/reload({id})", ex); }
                }
                DiagLog.Log(Tag, $"Apply('{profileName}'): {applied.Count} file(s) applied, {reloaded} live instance(s) reloaded");
                return applied.Count;
            }
            catch (Exception ex) { DiagLog.LogCaught(Tag, $"Apply({profileName})", ex); return 0; }
        }

        private static bool IsScoped(BaseSettings inst)
            => inst is BasePerSaveSettings || inst is BasePerCampaignSettings;
    }
}

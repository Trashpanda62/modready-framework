// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.
//
// JSON persistence for AttributeGlobalSettings. Settings files live under:
//   Documents\Mount and Blade II Bannerlord\Configs\ModSettings\Global\<Id>.json
//
// Internal namespace -- not part of the public API surface; AttributeGlobalSettings
// is the entry point consumer mods use.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

using BetaDeps.Foundation;

using MCM.Abstractions.Attributes;
using MCM.Abstractions.Events;
using MCM.Common;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCM.Internal;

internal static class SettingsStorage
{
    private const string Tag = "MCM.SettingsStorage";

    private static readonly DropdownConverter _dropdownConverter = new DropdownConverter();
    private static readonly JsonSerializer _serializer = MakeSerializer();

    // v0.7.5 ship-blocker: re-entrance / feedback-loop guard. XorberaxLegacy
    // hit a MissingMethodException that propagated into MCM's settings reset
    // flow; the reset triggered a Save, which triggered a PropertyChanged,
    // which triggered another Load, which triggered another Save, looping
    // indefinitely until the campaign-load step CTD'd.
    //
    // That loop is SYNCHRONOUS -- Save -> PropertyChanged -> Load -> Save all on
    // one call stack -- so a re-entrancy guard breaks it precisely: the inner
    // Save is skipped because the outer Save for the same id is still on the
    // stack. The earlier defense was a 100ms time-throttle, which ALSO blocked
    // legitimate back-to-back sequential calls (notably the Cancel reload right
    // after a Done save) and silently dropped them. The in-progress-set guard
    // below keeps the loop protection without the false positives.
    private static readonly object _recurseLock = new object();
    // Phase 1.1 fix: the old guard was a 100ms time-throttle on repeated Save/
    // Load per id. It also suppressed LEGITIMATE rapid sequential reloads -- the
    // Cancel reload right after a Done save, and the self-test's Save->Load round
    // trips -- so Cancel silently kept edits (selftest.log: "expected 2000, got
    // 2001"). Replaced with a re-entrancy guard: skip a Save/Load only while the
    // SAME id's Save/Load is already on the call stack (the real feedback-loop
    // shape), which still breaks loops but allows back-to-back sequential calls.
    private static readonly HashSet<string> _saveInProgress = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _loadInProgress = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _loopWarned = new(StringComparer.Ordinal);

    /// <summary>
    /// Marks a Save in progress for this id. Returns false if a Save for the
    /// SAME id is already on the stack (a re-entrant feedback loop) so the caller
    /// must skip; true to proceed. Every true MUST be paired with EndSave in a
    /// finally block.
    /// </summary>
    private static bool BeginSave(string settingsId)
    {
        lock (_recurseLock)
        {
            if (!_saveInProgress.Add(settingsId))
            {
                if (_loopWarned.Add("save:" + settingsId))
                    try { DiagLog.Log(Tag, $"GUARD: re-entrant Save('{settingsId}') skipped to break feedback loop. (logged once per id)"); } catch { }
                return false;
            }
            return true;
        }
    }
    private static void EndSave(string settingsId) { lock (_recurseLock) { _saveInProgress.Remove(settingsId); } }

    /// <summary>Re-entrancy guard for Load. See BeginSave. Pair with EndLoad.</summary>
    private static bool BeginLoad(string settingsId)
    {
        lock (_recurseLock)
        {
            if (!_loadInProgress.Add(settingsId))
            {
                if (_loopWarned.Add("load:" + settingsId))
                    try { DiagLog.Log(Tag, $"GUARD: re-entrant Load('{settingsId}') skipped to break feedback loop. (logged once per id)"); } catch { }
                return false;
            }
            return true;
        }
    }
    private static void EndLoad(string settingsId) { lock (_recurseLock) { _loadInProgress.Remove(settingsId); } }

    private static JsonSerializer MakeSerializer()
    {
        var s = new JsonSerializer
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            // Some consumer mods define dropdown-option types whose objects refer back
            // to themselves (BetterSmithingContinued.KeybindingDropdownOption, etc.).
            // Without Ignore, Newtonsoft throws and the property silently drops.
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        };
        s.Converters.Add(_dropdownConverter);
        return s;
    }

    public static string ResolvePath(string settingsId)
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var dir = Path.Combine(docs, "Mount and Blade II Bannerlord", "Configs", "ModSettings", "Global");
        return Path.Combine(dir, (settingsId ?? "Unnamed") + ".json");
    }

    // Phase 2.4 / finding H5 (2026-06-10 review): per-save and per-campaign
    // settings used to resolve to the GLOBAL path -- "per-save" flags bled
    // across every campaign on the install. Scope is now routed by the
    // instance's base type:
    //   Global       Configs\ModSettings\Global\<Id>.json                (unchanged)
    //   PerCampaign  Configs\ModSettings\PerCampaign\<campaignId>\<Id>.json
    //   PerSave      Configs\ModSettings\PerSave\<campaignId>\<Id>.json
    // Campaign id comes from Campaign.Current.UniqueGameId via reflection
    // (this project compiles against ReferenceAssemblies.Core only). Outside
    // a campaign the scoped folders fall back to "_NoCampaign" -- values
    // written there are intentionally sacrificial. NOTE: upstream per-save
    // truly persists inside the save FILE; campaign-id scoping fixes the
    // cross-campaign bleed without save-format surgery. Two manual saves in
    // one campaign share values -- documented limitation.
    public static string ResolvePathFor(object instance, string settingsId)
    {
        try
        {
            string? scope = null;
            if (instance is MCM.Abstractions.Base.PerSave.BasePerSaveSettings) scope = "PerSave";
            else if (instance is MCM.Abstractions.Base.PerCampaign.BasePerCampaignSettings) scope = "PerCampaign";
            if (scope == null) return ResolvePath(settingsId);

            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var dir = Path.Combine(docs, "Mount and Blade II Bannerlord", "Configs", "ModSettings",
                                   scope, GetCampaignIdOrFallback());
            return Path.Combine(dir, (settingsId ?? "Unnamed") + ".json");
        }
        catch
        {
            return ResolvePath(settingsId);
        }
    }

    /// <summary>Campaign.Current.UniqueGameId via reflection; "_NoCampaign"
    /// at the main menu / outside campaigns.</summary>
    private static string GetCampaignIdOrFallback()
    {
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!asm.GetName().Name.StartsWith("TaleWorlds.CampaignSystem", StringComparison.Ordinal))
                    continue;
                var campaignType = asm.GetType("TaleWorlds.CampaignSystem.Campaign");
                var current = campaignType?.GetProperty("Current",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null);
                if (current == null) return "_NoCampaign";
                var id = current.GetType().GetProperty("UniqueGameId",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)?.GetValue(current) as string;
                if (string.IsNullOrEmpty(id)) return "_NoCampaign";
                // Sanitize: campaign ids are alphanumeric today, but never
                // trust a string that becomes a folder name.
                var bad = Path.GetInvalidFileNameChars();
                var sb = new System.Text.StringBuilder(id!.Length);
                foreach (var c in id!) sb.Append(Array.IndexOf(bad, c) >= 0 ? '_' : c);
                return sb.ToString();
            }
        }
        catch { }
        return "_NoCampaign";
    }

    // ---------- Preset file-system layer (Suberfudge feature, v0.8.2) ----------
    //
    // Per-mod preset snapshots live at:
    //   Documents\Mount and Blade II Bannerlord\Configs\ModSettings\<SettingsId>\Presets\<PresetName>.json
    //
    // The file format is identical to Global\<SettingsId>.json — same JSON
    // schema, just renamed. "Load preset" copies a preset file's content into
    // the live Global file then re-runs Load(); "save preset" writes the
    // current live Global file out under a user-chosen name. This means
    // users can hand-author preset files outside the game (export from a
    // friend's install, hand-edit, version-control them, etc.) and they'll
    // be discovered automatically.

    /// <summary>
    /// Returns the directory holding preset JSON files for the given
    /// settings id. Does not create the directory; caller decides.
    /// </summary>
    public static string ResolvePresetsDirectory(string settingsId)
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(docs, "Mount and Blade II Bannerlord", "Configs", "ModSettings",
                            settingsId ?? "Unnamed", "Presets");
    }

    /// <summary>
    /// Returns the full path of a preset JSON file for the given settings id
    /// and preset name. Caller is responsible for any name sanitisation.
    /// </summary>
    public static string ResolvePresetPath(string settingsId, string presetName)
    {
        return Path.Combine(ResolvePresetsDirectory(settingsId),
                            SanitisePresetName(presetName) + ".json");
    }

    /// <summary>
    /// Enumerate all preset names available for the given settings id.
    /// Returns an empty list when the directory doesn't exist (the common
    /// first-run case). Names are returned without the .json extension.
    /// </summary>
    public static System.Collections.Generic.IReadOnlyList<string> EnumeratePresets(string settingsId)
    {
        var list = new System.Collections.Generic.List<string>();
        try
        {
            var dir = ResolvePresetsDirectory(settingsId);
            if (!Directory.Exists(dir)) return list;
            foreach (var f in Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
            {
                list.Add(Path.GetFileNameWithoutExtension(f));
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, $"EnumeratePresets({settingsId})", ex); }
        return list;
    }

    /// <summary>
    /// Write the current Global\&lt;id&gt;.json out to a preset file with
    /// the given name. Overwrites any existing preset of the same name.
    /// Returns true on success.
    /// </summary>
    public static bool SavePreset(string settingsId, string presetName)
    {
        try
        {
            var src = ResolvePath(settingsId);
            if (!File.Exists(src))
            {
                DiagLog.Log(Tag, $"SavePreset({settingsId},{presetName}): no live settings file at {src} -- nothing to snapshot");
                return false;
            }
            var dstDir = ResolvePresetsDirectory(settingsId);
            Directory.CreateDirectory(dstDir);
            var dst = ResolvePresetPath(settingsId, presetName);
            File.Copy(src, dst, overwrite: true);
            DiagLog.Log(Tag, $"SavePreset: wrote {dst}");
            return true;
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, $"SavePreset({settingsId},{presetName})", ex); return false; }
    }

    /// <summary>
    /// Replace the live Global\&lt;id&gt;.json with the contents of the named
    /// preset file. Returns true on success. Caller is expected to re-run
    /// Load() afterwards (or to recreate the settings singleton) so the
    /// in-memory state reflects the new values.
    /// </summary>
    public static bool LoadPresetIntoLiveFile(string settingsId, string presetName)
    {
        try
        {
            var src = ResolvePresetPath(settingsId, presetName);
            if (!File.Exists(src))
            {
                DiagLog.Log(Tag, $"LoadPreset({settingsId},{presetName}): preset file not found at {src}");
                return false;
            }
            var dst = ResolvePath(settingsId);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, overwrite: true);
            DiagLog.Log(Tag, $"LoadPreset: copied {src} -> {dst}");
            return true;
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, $"LoadPreset({settingsId},{presetName})", ex); return false; }
    }

    /// <summary>
    /// Delete a preset file. Returns true if the file was present and
    /// successfully removed.
    /// </summary>
    public static bool DeletePreset(string settingsId, string presetName)
    {
        try
        {
            var p = ResolvePresetPath(settingsId, presetName);
            if (!File.Exists(p)) return false;
            File.Delete(p);
            DiagLog.Log(Tag, $"DeletePreset: removed {p}");
            return true;
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, $"DeletePreset({settingsId},{presetName})", ex); return false; }
    }

    // Strip filesystem-unsafe characters from a user-supplied preset name so
    // we never write an invalid path. Replace with '_'; collapse runs.
    private static string SanitisePresetName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Unnamed";
        var bad = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(raw.Length);
        var lastUnderscore = false;
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

    public static void Load(object instance, string settingsId)
    {
        // Re-entrancy guard. See BeginLoad.
        var __id = settingsId ?? string.Empty;
        if (!BeginLoad(__id)) return;
        try
        {
            var path = ResolvePathFor(instance, __id); // H5: scope-aware
            if (!File.Exists(path))
            {
                // First run -- write defaults so the file exists for the next launch.
                Save(instance, __id);
                return;
            }
            // Phase 1.3 / finding H8: parse with recovery. A crash/power-loss
            // mid-write used to leave truncated JSON; the parse threw, this
            // method kept compiled defaults, and the next Done-click then
            // OVERWROTE the damaged file with defaults -- the user's settings
            // were unrecoverable. Now: damaged file is quarantined as
            // .corrupt-<timestamp>, the .bak (previous good save, rotated by
            // WriteAtomic) is restored when parseable, and only then do we
            // fall back to compiled defaults.
            var obj = ReadJsonWithRecovery(path, __id);
            if (obj == null)
                return; // unreadable + unrecoverable: keep compiled defaults; evidence preserved

            // Fluent-builder settings: keys are property ids, values are
            // scalars / objects. Write each into the fluent dictionary via
            // Set(); the FluentGlobalSettings.Get<T> path handles type
            // coercion when consumer mods read the value back.
            if (instance is IFluentSettings fluent) // 2.3/H6: any fluent scope
            {
                foreach (var kv in obj)
                {
                    try
                    {
                        var token = kv.Value;
                        if (token == null) continue;
                        object? value;
                        switch (token.Type)
                        {
                            case JTokenType.Boolean: value = token.Value<bool>(); break;
                            case JTokenType.Integer: value = token.Value<long>(); break;
                            case JTokenType.Float:   value = token.Value<double>(); break;
                            case JTokenType.String:  value = token.Value<string>() ?? string.Empty; break;
                            default:                 value = token.ToObject<object>(_serializer); break;
                        }
                        fluent.Set(kv.Key, value);
                    }
                    catch (Exception ex)
                    {
                        DiagLog.LogCaught(Tag, $"Load({settingsId}).{kv.Key} (fluent)", ex);
                    }
                }
                DiagLog.Log(Tag, $"loaded '{settingsId}' from {path} (fluent, {obj.Count} keys)");
                return;
            }

            foreach (var prop in EnumerateSettingProperties(instance.GetType()))
            {
                if (!obj.TryGetValue(prop.Name, out var token)) continue;
                try
                {
                    using (var jr = token.CreateReader())
                    {
                        var existing = prop.GetValue(instance);

                        // For Dropdown<T> / DropdownDefault<T> properties, ALWAYS route
                        // through DropdownConverter with the existing instance passed in
                        // as existingValue. Otherwise Newtonsoft's Deserialize constructs
                        // a fresh empty Dropdown<T>() with no items, overwriting the
                        // ctor-initialized one that has the mod's option list -- consumer
                        // mods like IDontCare then read SelectedValue from the now-empty
                        // dropdown, get null/default, and NRE on first use.
                        if (_dropdownConverter.CanConvert(prop.PropertyType))
                        {
                            var result = _dropdownConverter.ReadJson(jr, prop.PropertyType, existing, _serializer);
                            // If the ctor didn't populate the dropdown, take whatever the
                            // converter returned (might still be empty, but at least non-null).
                            if (existing == null && result != null && prop.CanWrite)
                                prop.SetValue(instance, result);
                            continue;
                        }

                        if (prop.CanWrite)
                        {
                            var value = _serializer.Deserialize(jr, prop.PropertyType);
                            if (value != null || !prop.PropertyType.IsValueType)
                                prop.SetValue(instance, value);
                        }
                        else
                        {
                            // Read-only non-Dropdown property -- populate in place.
                            if (existing == null) continue;
                            _serializer.Populate(jr, existing);
                        }
                    }
                }
                catch (Exception ex)
                {
                    DiagLog.LogCaught(Tag, $"Load({settingsId}).{prop.Name}", ex);
                }
            }
            DiagLog.Log(Tag, $"loaded '{settingsId}' from {path}");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"Load({settingsId})", ex);
        }
        finally { EndLoad(__id); }
    }

    public static void Save(object instance, string settingsId)
    {
        // Re-entrancy guard. See BeginSave.
        var __id = settingsId ?? string.Empty;
        if (!BeginSave(__id)) return;
        try
        {
            var path = ResolvePathFor(instance, __id); // H5: scope-aware
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            JObject jo;
            if (instance is IFluentSettings fluent) // 2.3/H6: any fluent scope
            {
                jo = BuildFluentJson(fluent);
            }
            else
            {
                jo = new JObject();
                foreach (var prop in EnumerateSettingProperties(instance.GetType()))
                {
                    try
                    {
                        var value = prop.GetValue(instance);
                        if (value == null) continue; // don't write nulls
                        jo[prop.Name] = JToken.FromObject(value, _serializer);
                    }
                    catch (Exception ex)
                    {
                        // Per-property failure must not break the whole save; just skip
                        // properties that can't round-trip cleanly (e.g. button-action
                        // callbacks whose delegate target serializes to garbage).
                        DiagLog.LogCaught(Tag, $"Save({settingsId}).{prop.Name}", ex);
                    }
                }
            }
            WriteAtomic(path, jo.ToString());
            DiagLog.Log(Tag, $"saved '{settingsId}' to {path}");
            // M15: this is the single choke-point every scope's Save() routes
            // through (global, per-save, per-campaign, fluent), so raising here
            // means consumers subscribed to SettingsEvents.SaveTriggered (CREST
            // syncs crest.json from it) actually get the signal -- it was never
            // raised before. RaiseSaveTriggered swallows its own exceptions.
            SettingsEvents.RaiseSaveTriggered(__id);
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"Save({settingsId})", ex);
        }
        finally { EndSave(__id); }
    }

    // Phase 1.3 / finding H8 -------------------------------------------------

    /// <summary>
    /// Crash-safe file write: content goes to a temp file first, then swaps
    /// into place. The previous file content survives as "<path>.bak", so a
    /// torn write can never destroy the last good save. File.Replace is the
    /// atomic path; the copy fallback covers exotic filesystems/AV locks
    /// where Replace throws.
    /// </summary>
    private static void WriteAtomic(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);

        if (!File.Exists(path))
        {
            File.Move(tmp, path);
            return;
        }

        try
        {
            File.Replace(tmp, path, path + ".bak");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"WriteAtomic: File.Replace({path}) -- using copy fallback", ex);
            try { File.Copy(path, path + ".bak", overwrite: true); } catch { }
            File.Copy(tmp, path, overwrite: true);
            try { File.Delete(tmp); } catch { }
        }
    }

    /// <summary>
    /// Read + parse a settings JSON. On parse failure: quarantine the damaged
    /// file (.corrupt-&lt;timestamp&gt;), then try the .bak; if the .bak parses
    /// it is restored as the live file. Returns null only when nothing
    /// readable exists -- caller keeps compiled defaults.
    /// </summary>
    private static JObject? ReadJsonWithRecovery(string path, string settingsId)
    {
        try
        {
            return JObject.Parse(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"ReadJsonWithRecovery: parse failed for {path}", ex);
        }

        // Preserve the evidence before anything can overwrite it.
        try
        {
            var quarantine = path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            if (!File.Exists(quarantine)) File.Copy(path, quarantine, overwrite: false);
            DiagLog.Log(Tag, $"  damaged file preserved as {quarantine}");
        }
        catch { }

        var bak = path + ".bak";
        try
        {
            if (File.Exists(bak))
            {
                var obj = JObject.Parse(File.ReadAllText(bak));
                try { File.Copy(bak, path, overwrite: true); } catch { }
                DiagLog.Log(Tag, $"  recovered '{settingsId}' from {bak} (previous good save)");
                return obj;
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"ReadJsonWithRecovery: .bak recovery for {path}", ex);
        }

        DiagLog.Log(Tag, $"  '{settingsId}': no usable .bak; using compiled defaults");
        return null;
    }

    /// <summary>Serialize a FluentGlobalSettings instance by walking its
    /// internal _values dictionary. Skips button-type properties (their
    /// FluentProperty.Value is an Action / nothing serializable).</summary>
    private static JObject BuildFluentJson(IFluentSettings fluent)
    {
        var jo = new JObject();
        foreach (var kv in fluent.ValuesSnapshot)
        {
            if (kv.Value == null) continue;
            try
            {
                jo[kv.Key] = JToken.FromObject(kv.Value, _serializer);
            }
            catch (Exception ex)
            {
                DiagLog.LogCaught(Tag, $"BuildFluentJson({fluent.Id}.{kv.Key})", ex);
            }
        }
        return jo;
    }

    /// <summary>
    /// Yields the [SettingProperty*] properties of a settings type that should
    /// be serialized. Filters out [SettingPropertyButton] because button props
    /// are Action callbacks, not stored state -- their delegate target has
    /// fields containing method pointers (IntPtr) which Newtonsoft can't
    /// round-trip via the default ISerializable contract.
    /// </summary>
    public static IEnumerable<PropertyInfo> EnumerateSettingProperties(Type t)
    {
        if (t == null) yield break;
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var anySetting = false;
            var isButton = false;
            foreach (var a in p.GetCustomAttributes(inherit: true))
            {
                var an = a.GetType().FullName ?? string.Empty;
                if (an.StartsWith("MCM.Abstractions.Attributes.") && an.Contains("SettingProperty"))
                    anySetting = true;
                if (an.EndsWith(".SettingPropertyButtonAttribute"))
                    isButton = true;
            }
            if (!anySetting) continue;
            if (isButton) continue; // buttons are callbacks, not data
            yield return p;
        }
    }
}

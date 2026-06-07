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
            var path = ResolvePath(settingsId);
            if (!File.Exists(path))
            {
                // First run -- write defaults so the file exists for the next launch.
                Save(instance, settingsId);
                return;
            }
            var json = File.ReadAllText(path);
            var obj = JObject.Parse(json);

            // Fluent-builder settings: keys are property ids, values are
            // scalars / objects. Write each into the fluent dictionary via
            // Set(); the FluentGlobalSettings.Get<T> path handles type
            // coercion when consumer mods read the value back.
            if (instance is MCM.Abstractions.Base.Global.FluentGlobalSettings fluent)
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
            var path = ResolvePath(settingsId);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            JObject jo;
            if (instance is MCM.Abstractions.Base.Global.FluentGlobalSettings fluent)
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
            File.WriteAllText(path, jo.ToString());
            DiagLog.Log(Tag, $"saved '{settingsId}' to {path}");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"Save({settingsId})", ex);
        }
        finally { EndSave(__id); }
    }

    /// <summary>Serialize a FluentGlobalSettings instance by walking its
    /// internal _values dictionary. Skips button-type properties (their
    /// FluentProperty.Value is an Action / nothing serializable).</summary>
    private static JObject BuildFluentJson(MCM.Abstractions.Base.Global.FluentGlobalSettings fluent)
    {
        var jo = new JObject();
        foreach (var kv in fluent.Values)
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

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

    public static void Load(object instance, string settingsId)
    {
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
    }

    public static void Save(object instance, string settingsId)
    {
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

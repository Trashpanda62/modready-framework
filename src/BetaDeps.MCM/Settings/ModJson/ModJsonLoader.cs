// BetaDeps clean-room implementation. MIT, copyright 2026 Maxfield Management Group.
//
// ModJsonLoader -- BetaDeps v1.x "declarative settings" modder-layer feature.
// A mod author drops a `mod.json` (or `*.betadeps.json`) into their module
// folder and BetaDeps builds an MCM settings page from it at load — no C#, no
// AttributeGlobalSettings class, no fluent-builder calls. Tweak mods that just
// "change a number" need zero compiled code.
//
//   {
//     "id": "MyTweaks_v1",
//     "name": "My Tweaks",
//     "scope": "global",                // global | percampaign | persave
//     "groups": [
//       { "name": "Combat", "order": 0, "properties": [
//         { "id": "enable",  "name": "Enable",     "type": "bool",  "default": true, "hint": "..." },
//         { "id": "dmg",     "name": "Damage x",   "type": "int",   "min": 0, "max": 100, "default": 50 },
//         { "id": "speed",   "name": "Speed",      "type": "float", "min": 0.0, "max": 5.0, "default": 1.0 },
//         { "id": "tag",     "name": "Tag",        "type": "text",  "default": "hi" }
//       ]}
//     ]
//   }
//
// A flat top-level "properties" array (no "groups") is also accepted and lands
// in a single "General" group. The built settings register through the same
// fluent pipeline consumer mods already use (BaseSettingsBuilder.BuildAsGlobal),
// so persistence, the Mod Config UI, and presets all work unchanged.
//
// ModJsonParser.Parse is pure (JSON -> schema, no side effects); ModJsonLoader.
// Load also builds + registers the live settings.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using BetaDeps.Foundation;

using MCM.Abstractions.FluentBuilder;
using MCM.Abstractions.FluentBuilder.Models;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetaDeps.Framework
{
    /// <summary>Pure parser: mod.json text -> validated <see cref="ModJsonSchema"/>.</summary>
    public static class ModJsonParser
    {
        private static readonly HashSet<string> ValidTypes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bool", "int", "float", "text" };
        private static readonly HashSet<string> ValidScopes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "global", "percampaign", "persave" };

        /// <summary>
        /// Parse + validate mod.json text. Never throws; returns a result whose
        /// <c>Ok</c> is false (with <c>Errors</c> populated) on any problem.
        /// </summary>
        public static ModJsonResult Parse(string json)
        {
            var result = new ModJsonResult();
            JObject root;
            try
            {
                root = JObject.Parse(json ?? "");
            }
            catch (Exception ex)
            {
                result.Errors.Add($"invalid JSON: {ex.Message}");
                return result;
            }

            var schema = new ModJsonSchema
            {
                Id = (root.Value<string>("id") ?? "").Trim(),
                Scope = (root.Value<string>("scope") ?? "global").Trim(),
            };
            schema.Name = (root.Value<string>("name") ?? schema.Id).Trim();
            result.Id = schema.Id;

            if (string.IsNullOrEmpty(schema.Id))
                result.Errors.Add("missing required field: id");
            if (!ValidScopes.Contains(schema.Scope))
            {
                result.Warnings.Add($"unknown scope '{schema.Scope}', defaulting to global");
                schema.Scope = "global";
            }
            else
            {
                schema.Scope = schema.Scope.ToLowerInvariant();
            }

            // Groups: either an explicit "groups" array, or a flat top-level
            // "properties" array wrapped in a single group.
            var groupsToken = root["groups"] as JArray;
            if (groupsToken != null)
            {
                foreach (var gt in groupsToken)
                    ParseGroup(gt as JObject, schema, result);
            }
            if (root["properties"] is JArray flatProps)
            {
                var g = new ModJsonGroup { Name = root.Value<string>("group") ?? "General" };
                foreach (var pt in flatProps)
                    ParseProperty(pt as JObject, g, result);
                if (g.Properties.Count > 0) schema.Groups.Add(g);
            }

            // Duplicate-id guard across the whole schema (would collide in the
            // settings dictionary and silently shadow one another).
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in schema.Groups.SelectMany(g => g.Properties))
            {
                if (!seen.Add(p.Id))
                    result.Errors.Add($"duplicate property id: '{p.Id}'");
            }

            if (schema.Groups.Count == 0 || schema.Groups.All(g => g.Properties.Count == 0))
                result.Warnings.Add("no properties declared");

            schema.Groups.RemoveAll(g => g.Properties.Count == 0);
            result.Schema = schema;
            result.Ok = result.Errors.Count == 0;
            return result;
        }

        private static void ParseGroup(JObject? gt, ModJsonSchema schema, ModJsonResult result)
        {
            if (gt == null) return;
            var g = new ModJsonGroup
            {
                Name = gt.Value<string>("name") ?? "General",
                Order = gt.Value<int?>("order") ?? 0,
            };
            if (gt["properties"] is JArray props)
                foreach (var pt in props)
                    ParseProperty(pt as JObject, g, result);
            if (g.Properties.Count > 0) schema.Groups.Add(g);
        }

        private static void ParseProperty(JObject? pt, ModJsonGroup group, ModJsonResult result)
        {
            if (pt == null) return;
            var prop = new ModJsonProperty
            {
                Id = (pt.Value<string>("id") ?? "").Trim(),
                Type = (pt.Value<string>("type") ?? "").Trim().ToLowerInvariant(),
                Hint = pt.Value<string>("hint") ?? "",
                RequireRestart = pt.Value<bool?>("requireRestart") ?? false,
            };
            prop.Name = (pt.Value<string>("name") ?? prop.Id).Trim();

            if (string.IsNullOrEmpty(prop.Id)) { result.Errors.Add("property missing id"); return; }
            if (!ValidTypes.Contains(prop.Type))
            {
                result.Errors.Add($"property '{prop.Id}': unknown type '{prop.Type}' (expected bool|int|float|text)");
                return;
            }

            var defTok = pt["default"];
            switch (prop.Type)
            {
                case "bool":
                    prop.Default = defTok?.Type == JTokenType.Boolean ? defTok.Value<bool>() : (object)false;
                    break;
                case "int":
                    prop.Min = pt.Value<double?>("min") ?? 0;
                    prop.Max = pt.Value<double?>("max") ?? 100;
                    if (prop.Min > prop.Max) { result.Errors.Add($"property '{prop.Id}': min {prop.Min} > max {prop.Max}"); return; }
                    prop.Default = ClampInt(defTok != null ? defTok.Value<long>() : (long)prop.Min, prop, result);
                    break;
                case "float":
                    prop.Min = pt.Value<double?>("min") ?? 0;
                    prop.Max = pt.Value<double?>("max") ?? 1;
                    if (prop.Min > prop.Max) { result.Errors.Add($"property '{prop.Id}': min {prop.Min} > max {prop.Max}"); return; }
                    prop.Default = ClampFloat(defTok != null ? defTok.Value<double>() : prop.Min, prop, result);
                    break;
                case "text":
                    prop.Default = defTok?.Value<string>() ?? "";
                    break;
            }
            group.Properties.Add(prop);
        }

        private static int ClampInt(long v, ModJsonProperty p, ModJsonResult r)
        {
            if (v < p.Min || v > p.Max)
            {
                r.Warnings.Add($"property '{p.Id}': default {v} outside [{p.Min},{p.Max}] -- clamped");
                v = (long)Math.Max(p.Min, Math.Min(p.Max, v));
            }
            return (int)v;
        }

        private static float ClampFloat(double v, ModJsonProperty p, ModJsonResult r)
        {
            if (v < p.Min || v > p.Max)
            {
                r.Warnings.Add($"property '{p.Id}': default {v} outside [{p.Min},{p.Max}] -- clamped");
                v = Math.Max(p.Min, Math.Min(p.Max, v));
            }
            return (float)v;
        }
    }

    /// <summary>Builds + registers live MCM settings from a mod.json.</summary>
    public static class ModJsonLoader
    {
        private const string Tag = "BetaDeps.ModJson";

        /// <summary>Parse + build + register settings from mod.json text.</summary>
        public static ModJsonResult Load(string json)
        {
            var result = ModJsonParser.Parse(json);
            if (!result.Ok || result.Schema == null)
            {
                foreach (var e in result.Errors) DiagLog.Log(Tag, $"parse error: {e}");
                return result;
            }
            try
            {
                Build(result.Schema, result);
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Errors.Add($"build failed: {ex.Message}");
                DiagLog.LogCaught(Tag, $"Build({result.Schema.Id})", ex);
            }
            return result;
        }

        /// <summary>Read a mod.json file and load it.</summary>
        public static ModJsonResult LoadFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    var r = new ModJsonResult();
                    r.Errors.Add($"file not found: {path}");
                    return r;
                }
                return Load(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                var r = new ModJsonResult();
                r.Errors.Add($"read failed: {ex.Message}");
                DiagLog.LogCaught(Tag, $"LoadFile({path})", ex);
                return r;
            }
        }

        /// <summary>
        /// Scan each enabled module folder for `mod.json` / `*.betadeps.json`
        /// and load every one found. Returns the count successfully loaded.
        /// Intended to run once at MCM SubModule load.
        /// </summary>
        public static int DiscoverAndLoad()
        {
            int loaded = 0;
            try
            {
                var modulesRoot = TryGetModulesRoot();
                if (modulesRoot == null) { DiagLog.Log(Tag, "DiscoverAndLoad: no Modules root"); return 0; }
                foreach (var modDir in Directory.GetDirectories(modulesRoot))
                {
                    foreach (var file in EnumerateModJson(modDir))
                    {
                        var r = LoadFile(file);
                        if (r.Ok)
                        {
                            loaded++;
                            DiagLog.Log(Tag, $"loaded declarative settings '{r.Id}' from {Path.GetFileName(file)} ({Path.GetFileName(modDir)})");
                        }
                        else
                        {
                            DiagLog.Log(Tag, $"skipped {file}: {string.Join("; ", r.Errors)}");
                        }
                    }
                }
            }
            catch (Exception ex) { DiagLog.LogCaught(Tag, "DiscoverAndLoad", ex); }
            return loaded;
        }

        private static IEnumerable<string> EnumerateModJson(string modDir)
        {
            var list = new List<string>();
            try
            {
                var top = Path.Combine(modDir, "mod.json");
                if (File.Exists(top)) list.Add(top);
                foreach (var f in Directory.GetFiles(modDir, "*.betadeps.json", SearchOption.TopDirectoryOnly))
                    list.Add(f);
            }
            catch { }
            return list;
        }

        // Drive the fluent builder from the schema and register the result.
        private static void Build(ModJsonSchema schema, ModJsonResult result)
        {
            var builder = BaseSettingsBuilder.Create(schema.Id, schema.Name);
            foreach (var g in schema.Groups)
            {
                builder.CreateGroup(g.Name, gb =>
                {
                    if (g.Order != 0) gb.SetGroupOrder(g.Order);
                    foreach (var p in g.Properties)
                        AddProperty(gb, p);
                });
            }

            MCM.Abstractions.BaseSettings settings = schema.Scope switch
            {
                "percampaign" => builder.BuildAsPerCampaign(),
                "persave" => builder.BuildAsPerSave(),
                _ => builder.BuildAsGlobal(),
            };

            result.Settings = settings;
            result.Ok = settings != null;
            if (settings != null)
            {
                try { result.SettingsFilePath = MCM.Internal.SettingsStorage.ResolvePathFor(settings, settings.Id); }
                catch { /* path resolution is best-effort, for inspection only */ }
                DiagLog.Log(Tag, $"built '{schema.Id}' ({schema.Scope}, {schema.Groups.Count} group(s))");
            }
            else
            {
                result.Errors.Add("build produced no settings instance");
                DiagLog.Log(Tag, $"build '{schema.Id}' produced null settings");
            }
        }

        private static void AddProperty(ISettingsPropertyGroupBuilder gb, ModJsonProperty p)
        {
            switch (p.Type)
            {
                case "bool":
                    gb.AddBool(p.Id, p.Name, Convert.ToBoolean(p.Default ?? false),
                        b => Configure(b, p));
                    break;
                case "int":
                    gb.AddInteger(p.Id, p.Name, (int)p.Min, (int)p.Max,
                        Convert.ToInt32(p.Default ?? p.Min, CultureInfo.InvariantCulture),
                        b => Configure(b, p));
                    break;
                case "float":
                    gb.AddFloatingInteger(p.Id, p.Name, (float)p.Min, (float)p.Max,
                        Convert.ToSingle(p.Default ?? p.Min, CultureInfo.InvariantCulture),
                        b => Configure(b, p));
                    break;
                case "text":
                    gb.AddText(p.Id, p.Name, Convert.ToString(p.Default ?? "", CultureInfo.InvariantCulture) ?? "",
                        b => Configure(b, p));
                    break;
            }
        }

        // All typed property builders expose SetHintText / SetRequireRestart via
        // ISettingsPropertyBuilder<TSelf>; this generic helper applies both.
        private static void Configure<T>(T builder, ModJsonProperty p)
            where T : ISettingsPropertyBuilder<T>
        {
            if (!string.IsNullOrEmpty(p.Hint)) builder.SetHintText(p.Hint);
            if (p.RequireRestart) builder.SetRequireRestart(true);
        }

        private static string? TryGetModulesRoot()
        {
            try
            {
                var asmPath = typeof(ModJsonLoader).Assembly.Location;
                var dir = Path.GetDirectoryName(asmPath);
                while (!string.IsNullOrEmpty(dir))
                {
                    if (string.Equals(Path.GetFileName(dir), "Modules", StringComparison.OrdinalIgnoreCase))
                        return dir;
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch { }
            return null;
        }
    }
}

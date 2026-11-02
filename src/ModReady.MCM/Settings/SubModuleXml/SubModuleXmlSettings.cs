// ModReady clean-room implementation. MIT, copyright 2026 Maxfield Management Group.
//
// SubModuleXmlSettings -- MCM v5.13 "settings declared in SubModule.xml".
// Implemented from the public MCM documentation (mcmv5-submodule-xml-settings):
// a mod puts <Settings> blocks directly under <Module> in its SubModule.xml and
// MCM shows them on the options screen. Each property ships a Default; when the
// user changes it, the new value is written back into the same file as a Value
// attribute next to it, leaving the rest of the file untouched. The mod reads
// Value when present and Default otherwise.
//
//   <Settings Id="MyMod" DisplayName="My Mod">
//     <Group Name="General" Order="1">
//       <Bool Id="Enabled" DisplayName="Enabled" Default="true" IsToggle="true" />
//       <Integer Id="Retries" MinValue="0" MaxValue="10" Default="3" />
//       <FloatingInteger Id="Scale" MinValue="0.5" MaxValue="2" Default="1" ValueFormat="0.00" />
//       <Text Id="Prefix" Default="[Mod]" />
//       <Dropdown Id="Quality" Default="1"><Option Value="Low" /><Option Value="High" /></Dropdown>
//       <Group Name="Advanced"> ...nested = sub group... </Group>
//     </Group>
//     <Preset Id="quiet" DisplayName="Quiet"><Property Id="Quality" Value="0" /></Preset>
//   </Settings>
//
// Properties directly under <Settings> go to the "Misc" group. The built
// settings register through the same fluent pipeline consumer mods use, with
// IRefs that read and write the Value attributes, so nothing goes to
// Configs/ModSettings for these settings.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using MCM.Abstractions.FluentBuilder;
using MCM.Common;
using ModReady.Foundation;

namespace ModReady.Framework
{
    public static class SubModuleXmlSettings
    {
        private const string Tag = "MCM.SubModuleXml";

        private static readonly HashSet<string> RegisteredIds = new(StringComparer.Ordinal);

        /// <summary>
        /// Scan the SubModule.xml of every module in this session for &lt;Settings&gt;
        /// blocks and register each as Global settings. Returns the count registered.
        /// </summary>
        public static int DiscoverAndLoad()
        {
            int loaded = 0;
            foreach (var (moduleId, moduleName, xmlPath) in EnumerateSessionModules())
            {
                try { loaded += LoadFile(xmlPath, moduleId, moduleName); }
                catch (Exception ex) { DiagLog.LogCaught(Tag, $"LoadFile({moduleId})", ex); }
            }
            return loaded;
        }

        /// <summary>Register every &lt;Settings&gt; block found in one SubModule.xml.</summary>
        public static int LoadFile(string xmlPath, string moduleId, string moduleName)
        {
            if (!File.Exists(xmlPath)) return 0;
            var store = new XmlStore(xmlPath);
            var module = store.Document.DocumentElement;
            if (module == null) return 0;

            int loaded = 0;
            foreach (var settings in ChildElements(module, "Settings"))
            {
                var id = Attr(settings, "Id", moduleId);
                if (string.IsNullOrEmpty(id)) continue;
                if (!RegisteredIds.Add(id))
                {
                    DiagLog.Log(Tag, $"WARNING: settings id '{id}' declared more than once ({moduleId}); later block ignored");
                    continue;
                }
                if (Build(store, settings, id, Attr(settings, "DisplayName", moduleName)))
                {
                    loaded++;
                    DiagLog.Log(Tag, $"registered SubModule.xml settings '{id}' ({moduleId})");
                }
            }
            return loaded;
        }

        private static bool Build(XmlStore store, XmlElement settings, string id, string displayName)
        {
            var builder = BaseSettingsBuilder.Create(id, displayName);
            var delimiter = Attr(settings, "SubGroupDelimiter", "/");
            if (delimiter != "/") builder.SetSubGroupDelimiter(delimiter);

            // Properties placed directly under <Settings> go to the Misc group.
            var loose = ChildElements(settings, null).Where(IsProperty).ToList();
            if (loose.Count > 0)
                builder.CreateGroup("Misc", gb => { foreach (var p in loose) AddProperty(store, gb, p); });

            foreach (var group in ChildElements(settings, "Group"))
                AddGroup(store, builder, group, string.Empty, delimiter);

            foreach (var preset in ChildElements(settings, "Preset"))
                AddPreset(builder, preset);

            var built = builder.BuildAsGlobal();
            if (built == null) { DiagLog.Log(Tag, $"build '{id}' produced null settings"); return false; }
            built.Register();
            return true;
        }

        private static void AddGroup(XmlStore store, ISettingsBuilder builder, XmlElement group, string parent, string delimiter)
        {
            var name = Attr(group, "Name", "Misc");
            var fullName = string.IsNullOrEmpty(parent) ? name : parent + delimiter + name;
            var order = ParseInt(Attr(group, "Order", "0"), 0);
            var props = ChildElements(group, null).Where(IsProperty).ToList();
            builder.CreateGroup(fullName, gb =>
            {
                if (order != 0) gb.SetGroupOrder(order);
                foreach (var p in props) AddProperty(store, gb, p);
            });
            foreach (var sub in ChildElements(group, "Group"))
                AddGroup(store, builder, sub, fullName, delimiter);
        }

        private static void AddProperty(XmlStore store, ISettingsPropertyGroupBuilder gb, XmlElement p)
        {
            var id = Attr(p, "Id", string.Empty);
            if (string.IsNullOrEmpty(id)) return;
            var name = Attr(p, "DisplayName", id);
            switch (p.LocalName)
            {
                case "Bool":
                {
                    var r = new ProxyRef<bool>(
                        () => ParseBool(store.Get(p), ParseBool(Attr(p, "Default", "false"), false)),
                        v => store.Set(p, v ? "true" : "false"));
                    if (ParseBool(Attr(p, "IsToggle", "false"), false))
                        gb.AddToggle(id, name, r, b => Configure(b, p));
                    else
                        gb.AddBool(id, name, r, b => Configure(b, p));
                    break;
                }
                case "Integer":
                {
                    var min = ParseDecimal(Attr(p, "MinValue", "0"), 0m);
                    var max = ParseDecimal(Attr(p, "MaxValue", "100"), 100m);
                    var def = ParseDecimal(Attr(p, "Default", null), min);
                    var r = new ProxyRef<int>(
                        () => (int)ParseDecimal(store.Get(p), def),
                        v => store.Set(p, v.ToString(CultureInfo.InvariantCulture)));
                    gb.AddInteger(id, name, (int)min, (int)max, r, b =>
                    {
                        Configure(b, p);
                        var fmt = Attr(p, "ValueFormat", null);
                        if (!string.IsNullOrEmpty(fmt)) b.AddValueFormat(fmt!);
                    });
                    break;
                }
                case "FloatingInteger":
                {
                    var min = ParseDecimal(Attr(p, "MinValue", "0"), 0m);
                    var max = ParseDecimal(Attr(p, "MaxValue", "1"), 1m);
                    var def = ParseDecimal(Attr(p, "Default", null), min);
                    var r = new ProxyRef<float>(
                        () => (float)ParseDecimal(store.Get(p), def),
                        v => store.Set(p, ((decimal)v).ToString(CultureInfo.InvariantCulture)));
                    gb.AddFloatingInteger(id, name, (float)min, (float)max, r, b =>
                    {
                        Configure(b, p);
                        var fmt = Attr(p, "ValueFormat", null);
                        if (!string.IsNullOrEmpty(fmt)) b.AddValueFormat(fmt!);
                    });
                    break;
                }
                case "Text":
                {
                    var def = Attr(p, "Default", string.Empty);
                    var r = new ProxyRef<string>(() => store.Get(p) ?? def, v => store.Set(p, v ?? string.Empty));
                    gb.AddText(id, name, r, b => Configure(b, p));
                    break;
                }
                case "Dropdown":
                {
                    var options = ChildElements(p, "Option").Select(o => Attr(o, "Value", string.Empty)).ToList();
                    var def = ParseInt(Attr(p, "Default", "0"), 0);
                    var selected = Clamp(ParseInt(store.Get(p), def), options.Count);
                    var dropdown = new Dropdown<string>(options, selected);
                    dropdown.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName == nameof(DropdownDefault<string>.SelectedIndex))
                            store.Set(p, dropdown.SelectedIndex.ToString(CultureInfo.InvariantCulture));
                    };
                    var r = new ProxyRef<Dropdown<string>>(() => dropdown, null);
                    gb.AddDropdown(id, name, selected, r, b => Configure(b, p));
                    break;
                }
            }
        }

        private static void AddPreset(ISettingsBuilder builder, XmlElement preset)
        {
            var id = Attr(preset, "Id", string.Empty);
            if (string.IsNullOrEmpty(id)) return;
            var values = ChildElements(preset, "Property")
                .Select(e => (Id: Attr(e, "Id", string.Empty), Value: Attr(e, "Value", string.Empty)))
                .Where(v => !string.IsNullOrEmpty(v.Id))
                .ToList();
            builder.CreatePreset(id, Attr(preset, "DisplayName", id), pb =>
            {
                foreach (var (pid, value) in values) pb.SetPropertyValue(pid, value);
            });
        }

        private static void Configure<T>(T builder, XmlElement p) where T : ISettingsPropertyBuilder<T>
        {
            var hint = Attr(p, "HintText", null);
            if (!string.IsNullOrEmpty(hint)) builder.SetHintText(hint!);
            var order = Attr(p, "Order", null);
            if (order != null) builder.SetOrder(ParseInt(order, 0));
            builder.SetRequireRestart(ParseBool(Attr(p, "RequireRestart", "true"), true));
        }

        // ---- module discovery -------------------------------------------------

        private static IEnumerable<(string Id, string Name, string XmlPath)> EnumerateSessionModules()
        {
            var list = new List<(string, string, string)>();
            try
            {
                foreach (var id in TaleWorlds.Engine.Utilities.GetModulesNames())
                {
                    var info = TaleWorlds.ModuleManager.ModuleHelper.GetModuleInfo(id);
                    if (info == null || string.IsNullOrEmpty(info.FolderPath)) continue;
                    list.Add((info.Id, info.Name, Path.Combine(info.FolderPath, "SubModule.xml")));
                }
            }
            catch (Exception ex) { DiagLog.LogCaught(Tag, "EnumerateSessionModules", ex); }
            return list;
        }

        // ---- XML helpers ------------------------------------------------------

        private static bool IsProperty(XmlElement e) =>
            e.LocalName is "Bool" or "Integer" or "FloatingInteger" or "Text" or "Dropdown";

        private static IEnumerable<XmlElement> ChildElements(XmlNode node, string? name) =>
            node.ChildNodes.OfType<XmlElement>().Where(e => name == null || e.LocalName == name);

        private static string Attr(XmlElement e, string name, string? fallback)
        {
            var a = e.Attributes[name];
            return a != null ? a.Value : fallback!;
        }

        private static bool ParseBool(string? s, bool fallback) =>
            bool.TryParse(s, out var b) ? b : fallback;

        private static int ParseInt(string? s, int fallback) =>
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : fallback;

        private static decimal ParseDecimal(string? s, decimal fallback) =>
            decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : fallback;

        private static int Clamp(int index, int count) => count == 0 ? 0 : Math.Max(0, Math.Min(index, count - 1));

        /// <summary>
        /// The SubModule.xml is the store. Loaded with PreserveWhitespace and written
        /// back in place so comments, formatting and mod-manager links survive. On a
        /// read-only module folder the change applies for this session only.
        /// </summary>
        private sealed class XmlStore
        {
            private readonly string _path;
            public XmlDocument Document { get; } = new() { PreserveWhitespace = true };

            public XmlStore(string path)
            {
                _path = path;
                Document.Load(path);
            }

            public string? Get(XmlElement property) =>
                property.Attributes["Value"]?.Value;

            public void Set(XmlElement property, string value)
            {
                if (property.GetAttribute("Value") == value) return;
                property.SetAttribute("Value", value);
                try
                {
                    var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false };
                    using var ms = new MemoryStream();
                    using (var w = XmlWriter.Create(ms, settings)) Document.Save(w);
                    using var fs = new FileStream(_path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                    fs.SetLength(0);
                    fs.Write(ms.ToArray(), 0, (int)ms.Length);
                }
                catch (Exception ex)
                {
                    DiagLog.LogCaught(Tag, $"save {Path.GetFileName(Path.GetDirectoryName(_path))}\\SubModule.xml (value kept for this session)", ex);
                }
            }
        }
    }
}

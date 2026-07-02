// ModReady clean-room implementation. MIT, copyright 2026 Maxfield Management Group.
//
// ModScaffolder -- ModReady v1.x modder-layer "new-mod" generator. Produces a
// ready-to-build starter mod that consumes ModReady, so an author isn't staring
// at a blank folder wondering which SubModule.xml fields the launcher needs.
//
// Three templates:
//   SettingsOnly -- no C# at all: a SubModule.xml + a declarative mod.json. The
//                   leanest "change a number" tweak mod. (Pairs with ModJsonLoader.)
//   HarmonyTweak -- SubModule.xml + csproj + a starter MBSubModuleBase that
//                   applies a Harmony patch via ModReady.Harmony's SafeBind and
//                   logs through DiagLog.
//   Full         -- HarmonyTweak plus an AttributeGlobalSettings class wired
//                   into the patch, the canonical "coded mod with settings".
//
// Engine-free (pure System.IO + string templates), so the whole generator is
// unit-tested off-engine: generate into a temp dir, assert the file set + key
// contents. A thin `modready new-mod` CLI can wrap Generate() later.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using ModReady.Foundation;   // DiagLog

namespace ModReady.Framework
{
    public enum ModTemplate { SettingsOnly, HarmonyTweak, Full }

    /// <summary>Inputs for <see cref="ModScaffolder.Generate"/>.</summary>
    public sealed class ScaffoldOptions
    {
        /// <summary>Module id + folder name (e.g. "MyFirstTweak"). Required.</summary>
        public string ModId { get; set; } = "";
        /// <summary>Human display name. Defaults to ModId.</summary>
        public string? ModName { get; set; }
        public string Author { get; set; } = "Unknown";
        public string Version { get; set; } = "v1.0.0";
        public ModTemplate Template { get; set; } = ModTemplate.SettingsOnly;
    }

    public static class ModScaffolder
    {
        private const string Tag = "ModReady.ModScaffolder";

        /// <summary>
        /// Generate a starter mod under <paramref name="targetRoot"/>\&lt;ModId&gt;\.
        /// Returns the list of files written (absolute paths). Throws
        /// ArgumentException on an invalid/empty ModId; otherwise best-effort
        /// (per-file failures are logged but don't abort the rest).
        /// </summary>
        public static IReadOnlyList<string> Generate(ScaffoldOptions opts, string targetRoot)
        {
            if (opts == null) throw new ArgumentNullException(nameof(opts));
            if (string.IsNullOrWhiteSpace(opts.ModId))
                throw new ArgumentException("ModId is required", nameof(opts));
            if (!IsValidIdentifier(opts.ModId))
                throw new ArgumentException($"ModId '{opts.ModId}' must be a valid identifier (letters, digits, underscore; not starting with a digit)", nameof(opts));

            var id = opts.ModId;
            var name = string.IsNullOrWhiteSpace(opts.ModName) ? id : opts.ModName!;
            var modDir = Path.Combine(targetRoot, id);
            var written = new List<string>();

            Directory.CreateDirectory(modDir);

            Write(written, Path.Combine(modDir, "SubModule.xml"), SubModuleXml(opts, id, name));
            Write(written, Path.Combine(modDir, "README.md"), Readme(opts, id, name));

            if (opts.Template == ModTemplate.SettingsOnly)
            {
                Write(written, Path.Combine(modDir, "mod.json"), SampleModJson(id, name));
            }
            else
            {
                var srcDir = Path.Combine(modDir, "src");
                Directory.CreateDirectory(srcDir);
                Write(written, Path.Combine(srcDir, id + ".csproj"), Csproj(id));
                Write(written, Path.Combine(srcDir, "SubModule.cs"), SubModuleCs(opts, id, name));
                if (opts.Template == ModTemplate.Full)
                    Write(written, Path.Combine(srcDir, "Settings.cs"), SettingsCs(id, name));
            }

            DiagLog.Log(Tag, $"scaffolded '{id}' ({opts.Template}) -> {written.Count} file(s) under {modDir}");
            return written;
        }

        private static void Write(List<string> written, string path, string content)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, content);
                written.Add(path);
            }
            catch (Exception ex) { DiagLog.LogCaught(Tag, $"write {path}", ex); }
        }

        internal static bool IsValidIdentifier(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (char.IsDigit(s[0])) return false;
            foreach (var c in s)
                if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
            return true;
        }

        // ---- templates ----

        private static string SubModuleXml(ScaffoldOptions o, string id, string name)
        {
            // Declares ModReady as a dependency so the launcher loads it first.
            // Code templates list the DLL SubModule; settings-only omits it.
            string subModulesBlock = o.Template == ModTemplate.SettingsOnly
                ? "  <SubModules />\n"
                : $@"  <SubModules>
    <SubModule>
      <Name value=""{name}"" />
      <DLLName value=""{id}.dll"" />
      <SubModuleClassType value=""{id}.SubModule"" />
      <Tags />
    </SubModule>
  </SubModules>
";
            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Module>
  <Id value=""{id}"" />
  <Name value=""{name}"" />
  <Version value=""{o.Version}"" />
  <DefaultModule value=""false"" />
  <ModuleCategory value=""Singleplayer"" />
  <ModuleType value=""Community"" />
  <DependedModules>
    <DependedModule Id=""ModReady"" />
  </DependedModules>
{subModulesBlock}</Module>
";
        }

        private static string SampleModJson(string id, string name) => $@"{{
  ""id"": ""{id}"",
  ""name"": ""{name}"",
  ""scope"": ""global"",
  ""groups"": [
    {{
      ""name"": ""General"",
      ""properties"": [
        {{ ""id"": ""enabled"", ""name"": ""Enabled"", ""type"": ""bool"", ""default"": true, ""hint"": ""Turn this mod on or off."" }},
        {{ ""id"": ""amount"", ""name"": ""Amount"", ""type"": ""int"", ""min"": 0, ""max"": 100, ""default"": 50, ""hint"": ""Tune the effect strength."" }}
      ]
    }}
  ]
}}
";

        private static string Csproj(string id) => $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <LangVersion>latest</LangVersion>
    <AssemblyName>{id}</AssemblyName>
    <RootNamespace>{id}</RootNamespace>
    <Platforms>x64</Platforms>
    <!-- The game already ships these; reference-only. -->
    <CopyLocalLockFileAssemblies>false</CopyLocalLockFileAssemblies>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Bannerlord.ReferenceAssemblies.Core"" Version=""1.4.5.115026"" PrivateAssets=""all"" />
    <PackageReference Include=""Lib.Harmony"" Version=""2.4.2"" PrivateAssets=""all"" />
    <!-- ModReady assemblies are provided at runtime by the ModReady module.
         Reference them locally for compile only (set HintPath to your install). -->
  </ItemGroup>
</Project>
";

        private static string SubModuleCs(ScaffoldOptions o, string id, string name)
        {
            string settingsLine = o.Template == ModTemplate.Full
                ? $"\n            // Read your settings anywhere: var on = {id}Settings.Instance.Enabled;"
                : "";
            return $@"// {name} -- generated by ModReady ModScaffolder.
using System;
using HarmonyLib;
using TaleWorlds.MountAndBlade;
using ModReady.Foundation;   // DiagLog
using ModReady.Harmony;       // SafeBind

namespace {id}
{{
    public class SubModule : MBSubModuleBase
    {{
        protected override void OnSubModuleLoad()
        {{
            base.OnSubModuleLoad();
            DiagLog.Log(""{id}"", ""OnSubModuleLoad"");{settingsLine}

            // Example: signature-safe Harmony patch via ModReady. SafeBind
            // no-ops (instead of crashing) if the engine method signature drifts.
            try
            {{
                var harmony = new Harmony(""{id}"");
                // var target = SafeBind.Method(typeof(SomeEngineType), ""SomeMethod"", parameterCount: 1);
                // if (target != null) SafeBind.TryPatch(harmony, target,
                //     prefix: new HarmonyMethod(typeof(SubModule), nameof(MyPrefix)));
            }}
            catch (Exception ex) {{ DiagLog.LogCaught(""{id}"", ""patch"", ex); }}
        }}
    }}
}}
";
        }

        private static string SettingsCs(string id, string name) => $@"// {name} settings -- generated by ModReady ModScaffolder.
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;

namespace {id}
{{
    public class {id}Settings : AttributeGlobalSettings<{id}Settings>
    {{
        public override string Id => ""{id}"";
        public override string DisplayName => ""{name}"";

        [SettingPropertyBool(""Enabled"", HintText = ""Turn this mod on or off."")]
        [SettingPropertyGroup(""General"")]
        public bool Enabled {{ get; set; }} = true;

        [SettingPropertyInteger(""Amount"", 0, 100, HintText = ""Tune the effect strength."")]
        [SettingPropertyGroup(""General"")]
        public int Amount {{ get; set; }} = 50;
    }}
}}
";

        private static string Readme(ScaffoldOptions o, string id, string name)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# {name}");
            sb.AppendLine();
            sb.AppendLine($"A Bannerlord mod scaffolded by ModReady ({o.Template}). Author: {o.Author}.");
            sb.AppendLine();
            sb.AppendLine("## Install (for testing)");
            sb.AppendLine($"Copy this `{id}` folder into your Bannerlord `Modules\\` folder, enable");
            sb.AppendLine("**ModReady** and this mod in the launcher, and launch.");
            sb.AppendLine();
            if (o.Template == ModTemplate.SettingsOnly)
            {
                sb.AppendLine("## Editing settings");
                sb.AppendLine("This mod has no code — edit `mod.json` to add/remove settings. ModReady");
                sb.AppendLine("builds the Mod Config page automatically. Types: bool, int, float, text.");
            }
            else
            {
                sb.AppendLine("## Building");
                sb.AppendLine($"`cd src` then `dotnet build -c Release`. Drop the built `{id}.dll` into");
                sb.AppendLine($"`{id}\\bin\\Win64_Shipping_Client\\`.");
            }
            return sb.ToString();
        }
    }
}

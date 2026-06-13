// BetaDeps v2.0 framework self-test harness. MIT, copyright 2026 Maxfield
// Management Group. NOT shipped -- dev verification only.
//
// Exercises the engine-free framework primitives with real assertions so a
// dev box can prove them correct without launching Bannerlord. Exit code 0 =
// all passed, 1 = at least one failure (so CI / Build gates can chain on it).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

using BetaDeps.Framework;

using HarmonyLib;

namespace BetaDeps.FrameworkSelfTest
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;
        private static readonly List<string> _failures = new();

        private static int Main()
        {
            Console.WriteLine("=== BetaDeps v2.0 framework self-test ===");
            try
            {
                EventBusTests.Run();
                ConflictDetectorTests.Run();
                PerfProfilerTests.Run();
                ProfileStoreTests.Run();
                ModJsonTests.Run();
                ModScaffolderTests.Run();
                NavalGateTests.Run();
            }
            catch (Exception ex)
            {
                Fail("UNCAUGHT", ex.ToString());
            }

            Console.WriteLine();
            Console.WriteLine($"PASSED: {_passed}   FAILED: {_failed}");
            if (_failed > 0)
            {
                Console.WriteLine("--- failures ---");
                foreach (var f in _failures) Console.WriteLine("  " + f);
                return 1;
            }
            Console.WriteLine("ALL GREEN");
            return 0;
        }

        // ---- tiny assertion kit (shared with the per-area test classes) ----
        internal static void Check(string name, bool cond, string? detail = null)
        {
            if (cond) { _passed++; Console.WriteLine($"  ok   {name}"); }
            else { Fail(name, detail ?? "condition was false"); }
        }
        internal static void Eq<T>(string name, T expected, T actual)
        {
            bool ok = EqualityComparer<T>.Default.Equals(expected, actual);
            if (ok) { _passed++; Console.WriteLine($"  ok   {name}"); }
            else { Fail(name, $"expected <{expected}> got <{actual}>"); }
        }
        private static void Fail(string name, string detail)
        {
            _failed++;
            _failures.Add($"{name}: {detail}");
            Console.WriteLine($"  FAIL {name}: {detail}");
        }
    }

    // ----------------------------------------------------------------------
    // Slice 1 -- EventBus
    // ----------------------------------------------------------------------
    internal static class EventBusTests
    {
        private sealed class Ping { public int N; }

        public static void Run()
        {
            Console.WriteLine("[EventBus]");
            EventBus.Reset();

            // typed publish/subscribe delivers exactly once
            int got = 0; int payload = -1;
            var sub = EventBus.Subscribe<Ping>(p => { got++; payload = p.N; });
            int fired = EventBus.Publish(new Ping { N = 42 });
            Program.Eq("typed: fired count", 1, fired);
            Program.Eq("typed: handler invoked", 1, got);
            Program.Eq("typed: payload carried", 42, payload);

            // dispose unsubscribes
            sub.Dispose();
            EventBus.Publish(new Ping { N = 7 });
            Program.Eq("typed: no delivery after dispose", 1, got);

            // double dispose is a no-op (must not throw)
            sub.Dispose();
            Program.Check("typed: double dispose safe", true);

            // named channel with no shared type
            string? seen = null;
            var nsub = EventBus.Subscribe("BetaDeps.Test.Chan", o => seen = o as string);
            EventBus.Publish("BetaDeps.Test.Chan", "hello");
            Program.Eq("named: payload delivered", "hello", seen);

            // null payload on a named channel -> handler sees String.Empty, never null
            object? nseen = "sentinel";
            using (EventBus.Subscribe("BetaDeps.Test.Null", o => nseen = o))
                EventBus.Publish("BetaDeps.Test.Null", null);
            Program.Check("named: null payload becomes empty string",
                (nseen as string) == string.Empty);
            nsub.Dispose();

            // a throwing handler is isolated: sibling still fires, publish returns
            EventBus.Reset();
            int sib = 0;
            using (EventBus.Subscribe<Ping>(_ => throw new InvalidOperationException("boom")))
            using (EventBus.Subscribe<Ping>(_ => sib++))
            {
                int f = EventBus.Publish(new Ping { N = 1 });
                Program.Eq("isolation: sibling still fired", 1, sib);
                Program.Eq("isolation: publish reports only successful deliveries", 1, f);
            }
            Program.Check("isolation: fault counter advanced", EventBus.HandlerFaultCount >= 1);

            // subscribe DURING dispatch must not corrupt the in-flight loop
            EventBus.Reset();
            int outer = 0; int inner = 0;
            IDisposable? innerSub = null;
            var d = EventBus.Subscribe<Ping>(_ =>
            {
                outer++;
                innerSub ??= EventBus.Subscribe<Ping>(__ => inner++);
            });
            EventBus.Publish(new Ping { N = 1 }); // outer fires, registers inner; inner must NOT fire this round
            Program.Eq("reentrancy: outer fired once", 1, outer);
            Program.Eq("reentrancy: inner not fired same round", 0, inner);
            EventBus.Publish(new Ping { N = 2 }); // now both fire
            Program.Eq("reentrancy: inner fires next round", 1, inner);
            d.Dispose(); innerSub?.Dispose();

            // throttle: second immediate delivery is suppressed
            EventBus.Reset();
            int tcount = 0;
            using (EventBus.Subscribe("BetaDeps.Test.Throttle", _ => tcount++, minIntervalMs: 1000))
            {
                EventBus.Publish("BetaDeps.Test.Throttle", "a");
                EventBus.Publish("BetaDeps.Test.Throttle", "b"); // within window -> dropped
                Program.Eq("throttle: only first delivered", 1, tcount);
            }

            // Unsubscribe-by-delegate
            EventBus.Reset();
            int u = 0;
            Action<Ping> handler = _ => u++;
            EventBus.Subscribe(handler);
            Program.Check("unsub: removed by delegate", EventBus.Unsubscribe(handler));
            EventBus.Publish(new Ping { N = 1 });
            Program.Eq("unsub: no delivery after removal", 0, u);
            Program.Eq("unsub: channel empties", 0, EventBus.SubscriptionCount);

            EventBus.Reset();
        }
    }

    // ----------------------------------------------------------------------
    // Slice 2 -- ModConflictDetector  (filled in when the slice lands)
    // ----------------------------------------------------------------------
    internal static class ConflictDetectorTests
    {
        // Two distinct "mods" (Harmony owner ids) patch the same target -> conflict.
        public static int TargetMedium(int x) => x * 2;
        public static int TargetHigh(int x) => x + 1;
        public static int TargetSolo(int x) => x - 1;

        public static void PrefixA() { }
        public static void PrefixB() { }
        public static void BetaDepsPrefix() { }
        public static IEnumerable<CodeInstruction> TranspileA(IEnumerable<CodeInstruction> i) => i;
        public static IEnumerable<CodeInstruction> TranspileB(IEnumerable<CodeInstruction> i) => i;

        public static void Run()
        {
            Console.WriteLine("[ModConflictDetector]");
            var t = typeof(ConflictDetectorTests);

            // Medium: two owners both prefix TargetMedium
            var a = new HarmonyLib.Harmony("Test.ModA");
            a.Patch(t.GetMethod(nameof(TargetMedium)),
                prefix: new HarmonyMethod(t.GetMethod(nameof(PrefixA))));
            var b = new HarmonyLib.Harmony("Test.ModB");
            b.Patch(t.GetMethod(nameof(TargetMedium)),
                prefix: new HarmonyMethod(t.GetMethod(nameof(PrefixB))));

            // High: two owners both transpile TargetHigh
            a.Patch(t.GetMethod(nameof(TargetHigh)),
                transpiler: new HarmonyMethod(t.GetMethod(nameof(TranspileA))));
            b.Patch(t.GetMethod(nameof(TargetHigh)),
                transpiler: new HarmonyMethod(t.GetMethod(nameof(TranspileB))));

            // Solo: one real owner + a BetaDeps owner -> must NOT count as a conflict
            a.Patch(t.GetMethod(nameof(TargetSolo)),
                prefix: new HarmonyMethod(t.GetMethod(nameof(PrefixA))));
            var bd = new HarmonyLib.Harmony("BetaDeps.Foundation.PatchShield");
            bd.Patch(t.GetMethod(nameof(TargetSolo)),
                prefix: new HarmonyMethod(t.GetMethod(nameof(BetaDepsPrefix))));

            var conflicts = ModConflictDetector.Scan();

            var med = conflicts.FirstOrDefaultByMethod(nameof(TargetMedium));
            Program.Check("conflict: TargetMedium detected", med != null);
            if (med != null)
            {
                Program.Eq("conflict: TargetMedium severity Medium", ConflictSeverity.Medium, med.Severity);
                Program.Eq("conflict: TargetMedium has 2 contributors", 2, med.Contributors.Count);
                Program.Check("conflict: owners named",
                    med.Contributors.Any(c => c.Owner == "Test.ModA")
                    && med.Contributors.Any(c => c.Owner == "Test.ModB"));
            }

            var high = conflicts.FirstOrDefaultByMethod(nameof(TargetHigh));
            Program.Check("conflict: TargetHigh detected", high != null);
            if (high != null)
                Program.Eq("conflict: TargetHigh severity High", ConflictSeverity.High, high.Severity);

            Program.Check("conflict: BetaDeps owner excluded (TargetSolo not reported)",
                conflicts.FirstOrDefaultByMethod(nameof(TargetSolo)) == null);

            // severity ordering: High sorts before Medium
            if (conflicts.Count >= 2)
                Program.Check("conflict: sorted most-severe first",
                    conflicts[0].Severity >= conflicts[conflicts.Count - 1].Severity);

            // report renders without throwing and mentions both severities
            var text = ModConflictDetector.ToText(conflicts);
            Program.Check("conflict: text report non-empty", text.Length > 0 && text.Contains("contested"));
            var md = ModConflictDetector.ToMarkdown(conflicts);
            Program.Check("conflict: markdown report has table", md.Contains("| Severity |"));

            // cleanup
            a.UnpatchAll("Test.ModA");
            b.UnpatchAll("Test.ModB");
            bd.UnpatchAll("BetaDeps.Foundation.PatchShield");
        }
    }

    internal static class ConflictExtensions
    {
        public static MethodConflict? FirstOrDefaultByMethod(this IReadOnlyList<MethodConflict> list, string method)
            => list.FirstOrDefault(c => c.TargetMethod == method);
    }

    // ----------------------------------------------------------------------
    // Music picker -- NavalGate (War Sails DLC detection)
    // ----------------------------------------------------------------------
    internal static class NavalGateTests
    {
        public static void Run()
        {
            Console.WriteLine("[NavalGate]");
            var baseDir = Path.Combine(Path.GetTempPath(), "bd-naval-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(baseDir);
            try
            {
                // No NavalDLC folder + no naval assembly loaded -> not available.
                Program.Check("naval: absent when no folder/assembly",
                    !BetaDeps.Harmony.Music.NavalGate.IsAvailable(baseDir));
                // Create the module folder -> available.
                Directory.CreateDirectory(Path.Combine(baseDir, "NavalDLC"));
                Program.Check("naval: detected via Modules\\NavalDLC folder",
                    BetaDeps.Harmony.Music.NavalGate.IsAvailable(baseDir));
                // Null modules root falls back to assembly scan (none here) -> false.
                Program.Check("naval: null root + no naval assembly -> false",
                    !BetaDeps.Harmony.Music.NavalGate.IsAvailable(null));

                // SettlementMusicManager.ClassifySettlement (pure mapping)
                var Town = BetaDeps.Harmony.Music.MusicContext.SettlementTown;
                var Village = BetaDeps.Harmony.Music.MusicContext.SettlementVillage;
                var Tavern = BetaDeps.Harmony.Music.MusicContext.SettlementTavern;
                Program.Check("settlement: town",
                    BetaDeps.Harmony.Music.SettlementMusicManager.ClassifySettlement(true, false, false) == Town);
                Program.Check("settlement: village",
                    BetaDeps.Harmony.Music.SettlementMusicManager.ClassifySettlement(false, true, false) == Village);
                Program.Check("settlement: tavern beats town",
                    BetaDeps.Harmony.Music.SettlementMusicManager.ClassifySettlement(true, false, true) == Tavern);
                Program.Check("settlement: village+tavern stays village",
                    BetaDeps.Harmony.Music.SettlementMusicManager.ClassifySettlement(false, true, true) == Village);
                Program.Check("settlement: neither -> null",
                    BetaDeps.Harmony.Music.SettlementMusicManager.ClassifySettlement(false, false, false) == null);
            }
            finally
            {
                try { Directory.Delete(baseDir, recursive: true); } catch { }
            }
        }
    }

    // ----------------------------------------------------------------------
    // Modder layer -- new-mod scaffolding (ModScaffolder)
    // ----------------------------------------------------------------------
    internal static class ModScaffolderTests
    {
        public static void Run()
        {
            Console.WriteLine("[ModScaffolder]");
            var baseDir = Path.Combine(Path.GetTempPath(), "bd-scaffold-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(baseDir);
            try
            {
                // SettingsOnly: no C#, just SubModule.xml + mod.json + README
                var so = ModScaffolder.Generate(
                    new ScaffoldOptions { ModId = "MyTweak", ModName = "My Tweak", Author = "Steve", Template = ModTemplate.SettingsOnly },
                    baseDir);
                var modDir = Path.Combine(baseDir, "MyTweak");
                Program.Check("scaffold: SubModule.xml written", File.Exists(Path.Combine(modDir, "SubModule.xml")));
                Program.Check("scaffold: mod.json written", File.Exists(Path.Combine(modDir, "mod.json")));
                Program.Check("scaffold: README written", File.Exists(Path.Combine(modDir, "README.md")));
                Program.Check("scaffold: settings-only has no src/", !Directory.Exists(Path.Combine(modDir, "src")));

                var subxml = File.ReadAllText(Path.Combine(modDir, "SubModule.xml"));
                Program.Check("scaffold: SubModule.xml declares BetaDeps dependency",
                    subxml.Contains("DependedModule Id=\"BetaDeps\"") && subxml.Contains("<Id value=\"MyTweak\""));
                Program.Check("scaffold: settings-only SubModule.xml has empty SubModules",
                    subxml.Contains("<SubModules />"));

                // The generated mod.json must be valid + parse via ModJsonParser
                var genJson = File.ReadAllText(Path.Combine(modDir, "mod.json"));
                var parsed = ModJsonParser.Parse(genJson);
                Program.Check("scaffold: generated mod.json parses clean", parsed.Ok && parsed.Schema!.Id == "MyTweak");

                // HarmonyTweak: code template
                ModScaffolder.Generate(
                    new ScaffoldOptions { ModId = "MyPatch", Template = ModTemplate.HarmonyTweak },
                    baseDir);
                var pDir = Path.Combine(baseDir, "MyPatch");
                Program.Check("scaffold: harmony csproj written", File.Exists(Path.Combine(pDir, "src", "MyPatch.csproj")));
                Program.Check("scaffold: harmony SubModule.cs written", File.Exists(Path.Combine(pDir, "src", "SubModule.cs")));
                Program.Check("scaffold: harmony has no Settings.cs", !File.Exists(Path.Combine(pDir, "src", "Settings.cs")));
                var cs = File.ReadAllText(Path.Combine(pDir, "src", "SubModule.cs"));
                Program.Check("scaffold: SubModule.cs derives MBSubModuleBase + right namespace",
                    cs.Contains("MBSubModuleBase") && cs.Contains("namespace MyPatch"));
                var csproj = File.ReadAllText(Path.Combine(pDir, "src", "MyPatch.csproj"));
                Program.Check("scaffold: csproj targets net472", csproj.Contains("net472"));

                // Full: adds Settings.cs
                ModScaffolder.Generate(
                    new ScaffoldOptions { ModId = "MyFull", Template = ModTemplate.Full },
                    baseDir);
                Program.Check("scaffold: full template adds Settings.cs",
                    File.Exists(Path.Combine(baseDir, "MyFull", "src", "Settings.cs")));
                var setcs = File.ReadAllText(Path.Combine(baseDir, "MyFull", "src", "Settings.cs"));
                Program.Check("scaffold: Settings.cs uses AttributeGlobalSettings",
                    setcs.Contains("AttributeGlobalSettings<MyFullSettings>"));

                // Invalid ids rejected
                Program.Check("scaffold: empty id throws", Throws(() =>
                    ModScaffolder.Generate(new ScaffoldOptions { ModId = "" }, baseDir)));
                Program.Check("scaffold: id starting with digit throws", Throws(() =>
                    ModScaffolder.Generate(new ScaffoldOptions { ModId = "9bad" }, baseDir)));
                Program.Check("scaffold: id with space throws", Throws(() =>
                    ModScaffolder.Generate(new ScaffoldOptions { ModId = "bad name" }, baseDir)));
            }
            finally
            {
                try { Directory.Delete(baseDir, recursive: true); } catch { }
            }
        }

        private static bool Throws(Action a)
        {
            try { a(); return false; } catch (ArgumentException) { return true; } catch { return false; }
        }
    }

    // ----------------------------------------------------------------------
    // Modder layer -- mod.json declarative settings (ModJsonParser/Loader)
    // ----------------------------------------------------------------------
    internal static class ModJsonTests
    {
        public static void Run()
        {
            Console.WriteLine("[ModJson declarative settings]");

            // --- parser: full valid schema with groups + all four types ---
            string full = @"{
              ""id"": ""MyTweaks_v1"", ""name"": ""My Tweaks"", ""scope"": ""global"",
              ""groups"": [{ ""name"": ""Combat"", ""order"": 2, ""properties"": [
                { ""id"": ""enable"", ""name"": ""Enable"", ""type"": ""bool"", ""default"": true, ""hint"": ""on/off"" },
                { ""id"": ""dmg"", ""name"": ""Damage"", ""type"": ""int"", ""min"": 0, ""max"": 100, ""default"": 50 },
                { ""id"": ""speed"", ""name"": ""Speed"", ""type"": ""float"", ""min"": 0.0, ""max"": 5.0, ""default"": 1.5 },
                { ""id"": ""tag"", ""name"": ""Tag"", ""type"": ""text"", ""default"": ""hi"" }
              ]}]
            }";
            var r = ModJsonParser.Parse(full);
            Program.Check("modjson: valid parse Ok", r.Ok);
            Program.Eq("modjson: id", "MyTweaks_v1", r.Schema?.Id);
            Program.Eq("modjson: name", "My Tweaks", r.Schema?.Name);
            Program.Eq("modjson: scope global", "global", r.Schema?.Scope);
            Program.Eq("modjson: one group", 1, r.Schema?.Groups.Count ?? 0);
            Program.Eq("modjson: group order", 2, r.Schema?.Groups[0].Order ?? -1);
            Program.Eq("modjson: four properties", 4, r.Schema?.Groups[0].Properties.Count ?? 0);
            var dmg = r.Schema!.Groups[0].Properties.First(p => p.Id == "dmg");
            Program.Check("modjson: int min/max/default", dmg.Min == 0 && dmg.Max == 100 && (int)dmg.Default! == 50);
            var en = r.Schema.Groups[0].Properties.First(p => p.Id == "enable");
            Program.Check("modjson: bool default + hint", (bool)en.Default! && en.Hint == "on/off");

            // --- flat properties (no groups) -> single General group ---
            string flat = @"{ ""id"":""Flat_v1"", ""properties"":[
                { ""id"":""x"", ""type"":""bool"", ""default"":false } ] }";
            var rf = ModJsonParser.Parse(flat);
            Program.Check("modjson: flat props wrapped in a group",
                rf.Ok && rf.Schema!.Groups.Count == 1 && rf.Schema.Groups[0].Properties.Count == 1);
            Program.Eq("modjson: name falls back to id", "Flat_v1", rf.Schema?.Name);

            // --- validation failures ---
            Program.Check("modjson: missing id fails",
                !ModJsonParser.Parse(@"{ ""name"":""x"", ""properties"":[] }").Ok);
            Program.Check("modjson: unknown type fails",
                !ModJsonParser.Parse(@"{ ""id"":""a"", ""properties"":[{""id"":""p"",""type"":""color""}] }").Ok);
            Program.Check("modjson: min>max fails",
                !ModJsonParser.Parse(@"{ ""id"":""a"", ""properties"":[{""id"":""p"",""type"":""int"",""min"":10,""max"":1}] }").Ok);
            Program.Check("modjson: malformed JSON fails",
                !ModJsonParser.Parse(@"{ not json").Ok);
            Program.Check("modjson: duplicate id fails",
                !ModJsonParser.Parse(@"{ ""id"":""a"", ""properties"":[{""id"":""p"",""type"":""bool""},{""id"":""p"",""type"":""bool""}] }").Ok);
            // regression: Parse must NOT throw on a type-mismatched default/min/max
            // (Newtonsoft Value<long>/<double> throw) -- it returns Ok=false instead.
            Program.Check("modjson: string default on float fails (no throw)",
                !ModJsonParser.Parse(@"{ ""id"":""a"", ""properties"":[{""id"":""p"",""type"":""float"",""default"":""abc""}] }").Ok);
            Program.Check("modjson: array default on int fails (no throw)",
                !ModJsonParser.Parse(@"{ ""id"":""a"", ""properties"":[{""id"":""p"",""type"":""int"",""default"":[1,2]}] }").Ok);
            Program.Check("modjson: object min fails (no throw)",
                !ModJsonParser.Parse(@"{ ""id"":""a"", ""properties"":[{""id"":""p"",""type"":""int"",""min"":{}}] }").Ok);

            // --- default-out-of-range clamps + warns ---
            var rc = ModJsonParser.Parse(@"{ ""id"":""a"", ""properties"":[{""id"":""p"",""type"":""int"",""min"":0,""max"":10,""default"":99}] }");
            Program.Check("modjson: out-of-range default clamped to max",
                rc.Ok && (int)rc.Schema!.Groups[0].Properties[0].Default! == 10 && rc.Warnings.Count > 0);

            // --- unknown scope warns, defaults global ---
            var rs = ModJsonParser.Parse(@"{ ""id"":""a"", ""scope"":""weird"", ""properties"":[{""id"":""p"",""type"":""bool""}] }");
            Program.Check("modjson: unknown scope -> global + warning",
                rs.Schema?.Scope == "global" && rs.Warnings.Any(w => w.Contains("scope")));

            // --- end-to-end Load: build + register real fluent settings, read back ---
            string loadJson = @"{ ""id"":""SelfTestModJson_v1"", ""name"":""Self Test"", ""properties"":[
                { ""id"":""enable"", ""type"":""bool"", ""default"":true },
                { ""id"":""dmg"", ""type"":""int"", ""min"":0, ""max"":100, ""default"":42 },
                { ""id"":""speed"", ""type"":""float"", ""min"":0, ""max"":5, ""default"":2.5 },
                { ""id"":""tag"", ""type"":""text"", ""default"":""hello"" } ] }";
            var lr = ModJsonLoader.Load(loadJson);
            try
            {
                Program.Check("modjson: Load Ok + settings built", lr.Ok && lr.Settings != null);
                Program.Eq("modjson: built id", "SelfTestModJson_v1", lr.Settings?.Id);
                var fs = lr.Settings as MCM.Abstractions.Base.Global.FluentGlobalSettings;
                Program.Check("modjson: built settings is FluentGlobalSettings", fs != null);
                if (fs != null)
                {
                    Program.Eq("modjson: read back bool", true, fs.Get<bool>("enable"));
                    Program.Eq("modjson: read back int", 42, fs.Get<int>("dmg"));
                    Program.Eq("modjson: read back float", 2.5f, fs.Get<float>("speed"));
                    Program.Eq("modjson: read back text", "hello", fs.Get<string>("tag"));
                }
            }
            finally
            {
                // hermetic: remove the defaults file Load wrote under Documents
                try { if (lr.SettingsFilePath != null && File.Exists(lr.SettingsFilePath)) File.Delete(lr.SettingsFilePath); } catch { }
            }
        }
    }

    // ----------------------------------------------------------------------
    // Slice 3 -- SettingsProfileStore (cross-mod preset profiles, file engine)
    // ----------------------------------------------------------------------
    internal static class ProfileStoreTests
    {
        public static void Run()
        {
            Console.WriteLine("[SettingsProfileStore]");
            var baseDir = Path.Combine(Path.GetTempPath(), "bd-fwtest-" + Guid.NewGuid().ToString("N"));
            var live = Path.Combine(baseDir, "live");
            var profiles = Path.Combine(baseDir, "profiles");
            Directory.CreateDirectory(live);
            try
            {
                File.WriteAllText(Path.Combine(live, "ModA.json"), "{\"v\":1}");
                File.WriteAllText(Path.Combine(live, "ModB.json"), "{\"v\":2}");

                var store = new SettingsProfileStore(live, profiles);

                // capture all
                var captured = store.Capture("Hardcore");
                Program.Check("profile: captured both ids",
                    captured.Contains("ModA") && captured.Contains("ModB"));
                Program.Check("profile: Exists after capture", store.Exists("Hardcore"));
                Program.Eq("profile: GetIds count", 2, store.GetIds("Hardcore").Count);
                Program.Check("profile: appears in List", store.List().Contains("Hardcore"));

                // mutate live, then apply -> restored
                File.WriteAllText(Path.Combine(live, "ModA.json"), "{\"v\":99}");
                var applied = store.Apply("Hardcore");
                Program.Eq("profile: applied count", 2, applied.Count);
                Program.Eq("profile: ModA restored from profile",
                    "{\"v\":1}", File.ReadAllText(Path.Combine(live, "ModA.json")));

                // subset capture
                store.Capture("JustA", new[] { "ModA" });
                var subIds = store.GetIds("JustA");
                Program.Check("profile: subset captures only ModA",
                    subIds.Count == 1 && subIds[0] == "ModA");

                // re-capture overwrites stale (remove ModB from live, recapture Hardcore)
                File.Delete(Path.Combine(live, "ModB.json"));
                store.Capture("Hardcore");
                Program.Check("profile: recapture drops removed mod",
                    !store.GetIds("Hardcore").Contains("ModB"));

                // manifest is not treated as a settings id
                Program.Check("profile: manifest excluded from ids",
                    !store.GetIds("Hardcore").Any(i => i.StartsWith("_profile")));

                // sanitize bad name, then find it sanitized
                store.Capture("bad/name:x");
                Program.Check("profile: sanitized name is creatable+findable",
                    store.Exists("bad/name:x") && store.List().Any(n => n.Contains("bad")));

                // delete
                Program.Check("profile: delete returns true", store.Delete("Hardcore"));
                Program.Check("profile: gone after delete", !store.Exists("Hardcore"));
                Program.Check("profile: delete missing returns false", !store.Delete("nope"));

                // apply missing profile -> empty, no throw
                Program.Eq("profile: apply missing yields 0", 0, store.Apply("ghost").Count);
            }
            finally
            {
                try { Directory.Delete(baseDir, recursive: true); } catch { }
            }
        }
    }

    // ----------------------------------------------------------------------
    // Slice 5 -- PerfProfiler  (filled in when the slice lands)
    // ----------------------------------------------------------------------
    internal static class PerfProfilerTests
    {
        // NoInlining: Harmony detours the method body, but if the JIT already
        // inlined a call site (these get called pre-instrument in the manual
        // section) the patch never fires there. Real engine methods are large
        // and not inlined; we force the same guarantee on the test targets.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static int PerfWork(int n) { int s = 0; for (int i = 0; i < n; i++) s += i; return s; }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static void PerfBoom() => throw new InvalidOperationException("expected");
        public static void OwnerPrefix() { }

        public static void Run()
        {
            Console.WriteLine("[PerfProfiler]");
            PerfProfiler.Reset();
            PerfProfiler.Enabled = true;

            // manual scope accumulates calls + time
            for (int i = 0; i < 3; i++)
                using (PerfProfiler.Measure("ModX", "work"))
                    PerfWork(1000);
            using (PerfProfiler.Measure("ModX", "slow"))
                Thread.Sleep(5);

            var snap = PerfProfiler.Snapshot();
            var work = snap.FirstOrDefault(e => e.Key == "ModX::work");
            Program.Check("perf: manual bucket recorded", work != null);
            if (work != null)
            {
                Program.Eq("perf: manual call count", 3L, work.Calls);
                Program.Check("perf: manual owner credited", work.Owners.Contains("ModX"));
            }
            var slow = snap.FirstOrDefault(e => e.Key == "ModX::slow");
            Program.Check("perf: slow scope measured > 0ms", slow != null && slow.TotalMs > 0);

            // disabled -> no new buckets
            PerfProfiler.Reset();
            PerfProfiler.Enabled = false;
            using (PerfProfiler.Measure("ModX", "ignored")) PerfWork(100);
            Program.Eq("perf: disabled records nothing", 0, PerfProfiler.Snapshot().Count);
            PerfProfiler.Enabled = true;

            // auto-instrument: patch PerfWork with an owner, instrument, call it
            PerfProfiler.Reset();
            var t = typeof(PerfProfilerTests);
            var h = new HarmonyLib.Harmony("Test.Perf");
            h.Patch(t.GetMethod(nameof(PerfWork)),
                prefix: new HarmonyMethod(t.GetMethod(nameof(OwnerPrefix))));
            int n = PerfProfiler.Instrument(new[] { (MethodBase)t.GetMethod(nameof(PerfWork))! });
            Program.Eq("perf: instrument added 1 method", 1, n);
            for (int i = 0; i < 5; i++) PerfWork(500);

            var asnap = PerfProfiler.Snapshot();
            var auto = asnap.FirstOrDefault(e => e.Key.Contains("PerfWork"));
            Program.Check("perf: auto-instrument recorded calls", auto != null);
            if (auto != null)
            {
                Program.Eq("perf: auto call count", 5L, auto.Calls);
                Program.Check("perf: auto owner attributed", auto.Owners.Contains("Test.Perf"));
            }

            // Enabled toggled mid-instrument must NOT corrupt the timing stack:
            // prefix/finalizer push+pop unconditionally, only accounting is gated.
            PerfProfiler.Reset();
            PerfProfiler.Enabled = false;
            for (int i = 0; i < 3; i++) PerfWork(50);   // pushed+popped, not recorded
            PerfProfiler.Enabled = true;
            PerfWork(50);                                // recorded exactly once
            var togg = PerfProfiler.Snapshot().FirstOrDefault(e => e.Key.Contains("PerfWork"));
            Program.Check("perf: Enabled-toggle keeps stack balanced (exactly 1 recorded)",
                togg != null && togg.Calls == 1);

            // exception balance: finalizer still accounts a throwing method, and
            // the per-thread timing stack stays balanced afterwards.
            var hb = new HarmonyLib.Harmony("Test.PerfBoom");
            hb.Patch(t.GetMethod(nameof(PerfBoom)),
                prefix: new HarmonyMethod(t.GetMethod(nameof(OwnerPrefix))));
            PerfProfiler.Instrument(new[] { (MethodBase)t.GetMethod(nameof(PerfBoom))! });
            try { PerfBoom(); } catch (InvalidOperationException) { }
            var boom = PerfProfiler.Snapshot().FirstOrDefault(e => e.Key.Contains("PerfBoom"));
            Program.Check("perf: throwing method still accounted", boom != null && boom.Calls == 1);
            // stack balanced? a fresh measure must still work
            PerfProfiler.Reset();
            using (PerfProfiler.Measure("ModX", "after-throw")) PerfWork(10);
            Program.Check("perf: timing stack balanced after exception",
                PerfProfiler.Snapshot().Any(e => e.Key == "ModX::after-throw"));

            // remove instrumentation -> no further accounting
            PerfProfiler.RemoveInstrumentation();
            PerfProfiler.Reset();
            for (int i = 0; i < 3; i++) PerfWork(10);
            Program.Check("perf: removed instrumentation stops accounting",
                !PerfProfiler.Snapshot().Any(e => e.Key.Contains("PerfWork")));

            // report renders
            PerfProfiler.Reset();
            using (PerfProfiler.Measure("ModX", "rep")) PerfWork(10);
            Program.Check("perf: text report non-empty", PerfProfiler.ToText().Contains("perf report"));

            // cleanup
            h.UnpatchAll("Test.Perf");
            hb.UnpatchAll("Test.PerfBoom");
            PerfProfiler.Reset();
        }
    }
}

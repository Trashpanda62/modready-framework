// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// SaveShield: wraps TaleWorlds save-load entry points with Harmony
// finalizers that catch deserialization failures and log them in detail.
//
// The headline target is the "An item with the same key has already been
// added" ArgumentException -- a Dictionary&lt;TKey,TValue&gt;.Add collision that
// fires deep inside TaleWorlds.SaveSystem when two pieces of mod content
// claim the same stringId. The stock crash gives the user no clue which
// dictionary or which key, so they bisect their modlist by hand.
//
// SaveShield doesn't recover the load -- the inner state is too far gone
// once a duplicate key fires inside the serializer -- but it captures
// everything actionable to runtime.log:
//   - The save file name being loaded
//   - The exception type and message
//   - The full inner stack trace (names the TaleWorlds.SaveSystem frame
//     that was reading the dictionary, which tells us the container)
//   - The argument list snapshot (for LoadGameAction, this exposes the
//     SaveGameFileInfo, which carries the mod-list captured at save time)
//
// Throw/swallow policy (retightened Phase 1.5 / finding H2, 2026-06-10):
//   - SAVE-LOAD targets ALWAYS re-throw -- the user sees the crash UI and
//     runtime.log has the diagnostics. No exceptions: the finalizer can't
//     synthesize a usable return value, and continuing on a
//     half-deserialized campaign corrupts saves.
//   - MISSION-INIT targets that return void may swallow
//     MissingMethod/MissingField/TypeLoad thrown by a non-engine culprit
//     frame (a stale mod's handler), unless the user opted out via
//     saveshield-swallow-disabled.flag.
//   - Duplicate-key ArgumentException is never swallowed anywhere.
//
// Lifecycle: installed alongside PatchShield at every campaign-init
// lifecycle hook (BetaDepsHarmonySubModule).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

using HarmonyLib;

namespace BetaDeps.Foundation;

public static class SaveShield
{
    private const string Tag = "BetaDeps.SaveShield";
    private const string HarmonyId = "BetaDeps.Foundation.SaveShield";

    private static readonly HashSet<MethodBase> _shielded = new();
    private static readonly object _lock = new();
    private static long _duplicateKeyHits;
    private static long _otherFailureHits;
    private static long _swallowedCount;

    // v0.7.3: swallow-mode is now ON by default (was opt-in in v0.7.2).
    // SaveShield drops the throw on MissingMethodException /
    // MissingFieldException / TypeLoadException / duplicate-key
    // ArgumentException when the CULPRIT frame belongs to a non-engine
    // assembly -- the mod's broken handler is logged but doesn't crash
    // the game. The user can opt OUT by clicking "Toggle SaveShield
    // Swallow" in Mod Config (which creates saveshield-swallow-disabled.flag
    // next to runtime.log). Naming matches the PatchShield convention --
    // both default ON, both opt out via a *-disabled.flag file.
    private const string SwallowDisableFlagName = "saveshield-swallow-disabled.flag";
    // Legacy flag name from v0.7.2's opt-in design. We clean this up on
    // first install so stale files don't sit in the user's BetaDeps folder.
    private const string LegacyOptInFlagName = "saveshield-swallow.flag";

    // v4: ring buffer of the most recent FailureRecord objects, so the
    // selftest report and the GitHub-issue button can hand mod authors
    // structured data instead of re-scanning runtime.log.
    private const int RecentCapacity = 20;
    private static readonly LinkedList<FailureRecord> _recent = new();
    private static readonly object _recentLock = new();

    /// <summary>Methods SaveShield has finalizer-wrapped this session.</summary>
    public static int ShieldedCount { get { lock (_lock) return _shielded.Count; } }

    /// <summary>Duplicate-key save crashes caught this session.</summary>
    public static long DuplicateKeyHits => Interlocked.Read(ref _duplicateKeyHits);

    /// <summary>Other (non-duplicate-key) save-load failures caught.</summary>
    public static long OtherFailureHits => Interlocked.Read(ref _otherFailureHits);

    /// <summary>Exceptions SaveShield swallowed (returned null instead of re-throw) this session.</summary>
    public static long SwallowedCount => Interlocked.Read(ref _swallowedCount);

    /// <summary>
    /// True (the default) unless saveshield-swallow-disabled.flag exists next
    /// to runtime.log. v0.7.3 flipped the default from opt-in to opt-out --
    /// swallowing recoverable exceptions from consumer-mod frames is now
    /// the protective default, matching the PatchShield convention.
    /// </summary>
    public static bool IsSwallowEnabled()
    {
        try
        {
            var rt = RuntimeLog.Path;
            var dir = System.IO.Path.GetDirectoryName(rt);
            if (string.IsNullOrEmpty(dir)) return true;
            return !System.IO.File.Exists(System.IO.Path.Combine(dir!, SwallowDisableFlagName));
        }
        catch { return true; }
    }

    /// <summary>
    /// Toggle the opt-out flag. If the disable flag exists, delete it (and
    /// re-enable swallow); otherwise create it (and disable). Returns the
    /// new enabled state.
    /// </summary>
    public static bool ToggleSwallow()
    {
        try
        {
            var rt = RuntimeLog.Path;
            var dir = System.IO.Path.GetDirectoryName(rt);
            if (string.IsNullOrEmpty(dir)) return IsSwallowEnabled();
            var path = System.IO.Path.Combine(dir!, SwallowDisableFlagName);
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
                return true; // re-enabled
            }
            else
            {
                System.IO.File.WriteAllText(path,
                    "# Presence of this file DISABLES SaveShield swallow-mode.\n" +
                    "# (v0.7.3+: swallow-mode is ON by default. This file opts you out.)\n" +
                    "#\n" +
                    "# When swallow is enabled (no file), MissingMethodException /\n" +
                    "# MissingFieldException / TypeLoadException / duplicate-key\n" +
                    "# ArgumentException thrown from non-engine frames during save-load\n" +
                    "# or mission-init are LOGGED but not re-thrown -- the mod's broken\n" +
                    "# handler is dropped at the failing call site and the game keeps\n" +
                    "# running. Delete this file (or click 'Toggle SaveShield Swallow'\n" +
                    "# in Mod Config again) to re-enable the protective default.\n");
                return false; // disabled
            }
        }
        catch (Exception ex)
        {
            try { DiagLog.LogCaught(Tag, "ToggleSwallow", ex); } catch { }
            return IsSwallowEnabled();
        }
    }

    /// <summary>
    /// One-shot migration: if the legacy v0.7.2 opt-in flag is still on
    /// disk, delete it so it doesn't clutter the user's folder. The new
    /// semantics are inverted, so the legacy file no longer has any
    /// effect even if left in place -- this just cleans up.
    /// </summary>
    private static void MigrateLegacyFlag()
    {
        try
        {
            var rt = RuntimeLog.Path;
            var dir = System.IO.Path.GetDirectoryName(rt);
            if (string.IsNullOrEmpty(dir)) return;
            var legacy = System.IO.Path.Combine(dir!, LegacyOptInFlagName);
            if (System.IO.File.Exists(legacy))
            {
                try
                {
                    System.IO.File.Delete(legacy);
                    DiagLog.Log(Tag, $"migrated v0.7.2 legacy opt-in flag (deleted {LegacyOptInFlagName}); swallow-mode is now ON by default in v0.7.3+");
                }
                catch (Exception delEx) { try { DiagLog.LogCaught(Tag, "MigrateLegacyFlag/delete", delEx); } catch { } }
            }
        }
        catch (Exception ex)
        {
            try { DiagLog.LogCaught(Tag, "MigrateLegacyFlag", ex); } catch { }
        }
    }

    /// <summary>Most-recent-first list of every FailureRecord this session (capped).</summary>
    public static IReadOnlyList<FailureRecord> RecentFailures
    {
        get
        {
            lock (_recentLock)
            {
                return _recent.ToArray();
            }
        }
    }

    /// <summary>The single most recent failure, or null if none.</summary>
    public static FailureRecord? LastFailure
    {
        get
        {
            lock (_recentLock)
            {
                return _recent.First?.Value;
            }
        }
    }

    private static void RecordFailure(FailureRecord rec)
    {
        if (rec == null) return;
        lock (_recentLock)
        {
            _recent.AddFirst(rec);
            while (_recent.Count > RecentCapacity)
                _recent.RemoveLast();
        }
    }

    /// <summary>
    /// One-liner session-summary mirroring PatchShield.WriteSessionSummary.
    /// Fires at AppDomain.ProcessExit. Quotes the top CULPRIT mod (most
    /// common offender across all FailureRecords this session) so users
    /// have a single line in runtime.log that names the worst-behaved mod.
    /// </summary>
    public static void WriteSessionSummary()
    {
        try
        {
            string topCulprit = "(none)";
            lock (_recentLock)
            {
                if (_recent.Count > 0)
                {
                    var grouped = _recent
                        .Where(r => !string.IsNullOrEmpty(r.CulpritAssembly))
                        .GroupBy(r => r.CulpritAssembly)
                        .OrderByDescending(g => g.Count())
                        .FirstOrDefault();
                    if (grouped != null)
                        topCulprit = $"{grouped.Key} ({grouped.Count()})";
                }
            }
            DiagLog.Log(Tag,
                $"SESSION SUMMARY: shielded {ShieldedCount} entry-point(s), " +
                $"caught {DuplicateKeyHits + OtherFailureHits} failure(s) " +
                $"(duplicate-key {DuplicateKeyHits}, other {OtherFailureHits}), " +
                $"swallowed {SwallowedCount}, swallow-mode {(IsSwallowEnabled() ? "ON" : "OFF")}. " +
                $"Top CULPRIT: {topCulprit}.");
        }
        catch (Exception ex)
        {
            try { DiagLog.LogCaught(Tag, "WriteSessionSummary", ex); } catch { }
        }
    }

    // Methods we want to wrap. We resolve by reflection so SaveShield doesn't
    // compile-bind to TaleWorlds.SaveSystem.dll or SandBox.dll. The probe
    // is idempotent -- a method already in _shielded skips silently.
    //
    // v3 widens the coverage past save-load into the broader "loading
    // something" surface: mission init, battle-mode transitions, scene
    // start. Each finalizer surfaces a CULPRIT line naming the deepest
    // non-engine, non-BetaDeps frame so mod authors get a one-line answer.
    private static readonly (string TypeFullName, string AssemblyName, string MethodName, string Category)[] _targets =
    {
        // ---- SAVE-LOAD ----
        ("TaleWorlds.Core.MBSaveLoad",       "TaleWorlds.Core",       "LoadSaveGameData",  "SAVE-LOAD"),
        ("SandBox.SandBoxSaveHelper",         "SandBox",                "LoadGameAction",     "SAVE-LOAD"),
        ("SandBox.SandBoxSaveHelper",         "SandBox",                "LoadSaveGame",       "SAVE-LOAD"),
        ("TaleWorlds.SaveSystem.SaveManager", "TaleWorlds.SaveSystem", "Load",               "SAVE-LOAD"),
        ("TaleWorlds.SaveSystem.LoadResult",  "TaleWorlds.SaveSystem", "Load",               "SAVE-LOAD"),
        ("TaleWorlds.SaveSystem.Load.LoadResult", "TaleWorlds.SaveSystem", "Load",           "SAVE-LOAD"),

        // ---- BATTLE / MISSION INIT ----
        // MissionState.FinishMissionLoading is the top-level mission-load
        // entry point; whoever's prefix/postfix throws here surfaces in
        // its finalizer.
        ("TaleWorlds.MountAndBlade.MissionState", "TaleWorlds.MountAndBlade", "FinishMissionLoading", "MISSION-INIT"),
        // SetMissionMode fires the OnMissionModeChange event chain on every
        // attached MissionLogic. A throw inside any of those handlers --
        // ReinforcementSystem.OnMissionModeChange was the canonical case in
        // user repro 2026-05-25 -- bubbles up through SetMissionMode.
        ("TaleWorlds.MountAndBlade.Mission",  "TaleWorlds.MountAndBlade", "SetMissionMode",  "MISSION-INIT"),
        // OnInitialize is when mods' AddMissionBehavior calls fire; throws
        // during behavior construction surface here.
        ("TaleWorlds.MountAndBlade.Mission",  "TaleWorlds.MountAndBlade", "OnInitialize",    "MISSION-INIT"),
        // SpawnTroop is the path RBM-pattern mods patch with old signatures;
        // shield it so Harmony-patch-install failures get a culprit line too.
        ("TaleWorlds.MountAndBlade.Mission",  "TaleWorlds.MountAndBlade", "SpawnTroop",      "MISSION-INIT"),
    };

    // Assembly-name prefixes that are part of the engine or our own
    // infrastructure; the culprit walker skips frames in these to find
    // the first MOD-OWNED frame in the stack.
    // Engine/infrastructure prefix list and culprit-frame attribution live
    // in the shared CulpritFinder (Phase 3.1) -- PatchShield uses the same core.

    public static void Install()
    {
        // v0.7.3 migration: clean up the v0.7.2 opt-in flag if present.
        // Idempotent + cheap; safe to call on every install pass.
        MigrateLegacyFlag();

        try
        {
            var harmony = new Harmony(HarmonyId);
            var finalizer = typeof(SaveShield).GetMethod(
                nameof(SaveLoadFinalizer),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (finalizer == null)
            {
                DiagLog.Log(Tag, "could not resolve SaveLoadFinalizer; aborting install");
                return;
            }

            int patched = 0;
            int skipped = 0;
            int already = 0;

            foreach (var (typeName, asmName, methodName, _) in _targets)
            {
                var type = ResolveType(typeName, asmName);
                if (type == null)
                {
                    skipped++;
                    continue;
                }

                // Walk every overload of the target method.
                var methods = type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(m => m.Name == methodName)
                    .ToArray();

                if (methods.Length == 0)
                {
                    DiagLog.Log(Tag, $"target {typeName}.{methodName} not found on this Bannerlord build; skipping");
                    skipped++;
                    continue;
                }

                foreach (var m in methods)
                {
                    lock (_lock)
                    {
                        if (_shielded.Contains(m))
                        {
                            already++;
                            continue;
                        }

                        try
                        {
                            harmony.Patch(m, finalizer: new HarmonyMethod(finalizer));
                            _shielded.Add(m);
                            patched++;
                            DiagLog.Log(Tag, $"shielded {typeName}.{methodName}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})");
                        }
                        catch (Exception ex)
                        {
                            skipped++;
                            DiagLog.LogCaught(Tag, $"shielding {typeName}.{methodName}", ex);
                        }
                    }
                }
            }

            if (patched > 0 || already == 0)
            {
                DiagLog.Log(Tag, $"shield pass: +{patched} new, {already} already-shielded, {skipped} skipped (total shielded: {_shielded.Count})");
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "Install", ex);
        }
    }

    private static Type ResolveType(string typeFullName, string assemblyName)
    {
        try
        {
            // Try the assembly-qualified resolve first (cheap if loaded).
            var t = Type.GetType($"{typeFullName}, {assemblyName}");
            if (t != null) return t;

            // Fall back to scanning loaded assemblies.
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var name = asm.GetName().Name ?? string.Empty;
                    if (!string.Equals(name, assemblyName, StringComparison.OrdinalIgnoreCase)) continue;
                    var probe = asm.GetType(typeFullName, throwOnError: false);
                    if (probe != null) return probe;
                }
                catch { /* keep scanning */ }
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"ResolveType({typeFullName})", ex);
        }
        return null!;
    }

#pragma warning disable IDE0051, IDE1006
    private static Exception SaveLoadFinalizer(MethodBase __originalMethod, object[] __args, Exception __exception)
#pragma warning restore IDE0051, IDE1006
    {
        if (__exception == null) return null!;

        FailureRecord? record = null;
        try
        {
            var ex = __exception;
            while (ex is TargetInvocationException && ex.InnerException != null)
                ex = ex.InnerException;

            bool isDupKey = IsDuplicateKeyException(ex);

            if (isDupKey) Interlocked.Increment(ref _duplicateKeyHits);
            else Interlocked.Increment(ref _otherFailureHits);

            var ownerType = __originalMethod?.DeclaringType?.FullName ?? "?";
            var ownerName = __originalMethod?.Name ?? "?";
            var category = ResolveCategory(ownerType, ownerName);

            // Walk the exception's frames and pick the deepest one that's
            // NOT engine / NOT us. That's the most likely culprit mod.
            var culprit = FindCulpritFrame(ex);

            // v4: populate a structured record alongside the text log.
            record = new FailureRecord
            {
                When = DateTime.UtcNow,
                Category = category,
                OwnerType = ownerType,
                OwnerMethod = ownerName,
                ExceptionType = ex.GetType().FullName ?? "?",
                Message = ex.Message ?? string.Empty,
                IsDuplicateKey = isDupKey,
                CulpritAssembly = culprit.AssemblyName,
                CulpritFrame = culprit.FrameDescription,
                CulpritAssemblyPath = culprit.AssemblyLocation,
                StackTraceRaw = ex.StackTrace ?? string.Empty,
                ExSource = ex.Source ?? string.Empty,
            };
            try
            {
                var site = ex.TargetSite;
                if (site != null)
                {
                    record.ThrowSiteType = site.DeclaringType?.FullName ?? string.Empty;
                    record.ThrowSiteMethod = site.Name ?? string.Empty;
                }
            }
            catch { /* swallow */ }
            if (__args != null && __args.Length > 0 && __args[0] != null)
            {
                if (__args[0] is string sArg) record.FirstArgSummary = sArg;
                else record.FirstArgSummary = __args[0].GetType().FullName ?? string.Empty;
            }

            DiagLog.Log(Tag, "========================================================");
            DiagLog.Log(Tag, $"{category} FAILURE in {ownerType}.{ownerName}");
            if (!string.IsNullOrEmpty(culprit.AssemblyName))
            {
                DiagLog.Log(Tag, $"  CULPRIT:      {culprit.AssemblyName}");
                DiagLog.Log(Tag, $"                ({culprit.FrameDescription})");
            }
            else
            {
                DiagLog.Log(Tag, $"  CULPRIT:      (no non-engine frame found -- failure likely in TaleWorlds itself or a mod whose stack frames were inlined)");
            }
            DiagLog.Log(Tag, $"  exception:    {ex.GetType().FullName}");
            DiagLog.Log(Tag, $"  message:      {ex.Message}");

            // Argument snapshot. For LoadSaveGameData(string saveName) this
            // captures the save name. For LoadGameAction(SaveGameFileInfo, ...)
            // we attempt to read SaveGameFileInfo fields via reflection.
            if (__args != null)
            {
                for (int i = 0; i < __args.Length; i++)
                {
                    var arg = __args[i];
                    if (arg == null)
                    {
                        DiagLog.Log(Tag, $"  arg[{i}]:       null");
                        continue;
                    }

                    if (arg is string s)
                    {
                        DiagLog.Log(Tag, $"  arg[{i}] str:   \"{s}\"");
                        continue;
                    }

                    DiagLog.Log(Tag, $"  arg[{i}] type:  {arg.GetType().FullName}");
                    TryDumpSaveGameFileInfo(arg);
                }
            }

            if (isDupKey)
            {
                DiagLog.Log(Tag, $"  diagnosis:    Dictionary&lt;TKey,TValue&gt;.Add key-collision during save deserialization.");
                DiagLog.Log(Tag, $"                Two pieces of registered content share a stringId. The stack trace");
                DiagLog.Log(Tag, $"                below names the TaleWorlds.SaveSystem container that was being read;");
                DiagLog.Log(Tag, $"                the mod that registers that container's element type is the culprit.");
            }

            // ex.TargetSite is the method that threw -- usually Dictionary.Insert
            // (the internal mscorlib helper) on its own, but combined with the
            // stack frames it nails the throw site.
            try
            {
                var site = ex.TargetSite;
                if (site != null)
                {
                    DiagLog.Log(Tag, $"  throw site:   {site.DeclaringType?.FullName ?? "?"}.{site.Name}");
                    DiagLog.Log(Tag, $"  ex.Source:    {ex.Source ?? "?"}");
                }
            }
            catch { /* swallow */ }

            DiagLog.Log(Tag, "  stack trace:");
            foreach (var line in (ex.StackTrace ?? string.Empty).Split('\n'))
            {
                var trimmed = line.TrimEnd();
                if (trimmed.Length == 0) continue;
                DiagLog.Log(Tag, $"    {trimmed}");
            }

            // Walk a fresh System.Diagnostics.StackTrace built from the
            // exception. This sometimes preserves frames the StackTrace
            // string version dropped (especially for inlined methods that
            // get re-discovered via debug symbols).
            try
            {
                var st = new System.Diagnostics.StackTrace(ex, fNeedFileInfo: true);
                var frames = st.GetFrames();
                if (frames != null && frames.Length > 0)
                {
                    DiagLog.Log(Tag, "  parsed frames:");
                    foreach (var f in frames)
                    {
                        var m = f.GetMethod();
                        if (m == null) continue;
                        var line = $"{m.DeclaringType?.FullName ?? "?"}.{m.Name} (IL offset {f.GetILOffset()})";
                        DiagLog.Log(Tag, $"    {line}");
                        if (record != null) record.ParsedFrames.Add(line);
                    }
                }
            }
            catch (Exception walkEx)
            {
                try { DiagLog.LogCaught(Tag, "stack-walk", walkEx); } catch { }
            }

            // v4 #7: capture the finalizer's own call chain. This is the
            // chain that led TO the patched method (not the exception's
            // throw stack). On non-throwing inline paths these frames are
            // gone, but for a finalizer firing inside a wrapper there's
            // usually 5-10 useful frames showing how the patched method
            // was reached -- often including the mod's behavior-register
            // site or the engine code path that triggered it.
            try
            {
                var finST = new System.Diagnostics.StackTrace(skipFrames: 1, fNeedFileInfo: false);
                var fframes = finST.GetFrames();
                if (fframes != null)
                {
                    DiagLog.Log(Tag, "  finalizer call chain (how we got to the patched method):");
                    int kept = 0;
                    foreach (var f in fframes)
                    {
                        var m = f.GetMethod();
                        if (m == null) continue;
                        var declType = m.DeclaringType;
                        var asm = declType?.Assembly;
                        var asmName = asm?.GetName()?.Name ?? string.Empty;
                        // Skip Harmony wrapper frames -- noise.
                        if (asmName.StartsWith("0Harmony", StringComparison.Ordinal) ||
                            asmName.StartsWith("HarmonyLib", StringComparison.Ordinal))
                            continue;
                        var line = $"{declType?.FullName ?? "?"}.{m.Name}";
                        DiagLog.Log(Tag, $"    {line}");
                        if (record != null) record.FinalizerCallChain.Add(line);
                        if (++kept >= 20) break;
                    }
                }
            }
            catch (Exception finEx)
            {
                try { DiagLog.LogCaught(Tag, "finalizer-stack-walk", finEx); } catch { }
            }

            // v4 #1: probe the current Bannerlord build for overloads of the
            // method named in the MissingMethodException message. Most useful
            // case: "Method not found: 'Void Mission.GetFormationSpawnFrame(...)'"
            // -- we can print the current signatures so the mod author sees
            // exactly what to change to.
            try
            {
                if (record != null)
                {
                    var sigs = ProbeCurrentSignatures(ex);
                    if (sigs.Count > 0)
                    {
                        DiagLog.Log(Tag, $"  current API ({sigs.Count} overload(s) of the named method exist on this build):");
                        foreach (var s in sigs)
                        {
                            DiagLog.Log(Tag, $"    {s}");
                            record.CurrentSignatures.Add(s);
                        }
                    }
                }
            }
            catch (Exception sigEx)
            {
                try { DiagLog.LogCaught(Tag, "probe-current-signatures", sigEx); } catch { }
            }

            // v4 #2: probe the CULPRIT mod's manifest (SubModule.xml + main
            // DLL's AssemblyVersion + TaleWorlds references). One-stop view
            // of which version the mod shipped and against which API.
            try
            {
                if (record != null && !string.IsNullOrEmpty(record.CulpritAssemblyPath))
                {
                    var manifest = ProbeModManifest(record.CulpritAssemblyPath);
                    if (manifest != null)
                    {
                        record.CulpritManifest = manifest;
                        DiagLog.Log(Tag, "  CULPRIT manifest:");
                        foreach (var l in manifest.ToLines())
                            DiagLog.Log(Tag, $"    {l}");
                    }
                }
            }
            catch (Exception manEx)
            {
                try { DiagLog.LogCaught(Tag, "probe-manifest", manEx); } catch { }
            }

            // v4 #8: Cecil import scan -- list every TaleWorlds.* member the
            // CULPRIT DLL imports that matches the failing method/field name.
            try
            {
                if (record != null && !string.IsNullOrEmpty(record.CulpritAssemblyPath))
                {
                    var matches = ScanImportsForMissing(record.CulpritAssemblyPath, ex);
                    if (matches.Count > 0)
                    {
                        DiagLog.Log(Tag, $"  matching imports in CULPRIT DLL ({matches.Count}):");
                        foreach (var s in matches.Take(15))
                        {
                            DiagLog.Log(Tag, $"    {s}");
                            record.ImportMatches.Add(s);
                        }
                    }
                }
            }
            catch (Exception impEx)
            {
                try { DiagLog.LogCaught(Tag, "import-scan", impEx); } catch { }
            }

            DiagLog.Log(Tag, "========================================================");

            // v4: commit the structured record + append the catalog entry.
            if (record != null)
            {
                RecordFailure(record);
                try { FailedModsCatalog.Append(record); }
                catch (Exception catEx) { try { DiagLog.LogCaught(Tag, "catalog-append", catEx); } catch { } }
            }

            // v4 #99 swallow-mode, retightened in Phase 1.5 / finding H2
            // (2026-06-10 review). Swallow ONLY when ALL of these hold:
            //   1. swallow not opted out,
            //   2. recoverable exception kind (MissingMethod/MissingField/
            //      TypeLoad -- duplicate-key ArgumentException was REMOVED:
            //      this file's own header documents that state is
            //      unrecoverable once a duplicate key fires in the
            //      serializer, and continuing risks writing a corrupted
            //      save later),
            //   3. culprit frame is a non-engine mod,
            //   4. category is MISSION-INIT -- NEVER SAVE-LOAD. The
            //      finalizer has no ref __result, so swallowing on a
            //      non-void SAVE-LOAD target (LoadSaveGameData,
            //      LoadGameAction) handed the engine a null/default and it
            //      carried on with a half-deserialized campaign,
            //   5. the target method returns void -- same reasoning; e.g.
            //      Mission.SpawnTroop returns an Agent, and a silent null
            //      Agent just moves the crash somewhere unrecognizable.
            var returnsVoid = __originalMethod is MethodInfo mi && mi.ReturnType == typeof(void);
            if (record != null && IsSwallowEnabled() && IsRecoverableException(ex)
                && !string.IsNullOrEmpty(record.CulpritAssembly)
                && string.Equals(category, "MISSION-INIT", StringComparison.Ordinal)
                && returnsVoid)
            {
                Interlocked.Increment(ref _swallowedCount);
                DiagLog.Log(Tag, $"SWALLOWED {ex.GetType().Name} from '{record.CulpritAssembly}' -- game continues. " +
                                 "(Swallow is on by default; click 'Toggle SaveShield Swallow' in Mod Config to create " +
                                 SwallowDisableFlagName + " and disable it.)");
                return null!;
            }
        }
        catch (Exception logEx)
        {
            try { DiagLog.LogCaught(Tag, "SaveLoadFinalizer/log", logEx); } catch { /* logging poisoning guard */ }
        }

        // Default: re-throw. The user still sees the crash UI and runtime.log
        // has the diagnostic detail.
        return __exception;
    }

    private static bool IsRecoverableException(Exception ex)
    {
        if (ex == null) return false;
        // Phase 1.5 / H2: duplicate-key ArgumentException removed from this
        // list. The header's own analysis is right: once the serializer hits
        // a duplicate key the object table is half-populated and nothing
        // downstream is trustworthy -- swallowing it traded an honest crash
        // for delayed corruption. (It also depended on English exception
        // text; non-English installs never matched. M2/Phase 3 replaces the
        // remaining text matching used for diagnostics.)
        return ex is MissingMethodException
            || ex is MissingFieldException
            || ex is TypeLoadException;
    }

    /// <summary>
    /// Dump every readable property + field on a SaveGameFileInfo-shaped
    /// object. The actual type lives in TaleWorlds.SaveSystem and its
    /// member names shift across versions, so we don't hard-bind: every
    /// public+instance member gets a value-toString shot, anything
    /// enumerable gets up to 30 entries listed.
    /// </summary>
    private static void TryDumpSaveGameFileInfo(object arg)
    {
        try
        {
            var t = arg.GetType();
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            foreach (var p in t.GetProperties(flags))
            {
                // Skip indexed properties (would need args to GetValue).
                if (p.GetIndexParameters().Length > 0) continue;
                object? val = null;
                try { val = p.GetValue(arg); }
                catch { /* keep going */ continue; }
                LogMember($"prop {p.Name}", val);
            }
            foreach (var f in t.GetFields(flags))
            {
                object? val = null;
                try { val = f.GetValue(arg); }
                catch { continue; }
                LogMember($"fld  {f.Name}", val);
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "TryDumpSaveGameFileInfo", ex);
        }
    }

    private static void LogMember(string label, object? val)
    {
        try
        {
            if (val == null)
            {
                DiagLog.Log(Tag, $"    {label,-32} = null");
                return;
            }

            // Enumerables (lists, dictionaries, arrays) get expanded inline.
            if (val is System.Collections.IEnumerable enumerable && !(val is string))
            {
                int count = 0;
                foreach (var entry in enumerable)
                {
                    DiagLog.Log(Tag, $"    {label}[{count}] = {Truncate(entry?.ToString(), 220)}");
                    if (++count >= 30) { DiagLog.Log(Tag, $"    {label}[..]   (truncated)"); break; }
                }
                if (count == 0) DiagLog.Log(Tag, $"    {label,-32} = (empty enumerable)");
                return;
            }

            DiagLog.Log(Tag, $"    {label,-32} = {Truncate(val.ToString(), 220)}");
        }
        catch (Exception ex)
        {
            try { DiagLog.LogCaught(Tag, $"LogMember({label})", ex); } catch { }
        }
    }

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s!.Length <= max ? s : s.Substring(0, max) + "...";
    }

    /// <summary>
    /// Look up the (TypeFullName, MethodName) tuple in the targets table to
    /// get the category string (SAVE-LOAD / MISSION-INIT / etc.). Falls back
    /// to "FAILURE" if not found.
    /// </summary>
    private static string ResolveCategory(string typeFullName, string methodName)
    {
        try
        {
            foreach (var (t, _, m, cat) in _targets)
            {
                if (string.Equals(t, typeFullName, StringComparison.Ordinal) &&
                    string.Equals(m, methodName, StringComparison.Ordinal))
                {
                    return cat;
                }
            }
        }
        catch { /* fall through */ }
        return "FAILURE";
    }

    /// <summary>Retained alias: SaveShield's attribution now delegates to the
    /// shared CulpritFinder core (Phase 3.1).</summary>
    private static CulpritFinder.CulpritInfo FindCulpritFrame(Exception ex)
        => CulpritFinder.FindCulpritFrame(ex);

    /// <summary>
    /// Locale-proof duplicate-key detection (M2). The old check matched the
    /// English exception text ("same key" / "already been added"), which
    /// silently misclassified on non-English .NET language packs. Structure
    /// check instead: a Dictionary duplicate-add is an ArgumentException
    /// (exactly -- the Null/OutOfRange subclasses are different failures)
    /// thrown from System.ThrowHelper / Dictionary`2 insert frames. The
    /// English text check stays as a fast path.
    /// </summary>
    private static bool IsDuplicateKeyException(Exception ex)
    {
        if (ex is not ArgumentException || ex is ArgumentNullException || ex is ArgumentOutOfRangeException)
            return false;

        var msg = ex.Message ?? string.Empty;
        if (msg.Contains("same key") || msg.Contains("already been added"))
            return true;

        try
        {
            var site = ex.TargetSite;
            var siteType = site?.DeclaringType?.FullName ?? string.Empty;
            var siteName = site?.Name ?? string.Empty;
            bool thrownByDictionaryMachinery =
                siteName == "ThrowAddingDuplicateWithKeyArgumentException" ||
                siteType == "System.ThrowHelper" ||
                siteType.StartsWith("System.Collections.Generic.Dictionary", StringComparison.Ordinal);
            if (!thrownByDictionaryMachinery) return false;

            var stack = ex.StackTrace ?? string.Empty;
            return stack.Contains("Dictionary`2.Insert")
                || stack.Contains("Dictionary`2.Add")
                || stack.Contains("Dictionary`2.set_Item")
                || siteName == "ThrowAddingDuplicateWithKeyArgumentException";
        }
        catch
        {
            return false;
        }
    }

    // v4 forwarders into SaveShieldProbes (split out for readability).
    private static List<string> ProbeCurrentSignatures(Exception ex) =>
        SaveShieldProbes.ProbeCurrentSignatures(ex);

    private static ModManifest? ProbeModManifest(string dllPath) =>
        SaveShieldProbes.ProbeModManifest(dllPath);

    private static List<string> ScanImportsForMissing(string dllPath, Exception ex) =>
        SaveShieldProbes.ScanImportsForMissing(dllPath, ex);

}

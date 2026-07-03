// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// ContinueLoadGuard: fixes the "Continue" (main-menu) save-load loop that
// does NOT happen when loading the same save from the Saved Games list.
//
// ROOT CAUSE (native SandBox, verified 2026-07-02 by IL disassembly):
//   The two entry points differ.
//     - Saved Games list -> SavedGameVM.ExecuteSaveLoad -> TryLoadSave  (one-shot)
//     - Continue          -> SandBoxViewSubModule.ContinueCampaign
//                            -> PreloadScreen.OnFrameTick -> TryLoadSave
//   PreloadScreen.OnFrameTick runs EVERY frame. It kicks the load via the
//   async SandBoxSaveHelper.TryLoadSave (which pops the "modules are
//   different" inquiry and, on accept, StartGame). PreloadScreen is never
//   popped by StartGame itself, so if the screen re-activates / re-ticks
//   (which happens on a module-mismatched save) OnFrameTick calls
//   TryLoadSave a SECOND time -> the warning shows again and the load
//   re-enters -> the loading loop the user reported. The list path has no
//   ticking screen, so it never loops.
//
// FIX:
//   Allow at most ONE TryLoadSave dispatch per PreloadScreen instance.
//   We only intervene while executing inside a PreloadScreen.OnFrameTick
//   (a thread-static scope), so the Saved-Games-list path -- which calls
//   TryLoadSave outside any preload tick -- is never touched. Keyed on the
//   screen instance via a GC-safe ConditionalWeakTable, so a genuinely new
//   Continue (new PreloadScreen) always loads, while a re-tick of the same
//   screen is blocked.
//
// All native; ModReady owns none of these methods. We patch them the same
// way SaveShield/PatchShield wrap native save/mission entry points.
//
// Install: from ModReadyHarmonySubModule.OnBeforeInitialModuleScreenSetAsRoot
// (SandBox assemblies are loaded, main menu not yet shown). Idempotent.

using System;
using System.Reflection;
using System.Runtime.CompilerServices;

using HarmonyLib;

namespace ModReady.Foundation;

public static class ContinueLoadGuard
{
    private const string Tag = "ModReady.ContinueLoadGuard";
    private const string HarmonyId = "ModReady.Foundation.ContinueLoadGuard";

    private static int _installed;

    // The PreloadScreen instance whose OnFrameTick is currently executing on
    // this thread (null when not inside a preload tick). TryLoadSave only
    // guards when this is set, which is exactly the Continue path.
    [ThreadStatic] private static object? _tickInstance;

    // Instances that have already dispatched their one load. Identity-keyed
    // and GC-safe -- when a PreloadScreen dies, its entry evaporates, so a
    // later Continue with a fresh screen is never falsely blocked.
    private static readonly ConditionalWeakTable<object, object> _dispatched = new();
    private static readonly object Marker = new();

    public static void Install()
    {
        if (System.Threading.Interlocked.Exchange(ref _installed, 1) != 0) return;

        try
        {
            var harmony = new Harmony(HarmonyId);

            var preloadTick = ResolveMethod("SandBox.View.PreloadScreen", "SandBox.View", "OnFrameTick");
            var tryLoadSave = ResolveMethod("SandBox.SandBoxSaveHelper", "SandBox", "TryLoadSave");

            if (preloadTick == null || tryLoadSave == null)
            {
                DiagLog.Log(Tag, $"targets not resolvable on this build (PreloadScreen.OnFrameTick={preloadTick != null}, " +
                                 $"TryLoadSave={tryLoadSave != null}); guard not installed");
                return;
            }

            harmony.Patch(preloadTick,
                prefix: new HarmonyMethod(typeof(ContinueLoadGuard).GetMethod(nameof(PreloadTickPrefix), BindingFlags.Static | BindingFlags.NonPublic)),
                finalizer: new HarmonyMethod(typeof(ContinueLoadGuard).GetMethod(nameof(PreloadTickFinalizer), BindingFlags.Static | BindingFlags.NonPublic)));

            harmony.Patch(tryLoadSave,
                prefix: new HarmonyMethod(typeof(ContinueLoadGuard).GetMethod(nameof(TryLoadSavePrefix), BindingFlags.Static | BindingFlags.NonPublic)));

            DiagLog.Log(Tag, "installed: Continue-path load de-duplicated (PreloadScreen.OnFrameTick + SandBoxSaveHelper.TryLoadSave)");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "Install", ex);
        }
    }

    // Mark that we're inside this PreloadScreen's tick for the duration of the
    // original method.
    private static void PreloadTickPrefix(object __instance)
    {
        _tickInstance = __instance;
    }

    // Runs whether or not the original threw; always clears the scope.
    private static Exception? PreloadTickFinalizer(Exception? __exception)
    {
        _tickInstance = null;
        return __exception; // never alter the native exception flow
    }

    // Return false to SKIP the native TryLoadSave (no inquiry, no reload) when
    // this PreloadScreen instance has already dispatched its one load.
    private static bool TryLoadSavePrefix()
    {
        try
        {
            var inst = _tickInstance;
            if (inst == null) return true; // not the Continue/PreloadScreen path (e.g. Saved Games list) -- never guard

            if (_dispatched.TryGetValue(inst, out _))
            {
                DiagLog.Log(Tag, "blocked duplicate Continue-path load dispatch (PreloadScreen re-tick) -- this is what caused the load loop");
                return false;
            }

            _dispatched.Add(inst, Marker);
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "TryLoadSavePrefix", ex);
            return true; // on any guard failure, fall back to native behavior
        }
        return true;
    }

    private static MethodInfo? ResolveMethod(string typeFullName, string assemblyName, string methodName)
    {
        try
        {
            var type = Type.GetType($"{typeFullName}, {assemblyName}");
            if (type == null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        if (!string.Equals(asm.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase)) continue;
                        type = asm.GetType(typeFullName, throwOnError: false);
                        if (type != null) break;
                    }
                    catch { /* keep scanning */ }
                }
            }
            if (type == null) return null;

            return type.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"ResolveMethod({typeFullName}.{methodName})", ex);
            return null;
        }
    }
}

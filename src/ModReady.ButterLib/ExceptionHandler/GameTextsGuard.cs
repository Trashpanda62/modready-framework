// ModReady.ButterLib -- GameTextsGuard
//
// Original work. MIT, copyright 2026 Maxfield Management Group.
//
// PROBLEM (diagnosed 2026-07-05 from a save-load "silent crash"):
//   The native static ctor of TaleWorlds.CampaignSystem.ViewModelCollection.
//   CampaignUIHelper initializes tooltip fields via GameTexts.FindText(...),
//   which is just `return _gameTextManager.FindText(id, variation);`. If a mod
//   queries campaign UI text BEFORE the game text manager is assigned (Retinues
//   is the canonical trigger), _gameTextManager is null -> NullReferenceException
//   inside CampaignUIHelper's .cctor. The CLR then PERMANENTLY poisons the type
//   (type-init failures are cached), so the later, legitimate map-screen build
//   re-throws the cached TypeInitializationException -> MapInfoVM can't construct
//   -> the map never finishes loading. BEW swallows it, so it presents as a
//   silent hang on save-load rather than a crash dialog.
//
// FIX:
//   A prefix on GameTexts.FindText(string, string): when _gameTextManager is
//   still null, return a harmless empty TextObject instead of letting the call
//   NRE. That lets CampaignUIHelper's .cctor complete, so the type is never
//   poisoned and the map screen builds normally once real text is available.
//   In the healthy state (manager set) the prefix is a single null-check that
//   returns true -> original runs unchanged. Zero behavior change when healthy.
//
// Reflection-only (AccessTools) so ButterLib needs no compile-time reference to
// GameTexts / TextObject; __result is typed object (Harmony boxes the return).

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

using HarmonyLib;

using ModReady.Foundation;

namespace Bannerlord.ButterLib.ExceptionHandler;

public static class GameTextsGuard
{
    private const string Tag = "GameTextsGuard";
    private static int _installed;
    private static int _guardHits;

    private static FieldInfo? _mgrField;
    private static ConstructorInfo? _textObjectCtor;

    public static void Install()
    {
        if (Interlocked.CompareExchange(ref _installed, 1, 0) != 0) return;
        try
        {
            var gameTexts = AccessTools.TypeByName("TaleWorlds.Core.GameTexts");
            if (gameTexts == null) { DiagLog.Log(Tag, "TaleWorlds.Core.GameTexts not found; guard not installed."); return; }

            var find = AccessTools.Method(gameTexts, "FindText", new[] { typeof(string), typeof(string) });
            if (find == null) { DiagLog.Log(Tag, "GameTexts.FindText(string,string) not found; guard not installed."); return; }

            _mgrField = AccessTools.Field(gameTexts, "_gameTextManager");
            var textObject = AccessTools.TypeByName("TaleWorlds.Localization.TextObject");
            _textObjectCtor = textObject?.GetConstructor(new[] { typeof(string), typeof(Dictionary<string, object>) });

            if (_mgrField == null || _textObjectCtor == null)
            {
                DiagLog.Log(Tag, $"missing member (_gameTextManager={_mgrField != null}, TextObject.ctor={_textObjectCtor != null}); guard not installed.");
                return;
            }

            var harmony = new HarmonyLib.Harmony("modready.butterlib.gametextsguard");
            harmony.Patch(find, prefix: new HarmonyMethod(typeof(GameTextsGuard), nameof(Prefix)));
            DiagLog.Log(Tag, "installed: GameTexts.FindText null-manager guard (prevents CampaignUIHelper type-init poisoning -> the Retinues-class save-load map-screen hang)");
        }
        catch (Exception ex)
        {
            try { DiagLog.LogCaught(Tag, "Install", ex); } catch { }
        }
    }

    // Return true = run the original FindText (healthy). Return false = we
    // supplied __result, skip the original (would have NRE'd).
    private static bool Prefix(string id, ref object? __result)
    {
        try
        {
            if (_mgrField!.GetValue(null) != null) return true; // text manager present -> normal path

            __result = _textObjectCtor!.Invoke(new object?[] { id ?? string.Empty, null });
            if (Interlocked.Increment(ref _guardHits) == 1)
                DiagLog.Log(Tag, $"GameTexts.FindText('{id}') called before the text manager loaded -- returned empty TextObject to prevent CampaignUIHelper type-init poisoning (save-load map-screen crash guard).");
            return false;
        }
        catch
        {
            // If the guard itself fails, don't make things worse -- run the original.
            return true;
        }
    }
}

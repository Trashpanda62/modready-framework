// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.
//
// Real hotkey input wiring (Phase 2C / H14, decision 2026-06-10: real
// wiring over warned stub). Upstream BUTR ButterLib registers hotkeys as
// TaleWorlds GameKeys in a GameKeyContext (which also makes them rebindable
// in Options). BetaDeps takes the version-stable route instead: poll
// TaleWorlds.InputSystem.Input for each registered key's DefaultKey once
// per application tick and dispatch the HotKeyBase lifecycle:
//
//   IsKeyPressed  -> OnPressedInternal   (edge: down this frame)
//   IsKeyDown     -> IsDownInternal      (level: held)
//   IsKeyReleased -> OnReleasedInternal  (+ IsDownAndReleasedInternal when
//                                         the key was observed held first)
//
// IsEnabled and Predicate gate dispatch per upstream semantics. What this
// deliberately does NOT do: create GameKeys or surface the keys in the
// game's Options for rebinding -- HotKeyManagerImpl.Build emits a
// CompatWarn so that remaining gap is visible to users.
//
// Tick() is called from ButterLibSubModule.OnApplicationTick (main thread,
// where the Input statics are valid). Registration swaps an immutable
// snapshot array so Tick never takes the lock.

using System;
using System.Collections.Generic;

using BetaDeps.Foundation;

using TaleWorlds.InputSystem;

namespace Bannerlord.ButterLib.HotKeys;

internal static class HotKeyTicker
{
    private const string Tag = "HotKeyTicker";

    private sealed class Entry
    {
        public HotKeyBase Key = null!;
        public string ModName = "?";
        public bool WasDown;
        public bool PredicateErrorLogged;
    }

    private static readonly object _gate = new();
    private static readonly List<Entry> _entries = new();
    private static Entry[] _snapshot = Array.Empty<Entry>();

    /// <summary>Activate a mod's built hotkeys for input polling. Re-registering
    /// the same HotKeyBase instance (Build called twice) is a no-op.</summary>
    public static void Register(IEnumerable<HotKeyBase> keys, string modName)
    {
        int added = 0, skipped = 0;
        lock (_gate)
        {
            foreach (var key in keys)
            {
                if (key == null) continue;
                if (_entries.Exists(e => ReferenceEquals(e.Key, key))) continue;
                if (key.DefaultKey == InputKey.Invalid)
                {
                    DiagLog.Log(Tag, $"'{key.Uid}' (mod {modName}) has DefaultKey=Invalid; it can never fire");
                    skipped++;
                    continue;
                }
                _entries.Add(new Entry { Key = key, ModName = modName });
                added++;
            }
            _snapshot = _entries.ToArray();
        }
        DiagLog.Log(Tag, $"mod '{modName}': {added} hotkey(s) now polled for input{(skipped > 0 ? $", {skipped} skipped (no default key)" : "")}");
    }

    /// <summary>Snapshot of every registered hotkey (for IHotKeyManagerStatic.HotKeys).</summary>
    public static IList<HotKeyBase> ActiveKeys
    {
        get
        {
            var snap = _snapshot;
            var list = new List<HotKeyBase>(snap.Length);
            foreach (var e in snap) list.Add(e.Key);
            return list;
        }
    }

    /// <summary>Poll input and dispatch. Called once per frame on the main thread.</summary>
    public static void Tick()
    {
        var snap = _snapshot;
        for (int i = 0; i < snap.Length; i++)
        {
            var entry = snap[i];
            var key = entry.Key;
            try
            {
                bool enabled = key.IsEnabled;
                if (enabled && key.Predicate != null)
                {
                    try { enabled = key.Predicate(); }
                    catch (Exception ex)
                    {
                        enabled = false;
                        if (!entry.PredicateErrorLogged)
                        {
                            entry.PredicateErrorLogged = true;
                            DiagLog.LogCaught(Tag, $"Predicate of '{key.Uid}' (mod {entry.ModName}) threw; treated as false (logged once)", ex);
                        }
                    }
                }
                if (!enabled)
                {
                    entry.WasDown = false;
                    continue;
                }

                var input = key.DefaultKey;
                bool pressedThisFrame = Input.IsKeyPressed(input);
                if (pressedThisFrame) key.OnPressedInternal();
                if (Input.IsKeyDown(input))
                {
                    // The rising-edge frame belongs to OnPressed alone; IsDown
                    // fires on the subsequent held frames (upstream semantics).
                    if (!pressedThisFrame) key.IsDownInternal();
                    entry.WasDown = true;
                }
                if (Input.IsKeyReleased(input))
                {
                    key.OnReleasedInternal();
                    if (entry.WasDown) key.IsDownAndReleasedInternal();
                    entry.WasDown = false;
                }
            }
            catch (Exception ex)
            {
                DiagLog.LogCaught(Tag, $"Tick('{key?.Uid}')", ex);
            }
        }
    }
}

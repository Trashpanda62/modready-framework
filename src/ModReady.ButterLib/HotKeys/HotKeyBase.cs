// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Public API mirror of upstream BUTR ButterLib v2.7.2's HotKeyBase. Consumer
// mods (FluidCombatLite's FluidAttackKey / FluidBlockKey etc.) derive from
// this class and override OnPressed / OnReleased / IsDown at `protected
// override` access. Property names, types, and visibility levels here must
// match upstream EXACTLY so the CLR's type loader accepts the consumer mod's
// overrides without "cannot reduce access" errors.

using System;

using ModReady.Foundation;

using TaleWorlds.InputSystem;

namespace Bannerlord.ButterLib.HotKeys;

public abstract class HotKeyBase
{
    // Internal seam for the input system to associate engine-side data.
    internal int Id { get; set; }
    internal GameKey? GameKey { get; set; }

    /// <summary>The unique-to-your-mod id for this hotkey.</summary>
    protected internal string Uid { get; }

    /// <summary>Display name in the options menu.</summary>
    protected internal virtual string DisplayName { get; }

    /// <summary>Description text in the options menu.</summary>
    protected internal virtual string Description { get; } = "No Description Set.";

    /// <summary>Default key if none is set explicitly.</summary>
    protected internal virtual InputKey DefaultKey { get; } = InputKey.Invalid;

    /// <summary>The Category in the options menu under which this hotkey appears.</summary>
    protected internal virtual string Category { get; } = string.Empty;

    /// <summary>Optional predicate that must return true for the key to process input.</summary>
    public Func<bool>? Predicate { get; set; }

    /// <summary>Whether this key is currently enabled for input processing.</summary>
    public bool IsEnabled { get; set; } = true;

    // Public events consumer mods may subscribe to.
    public event Action? OnPressedEvent;
    public event Action? OnReleasedEvent;
    public event Action? IsDownEvent;
    public event Action? IsDownAndReleasedEvent;

    /// <summary>Minimal ctor: just a uid; DisplayName defaults to the uid.</summary>
    protected internal HotKeyBase(string uid)
    {
        Uid = uid ?? string.Empty;
        DisplayName = Uid;
    }

    /// <summary>Full ctor: uid, display name, description, default key, category.</summary>
    protected internal HotKeyBase(string uid, string displayName, string description, InputKey defaultKey, string category)
    {
        Uid = uid ?? string.Empty;
        DisplayName = displayName ?? string.Empty;
        Description = description ?? "No Description Set.";
        DefaultKey = defaultKey;
        Category = category ?? string.Empty;
    }

    // Implicit conversion to GameKey, used at call sites that expect a GameKey.
    // ModReady polls hotkeys directly (HotKeyTicker) and never creates
    // TaleWorlds GameKeys, so this conversion has nothing to return -- warn
    // through the compat channel before throwing so the gap is reportable.
    public static implicit operator GameKey(HotKeyBase hotKey)
    {
        if (hotKey?.GameKey is { } gameKey) return gameKey;
        CompatWarn.Once("ButterLib.HotKeys", "implicit operator GameKey",
            hotKey?.GetType().Assembly.GetName().Name,
            "ModReady dispatches hotkeys by polling and does not create TaleWorlds GameKeys; this conversion always fails");
        throw new InvalidOperationException("HotKeyBase has no associated GameKey yet.");
    }

    // Override-points consumer mods derive (protected virtual matches upstream).
    protected virtual void OnPressed() { }
    protected virtual void OnReleased() { }
    protected virtual void IsDown() { }
    protected virtual void IsDownAndReleased() { }

    // Internal dispatch helpers. Stub bodies; not wired to the input pipeline
    // yet, but present so any internal caller compiles cleanly.
    internal void OnPressedInternal()
    {
        try { OnPressedEvent?.Invoke(); } catch { }
        try { OnPressed(); } catch { }
    }
    internal void OnReleasedInternal()
    {
        try { OnReleasedEvent?.Invoke(); } catch { }
        try { OnReleased(); } catch { }
    }
    internal void IsDownInternal()
    {
        try { IsDownEvent?.Invoke(); } catch { }
        try { IsDown(); } catch { }
    }
    internal void IsDownAndReleasedInternal()
    {
        try { IsDownAndReleasedEvent?.Invoke(); } catch { }
        try { IsDownAndReleased(); } catch { }
    }
}

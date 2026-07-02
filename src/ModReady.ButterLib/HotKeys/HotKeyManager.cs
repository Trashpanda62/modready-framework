// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Public API mirror of upstream BUTR ButterLib v2.7.2's HotKeyManager.
// Abstract base with static factory methods that dispatch through an
// internal IHotKeyManagerStatic. The concrete implementation
// (HotKeyManagerImpl) is published by ButterLibSubModule.OnSubModuleLoad
// into the DI container so the static factories return a usable instance.
//
// Method signatures MUST match upstream exactly -- consumer mods like FCL
// reference them through abstract dispatch, and any return-type or arity
// mismatch shows up as MissingMethodException at JIT time.

using System;
using System.Collections.Generic;

using ModReady.Foundation;

using TaleWorlds.MountAndBlade;

namespace Bannerlord.ButterLib.HotKeys;

public abstract class HotKeyManager
{
    private const string Tag = "HotKeyManager";

    private static IHotKeyManagerStatic? _staticInstance;

    /// <summary>
    /// Internal dispatcher for the static factory methods. Set during
    /// ButterLibSubModule.OnSubModuleLoad with a default no-op manager so
    /// consumer mods always get a non-null result.
    /// </summary>
    internal static IHotKeyManagerStatic? StaticInstance
    {
        get => _staticInstance;
        set => _staticInstance = value;
    }

    /// <summary>
    /// Map from HotKeyCategory to the TaleWorlds-side category string.
    /// Matches upstream BUTR's published mapping.
    /// </summary>
    public static readonly IReadOnlyDictionary<HotKeyCategory, string> Categories = new Dictionary<HotKeyCategory, string>
    {
        { HotKeyCategory.Action,       "ActionCategory" },
        { HotKeyCategory.Chat,         "ChatCategory" },
        { HotKeyCategory.CampaignMap,  "CampaignMapCategory" },
        { HotKeyCategory.MenuShortcut, "MenuShortcutCategory" },
        { HotKeyCategory.OrderMenu,    "OrderMenuCategory" }
    };

    /// <summary>Create a new HotKey group under a built-in category.</summary>
    public static HotKeyManager? Create(string modName)
    {
        try { return StaticInstance?.Create(modName); }
        catch (Exception ex) { try { DiagLog.LogCaught(Tag, $"Create({modName})", ex); } catch { } return null; }
    }

    /// <summary>Create a new HotKey group under its own custom category.</summary>
    public static HotKeyManager? CreateWithOwnCategory(string modName, string categoryName)
    {
        try { return StaticInstance?.CreateWithOwnCategory(modName, categoryName); }
        catch (Exception ex) { try { DiagLog.LogCaught(Tag, $"CreateWithOwnCategory({modName})", ex); } catch { } return null; }
    }

    /// <summary>Add an already-constructed hotkey to this manager.</summary>
    public abstract T Add<T>(T hotkey) where T : HotKeyBase;

    /// <summary>Construct + add a new hotkey of type T to this manager.</summary>
    public abstract T Add<T>() where T : HotKeyBase, new();

    /// <summary>Finalize the manager and return all registered hotkeys.</summary>
    public abstract IReadOnlyList<HotKeyBase> Build();
}

/// <summary>
/// Default HotKeyManager. Stores keys in-memory during the Add phase; Build()
/// hands them to <see cref="HotKeyTicker"/>, which polls the input system
/// every application tick and fires OnPressed / OnReleased / IsDown /
/// IsDownAndReleased on each key's DefaultKey (Phase 2C real wiring).
/// In-game rebinding via Options is not implemented yet -- Build emits a
/// CompatWarn so that residual gap stays visible.
/// </summary>
internal sealed class HotKeyManagerImpl : HotKeyManager
{
    private const string Tag = "HotKeyManager";
    private readonly string _modName;
    private readonly string? _categoryName;
    private readonly List<HotKeyBase> _keys = new();
    private bool _built;

    public HotKeyManagerImpl(string modName, string? categoryName)
    {
        _modName = modName ?? "?";
        _categoryName = categoryName;
        try { DiagLog.Log(Tag, $"new HotKeyManagerImpl(mod={_modName}, category={_categoryName ?? "default"})"); } catch { }
    }

    public override T Add<T>(T hotkey)
    {
        if (hotkey != null) _keys.Add(hotkey);
        try { DiagLog.Log(Tag, $"  Add<{typeof(T).Name}>(instance) -> mod '{_modName}'"); } catch { }
        return hotkey!;
    }

    public override T Add<T>()
    {
        T key;
        try { key = new T(); }
        catch (Exception ex) { try { DiagLog.LogCaught(Tag, $"Add<{typeof(T).FullName}> ctor", ex); } catch { } return default!; }
        _keys.Add(key);
        try { DiagLog.Log(Tag, $"  Add<{typeof(T).Name}>() -> mod '{_modName}'"); } catch { }
        return key;
    }

    public override IReadOnlyList<HotKeyBase> Build()
    {
        if (!_built)
        {
            _built = true;

            // Register a GameKeyContext so the keys show in Options > Keybinds
            // and player rebinds persist with the keybind profile. On failure
            // (game-version drift in the InputSystem surface) the keys still
            // fire on their defaults via the ticker -- degraded, and warned.
            try
            {
                var categoryId = _categoryName ?? _modName;
                var context = new HotKeyCategoryContainer(categoryId, _keys);
                global::TaleWorlds.InputSystem.HotKeyManager.RegisterContext(context, ignoreSerialize: false);
                DiagLog.Log(Tag, $"registered GameKeyContext '{categoryId}' -- keys visible in Options > Keybinds");
            }
            catch (Exception ex)
            {
                DiagLog.LogCaught(Tag, $"GameKeyContext registration for '{_modName}'", ex);
                CompatWarn.Once("ButterLib.HotKeys", "Build", _modName,
                    "hotkeys fire on their default keys, but Options keybind registration failed on this game version (see runtime.log)");
            }

            HotKeyTicker.Register(_keys, _modName);
            try { DiagLog.Log(Tag, $"Build() mod '{_modName}' with {_keys.Count} keys (input polling active)"); } catch { }
        }
        return _keys.ToArray();
    }
}

/// <summary>
/// Internal singleton that backs HotKeyManager.StaticInstance. Returns a
/// fresh HotKeyManagerImpl from Create / CreateWithOwnCategory; HotKeys
/// reflects every key activated through Build() (the ticker's registry).
/// </summary>
internal sealed class DefaultHotKeyManagerStatic : IHotKeyManagerStatic
{
    public IList<HotKeyBase> HotKeys => HotKeyTicker.ActiveKeys;

    public HotKeyManager Create(string modName) => new HotKeyManagerImpl(modName, null);
    public HotKeyManager CreateWithOwnCategory(string modName, string categoryName)
        => new HotKeyManagerImpl(modName, categoryName);
}

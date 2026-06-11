// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.
//
// GameKeyContext for a consumer mod's hotkey group. Registering one of
// these with TaleWorlds.InputSystem.HotKeyManager plus the
// OptionsProvider whitelist postfix (OptionsKeybindCategoryPatch) is what
// makes the keys appear in Options > Keybinds, rebindable and persisted
// with the player's keybind profile.
//
// Why GameKeyContextType.Default and not AuxiliarySerializedAndShownIn
// Options: decompiled GameKeyOptionCategoryVM (game 1.4.5, 2026-06-11)
// shows the rebindable list is built ONLY from Default-type contexts'
// RegisteredGameKeys, keyed by GameKey.MainCategoryId against a
// hardcoded whitelist (OptionsProvider.GetGameKeyCategoriesList); the
// "ShownInOptions" auxiliary type only surfaces HotKey combos, which are
// a different, non-rebindable class.
//
// The Options screen resolves display strings through the global text
// manager:
//   str_key_category_name . <categoryId>            (the section header)
//   str_key_name          . <groupId>_<stringId>    (each key's label)
//   str_key_description   . <groupId>_<stringId>    (each key's tooltip)
// so the constructor registers those variations from the HotKeyBase
// metadata before registering the GameKeys themselves.
//
// API shapes verified against the installed game's TaleWorlds.InputSystem
// on 2026-06-11: GameKeyContext(string id, int gameKeysCount,
// GameKeyContextType type); RegisterGameKey(GameKey gameKey, bool
// addIfMissing); GameKey(int id, string stringId, string groupId,
// InputKey defaultKeyboardKey, string mainCategoryId).

using System;
using System.Collections.Generic;

using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace Bannerlord.ButterLib.HotKeys;

internal sealed class HotKeyCategoryContainer : GameKeyContext
{
    private static readonly object _gate = new();
    private static readonly HashSet<string> _categoryIds = new(StringComparer.Ordinal);

    /// <summary>Category ids of every registered mod hotkey container.
    /// OptionsKeybindCategoryPatch appends these to the game's hardcoded
    /// keybind-category whitelist so the sections actually render.</summary>
    internal static string[] RegisteredCategoryIds
    {
        get { lock (_gate) { var a = new string[_categoryIds.Count]; _categoryIds.CopyTo(a); return a; } }
    }

    public HotKeyCategoryContainer(string categoryId, IReadOnlyList<HotKeyBase> keys)
        : base(categoryId, keys.Count, GameKeyContextType.Default)
    {
        lock (_gate) { _categoryIds.Add(categoryId); }

        var textManager = Module.CurrentModule.GlobalTextManager;
        var noTags = new List<GameTextManager.ChoiceTag>();
        textManager.AddGameText("str_key_category_name")
            .AddVariationWithId(categoryId, new TextObject(categoryId), noTags);
        var nameText = textManager.AddGameText("str_key_name");
        var descText = textManager.AddGameText("str_key_description");

        int id = 0;
        foreach (var key in keys)
        {
            if (key == null || key.DefaultKey == InputKey.Invalid) continue;
            var variation = categoryId + "_" + key.Uid;
            nameText.AddVariationWithId(variation, new TextObject(key.DisplayName ?? key.Uid), noTags);
            descText.AddVariationWithId(variation, new TextObject(key.Description ?? string.Empty), noTags);

            var gameKey = new GameKey(id, key.Uid, categoryId, key.DefaultKey, categoryId);
            RegisterGameKey(gameKey, true);
            key.GameKey = gameKey;
            key.Id = id;
            id++;
        }
    }
}

// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.
//
// GameKeyContext for a consumer mod's hotkey group. Registering one of
// these with TaleWorlds.InputSystem.HotKeyManager is what makes the keys
// appear in Options > Keybinds (rebindable, persisted with the player's
// keybind profile). GameKeyContextType.AuxiliarySerializedAndShownInOptions
// gives both the Options row and bind serialization.
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

using System.Collections.Generic;

using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace Bannerlord.ButterLib.HotKeys;

internal sealed class HotKeyCategoryContainer : GameKeyContext
{
    public HotKeyCategoryContainer(string categoryId, IReadOnlyList<HotKeyBase> keys)
        : base(categoryId, keys.Count, GameKeyContextType.AuxiliarySerializedAndShownInOptions)
    {
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

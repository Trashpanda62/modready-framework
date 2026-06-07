// BetaDeps.MCM -- PresetOptionVM
//
// Phase 2.4 (UI polish): one row in the preset "Custom" dropdown popup. The
// mixin exposes MBBindingList<PresetOptionVM> PresetOptions; the prefab binds a
// list to {PresetOptions}, and each row's ExecuteSelect calls back into the
// mixin to apply that preset (or the "(Current settings)" / "(Save current
// as new...)" sentinels). A real ViewModel so its @binding/Command resolve
// natively once ItemTemplate hands the row VM to the widget.
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using System;

using Bannerlord.UIExtenderEx.Attributes;

using BetaDeps.Foundation;

using TaleWorlds.Library;

namespace MCM.UI.PrefabExtensions;

public sealed class PresetOptionVM : ViewModel
{
    private const string Tag = "PresetOptionVM";

    private readonly string _value;          // the preset name / sentinel
    private readonly Action<string>? _onSelect;

    public PresetOptionVM(string value, Action<string>? onSelect)
    {
        _value = value ?? string.Empty;
        _onSelect = onSelect;
    }

    [DataSourceProperty] public string DisplayName => _value;

    [DataSourceMethod]
    public void ExecuteSelect()
    {
        try { _onSelect?.Invoke(_value); }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "ExecuteSelect", ex); }
    }
}

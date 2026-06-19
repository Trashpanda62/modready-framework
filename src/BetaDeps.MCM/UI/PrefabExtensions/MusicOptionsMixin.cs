// BetaDeps.MCM -- MusicOptionsMixin
//
// Surfaces the BYO music picker INSIDE the native Options > Sound tab, below the
// volume sliders. Two cooperating mixins:
//
//   MusicOptionsRootMixin  attaches to the root OptionsVM and captures the
//                          AudioOptions category instance. This is how we tell
//                          the audio category apart from Video/Gameplay WITHOUT
//                          matching a localized label -- we compare the category
//                          VM by reference.
//
//   MusicCategoryMixin     attaches to every GroupedOptionCategoryVM. Only the one
//                          that == the captured AudioOptions instance exposes the
//                          music rows; the others stay empty/hidden.
//
// IMPORTANT (timing): the root OptionsVM mixin attaches AFTER the per-category
// mixins (observed in runtime.log: categories at t+356ms, root at t+515ms). So
// the audio-category match CANNOT be decided eagerly in the category ctor -- the
// AudioOptions instance isn't captured yet. BetaDepsMusicVisible / BetaDepsMusicRows
// are therefore evaluated LAZILY (the binding reads them at render time, well
// after capture), and OnRefresh re-notifies so a tab switch re-reads them.
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using System;
using System.Reflection;

using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Extensions;   // ViewModel.NotifyPropertyChanged
using Bannerlord.UIExtenderEx.ViewModels;

using BetaDeps.Foundation;
using BetaDeps.Harmony.Music;

using TaleWorlds.Library;

namespace MCM.UI.PrefabExtensions;

/// <summary>Shared state: the live OptionsVM.AudioOptions category instance.</summary>
internal static class MusicUiState
{
    public static object? AudioCategory;
    private static bool _logged;

    public static void Capture(object? optionsVm)
    {
        try
        {
            if (optionsVm == null) return;
            // Resolve the AudioOptions getter robustly (property, then getter method).
            var t = optionsVm.GetType();
            var prop = t.GetProperty("AudioOptions", BindingFlags.Public | BindingFlags.Instance);
            var val = prop != null
                ? prop.GetValue(optionsVm)
                : t.GetMethod("get_AudioOptions", BindingFlags.Public | BindingFlags.Instance)?.Invoke(optionsVm, null);
            if (val != null)
            {
                AudioCategory = val;
                if (!_logged) { _logged = true; DiagLog.Log("MusicUiState", $"captured AudioOptions ({val.GetType().Name})."); }
            }
            else if (!_logged)
            {
                _logged = true;
                DiagLog.Log("MusicUiState", "AudioOptions property returned null at capture; music UI will not bind. " +
                    "Check the OptionsVM audio property name.");
            }
        }
        catch (Exception ex) { DiagLog.LogCaught("MusicUiState", "Capture", ex); }
    }
}

// v1.1 BYO music picker -- SHELVED for v1.0.0; see MusicOptionsPatch.cs to re-enable.
// [ViewModelMixin(
//     HandleDerived = true,
//     TargetTypeName = "TaleWorlds.MountAndBlade.ViewModelCollection.GameOptions.OptionsVM")]
internal sealed class MusicOptionsRootMixin : BaseViewModelMixin<ViewModel>
{
    public MusicOptionsRootMixin(ViewModel vm) : base(vm) { MusicUiState.Capture(ViewModel); }
    public override void OnRefresh() => MusicUiState.Capture(ViewModel);
}

// v1.1 BYO music picker -- SHELVED for v1.0.0; see MusicOptionsPatch.cs to re-enable.
// [ViewModelMixin(
//     HandleDerived = true,
//     TargetTypeName = "TaleWorlds.MountAndBlade.ViewModelCollection.GameOptions.GroupedOptionCategoryVM")]
internal sealed class MusicCategoryMixin : BaseViewModelMixin<ViewModel>
{
    private const string Tag = "MusicCategoryMixin";

    private MBBindingList<MusicRowVM> _rows = new();
    private bool _built;
    private bool _boundLogged;

    public MusicCategoryMixin(ViewModel vm) : base(vm) { }

    // The audio category isn't known until the root mixin captures AudioOptions
    // (which attaches AFTER us). Re-notify on every refresh so the binding
    // re-reads the (now lazy) visibility + rows once capture has happened.
    public override void OnRefresh()
    {
        try
        {
            ViewModel.NotifyPropertyChanged(nameof(BetaDepsMusicVisible));
            ViewModel.NotifyPropertyChanged(nameof(BetaDepsMusicRows));
        }
        catch { }
    }

    private bool IsAudioCategory()
        => MusicUiState.AudioCategory != null && ReferenceEquals(ViewModel, MusicUiState.AudioCategory);

    [DataSourceProperty]
    public bool BetaDepsMusicVisible
    {
        get
        {
            var audio = IsAudioCategory();
            if (audio) EnsureBuilt();
            return audio;
        }
    }

    [DataSourceProperty]
    public MBBindingList<MusicRowVM> BetaDepsMusicRows
    {
        get { EnsureBuilt(); return _rows; }
    }

    private void EnsureBuilt()
    {
        if (_built || !IsAudioCategory()) return;
        try
        {
            var cfg = MusicConfig.Current;
            var list = new MBBindingList<MusicRowVM>();
            if (cfg != null)
            {
                foreach (MusicContext ctx in Enum.GetValues(typeof(MusicContext)))
                    list.Add(new MusicRowVM(ctx));
            }
            _rows = list;
            _built = true;
            if (!_boundLogged)
            {
                _boundLogged = true;
                DiagLog.Log(Tag, $"BYO music UI bound to the audio category ({list.Count} row(s)).");
            }
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "EnsureBuilt", ex); }
    }
}

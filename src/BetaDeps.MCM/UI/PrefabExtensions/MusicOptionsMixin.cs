// BetaDeps.MCM -- MusicOptionsMixin
//
// Surfaces the BYO music picker INSIDE the native Options > Sound tab, below the
// volume sliders. Two cooperating mixins:
//
//   MusicOptionsRootMixin  attaches to the root OptionsVM and captures the
//                          AudioOptions category instance. This is how we tell
//                          the audio category apart from Video/Gameplay
//                          WITHOUT matching a localized label -- we compare the
//                          category VM by reference.
//
//   MusicCategoryMixin     attaches to every GroupedOptionCategoryVM (the type
//                          backing each <OptionsGroupedPage>). Only the one that
//                          == the captured AudioOptions instance exposes the
//                          music rows; the others stay empty/hidden. The
//                          MusicOptionsPatch panel (injected into AudioOptionsPage)
//                          binds {BetaDepsMusicRows} + @BetaDepsMusicVisible here.
//
// Both are fully guarded -- a mixin fault logs and leaves the vanilla audio tab
// untouched.
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
}

[ViewModelMixin(
    HandleDerived = true,
    TargetTypeName = "TaleWorlds.MountAndBlade.ViewModelCollection.GameOptions.OptionsVM")]
internal sealed class MusicOptionsRootMixin : BaseViewModelMixin<ViewModel>
{
    private const string Tag = "MusicOptionsRootMixin";

    public MusicOptionsRootMixin(ViewModel vm) : base(vm) { Capture(); }

    public override void OnRefresh() => Capture();

    private void Capture()
    {
        try
        {
            var p = ViewModel?.GetType().GetProperty("AudioOptions", BindingFlags.Public | BindingFlags.Instance);
            var v = p?.GetValue(ViewModel);
            if (v != null) MusicUiState.AudioCategory = v;
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "Capture", ex); }
    }
}

[ViewModelMixin(
    HandleDerived = true,
    TargetTypeName = "TaleWorlds.MountAndBlade.ViewModelCollection.GameOptions.GroupedOptionCategoryVM")]
internal sealed class MusicCategoryMixin : BaseViewModelMixin<ViewModel>
{
    private const string Tag = "MusicCategoryMixin";

    private MBBindingList<MusicRowVM> _rows = new();
    private bool _visible;
    private bool _built;

    public MusicCategoryMixin(ViewModel vm) : base(vm) { Build(); }

    public override void OnRefresh() => Build();

    [DataSourceProperty] public MBBindingList<MusicRowVM> BetaDepsMusicRows => _rows;

    [DataSourceProperty]
    public bool BetaDepsMusicVisible
    {
        get => _visible;
        set
        {
            if (_visible == value) return;
            _visible = value;
            try { ViewModel.NotifyPropertyChanged(nameof(BetaDepsMusicVisible)); } catch { }
        }
    }

    private void Build()
    {
        try
        {
            bool isAudio = MusicUiState.AudioCategory != null
                           && ReferenceEquals(ViewModel, MusicUiState.AudioCategory);
            if (!isAudio) { BetaDepsMusicVisible = false; return; }

            if (!_built)
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
                try { ViewModel.NotifyPropertyChanged(nameof(BetaDepsMusicRows)); } catch { }
                DiagLog.Log(Tag, $"BYO music UI bound to the audio category ({list.Count} row(s)).");
            }
            BetaDepsMusicVisible = true;
        }
        catch (Exception ex) { DiagLog.LogCaught(Tag, "Build", ex); }
    }
}

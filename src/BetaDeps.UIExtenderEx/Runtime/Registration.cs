// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield
// Management Group.
//
// Runtime registration records. These are the data structures the
// UIExtender class fills in during Register() and that the runtime engine
// (GauntletMovie hook + VM mixin proxy) reads at apply time.

using System;
using System.Collections.Generic;

using Bannerlord.UIExtenderEx.Attributes;

namespace Bannerlord.UIExtenderEx.Runtime;

/// <summary>One registered [PrefabExtension] patch class.</summary>
internal sealed class PrefabRegistration
{
    public string ModuleName { get; }
    public Type PatchType { get; }
    public PrefabExtensionAttribute Attribute { get; }

    public PrefabRegistration(string moduleName, Type patchType, PrefabExtensionAttribute attribute)
    {
        ModuleName = moduleName;
        PatchType = patchType;
        Attribute = attribute;
    }
}

/// <summary>One registered [ViewModelMixin] class plus its target VM type.</summary>
internal sealed class MixinRegistration
{
    public string ModuleName { get; }
    public Type MixinType { get; }
    public Type TargetViewModelType { get; }
    public ViewModelMixinAttribute Attribute { get; }

    public MixinRegistration(string moduleName, Type mixinType, Type targetVmType, ViewModelMixinAttribute attribute)
    {
        ModuleName = moduleName;
        MixinType = mixinType;
        TargetViewModelType = targetVmType;
        Attribute = attribute;
    }
}

/// <summary>Per-module registration set. One instance per UIExtender.Create call.</summary>
internal sealed class UIExtenderRegistry
{
    public string ModuleName { get; }
    public List<PrefabRegistration> Prefabs { get; } = new();
    public List<MixinRegistration> Mixins { get; } = new();
    public bool Enabled { get; set; }

    public UIExtenderRegistry(string moduleName) { ModuleName = moduleName; }
}

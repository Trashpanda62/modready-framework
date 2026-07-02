# Phase 2 — ModReady.UIExtenderEx rewrite scope

Goal: a clean-room replacement for UIExtenderEx that's just enough to let MCM's in-game Options tab work, so CREST and any mod that depends on `Bannerlord.UIExtenderEx` loads cleanly on beta.

## Scope finding

Searched the full source tree for actual UIExtenderEx public-API usage. Only one consumer exists in this codebase: **Crest.MCM.UI**. CREST.Harmony itself does not call any UIExtenderEx public type.

Crest.MCM.UI uses:

- `[ViewModelMixin]`
- `BaseViewModelMixin<TVM>` (in namespace `Bannerlord.UIExtenderEx.ViewModels`)
- `[DataSourceMethod]`
- `[PrefabExtension(movie, xpath, autoGenWidgetName)]` (3-arg form)
- `PrefabExtensionInsertPatch` (with `InsertType Type` property)
- `PrefabExtensionSetAttributePatch`
- `[PrefabExtensionXmlNode]`
- `[PrefabExtensionXmlDocument]`
- `InsertType` enum

Plus the `UIExtender` class with `Create / Register / Verify / Enable` methods that whoever wires MCM.UI calls at startup.

That's the implementation target.

## Implementation target (minimum)

```
namespace Bannerlord.UIExtenderEx
{
    public class UIExtender
    {
        public static UIExtender Create(string moduleName);
        public static UIExtender? GetUIExtenderFor(string moduleName);
        public void Register(Assembly assembly);
        public void Verify();
        public void Enable();
    }
}

namespace Bannerlord.UIExtenderEx.Attributes
{
    public abstract class BaseUIExtenderAttribute : Attribute { }
    public sealed class PrefabExtensionAttribute : BaseUIExtenderAttribute
    {
        public string Movie { get; }
        public string? XPath { get; }
        public string? AutoGenWidgetName { get; }
        public PrefabExtensionAttribute(string movie, string? xpath = null);
        public PrefabExtensionAttribute(string movie, string? xpath, string? autoGenWidgetName);
    }
    public sealed class ViewModelMixinAttribute : BaseUIExtenderAttribute
    {
        public string? RefreshMethodName { get; }
        public bool HandleDerived { get; }
        public ViewModelMixinAttribute();
        public ViewModelMixinAttribute(string refreshMethodName);
        public ViewModelMixinAttribute(bool handleDerived);
        public ViewModelMixinAttribute(string? refreshMethodName, bool handleDerived);
    }
    public sealed class DataSourceMethodAttribute : Attribute { }
}

namespace Bannerlord.UIExtenderEx.Prefabs2
{
    public enum InsertType { Prepend, ReplaceKeepChildren, Replace, Child, Append, Remove }

    public abstract partial class PrefabExtensionInsertPatch
    {
        public abstract InsertType Type { get; }
        public abstract class PrefabExtensionContentAttribute : Attribute { }
        public abstract class PrefabExtensionSingleContentAttribute : PrefabExtensionContentAttribute
        {
            public bool RemoveRootNode { get; }
            protected PrefabExtensionSingleContentAttribute(bool removeRootNode);
        }
        public sealed class PrefabExtensionFileNameAttribute : PrefabExtensionSingleContentAttribute { ... }
        public sealed class PrefabExtensionTextAttribute : PrefabExtensionSingleContentAttribute { ... }
        public sealed class PrefabExtensionXmlDocumentAttribute : PrefabExtensionSingleContentAttribute { ... }
        public sealed class PrefabExtensionXmlNodeAttribute : PrefabExtensionSingleContentAttribute { ... }
        public sealed class PrefabExtensionXmlNodesAttribute : PrefabExtensionContentAttribute { ... }
    }
    public abstract class PrefabExtensionSetAttributePatch { ... }
}

namespace Bannerlord.UIExtenderEx.ViewModels
{
    public abstract class BaseViewModelMixin<TVM> where TVM : ViewModel
    {
        protected TVM ViewModel { get; }
        protected BaseViewModelMixin(TVM vm);
        public virtual void OnRefresh();
        public virtual void OnFinalize();
    }
}
```

## Runtime engine

To make those public types actually do something:

1. **GauntletMovie hook** — Harmony patch that intercepts the prefab loader. On the running beta target the method is `WidgetPrefab.LoadFrom` or `WidgetFactory.GetCustomType`; we'll sigsafe-bind whichever exists.
2. **Prefab XML patcher** — walks the registered patches, matches by `Movie` name, applies the XPath selection + content-attribute-driven edit (Insert/Replace/SetAttribute/Remove).
3. **ViewModel mixin proxy** — Harmony postfix on the target VM constructor. Each constructed instance gets its mixin attached and its `[DataSourceProperty]` / `[DataSourceMethod]` members appended to the VM's data-source dictionary so Gauntlet bindings find them.

## What stays unimplemented in Phase 2

- `WidgetFactoryManager` custom-type registration (we don't ship custom widgets, only patch existing ones).
- `BrushFactoryManager` (same).
- Partial autogen support (only used in dev test builds).
- Tests/fixtures package.

These are explicit non-goals — adding them later is straightforward but not required to load MCM.

## Phase 2 deliverable

- `Bannerlord.UIExtenderEx.dll` shipped from `Modules\ModReady\bin\Win64_Shipping_Client\` (the actual assembly name; the project is `ModReady.UIExtenderEx` but the output filename matches the upstream name so consumer mods resolve)
- A `Modules\Bannerlord.UIExtenderEx\` alias folder with the structural files BLSE requires
- The build script extended to build, verify, and stage UIExtenderEx
- Smoke test: re-enable CREST in the launcher, launch, confirm the MCM tab appears in the Options screen

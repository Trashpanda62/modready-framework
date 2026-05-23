// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// ISettingsBuilder is MCM's alternative to AttributeGlobalSettings -- mods
// build their settings imperatively at runtime instead of declaring them via
// attributes on a class. Useful when settings depend on data the mod only
// knows after load (e.g. enumerating faction names).
//
// Two namespaces in one file:
//   - MCM.Abstractions.FluentBuilder        :  ISettingsBuilder, ISettingsPropertyGroupBuilder
//   - MCM.Abstractions.FluentBuilder.Models :  ISettingsPropertyBuilder + the per-type variants
// Upstream BUTR MCM splits them this way and consumer mod IL records the
// nested-namespace name for the property builders; if we collapse them into
// one namespace, the CLR throws TypeLoadException at consumer-mod JIT time.

using System;

using MCM.Abstractions.Base.Global;
using MCM.Abstractions.FluentBuilder.Models;
using MCM.Common;

namespace MCM.Abstractions.FluentBuilder
{

public interface ISettingsBuilder
{
    /// <summary>Settings Id (used as the JSON filename).</summary>
    string Id { get; }

    /// <summary>Display name shown in the MCM tab list.</summary>
    string DisplayName { get; }

    /// <summary>Add a group of properties under a named heading.</summary>
    ISettingsBuilder CreateGroup(string groupName, Action<ISettingsPropertyGroupBuilder> action);

    /// <summary>
    /// Sets the on-disk serialization format. Upstream BUTR MCM supports
    /// "json2" (default), "xml", "yaml". We always serialize as JSON; this
    /// is a no-op for API compatibility.
    /// </summary>
    ISettingsBuilder SetFormat(string format);

    /// <summary>Set the on-disk folder name. No-op stub.</summary>
    ISettingsBuilder SetFolderName(string folderName);

    /// <summary>Set the on-disk subfolder. No-op stub.</summary>
    ISettingsBuilder SetSubFolder(string subFolder);

    /// <summary>Set the subgroup delimiter for nested groups. No-op stub.</summary>
    ISettingsBuilder SetSubGroupDelimiter(string delimiter);

    /// <summary>
    /// Construct + register the underlying BaseSettings instance as a global
    /// setting and return it. Upstream BUTR returns the constructed
    /// FluentGlobalSettings so callers can chain .Register() after.
    /// </summary>
    FluentGlobalSettings BuildAsGlobal();
}

public interface ISettingsPropertyGroupBuilder
{
    string Name { get; }

    // Default-value overloads: the property starts at the given default and
    // mutates the FluentGlobalSettings's internal dictionary on write.
    ISettingsPropertyGroupBuilder AddBool(string id, string displayName, bool defaultValue, Action<ISettingsPropertyBoolBuilder>? configure = null);
    ISettingsPropertyGroupBuilder AddInteger(string id, string displayName, int min, int max, int defaultValue, Action<ISettingsPropertyIntegerBuilder>? configure = null);
    ISettingsPropertyGroupBuilder AddFloatingInteger(string id, string displayName, float min, float max, float defaultValue, Action<ISettingsPropertyFloatingIntegerBuilder>? configure = null);
    ISettingsPropertyGroupBuilder AddText(string id, string displayName, string defaultValue, Action<ISettingsPropertyTextBuilder>? configure = null);

    // IRef-based overloads: upstream BUTR MCM lets consumer mods bind a
    // property to an external IRef (PropertyRef, ProxyRef, etc.) so the
    // MCM panel reads/writes against that ref instead of holding the value
    // in the builder. Required for mods that already have their own state
    // store and just want MCM as the UI shim.
    ISettingsPropertyGroupBuilder AddBool(string id, string displayName, IRef @ref, Action<ISettingsPropertyBoolBuilder>? configure = null);
    ISettingsPropertyGroupBuilder AddInteger(string id, string displayName, int min, int max, IRef @ref, Action<ISettingsPropertyIntegerBuilder>? configure = null);
    ISettingsPropertyGroupBuilder AddFloatingInteger(string id, string displayName, float min, float max, IRef @ref, Action<ISettingsPropertyFloatingIntegerBuilder>? configure = null);
    ISettingsPropertyGroupBuilder AddText(string id, string displayName, IRef @ref, Action<ISettingsPropertyTextBuilder>? configure = null);

    // Button overload: BEW (BetterExceptionWindow) and other MCMv5 fluent
    // consumers use this to surface clickable buttons in their settings page.
    // `@ref` wraps the click handler (typically a ProxyRef<Action>); `content`
    // is the text rendered on the button itself. The builder action lets the
    // caller configure HintText / Order / RequireRestart on the button row.
    ISettingsPropertyGroupBuilder AddButton(string id, string displayName, IRef @ref, string content, Action<ISettingsPropertyButtonBuilder>? configure = null);
}

// Generic, self-typed fluent property builder. Newer BUTR MCM revisions
// expose this in FluentBuilder (NOT .Models) and have the typed property
// builders inherit from it for return-type-preserving chaining. Consumer
// mod IL references the full type name; we just need the type to exist so
// the CLR's type loader doesn't throw TypeLoadException. Methods mirror the
// non-generic Models.ISettingsPropertyBuilder but return TPropertyBuilder.
public interface ISettingsPropertyBuilder<TPropertyBuilder>
{
    TPropertyBuilder SetOrder(int order);
    TPropertyBuilder SetRequireRestart(bool requireRestart);
    TPropertyBuilder SetHintText(string hintText);
}

}  // namespace MCM.Abstractions.FluentBuilder


namespace MCM.Abstractions.FluentBuilder.Models
{

// Non-generic base. Kept for source-level back-compat with any earlier
// BetaDeps internal call sites; the upstream BUTR API surface uses the
// generic ISettingsPropertyBuilder<TSelf> declared above. Consumer mods
// like BEW resolve SetHintText / SetOrder / SetRequireRestart through the
// GENERIC interface (the IL emits `ISettingsPropertyBuilder\`1::SetHintText`
// not `ISettingsPropertyBuilder::SetHintText`), so each typed builder below
// must inherit from the GENERIC self-typed form, NOT this one. If you wire
// a typed builder to this non-generic interface instead, the CLR throws
// EntryPointNotFoundException at consumer-mod JIT time.
public interface ISettingsPropertyBuilder
{
    ISettingsPropertyBuilder SetOrder(int order);
    ISettingsPropertyBuilder SetRequireRestart(bool requireRestart);
    ISettingsPropertyBuilder SetHintText(string hintText);
}

// Typed builders inherit ISettingsPropertyBuilder<TSelf> from the parent
// FluentBuilder namespace so SetHintText("...") resolved at the call site
// returns the typed builder (BUTR's F-bounded polymorphism pattern).
public interface ISettingsPropertyBoolBuilder            : MCM.Abstractions.FluentBuilder.ISettingsPropertyBuilder<ISettingsPropertyBoolBuilder> { }
public interface ISettingsPropertyIntegerBuilder         : MCM.Abstractions.FluentBuilder.ISettingsPropertyBuilder<ISettingsPropertyIntegerBuilder> { }
public interface ISettingsPropertyFloatingIntegerBuilder : MCM.Abstractions.FluentBuilder.ISettingsPropertyBuilder<ISettingsPropertyFloatingIntegerBuilder> { }
public interface ISettingsPropertyTextBuilder            : MCM.Abstractions.FluentBuilder.ISettingsPropertyBuilder<ISettingsPropertyTextBuilder> { }

}  // namespace MCM.Abstractions.FluentBuilder.Models

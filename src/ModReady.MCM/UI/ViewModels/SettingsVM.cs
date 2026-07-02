// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// SettingsVM -- per-mod ViewModel in the MCM tab. One instance per
// AttributeGlobalSettings or FluentGlobalSettings registered. Holds the
// mod's display name + the list of property groups.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using MCM.Abstractions;
using MCM.Abstractions.Attributes;
using MCM.Internal;
using MCM.UI.GUI.ViewModels;

using TaleWorlds.Library;

namespace MCM.UI.GUI.ViewModels;

public class SettingsVM : ViewModel
{
    public BaseSettings Settings { get; }

    private MBBindingList<SettingsPropertyGroupVM> _groups = new();
    private string _displayName = string.Empty;

    [DataSourceProperty]
    public string DisplayName
    {
        get => _displayName;
        set { _displayName = value; OnPropertyChangedWithValue(value, nameof(DisplayName)); }
    }

    [DataSourceProperty]
    public MBBindingList<SettingsPropertyGroupVM> SettingPropertyGroups
    {
        get => _groups;
        set { _groups = value; OnPropertyChangedWithValue(value, nameof(SettingPropertyGroups)); }
    }

    public SettingsVM(BaseSettings settings)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        DisplayName = TextHelper.StripLocalizationKeys(settings.DisplayName ?? settings.Id);
        BuildGroups();
    }

    private void BuildGroups()
    {
        _groups.Clear();

        // S3: built-in SubSystem settings page (no properties on the class itself).
        if (Settings is MCM.Internal.SubSystemSettingsPage)
        {
            BuildGroupsForSubSystems();
            return;
        }

        // Fluent-builder path: settings were declared via ISettingsBuilder
        // chains (Diplomacy, ImprovedGarrisons, RTSCamera, BetterSmithing,
        // etc.). The settings object has no [SettingPropertyX]-decorated
        // properties; the property data lives in the internal builder.
        if (Settings is MCM.Internal.IFluentSettings) // 2.3/H6: any fluent scope
        {
            BuildGroupsFluent(Settings);
            return;
        }

        // S1: Foreign-MCM-assembly adapter — discover properties on the wrapped
        // instance rather than the adapter shell (which has none). LiveGet /
        // WriteBack detect ForeignSettingsAdapter on _owner and automatically
        // use Wrapped for reflection, so passing Settings as owner is correct.
        var reflectionType = Settings is MCM.Internal.SettingsRegistry.ForeignSettingsAdapter fa
            ? fa.Wrapped.GetType()
            : Settings.GetType();

        BuildGroupsReflection(reflectionType);
    }

    /// <summary>Core reflection-based group builder used by both the normal attribute
    /// path and the S1 foreign-settings-adapter path.</summary>
    private void BuildGroupsReflection(Type reflectionType)
    {
        var grouped      = new Dictionary<string, List<SettingsPropertyVM>>(StringComparer.Ordinal);
        var groupOrders  = new Dictionary<string, int>(StringComparer.Ordinal);
        var groupToggles = new Dictionary<string, SettingsPropertyVM>(StringComparer.Ordinal);
        // Tie-break groups with equal GroupOrder by FIRST-SEEN (declaration)
        // order, NOT alphabetically -- MCM preserves the order the author
        // declared groups in, so e.g. "Community & Support" stays at the bottom
        // instead of sorting above "Disease System".
        var groupFirstSeen = new Dictionary<string, int>(StringComparer.Ordinal);
        int seenIndex = 0;

        foreach (var p in reflectionType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            // GetCustomAttribute<T>(inherit:true) throws AmbiguousMatchException
            // when a property has multiple attributes of the same type. ChatAi
            // (and probably others) declare duplicate [SettingPropertyText] on
            // some properties. Use the plural accessor + OfType + First to
            // gracefully accept and ignore the duplicates.
            var spa = p.GetCustomAttributes(inherit: true).OfType<SettingPropertyAttribute>().FirstOrDefault();
            if (spa == null) continue;
            var groupAttr = p.GetCustomAttributes(inherit: true).OfType<SettingPropertyGroupAttribute>().FirstOrDefault();
            var groupName = TextHelper.StripLocalizationKeys(groupAttr?.GroupName ?? string.Empty);

            // Ensure the group bucket exists before the IsMainToggle check so
            // toggle-only groups (no other properties) still appear.
            if (!grouped.ContainsKey(groupName))
            {
                grouped[groupName] = new List<SettingsPropertyVM>();
                groupOrders[groupName] = groupAttr?.GroupOrder ?? 0;
                groupFirstSeen[groupName] = seenIndex++;
            }

            // S2: [SettingPropertyGroup(IsMainToggle=true)] marks the property
            // that enables/disables the whole group. Pull it out of the child
            // list and store it separately so the group VM can render it as a
            // header toggle instead of a regular row.
            if (groupAttr?.IsMainToggle == true && !groupToggles.ContainsKey(groupName))
            {
                groupToggles[groupName] = SettingsPropertyVM.Create(Settings, p, spa);
                continue; // don't add to child list
            }

            grouped[groupName].Add(SettingsPropertyVM.Create(Settings, p, spa));
        }

        foreach (var kv in grouped.OrderBy(kv => groupOrders[kv.Key]).ThenBy(kv => groupFirstSeen[kv.Key]))
        {
            groupToggles.TryGetValue(kv.Key, out var toggleVm);
            var gvm = new SettingsPropertyGroupVM(kv.Key, kv.Value.OrderBy(v => v.Order).ToList(), toggleVm);
            _groups.Add(gvm);
        }
    }

    /// <summary>S3: Build one group of bool toggles from the ButterLib subsystem roster.</summary>
    private void BuildGroupsForSubSystems()
    {
        if (!ModReady.Foundation.SubSystemBridge.IsAvailable) return;

        var all = ModReady.Foundation.SubSystemBridge.GetAll!();
        if (all.Count == 0) return;

        var rows = new List<SettingsPropertyVM>();
        int order = 0;
        foreach (var s in all)
        {
            var captured = s; // capture loop variable for closures
            rows.Add(new SettingsPropertyVM(
                owner:       Settings,
                displayName: captured.Name,
                hintText:    captured.Desc,
                readFunc:    () => ModReady.Foundation.SubSystemBridge.GetEnabled?.Invoke(captured.Id) ?? captured.IsEnabled,
                writeAction: v =>
                {
                    bool on = v is bool b && b;
                    ModReady.Foundation.SubSystemBridge.SetEnabled?.Invoke(captured.Id, on);
                    ModReady.Foundation.SubSystemBridge.Save?.Invoke();
                },
                order: order++));
        }

        if (rows.Count > 0)
            _groups.Add(new SettingsPropertyGroupVM("Sub Systems", rows));
    }

    /// <summary>Reach into the fluent settings' internal builder, iterate
    /// every group and every property declared on it, and build a
    /// SettingsPropertyGroupVM tree mirroring what the attribute path
    /// produces for AttributeGlobalSettings.</summary>
    private void BuildGroupsFluent(MCM.Abstractions.BaseSettings fluent) // 2.3/H6: all three fluent scopes (each carries a `_builder` field)
    {
        // _builder is internal; reach via reflection to keep the public API
        // surface stable. SettingsBuilderImpl exposes a _groups list of
        // PropertyGroupBuilderImpl, each with a Name + a _properties list of
        // FluentProperty entries.
        var builderField = fluent.GetType().GetField("_builder",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var builder = builderField?.GetValue(fluent);
        if (builder == null) return;

        var groupsField = builder.GetType().GetField("_groups",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var groups = groupsField?.GetValue(builder) as System.Collections.IEnumerable;
        if (groups == null) return;

        int groupOrderCounter = 0;
        foreach (var group in groups)
        {
            if (group == null) continue;
            var nameProp = group.GetType().GetProperty("Name",
                BindingFlags.Public | BindingFlags.Instance);
            var groupName = TextHelper.StripLocalizationKeys(
                nameProp?.GetValue(group) as string ?? string.Empty);

            var propsField = group.GetType().GetField("_properties",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var props = propsField?.GetValue(group) as System.Collections.IEnumerable;
            if (props == null) continue;

            var rowVms = new List<SettingsPropertyVM>();
            foreach (var p in props)
            {
                if (p is not MCM.Abstractions.FluentBuilder.FluentProperty fp) continue;
                try
                {
                    rowVms.Add(new SettingsPropertyVM(fluent, fp));
                }
                catch (Exception ex)
                {
                    ModReady.Foundation.DiagLog.LogCaught(
                        "SettingsVM", $"BuildGroupsFluent/Add({fluent.Id}.{fp.Id})", ex);
                }
            }

            if (rowVms.Count == 0) continue;
            var gvm = new SettingsPropertyGroupVM(groupName, rowVms.OrderBy(v => v.Order).ToList());
            _groups.Add(gvm);
            groupOrderCounter++;
        }
    }

    /// <summary>Persist the current values to disk (every scope: global,
    /// per-save, per-campaign, and fluent).</summary>
    public void Apply()
    {
        // M15: the old gate (is BaseGlobalSettings) silently no-opped for
        // per-save, per-campaign, and fluent settings -- none of which derive
        // from BaseGlobalSettings -- so their "Done" clicks never persisted.
        // SettingsStorage.Save is the shared choke-point each scope's own Save()
        // already routes through; call it directly (same assembly) so one path
        // covers them all, including fluent settings that expose no Save().
        MCM.Internal.SettingsStorage.Save(Settings, Settings.Id);
    }

    /// <summary>Discard pending property changes; re-read from JSON.</summary>
    public void Revert()
    {
        // M15: reload THIS instance's values from disk and rebuild the bound
        // rows. The old static-Reset reflection no-opped for fluent/per-save
        // (no GlobalSettings<TSelf>.Reset to find) and, even for attribute
        // settings, only nulled the singleton -- the panel stayed bound to the
        // instance still holding its unsaved edits. Load is scope-aware and
        // handles both fluent and attribute settings, writing disk values back
        // over the in-memory edits = a real revert.
        MCM.Internal.SettingsStorage.Load(Settings, Settings.Id);
        BuildGroups();
    }

    /// <summary>Reset every property to its CLR default value.</summary>
    public void RestoreDefaults()
    {
        // Naive impl: create a fresh instance of TSelf and copy its property
        // values onto Settings via reflection.
        try
        {
            // Fluent settings have no [SettingProperty] attributes; route to the
            // fluent default-reset (Nexus v0.9.2 "Reset to Defaults does nothing").
            if (Settings is MCM.Internal.IFluentSettings fluent)
            {
                fluent.ResetToDefaults();
                BuildGroups();
                return;
            }
            var t = Settings.GetType();
            var fresh = Activator.CreateInstance(t);
            if (fresh == null) return;
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (p.GetCustomAttributes(inherit: true).OfType<SettingPropertyAttribute>().FirstOrDefault() == null) continue;
                if (!p.CanWrite) continue;
                p.SetValue(Settings, p.GetValue(fresh));
            }
            BuildGroups();
        }
        catch { /* best effort */ }
    }

    /// <summary>Re-evaluate property visibility against IsPropertyVisibleHook.</summary>
    public void RefreshVisibility()
    {
        foreach (var g in _groups) g.RefreshVisibility(Settings);
    }
}

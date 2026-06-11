// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
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

        // Fluent-builder path: settings were declared via ISettingsBuilder
        // chains (Diplomacy, ImprovedGarrisons, RTSCamera, BetterSmithing,
        // etc.). The settings object has no [SettingPropertyX]-decorated
        // properties; the property data lives in the internal builder.
        if (Settings is MCM.Internal.IFluentSettings) // 2.3/H6: any fluent scope
        {
            BuildGroupsFluent(Settings);
            return;
        }

        // Phase 2.5 / finding M12: settings registered from a foreign MCM
        // assembly arrive wrapped in ForeignSettingsAdapter. Reflecting over
        // the ADAPTER finds zero [SettingProperty*] members, so the panel
        // rendered blank while logs claimed success. Rendering a foreign
        // instance properly needs SettingsPropertyVM to bind a non-BaseSettings
        // owner (deferred -- rare dual-MCM-assembly case); until then the gap
        // is REPORTED once per mod instead of silently shipping a blank page.
        if (Settings is MCM.Internal.SettingsRegistry.ForeignSettingsAdapter foreignAdapter)
        {
            BetaDeps.Foundation.CompatWarn.Once(
                "MCM.ForeignSettings", "SettingsVM.BuildGroups",
                foreignAdapter.Wrapped?.GetType().Assembly.GetName().Name,
                $"'{Settings.Id}' registered via a foreign MCM assembly; its settings panel cannot be rendered yet and will appear empty");
            return;
        }

        var grouped = new Dictionary<string, List<SettingsPropertyVM>>(StringComparer.Ordinal);
        var groupOrders = new Dictionary<string, int>(StringComparer.Ordinal);
        // Tie-break groups with equal GroupOrder by FIRST-SEEN (declaration)
        // order, NOT alphabetically -- MCM preserves the order the author
        // declared groups in, so e.g. "Community & Support" stays at the bottom
        // instead of sorting above "Disease System".
        var groupFirstSeen = new Dictionary<string, int>(StringComparer.Ordinal);
        int seenIndex = 0;

        foreach (var p in Settings.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
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
            if (!grouped.ContainsKey(groupName))
            {
                grouped[groupName] = new List<SettingsPropertyVM>();
                groupOrders[groupName] = groupAttr?.GroupOrder ?? 0;
                groupFirstSeen[groupName] = seenIndex++;
            }
            grouped[groupName].Add(SettingsPropertyVM.Create(Settings, p, spa));
        }

        foreach (var kv in grouped.OrderBy(kv => groupOrders[kv.Key]).ThenBy(kv => groupFirstSeen[kv.Key]))
        {
            var gvm = new SettingsPropertyGroupVM(kv.Key, kv.Value.OrderBy(v => v.Order).ToList());
            _groups.Add(gvm);
        }
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
                    BetaDeps.Foundation.DiagLog.LogCaught(
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

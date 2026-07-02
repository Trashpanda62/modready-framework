// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Newtonsoft.Json converter for DropdownDefault<T> and Dropdown<T>. Without
// this, Newtonsoft sees the IEnumerable<T> implementation and tries to treat
// the dropdown as a List<T>, which fails because Dropdown<T> has no Add()
// method and no settable Items.
//
// Wire format matches upstream BUTR-MCM, which writes just the SelectedValue
// as a scalar (string, int, bool, etc.). On read we look at the existing
// dropdown instance -- populated by the BaseSettings constructor with the
// authoring mod's full Items list -- and just set SelectedValue from the JSON
// scalar. This preserves the items list while updating the selection, which
// is the only piece that varies across saves.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using ModReady.Foundation;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCM.Common;

public sealed class DropdownConverter : JsonConverter
{
    private const string Tag = "MCM.DropdownConverter";

    public override bool CanConvert(Type objectType)
    {
        if (objectType == null) return false;
        // Match any closed generic Dropdown<T> or DropdownDefault<T>, and any subclass thereof.
        var t = objectType;
        while (t != null && t != typeof(object))
        {
            if (t.IsGenericType)
            {
                var def = t.GetGenericTypeDefinition();
                if (def == typeof(Dropdown<>) || def == typeof(DropdownDefault<>))
                    return true;
            }
            t = t.BaseType;
        }
        return false;
    }

    public override bool CanRead => true;
    public override bool CanWrite => true;

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        try
        {
            var token = JToken.Load(reader);

            // Resolve the closed Dropdown<T> type and its element type T.
            var elementType = ResolveElementType(objectType);
            if (elementType == null) return existingValue;

            // Ensure we have a target instance to mutate. If the parent type
            // didn't construct the dropdown (rare -- most mods do in their
            // ctor), create an empty one so we have something to return.
            var target = existingValue;
            if (target == null)
            {
                try { target = Activator.CreateInstance(objectType); }
                catch
                {
                    // Fallback: build a Dropdown<T> with no items. The SelectedValue
                    // setter below will be a no-op against an empty list, but at
                    // least we won't crash deserialization.
                    var dropdownGeneric = typeof(Dropdown<>).MakeGenericType(elementType);
                    target = Activator.CreateInstance(dropdownGeneric);
                }
            }
            if (target == null) return existingValue;

            // Different JSON shapes we'll accept:
            //  - scalar (string/int/bool) -> set SelectedValue
            //  - object with "SelectedIndex" or "SelectedValue" -> use that
            //  - object with "Items" array -> rebuild the items list, then SelectedValue/SelectedIndex
            //  - bare array -> rebuild items list (preserves first as default)
            switch (token.Type)
            {
                case JTokenType.Null:
                    return existingValue;

                case JTokenType.Object:
                {
                    var obj = (JObject)token;
                    bool handledStructured = false;
                    if (obj.TryGetValue("Items", out var itemsTok) && itemsTok is JArray itemsArr)
                    {
                        TryReplaceItems(target, elementType, itemsArr);
                        handledStructured = true;
                    }
                    // Phase 2.1 / finding H7 (2026-06-10 review): restore by
                    // SelectedVALUE first, SelectedIndex only as fallback.
                    // The old index-first order silently selected the WRONG
                    // option whenever a mod's item list reordered or grew
                    // across sessions (dynamically-built lists: key bindings,
                    // mod lists, FasterTime/BSC-style options) -- even though
                    // the correct value string sat right next to the stale
                    // index in the JSON. McmSelfTest.ValuesEqual documented
                    // exactly this drift.
                    bool selectionRestored = false;
                    if (obj.TryGetValue("SelectedValue", out var selTok) && selTok.Type != JTokenType.Null)
                    {
                        selectionRestored = TrySetSelectedByValue(target, elementType, selTok);
                        handledStructured = true;
                    }
                    if (!selectionRestored &&
                        obj.TryGetValue("SelectedIndex", out var idxTok) && idxTok.Type == JTokenType.Integer)
                    {
                        SetSelectedIndex(target, (int)idxTok);
                        handledStructured = true;
                    }
                    if (!handledStructured)
                    {
                        // The whole JObject IS the SelectedValue — happens when
                        // T is a custom class/struct serialized as an object
                        // (e.g. FasterTime.KeyDropdownOption with KeyName/KeyId
                        // fields). Hand the entire token to SetSelectedValue
                        // so FindMatchingItemIndex walks Items to match it.
                        SetSelectedValue(target, elementType, token);
                    }
                    return target;
                }

                case JTokenType.Array:
                {
                    TryReplaceItems(target, elementType, (JArray)token);
                    return target;
                }

                default:
                    // Scalar -- treat as SelectedValue.
                    SetSelectedValue(target, elementType, token);
                    return target;
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "ReadJson", ex);
            return existingValue;
        }
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        try
        {
            if (value == null) { writer.WriteNull(); return; }

            // Wrapper format. Always include BOTH SelectedIndex AND SelectedValue:
            //   { "SelectedIndex": N, "SelectedValue": <ToString-of-selected> }
            //
            // Why both? Some consumer T types (BSC's KeybindingDropdownOption,
            // FasterTime's KeyDropdownOption) don't round-trip cleanly through
            // Newtonsoft -- their fields are private with no public setter so
            // the serialized JSON is `{}`, leaving us with no way to match
            // back to the right item on read. By writing SelectedIndex too,
            // ReadJson can always restore by index regardless of how broken
            // the value serialization is.
            //
            // Value as a STRING repr (item.ToString()), not as the raw object,
            // so we never trigger a problematic deep-serialization of the
            // custom T type.
            var t = value.GetType();
            var idxProp = t.GetProperty("SelectedIndex", BindingFlags.Public | BindingFlags.Instance);
            var selProp = t.GetProperty("SelectedValue", BindingFlags.Public | BindingFlags.Instance);
            int idx = idxProp?.GetValue(value) is int i ? i : -1;
            var sel = selProp?.GetValue(value);
            string repr = sel?.ToString() ?? string.Empty;

            writer.WriteStartObject();
            writer.WritePropertyName("SelectedIndex");
            writer.WriteValue(idx);
            writer.WritePropertyName("SelectedValue");
            writer.WriteValue(repr);
            writer.WriteEndObject();
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "WriteJson", ex);
            writer.WriteNull();
        }
    }

    // ---- helpers ----

    private static Type? ResolveElementType(Type objectType)
    {
        var t = objectType;
        while (t != null && t != typeof(object))
        {
            if (t.IsGenericType)
            {
                var def = t.GetGenericTypeDefinition();
                if (def == typeof(Dropdown<>) || def == typeof(DropdownDefault<>))
                    return t.GetGenericArguments()[0];
            }
            t = t.BaseType;
        }
        return null;
    }

    /// <summary>
    /// Phase 2.1 / H7: value-first restore. Returns true only when an actual
    /// item match was found and the index was set; false means the caller may
    /// fall back to the persisted SelectedIndex. ToObject failures on custom
    /// T types are swallowed here (NOT thrown into ReadJson's catch) so a
    /// broken value deserialization can't abort the whole restore -- the
    /// stored repr string still gets a ToString match attempt, mirroring how
    /// WriteJson persisted it.
    /// </summary>
    private static bool TrySetSelectedByValue(object target, Type elementType, JToken token)
    {
        object? raw = null;
        try { raw = token.ToObject(elementType); } catch { /* custom T; fall through to repr match */ }
        if (raw == null && token.Type == JTokenType.String)
            raw = token.Value<string>(); // WriteJson stores item.ToString() -- match on repr

        int matchIdx = raw != null ? FindMatchingItemIndex(target, raw) : -1;
        if (matchIdx < 0 && token is JObject jobj)
            matchIdx = FindMatchingItemByJsonFields(target, jobj);

        if (matchIdx >= 0)
        {
            SetSelectedIndex(target, matchIdx);
            return true;
        }
        return false;
    }

    private static void SetSelectedValue(object target, Type elementType, JToken token)
    {
        var raw = token.ToObject(elementType);

        // Three-strategy match against Items, in priority order:
        //   1. Equals on deserialized `raw` against each item
        //   2. ToString comparison on deserialized `raw`
        //   3. JSON-aware field-by-field match: walk the JToken's property
        //      names and compare each to the same-named field on each item.
        //      This is the bulletproof option for custom T types (BSC's
        //      KeybindingDropdownOption, FasterTime's KeyDropdownOption)
        //      where Newtonsoft can't repopulate private fields and the
        //      deserialized `raw` is structurally empty.
        int matchIdx = FindMatchingItemIndex(target, raw);
        if (matchIdx < 0 && token is Newtonsoft.Json.Linq.JObject jobj)
        {
            matchIdx = FindMatchingItemByJsonFields(target, jobj);
        }
        if (matchIdx >= 0)
        {
            SetSelectedIndex(target, matchIdx);
            return;
        }

        var prop = target.GetType().GetProperty("SelectedValue", BindingFlags.Public | BindingFlags.Instance);
        prop?.SetValue(target, raw);
    }

    /// <summary>Walk the dropdown's Items list looking for an item that
    /// matches the given candidate via Equals, then via ToString comparison.
    /// Returns -1 if no match.</summary>
    private static int FindMatchingItemIndex(object dropdown, object? candidate)
    {
        if (candidate == null) return -1;
        try
        {
            // Try public Items property first (BUTR Dropdown<T> exposes it).
            IEnumerable? items = null;
            var itemsProp = dropdown.GetType().GetProperty("Items", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (itemsProp != null) items = itemsProp.GetValue(dropdown) as IEnumerable;
            if (items == null)
            {
                var f = WalkFields(dropdown.GetType(), "_items");
                if (f != null) items = f.GetValue(dropdown) as IEnumerable;
            }
            if (items == null) return -1;

            string candidateRepr = candidate.ToString() ?? string.Empty;
            int i = 0;
            int reprMatch = -1;
            foreach (var item in items)
            {
                if (item != null && item.Equals(candidate)) return i;
                if (reprMatch < 0 && string.Equals(item?.ToString() ?? string.Empty, candidateRepr, StringComparison.Ordinal))
                    reprMatch = i;
                i++;
            }
            return reprMatch;
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "FindMatchingItemIndex", ex);
            return -1;
        }
    }

    private static void SetSelectedIndex(object target, int index)
    {
        var prop = target.GetType().GetProperty("SelectedIndex", BindingFlags.Public | BindingFlags.Instance);
        prop?.SetValue(target, index);
    }

    /// <summary>
    /// JSON-aware structural match. Walks the JObject's properties and looks
    /// for an item in Items whose same-named field/property has a matching
    /// scalar value. This is the failsafe for custom T types where:
    ///   - Equals is reference-based (so no item ever equals the deserialized
    ///     raw instance)
    ///   - ToString returns null/typename because the deserialized raw has
    ///     null fields (Newtonsoft couldn't populate them, e.g. they're
    ///     private fields with no setter)
    /// We don't trust the deserialized raw at all -- we just look at the raw
    /// JObject keys and compare scalars against fields on each Item.
    /// </summary>
    private static int FindMatchingItemByJsonFields(object dropdown, Newtonsoft.Json.Linq.JObject jobj)
    {
        try
        {
            IEnumerable? items = null;
            var itemsProp = dropdown.GetType().GetProperty("Items", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (itemsProp != null) items = itemsProp.GetValue(dropdown) as IEnumerable;
            if (items == null)
            {
                var f = WalkFields(dropdown.GetType(), "_items");
                if (f != null) items = f.GetValue(dropdown) as IEnumerable;
            }
            if (items == null) return -1;

            // Capture each JSON property as (name, scalar repr).
            var jsonFields = new List<(string Name, string Repr)>();
            foreach (var prop in jobj.Properties())
            {
                if (prop.Value == null) continue;
                if (prop.Value.Type == JTokenType.Object || prop.Value.Type == JTokenType.Array)
                    continue; // only scalar-leaf fields for matching
                jsonFields.Add((prop.Name, prop.Value.ToString()));
            }
            if (jsonFields.Count == 0) return -1;

            int i = 0;
            foreach (var item in items)
            {
                if (item == null) { i++; continue; }
                bool allMatch = true;
                foreach (var jf in jsonFields)
                {
                    var t = item.GetType();
                    object? itemVal = null;
                    var propInfo = t.GetProperty(jf.Name, BindingFlags.Public | BindingFlags.Instance);
                    if (propInfo != null) itemVal = propInfo.GetValue(item);
                    else
                    {
                        var fieldInfo = WalkFields(t, jf.Name);
                        if (fieldInfo != null) itemVal = fieldInfo.GetValue(item);
                    }
                    if (!string.Equals(itemVal?.ToString() ?? string.Empty, jf.Repr, StringComparison.Ordinal))
                    {
                        allMatch = false;
                        break;
                    }
                }
                if (allMatch) return i;
                i++;
            }
            return -1;
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "FindMatchingItemByJsonFields", ex);
            return -1;
        }
    }

    /// <summary>
    /// Rebuild the items list of a DropdownDefault&lt;T&gt; in place. We reach
    /// into the private _items field via reflection because the public surface
    /// doesn't expose an items mutator (intentional -- mod authors set items
    /// at construction time).
    /// </summary>
    private static void TryReplaceItems(object target, Type elementType, JArray items)
    {
        try
        {
            var listType = typeof(System.Collections.Generic.List<>).MakeGenericType(elementType);
            var list = (IList)Activator.CreateInstance(listType)!;
            foreach (var el in items)
            {
                if (el.Type == JTokenType.Null) continue;
                list.Add(el.ToObject(elementType));
            }
            var field = WalkFields(target.GetType(), "_items");
            if (field != null) field.SetValue(target, list);
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "TryReplaceItems", ex);
        }
    }

    private static FieldInfo? WalkFields(Type t, string name)
    {
        while (t != null && t != typeof(object))
        {
            var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) return f;
            t = t.BaseType;
        }
        return null;
    }
}

// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// Diagnostic probe that runs once on UIExtenderEx startup, walks
// TaleWorlds.Library.ViewModel via reflection, and logs every public/internal
// method that could plausibly be the binding-lookup entry point Gauntlet uses
// when resolving @X bindings in XML. Output goes to runtime.log and lets us
// figure out which method to patch for the binding integration.
//
// Names we suspect (based on naming conventions across game versions):
//   * GetMethod / GetMethodInfo / GetCachedMethodInfo
//   * GetProperty / GetPropertyInfo / GetCachedPropertyInfo
//   * ExecuteCommand
//   * AddProperty / AddProperties / RefreshProperties
//   * RegisterDataSource / RegisterAtSlot
//
// Plus any fields that look like a property/method cache:
//   * _propertyInfos / _propertyTable / _propertyCache
//   * _methodInfos / _methodTable / _methodCache

using System;
using System.Linq;
using System.Reflection;
using System.Text;

using ModReady.Foundation;

using TaleWorlds.Library;

namespace Bannerlord.UIExtenderEx.Runtime;

internal static class ViewModelBindingProbe
{
    private const string Tag = "ViewModelBindingProbe";
    private static bool _ran;

    public static void RunOnce()
    {
        if (_ran) return;
        _ran = true;

        try
        {
            var t = typeof(ViewModel);
            DiagLog.Log(Tag, $"=== ViewModel surface probe (target {t.FullName}, assembly {t.Assembly.GetName().Name} v{t.Assembly.GetName().Version}) ===");

            // 1. All public + non-public instance + static methods.
            var methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => m.DeclaringType == t) // only methods declared on ViewModel itself, not inherited
                .OrderBy(m => m.Name)
                .ToArray();

            var sb = new StringBuilder();
            sb.AppendLine($"  Methods declared on ViewModel ({methods.Length} total):");
            foreach (var m in methods)
            {
                var args = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name));
                var mod = m.IsStatic ? "static " : "";
                var vis = m.IsPublic ? "public" : (m.IsAssembly ? "internal" : (m.IsFamily ? "protected" : "private"));
                sb.AppendLine($"    {vis} {mod}{m.ReturnType.Name} {m.Name}({args})");
            }
            DiagLog.Log(Tag, sb.ToString());

            // 2. All fields that look like a cache or table.
            var fields = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(f => f.DeclaringType == t)
                .OrderBy(f => f.Name)
                .ToArray();
            sb.Clear();
            sb.AppendLine($"  Fields declared on ViewModel ({fields.Length} total):");
            foreach (var f in fields)
            {
                var mod = f.IsStatic ? "static " : "";
                var vis = f.IsPublic ? "public" : (f.IsAssembly ? "internal" : (f.IsFamily ? "protected" : "private"));
                sb.AppendLine($"    {vis} {mod}{f.FieldType.Name} {f.Name}");
            }
            DiagLog.Log(Tag, sb.ToString());

            // 3. Highlight candidates that look like binding-lookup entry points.
            sb.Clear();
            sb.AppendLine("  Likely binding-lookup candidates (by name match):");
            foreach (var m in methods)
            {
                var n = m.Name;
                if (n.Contains("Property") || n.Contains("Method") || n.Contains("DataSource") || n.Contains("Bind") || n.Contains("Execute"))
                {
                    var args = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name));
                    sb.AppendLine($"    {m.ReturnType.Name} {n}({args})");
                }
            }
            DiagLog.Log(Tag, sb.ToString());

            DiagLog.Log(Tag, "=== probe complete -- next step: pick the right method to patch from the candidate list above ===");
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "RunOnce", ex);
        }
    }
}

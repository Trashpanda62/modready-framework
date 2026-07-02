// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// SaveShieldProbes: helper methods that enrich a FailureRecord beyond
// the basic stack-trace data. Split out of SaveShield.cs to keep the
// finalizer file legible.
//
// - ProbeCurrentSignatures(Exception) -- v4 #1. Parse the message of a
//   MissingMethodException ("Method not found: 'Void Type.Method(...)'")
//   and reflect the current Bannerlord type to list every overload that
//   actually exists today. Mod authors see one line of "here's what to
//   change to" instead of having to look it up.
//
// - ProbeModManifest(string dllPath) -- v4 #2. Walk up the path to find
//   the mod's Modules\<name>\ folder, parse SubModule.xml for version /
//   author / dependencies, and read AssemblyVersion + every TaleWorlds.*
//   referenced assembly from the DLL itself. Single-stop "which version
//   was shipped and against which API".
//
// - ScanImportsForMissing(dllPath, ex) -- v4 #8. Open the DLL with Cecil
//   (already shipped in ModReady' bin folder) and walk every type/method
//   reference. Filter for TaleWorlds.* members whose name matches the
//   missing-method/field signature. Lets the mod author confirm the
//   compile-time-bound vs reflection-bound origin of the call.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace ModReady.Foundation;

public static class SaveShieldProbes
{
    private const string Tag = "ModReady.SaveShield.Probes";

    // ===========================================================
    // #1 -- Current-API signature probe
    // ===========================================================

    /// <summary>
    /// Parse a MissingMethodException / MissingFieldException message and
    /// reflect the current type to list every member that matches by
    /// name. Returns C#-style signature strings ready for the log block.
    /// </summary>
    public static List<string> ProbeCurrentSignatures(Exception ex)
    {
        var result = new List<string>();
        try
        {
            if (ex == null) return result;
            if (!(ex is MissingMethodException || ex is MissingFieldException)) return result;

            // Message shapes we handle:
            //   "Method not found: 'Void TaleWorlds.MountAndBlade.Mission.GetFormationSpawnFrame(...)'"
            //   "Field not found: 'TaleWorlds.CampaignSystem.Hero.currentField'"
            //   "Method 'X' in type 'Y' from assembly 'Z' does not have an implementation."
            var msg = ex.Message ?? string.Empty;

            string? typeFullName = null;
            string? memberName = null;
            bool isField = ex is MissingFieldException;

            // Pattern A: "Method not found: 'ReturnType Type.Method(...)'."
            var matchA = Regex.Match(msg, @"(?:Method|Field) not found:\s*'(?:[\w\.`\[\],&<>\s]+\s)?([\w\.`+]+)\.([\w`<>]+)(?:\(|\b)");
            if (matchA.Success)
            {
                typeFullName = matchA.Groups[1].Value;
                memberName = matchA.Groups[2].Value;
            }

            // Pattern B: "Method 'X' in type 'Y' from assembly 'Z'..."
            if (typeFullName == null)
            {
                var matchB = Regex.Match(msg, @"Method '([\w`<>]+)' in type '([\w\.`+]+)' from assembly");
                if (matchB.Success)
                {
                    memberName = matchB.Groups[1].Value;
                    typeFullName = matchB.Groups[2].Value;
                }
            }

            if (string.IsNullOrEmpty(typeFullName) || string.IsNullOrEmpty(memberName))
                return result;

            var t = ResolveTypeLoose(typeFullName!);
            if (t == null)
            {
                result.Add($"(could not resolve {typeFullName} on current Bannerlord build)");
                return result;
            }

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

            if (isField)
            {
                foreach (var f in t.GetFields(flags))
                {
                    if (!string.Equals(f.Name, memberName, StringComparison.Ordinal)) continue;
                    result.Add($"{FormatVisibility(f)} {FormatType(f.FieldType)} {f.Name}");
                }
            }
            else
            {
                foreach (var m in t.GetMethods(flags))
                {
                    if (!string.Equals(m.Name, memberName, StringComparison.Ordinal)) continue;
                    result.Add(FormatMethodSignature(m));
                }
            }

            if (result.Count == 0)
            {
                // Member is gone outright -- list nearby names so the author
                // can spot a rename.
                var nameSet = new HashSet<string>(StringComparer.Ordinal);
                foreach (var m in t.GetMethods(flags))
                    if (Soundalike(memberName!, m.Name)) nameSet.Add(m.Name);
                foreach (var f in t.GetFields(flags))
                    if (Soundalike(memberName!, f.Name)) nameSet.Add(f.Name);
                if (nameSet.Count > 0)
                {
                    result.Add($"(no member named '{memberName}' on {t.FullName}; closest current names: {string.Join(", ", nameSet.Take(10))})");
                }
                else
                {
                    result.Add($"(no member named '{memberName}' on {t.FullName}; member appears removed -- check release notes for the version that dropped it)");
                }
            }
        }
        catch (Exception probeEx)
        {
            try { DiagLog.LogCaught(Tag, "ProbeCurrentSignatures", probeEx); } catch { }
        }
        return result;
    }

    private static Type? ResolveTypeLoose(string typeFullName)
    {
        try
        {
            var direct = Type.GetType(typeFullName, throwOnError: false);
            if (direct != null) return direct;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var probe = asm.GetType(typeFullName, throwOnError: false);
                    if (probe != null) return probe;
                }
                catch { }
            }
        }
        catch { }
        return null;
    }

    private static string FormatMethodSignature(MethodInfo m)
    {
        var sb = new StringBuilder();
        sb.Append(FormatVisibility(m)).Append(' ');
        if (m.IsStatic) sb.Append("static ");
        sb.Append(FormatType(m.ReturnType)).Append(' ');
        sb.Append(m.Name).Append('(');
        var ps = m.GetParameters();
        for (int i = 0; i < ps.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            var p = ps[i];
            if (p.IsOut) sb.Append("out ");
            else if (p.ParameterType.IsByRef) sb.Append("ref ");
            sb.Append(FormatType(p.ParameterType)).Append(' ').Append(p.Name);
        }
        sb.Append(')');
        return sb.ToString();
    }

    private static string FormatVisibility(MethodInfo m)
    {
        if (m.IsPublic) return "public";
        if (m.IsAssembly) return "internal";
        if (m.IsFamilyOrAssembly) return "protected internal";
        if (m.IsFamily) return "protected";
        if (m.IsPrivate) return "private";
        return "";
    }

    private static string FormatVisibility(FieldInfo f)
    {
        if (f.IsPublic) return "public";
        if (f.IsAssembly) return "internal";
        if (f.IsFamilyOrAssembly) return "protected internal";
        if (f.IsFamily) return "protected";
        if (f.IsPrivate) return "private";
        return "";
    }

    private static string FormatType(Type t)
    {
        if (t.IsByRef) return FormatType(t.GetElementType()!);
        if (!t.IsGenericType) return t.Name;
        var argList = string.Join(", ", t.GetGenericArguments().Select(FormatType));
        var bareName = t.Name;
        int tick = bareName.IndexOf('`');
        if (tick > 0) bareName = bareName.Substring(0, tick);
        return $"{bareName}<{argList}>";
    }

    private static bool Soundalike(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        if (a.Length < 4 || b.Length < 4) return false;
        return a.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0
            || b.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // ===========================================================
    // #2 -- Mod-manifest probe
    // ===========================================================

    /// <summary>
    /// Walk up from a culprit DLL path to find Modules\&lt;ModName&gt;\,
    /// read its SubModule.xml header, capture AssemblyVersion + every
    /// TaleWorlds.* AssemblyName the DLL references.
    /// </summary>
    public static ModManifest? ProbeModManifest(string dllPath)
    {
        try
        {
            if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath)) return null;

            var modFolder = WalkUpToModFolder(dllPath);
            if (string.IsNullOrEmpty(modFolder)) return null;

            var m = new ModManifest
            {
                ModFolder = modFolder!,
                AssemblyLocation = dllPath,
            };

            // SubModule.xml header (Name / Id / Version / Author + DependedModules).
            var subModuleXml = Path.Combine(modFolder!, "SubModule.xml");
            if (File.Exists(subModuleXml))
            {
                try
                {
                    var doc = new XmlDocument();
                    doc.Load(subModuleXml);
                    m.ModId = doc.SelectSingleNode("//Id/@value")?.Value ?? string.Empty;
                    m.ModName = doc.SelectSingleNode("//Name/@value")?.Value ?? string.Empty;
                    m.ModVersion = doc.SelectSingleNode("//Version/@value")?.Value ?? string.Empty;
                    m.ModAuthor = doc.SelectSingleNode("//Author/@value")?.Value ?? string.Empty;
                    var deps = doc.SelectNodes("//DependedModule/@Id");
                    if (deps != null)
                    {
                        foreach (XmlAttribute a in deps)
                        {
                            if (!string.IsNullOrEmpty(a.Value)) m.DependedModules.Add(a.Value);
                        }
                    }
                }
                catch (Exception xmlEx)
                {
                    try { DiagLog.LogCaught(Tag, "SubModule.xml parse", xmlEx); } catch { }
                }
            }

            // AssemblyName + AssemblyVersion + TaleWorlds.* referenced assemblies.
            try
            {
                var n = AssemblyName.GetAssemblyName(dllPath);
                m.AssemblyName = n.Name ?? string.Empty;
                m.AssemblyVersion = n.Version?.ToString() ?? string.Empty;
            }
            catch { }

            // Walk referenced assemblies via a metadata-only load (System.Reflection
            // Assembly.ReflectionOnlyLoadFrom doesn't keep the DLL locked but is
            // deprecated; Assembly.LoadFile keeps it loaded for the session but
            // works on net472. Use LoadFile defensively in a try/catch.).
            try
            {
                var asm = Assembly.LoadFile(dllPath);
                foreach (var refName in asm.GetReferencedAssemblies())
                {
                    var n = refName.Name ?? string.Empty;
                    if (n.StartsWith("TaleWorlds.", StringComparison.Ordinal) ||
                        n.StartsWith("SandBox", StringComparison.Ordinal) ||
                        n.StartsWith("StoryMode", StringComparison.Ordinal) ||
                        n.StartsWith("Native", StringComparison.Ordinal))
                    {
                        m.TaleWorldsReferences.Add($"{n} v{refName.Version}");
                    }
                }
            }
            catch (Exception loadEx)
            {
                try { DiagLog.LogCaught(Tag, $"LoadFile({dllPath})", loadEx); } catch { }
            }

            return m;
        }
        catch (Exception ex)
        {
            try { DiagLog.LogCaught(Tag, "ProbeModManifest", ex); } catch { }
            return null;
        }
    }

    private static string? WalkUpToModFolder(string dllPath)
    {
        try
        {
            // dllPath = ...\Modules\<Mod>\bin\Win64_Shipping_Client\<dll>
            // Climb until the parent's name is "Modules".
            var dir = Path.GetDirectoryName(dllPath);
            while (!string.IsNullOrEmpty(dir))
            {
                var parent = Path.GetDirectoryName(dir);
                if (!string.IsNullOrEmpty(parent) &&
                    string.Equals(Path.GetFileName(parent), "Modules", StringComparison.OrdinalIgnoreCase))
                {
                    return dir;
                }
                dir = parent;
            }
        }
        catch { }
        return null;
    }

    // ===========================================================
    // #8 -- Cecil import scan
    // ===========================================================

    /// <summary>
    /// Open the culprit DLL with Mono.Cecil (already shipped in ModReady'
    /// bin folder for Harmony's IL rewriting needs) and walk every
    /// TypeReference + MemberReference. Filter for TaleWorlds.* members
    /// whose name matches the missing-method/field name from the
    /// exception. Helps the mod author distinguish compile-time-bound
    /// references from reflection-bound ones.
    /// </summary>
    public static List<string> ScanImportsForMissing(string dllPath, Exception ex)
    {
        var result = new List<string>();
        try
        {
            if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath)) return result;
            if (ex == null) return result;

            // Extract the missing member name from the exception message.
            var msg = ex.Message ?? string.Empty;
            string? memberName = null;
            var matchA = Regex.Match(msg, @"(?:Method|Field) not found:\s*'(?:[\w\.`\[\],&<>\s]+\s)?[\w\.`+]+\.([\w`<>]+)");
            if (matchA.Success) memberName = matchA.Groups[1].Value;
            else
            {
                var matchB = Regex.Match(msg, @"Method '([\w`<>]+)' in type");
                if (matchB.Success) memberName = matchB.Groups[1].Value;
            }
            if (string.IsNullOrEmpty(memberName)) return result;

            // Cecil is loaded by ModReady.Harmony / .ButterLib / .MCM; we
            // reach it reflectively so Foundation doesn't take a direct
            // package reference.
            var cecilAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "Mono.Cecil", StringComparison.OrdinalIgnoreCase));
            if (cecilAsm == null)
            {
                // Not loaded yet -- skip silently (most Foundation calls run
                // before Harmony's been touched).
                return result;
            }

            var moduleDefinitionType = cecilAsm.GetType("Mono.Cecil.ModuleDefinition", throwOnError: false);
            if (moduleDefinitionType == null) return result;
            var readModuleM = moduleDefinitionType.GetMethod("ReadModule",
                new[] { typeof(string) });
            if (readModuleM == null) return result;

            object? module = null;
            try { module = readModuleM.Invoke(null, new object[] { dllPath }); }
            catch (Exception readEx)
            {
                try { DiagLog.LogCaught(Tag, $"Cecil.ReadModule({dllPath})", readEx); } catch { }
                return result;
            }
            if (module == null) return result;

            try
            {
                // module.GetMemberReferences() -> IEnumerable<MemberReference>
                var getMemberRefs = module.GetType().GetMethod("GetMemberReferences");
                if (getMemberRefs == null) return result;
                var memberRefs = getMemberRefs.Invoke(module, null) as System.Collections.IEnumerable;
                if (memberRefs == null) return result;

                foreach (var memberRef in memberRefs)
                {
                    if (memberRef == null) continue;
                    var memberRefType = memberRef.GetType();
                    var nameProp = memberRefType.GetProperty("Name");
                    var fullProp = memberRefType.GetProperty("FullName");
                    var declTypeProp = memberRefType.GetProperty("DeclaringType");
                    if (nameProp == null) continue;

                    var refName = nameProp.GetValue(memberRef) as string ?? string.Empty;
                    if (!string.Equals(refName, memberName, StringComparison.Ordinal)) continue;

                    var declType = declTypeProp?.GetValue(memberRef);
                    var declTypeFull = declType?.GetType().GetProperty("FullName")?.GetValue(declType) as string ?? string.Empty;
                    if (!declTypeFull.StartsWith("TaleWorlds.", StringComparison.Ordinal) &&
                        !declTypeFull.StartsWith("SandBox", StringComparison.Ordinal)) continue;

                    var fullName = fullProp?.GetValue(memberRef) as string ?? $"{declTypeFull}.{refName}";
                    result.Add(fullName);
                }
            }
            finally
            {
                try
                {
                    var disposable = module as IDisposable;
                    disposable?.Dispose();
                }
                catch { }
            }
        }
        catch (Exception ex2)
        {
            try { DiagLog.LogCaught(Tag, "ScanImportsForMissing", ex2); } catch { }
        }
        return result;
    }
}

// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
//
// ModConfigCapture -- fully headless visual verification.
//
// Driven from MCMSubModule.OnApplicationTick. When a
// `modready-capture-modconfig.flag` file is present in Modules\ModReady\, this
// state machine (after the main menu settles) programmatically:
//   1. Opens the Options screen (executes the "Options" initial-state-option --
//      the same code path the Options main-menu button uses).
//   2. Selects the injected "Mod Configuration" tab (ModReadyModConfigPage) by
//      walking the screen's Gauntlet widget tree and driving the TabControl.
//   3. Captures a screenshot to <runDir>\modconfig.png via the engine
//      (Utilities.TakeScreenshotFromStringPath).
//   4. Writes modready-capture-complete.flag, deletes the trigger flag, and
//      quits the game (Utilities.QuitGame) so the verify harness terminates.
//
// All engine UI internals (GauntletLayer / Widget / TabControl) are accessed by
// REFLECTION because the project references only Bannerlord.ReferenceAssemblies.Core
// (no GauntletUI reference assemblies). Every step is logged to <runDir>\capture-diag.log
// so failures are debuggable from files alone. The screenshot + quit always run
// even if tab-selection fails, so each test cycle yields a PNG to inspect.

using System;
using System.Collections;
using System.IO;
using System.Reflection;

using ModReady.Foundation;

using TaleWorlds.MountAndBlade;

namespace MCM.Internal;

internal static class ModConfigCapture
{
    private const string Tag = "ModConfigCapture";
    private const string CaptureFlagName = "modready-capture-modconfig.flag";
    private const string DoneFlagName    = "modready-capture-complete.flag";

    private static bool _active;
    private static bool _done;
    private static int  _state;
    private static int  _frames;
    private static string _outPng = string.Empty;
    private static string _diag   = string.Empty;
    private static string _moduleDir = string.Empty;
    private static int    _menuUpFrame = -1;
    private static object? _hoverRow;   // Slice 4: first mod-row, held Hovered for the capture
    // The self-test flag (dropped alongside the capture flag) is deleted at
    // OnBeforeInitialModuleScreenSetAsRoot -- i.e. exactly when the main menu
    // becomes the root screen. We use its disappearance as the "menu is up"
    // signal so we never open Options / capture over the loading screen.
    private const string SelfTestFlagName = "modready-run-selftest.flag";

    private const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags AnyStatic   = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    private static void D(string msg)
    {
        DiagLog.Log(Tag, msg);
        try { if (_diag.Length > 0) File.AppendAllText(_diag, $"{DateTime.Now:HH:mm:ss}  {msg}\n"); } catch { }
    }

    internal static void OnTick(float dt)
    {
        if (_done) return;
        try
        {
            var dir = Path.GetDirectoryName(RuntimeLog.Path);
            if (string.IsNullOrEmpty(dir)) return;
            _moduleDir = dir!;

            if (!_active)
            {
                var flagPath = Path.Combine(dir!, CaptureFlagName);
                if (!File.Exists(flagPath)) return;
                _active = true; _state = 0; _frames = 0;

                // The harness writes the run-results dir into the flag so the PNG
                // lands next to selftest.log; fall back to the module dir.
                string runDir = dir!;
                try { var t = File.ReadAllText(flagPath).Trim(); if (!string.IsNullOrEmpty(t) && Directory.Exists(t)) runDir = t; } catch { }
                _outPng = Path.Combine(runDir, "modconfig.png");
                _diag   = Path.Combine(runDir, "capture-diag.log");
                D($"capture flag detected; target PNG = {_outPng}");
            }

            _frames++;
            switch (_state)
            {
                case 0: // wait until the MAIN MENU is actually up, then settle, then open Options
                    bool selftestPending = File.Exists(Path.Combine(_moduleDir, SelfTestFlagName));
                    if (selftestPending && _frames < 3600) return;   // menu not root yet (cap ~60s)
                    if (_menuUpFrame < 0) { _menuUpFrame = _frames; D("main menu is up; settling ~3s before opening Options"); }
                    if (_frames - _menuUpFrame < 180) return;        // ~3s for the menu to render
                    D("opening Options screen");
                    OpenOptionsScreen();
                    _state = 1; _frames = 0;
                    return;

                case 1: // Options screen should be up; select the Mod Config tab
                    if (_frames < 90) return;
                    D("selecting Mod Configuration tab");
                    SelectModConfigTab();
                    _state = 2; _frames = 0;
                    return;

                case 2: // hold the first mod-row in Hovered (re-applied each frame so the
                        // engine's per-frame hit-test doesn't reset it), then capture so the
                        // screenshot shows the Slice 4 hover wash.
                    if (_hoverRow != null) TryInvoke(_hoverRow, "SetState", "Hovered");
                    if (_frames < 70) return;
                    D($"capturing screenshot -> {_outPng}");
                    if (_hoverRow != null) TryInvoke(_hoverRow, "SetState", "Hovered");
                    TakeScreenshot(_outPng);
                    _state = 3; _frames = 0;
                    return;

                case 3: // let the screenshot flush to disk, then finish + quit
                    if (_frames < 45) return;
                    Finish();
                    _done = true;
                    return;
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"OnTick state {_state}", ex);
            D($"EXCEPTION in state {_state}: {ex.GetType().Name}: {ex.Message}");
            // Don't get stuck: try to finish + quit so the harness isn't left hanging.
            if (_state >= 2) { try { Finish(); } catch { } _done = true; }
            else { _state = 2; _frames = 0; }
        }
    }

    // ---- v0.9.0 Slice 3: narrow the right description panel on our tab ----
    // The shared vanilla DescriptionsRightPanel is a fixed 650px; our hover hint
    // only needs ~360px. While the Mod Configuration tab is active we shrink it
    // so (a) the settings area widens and long slider values stop clipping at the
    // scrollbar, and (b) the description's left edge moves in toward its text
    // (no big empty gap). Restored to the cached vanilla width on every other
    // tab so Video/Audio descriptions + preview images are untouched. Driven
    // ~every 8th frame from MCMSubModule.OnApplicationTick; all best-effort.
    private static int _rpFrames;
    private static float _rpOrigWidth = -1f;
    private const float ModConfigPanelWidth = 380f;

    private static bool TopScreenIsOptions()
    {
        try
        {
            var smType = ReflectionUtils.ResolveTypeByFullName("TaleWorlds.ScreenSystem.ScreenManager");
            var top = smType?.GetProperty("TopScreen", AnyStatic)?.GetValue(null)
                   ?? smType?.GetMethod("get_TopScreen", AnyStatic)?.Invoke(null, null);
            return top != null && (top.GetType().FullName?.IndexOf("Options", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
        }
        catch { return false; }
    }

    internal static void MaintainRightPanel()
    {
        try
        {
            if (((_rpFrames++) & 7) != 0) return;          // throttle: ~7-8 checks/sec
            if (!TopScreenIsOptions()) return;             // cheap guard; skip map/menu/etc.
            var root = GetTopScreenRootWidget();
            if (root == null) return;
            var panel = FindWidgetById(root, "DescriptionsRightPanel", 0);
            if (panel == null) return;                     // not the Options screen tree
            if (_rpOrigWidth < 0f)                          // cache the vanilla width once
            {
                _rpOrigWidth = (GetMember(panel, "SuggestedWidth") as float?) ?? 650f;
                if (_rpOrigWidth < 400f) _rpOrigWidth = 650f; // sanity floor
            }
            var page = FindWidgetById(root, "ModReadyModConfigPage", 0);
            bool ourTab = page != null && (GetMember(page, "IsVisible") as bool? ?? false);
            float target = ourTab ? ModConfigPanelWidth : _rpOrigWidth;
            float cur = (GetMember(panel, "SuggestedWidth") as float?) ?? -1f;
            if (System.Math.Abs(cur - target) > 0.5f) TrySet(panel, "SuggestedWidth", target);
        }
        catch { }
    }

    // ---- step 1: open the Options screen via the initial-state-option ----
    private static void OpenOptionsScreen()
    {
        var mod = TaleWorlds.MountAndBlade.Module.CurrentModule;
        if (mod == null) { D("Module.CurrentModule is null"); return; }

        object? opt = null;
        var get = mod.GetType().GetMethod("GetInitialStateOptionWithId", AnyInstance);
        foreach (var id in new[] { "Options", "options" })
        {
            try { opt = get?.Invoke(mod, new object[] { id }); } catch { }
            if (opt != null) { D($"found initial-state-option id='{id}'"); break; }
        }
        if (opt == null) { D("could not find 'Options' initial-state-option"); return; }

        // InitialStateOption exposes the click handler as an Action (field or prop),
        // historically named "Action" / "DoAction" / "_action".
        Delegate? action =
            (GetMember(opt, "Action") as Delegate) ??
            (GetMember(opt, "DoAction") as Delegate) ??
            (GetMember(opt, "_action") as Delegate) ??
            (GetMember(opt, "OnExecute") as Delegate);
        if (action != null) { action.DynamicInvoke(); D("invoked option action"); return; }

        // Fallback: Module.ExecuteInitialStateOptionWithId(id)
        var exec = mod.GetType().GetMethod("ExecuteInitialStateOptionWithId", AnyInstance);
        if (exec != null) { try { exec.Invoke(mod, new object[] { "Options" }); D("ExecuteInitialStateOptionWithId('Options')"); } catch (Exception e) { D("exec fallback failed: " + e.Message); } }
        else D("no way to execute the Options option found");
    }

    // ---- step 2: select the Mod Config tab in the Options screen ----
    private static void SelectModConfigTab()
    {
        var root = GetTopScreenRootWidget();
        if (root == null) { D("could not resolve top-screen root widget"); return; }

        // Strategy A: find our injected tab toggle by Id and select it.
        var toggle = FindWidgetById(root, "ModReadyModConfigTabToggle", 0);
        if (toggle != null)
        {
            D("found ModReadyModConfigTabToggle");
            // OptionsTabToggle is a toggle/button; selecting it drives the TabControl.
            if (TrySet(toggle, "IsSelected", true)) { D("set toggle IsSelected=true"); }
            if (TrySet(toggle, "IsActive", true))   { D("set toggle IsActive=true"); }
            // Some toggles need their click event fired:
            TryInvoke(toggle, "OnClick");
            TryInvoke(toggle, "HandleClick");
        }
        else D("toggle not found by id");

        // Strategy B: find the TabControl widget and force the last tab active.
        var tabControl = FindWidgetByTypeName(root, "TabControl", 0);
        if (tabControl != null)
        {
            D("found TabControl");
            // Count tabs to target the last (our Mod Config page is appended last).
            int idx = -1;
            var tabs = GetMember(tabControl, "Tabs") as IEnumerable ?? GetMember(tabControl, "_tabs") as IEnumerable;
            if (tabs != null) { foreach (var _ in tabs) idx++; }
            if (idx >= 0)
            {
                if (TryInvoke(tabControl, "TrySetSelectedIndex", idx)) D($"TrySetSelectedIndex({idx})");
                else if (TryInvoke(tabControl, "SetSelectedTab", idx)) D($"SetSelectedTab({idx})");
            }
            // ActiveTab by name as a last resort:
            TrySet(tabControl, "TabName", "ModReadyModConfigPage");
        }
        else D("TabControl not found");

        // Slice 4: cache a NON-selected mod-row (prefer the 2nd; the 1st is the
        // selected mod, whose selection canvas would mask the hover wash) so state 2
        // can hold it Hovered for the capture.
        var modList = FindWidgetById(root, "ModReadyModListPanel", 0);
        var rows = new System.Collections.Generic.List<object>();
        if (modList != null) CollectByTypeName(modList, "ButtonWidget", rows, 0);
        _hoverRow = rows.Count > 1 ? rows[1] : (rows.Count > 0 ? rows[0] : null);
        D($"cached mod-row {(rows.Count > 1 ? "#2" : "#1")} of {rows.Count} for hover capture");
    }

    // ---- step 3: engine screenshot to a path ----
    private static void TakeScreenshot(string path)
    {
        try { var d = Path.GetDirectoryName(path); if (!string.IsNullOrEmpty(d)) Directory.CreateDirectory(d!); } catch { }
        var util = ReflectionUtils.ResolveTypeByFullName("TaleWorlds.Engine.Utilities");
        if (util == null) { D("TaleWorlds.Engine.Utilities not resolved"); return; }

        // Enumerate (no GetMethod signature guessing -> no AmbiguousMatchException).
        // Pick a static method whose FIRST parameter is a string (the path); fill
        // any extra parameters with their default / zero value.
        foreach (var name in new[] { "TakeScreenshotFromStringPath", "SaveScreenshot", "TakeScreenshot" })
        {
            foreach (var m in util.GetMethods(AnyStatic))
            {
                if (m.Name != name) continue;
                var ps = m.GetParameters();
                if (ps.Length == 0 || ps[0].ParameterType != typeof(string)) continue;
                var args = new object?[ps.Length];
                args[0] = path;
                for (int i = 1; i < ps.Length; i++)
                    args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue
                            : (ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null);
                try { m.Invoke(null, args); D($"screenshot via {name}({ps.Length} arg(s))"); return; }
                catch (Exception e) { D($"{name}({ps.Length}) invoke failed: {e.GetType().Name}: {e.Message}"); }
            }
        }
        D("no usable screenshot method found on Utilities");
    }

    // ---- step 4: write done marker, delete trigger, quit ----
    private static void Finish()
    {
        try { File.WriteAllText(Path.Combine(_moduleDir, DoneFlagName), $"capture done {DateTime.Now:yyyy-MM-dd HH:mm:ss}"); } catch { }
        try { File.Delete(Path.Combine(_moduleDir, CaptureFlagName)); } catch { }
        D("capture complete; quitting game");
        var util = ReflectionUtils.ResolveTypeByFullName("TaleWorlds.Engine.Utilities");
        bool quit = false;
        if (util != null)
        {
            foreach (var m in util.GetMethods(AnyStatic))
            {
                if (m.Name != "QuitGame") continue;
                var ps = m.GetParameters();
                if (ps.Length != 0) continue;  // prefer the no-arg overload
                try { m.Invoke(null, null); quit = true; D("QuitGame() invoked"); break; }
                catch (Exception e) { D("QuitGame failed: " + e.Message); }
            }
        }
        if (!quit) D("QuitGame not invoked (will rely on harness taskkill)");
    }

    // ---------- reflection helpers ----------
    private static object? GetTopScreenRootWidget()
    {
        var smType = ReflectionUtils.ResolveTypeByFullName("TaleWorlds.ScreenSystem.ScreenManager");
        var top = smType?.GetProperty("TopScreen", AnyStatic)?.GetValue(null)
               ?? smType?.GetMethod("get_TopScreen", AnyStatic)?.Invoke(null, null);
        if (top == null) { D("ScreenManager.TopScreen null"); return null; }
        D("TopScreen type = " + top.GetType().FullName);

        var layers = GetMember(top, "Layers") as IEnumerable;
        if (layers == null) { D("screen has no Layers"); return null; }
        foreach (var layer in layers)
        {
            if (layer == null) continue;
            var tn = layer.GetType().FullName ?? "";
            if (!tn.Contains("Gauntlet")) continue;
            D("gauntlet layer = " + tn);
            // GauntletLayer.UIContext.Root, or _gauntletUIContext.Root
            var ctx = GetMember(layer, "UIContext") ?? GetMember(layer, "_gauntletUIContext");
            var root = ctx == null ? null : GetMember(ctx, "Root");
            if (root != null) { D("root widget = " + root.GetType().FullName); return root; }
        }
        D("no gauntlet layer root found");
        return null;
    }

    private static object? FindWidgetById(object widget, string id, int depth)
    {
        if (widget == null || depth > 40) return null;
        if ((GetMember(widget, "Id") as string) == id) return widget;
        foreach (var child in EnumerateChildren(widget))
        {
            var hit = FindWidgetById(child, id, depth + 1);
            if (hit != null) return hit;
        }
        return null;
    }

    private static object? FindWidgetByTypeName(object widget, string typeNameContains, int depth)
    {
        if (widget == null || depth > 40) return null;
        if ((widget.GetType().Name).IndexOf(typeNameContains, StringComparison.OrdinalIgnoreCase) >= 0) return widget;
        foreach (var child in EnumerateChildren(widget))
        {
            var hit = FindWidgetByTypeName(child, typeNameContains, depth + 1);
            if (hit != null) return hit;
        }
        return null;
    }

    private static void CollectByTypeName(object widget, string typeNameContains, System.Collections.Generic.List<object> outList, int depth)
    {
        if (widget == null || depth > 40) return;
        if (widget.GetType().Name.IndexOf(typeNameContains, StringComparison.OrdinalIgnoreCase) >= 0) outList.Add(widget);
        foreach (var child in EnumerateChildren(widget)) CollectByTypeName(child, typeNameContains, outList, depth + 1);
    }

    private static IEnumerable EnumerateChildren(object widget)
    {
        // Widget exposes children via Children (IEnumerable) or ChildCount + GetChild(i).
        if (GetMember(widget, "Children") is IEnumerable en) return en;
        var cnt = GetMember(widget, "ChildCount");
        if (cnt is int n)
        {
            var list = new System.Collections.Generic.List<object>();
            var getChild = widget.GetType().GetMethod("GetChild", AnyInstance, null, new[] { typeof(int) }, null);
            for (int i = 0; i < n && getChild != null; i++)
            {
                var c = getChild.Invoke(widget, new object[] { i });
                if (c != null) list.Add(c);
            }
            return list;
        }
        return Array.Empty<object>();
    }

    private static object? GetMember(object obj, string name)
    {
        if (obj == null) return null;
        var t = obj.GetType();
        var p = t.GetProperty(name, AnyInstance);
        if (p != null && p.CanRead) { try { return p.GetValue(obj); } catch { } }
        var f = t.GetField(name, AnyInstance);
        if (f != null) { try { return f.GetValue(obj); } catch { } }
        return null;
    }

    private static bool TrySet(object obj, string name, object value)
    {
        if (obj == null) return false;
        var t = obj.GetType();
        var p = t.GetProperty(name, AnyInstance);
        if (p != null && p.CanWrite) { try { p.SetValue(obj, value); return true; } catch { } }
        var f = t.GetField(name, AnyInstance);
        if (f != null) { try { f.SetValue(obj, value); return true; } catch { } }
        return false;
    }

    private static bool TryInvoke(object obj, string name, params object[] args)
    {
        if (obj == null) return false;
        try
        {
            var m = obj.GetType().GetMethod(name, AnyInstance, null,
                Array.ConvertAll(args, a => a?.GetType() ?? typeof(object)), null);
            if (m == null && args.Length == 0) m = obj.GetType().GetMethod(name, AnyInstance);
            if (m == null) return false;
            m.Invoke(obj, args);
            return true;
        }
        catch { return false; }
    }
}

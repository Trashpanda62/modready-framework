// BetaDeps Tactics Editor -- in-mission editor MissionLogic.
//
// Activated by F11 from any battle mission. While active:
//   - 1-9, 0       Select formation slot (0 = slot 10).
//   - LMB          Place the selected slot at the world cursor position.
//                  If the slot already exists in the plan, its position
//                  updates; otherwise the slot is created.
//   - R            Rotate selected slot facing +30 degrees (Shift+R = +5).
//   - F            Cycle ArrangementOrder for selected slot
//                  (Line -> Loose -> Circle -> ShieldWall -> Skein -> Column).
//   - B            Cycle behavior for selected slot
//                  (Hold -> Defend -> Charge -> FollowMe -> Retreat).
//   - C            Cycle FormationClass for selected slot
//                  (Infantry -> Ranged -> Cavalry -> HorseArcher).
//   - Delete       Remove the selected slot from the plan.
//   - Ctrl+S       Export current plan as a standalone mod folder.
//   - F11          Exit editor (plan is kept in memory; re-enter to keep editing).
//
// The editor does NOT live-apply orders to formations. It only builds the
// plan; the exported mod's TacticApplyMissionLogic does the at-battle-start
// formation positioning. This separation means the editor is non-destructive
// to the in-progress battle.
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using BetaDeps.Foundation;

using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;

// Note: we intentionally do NOT reference TaleWorlds.MountAndBlade.View
// from this project. MissionScreen lives in that DLL and isn't on
// NuGet; reaching it via reflection (ScreenManager.TopScreen as object)
// keeps the build portable.

namespace BetaDeps.TacticsEditor.Editor;

public sealed class TacticsEditorMissionLogic : MissionLogic
{
    private const string Tag = "BetaDeps.TacticsEditor.Editor";

    private static readonly string[] _arrangements = new[]
    {
        "Line", "Loose", "Circle", "ShieldWall", "Skein", "Column", "Square"
    };

    private static readonly string[] _behaviors = new[]
    {
        "Hold", "Defend", "Charge", "FollowMe", "Retreat"
    };

    private static readonly string[] _classes = new[]
    {
        "Infantry", "Ranged", "Cavalry", "HorseArcher"
    };

    public bool Active { get; private set; }

    /// <summary>The plan currently being edited (in-memory).</summary>
    public TacticPlan Plan { get; private set; } = new();

    /// <summary>Which slot's edits the next R/F/B/C/Delete press will apply to.</summary>
    public int SelectedSlot { get; private set; }

    private float _toggleCooldown;
    private float _exportCooldown;

    public override void OnBehaviorInitialize()
    {
        base.OnBehaviorInitialize();
        DiagLog.Log(Tag, "TacticsEditorMissionLogic initialized; press F11 to enter edit mode");
    }

    public override void OnMissionTick(float dt)
    {
        try
        {
            if (_toggleCooldown > 0f) _toggleCooldown -= dt;
            if (_exportCooldown > 0f) _exportCooldown -= dt;

            if (Input.IsKeyPressed(InputKey.F11) && _toggleCooldown <= 0f)
            {
                _toggleCooldown = 0.25f;
                ToggleActive();
            }

            if (!Active) return;

            HandleSlotSelection();
            HandlePlacement();
            HandleRotation();
            HandleCycleArrangement();
            HandleCycleBehavior();
            HandleCycleClass();
            HandleDelete();
            HandleExport();
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "OnMissionTick", ex);
        }
    }

    private void ToggleActive()
    {
        Active = !Active;
        var msg = Active
            ? "[TacticsEditor] ENTERED edit mode (1-9 slot, LMB place, R rotate, F arrangement, B behavior, C class, Del remove, Ctrl+S export, F11 exit)"
            : "[TacticsEditor] exited edit mode (plan kept in memory)";
        InformationManager.DisplayMessage(new InformationMessage(msg,
            Active ? new Color(1.0f, 0.85f, 0.4f) : new Color(0.7f, 0.7f, 0.7f)));
        DiagLog.Log(Tag, msg);
    }

    private void HandleSlotSelection()
    {
        var digits = new[] {
            InputKey.D1, InputKey.D2, InputKey.D3, InputKey.D4, InputKey.D5,
            InputKey.D6, InputKey.D7, InputKey.D8, InputKey.D9, InputKey.D0
        };
        for (int i = 0; i < digits.Length; i++)
        {
            if (!Input.IsKeyPressed(digits[i])) continue;
            SelectedSlot = i; // D0 maps to slot 9
            var existing = Plan.Formations.FirstOrDefault(s => s.Slot == SelectedSlot);
            var msg = existing == null
                ? $"[TacticsEditor] slot {SelectedSlot + 1} selected (empty -- LMB to place)"
                : $"[TacticsEditor] slot {SelectedSlot + 1} selected ({existing.FormationClass}/{existing.Arrangement}/{existing.Behavior})";
            InformationManager.DisplayMessage(new InformationMessage(msg, new Color(0.4f, 0.85f, 1.0f)));
            DiagLog.Log(Tag, msg);
            return;
        }
    }

    private void HandlePlacement()
    {
        if (!Input.IsKeyPressed(InputKey.LeftMouseButton)) return;
        if (!RaycastFromCursor(out var worldPos)) return;

        var slot = GetOrCreateSlot(SelectedSlot);
        slot.Position = new[] { worldPos.x, worldPos.y, worldPos.z };
        DiagLog.Log(Tag, $"slot {SelectedSlot + 1} placed at ({worldPos.x:F1}, {worldPos.y:F1}, {worldPos.z:F1})");
        InformationManager.DisplayMessage(new InformationMessage(
            $"[TacticsEditor] slot {SelectedSlot + 1} placed at {worldPos.x:F0},{worldPos.y:F0}",
            new Color(0.5f, 1.0f, 0.5f)));
    }

    private void HandleRotation()
    {
        if (!Input.IsKeyPressed(InputKey.R)) return;
        var slot = GetOrCreateSlot(SelectedSlot);
        var stepRad = (float)(Math.PI / 6.0); // 30°
        bool fine = Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift);
        if (fine) stepRad = (float)(Math.PI / 36.0); // 5°
        slot.FacingYaw = (slot.FacingYaw + stepRad) % ((float)Math.PI * 2f);
        var deg = slot.FacingYaw * 180f / (float)Math.PI;
        DiagLog.Log(Tag, $"slot {SelectedSlot + 1} facing {deg:F0}°");
        InformationManager.DisplayMessage(new InformationMessage(
            $"[TacticsEditor] slot {SelectedSlot + 1} facing {deg:F0}°"));
    }

    private void HandleCycleArrangement() => HandleCycle(InputKey.F, _arrangements,
        getter: s => s.Arrangement, setter: (s, v) => s.Arrangement = v, label: "arrangement");

    private void HandleCycleBehavior() => HandleCycle(InputKey.B, _behaviors,
        getter: s => s.Behavior, setter: (s, v) => s.Behavior = v, label: "behavior");

    private void HandleCycleClass() => HandleCycle(InputKey.C, _classes,
        getter: s => s.FormationClass, setter: (s, v) => s.FormationClass = v, label: "class");

    private void HandleCycle(InputKey key, string[] options,
        Func<FormationSlot, string> getter, Action<FormationSlot, string> setter, string label)
    {
        if (!Input.IsKeyPressed(key)) return;
        var slot = GetOrCreateSlot(SelectedSlot);
        var cur = getter(slot);
        var idx = Array.IndexOf(options, cur);
        var next = options[(idx + 1 + options.Length) % options.Length];
        setter(slot, next);
        DiagLog.Log(Tag, $"slot {SelectedSlot + 1} {label} -> {next}");
        InformationManager.DisplayMessage(new InformationMessage(
            $"[TacticsEditor] slot {SelectedSlot + 1} {label}: {next}"));
    }

    private void HandleDelete()
    {
        if (!Input.IsKeyPressed(InputKey.Delete)) return;
        var existing = Plan.Formations.FirstOrDefault(s => s.Slot == SelectedSlot);
        if (existing == null) return;
        Plan.Formations.Remove(existing);
        DiagLog.Log(Tag, $"slot {SelectedSlot + 1} removed from plan");
        InformationManager.DisplayMessage(new InformationMessage(
            $"[TacticsEditor] slot {SelectedSlot + 1} removed"));
    }

    private void HandleExport()
    {
        bool ctrl = Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl);
        if (!ctrl || !Input.IsKeyPressed(InputKey.S) || _exportCooldown > 0f) return;
        _exportCooldown = 0.5f;

        if (Plan.Formations.Count == 0)
        {
            InformationManager.DisplayMessage(new InformationMessage(
                "[TacticsEditor] nothing to export -- place at least one formation first",
                new Color(1.0f, 0.5f, 0.3f)));
            return;
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var modId = $"Tactic_{stamp}";
        Plan.Name = $"Tactic {DateTime.Now:yyyy-MM-dd HH:mm}";

        var result = TacticModExporter.Export(Plan, modId);
        var color = result.Success ? new Color(0.5f, 1.0f, 0.5f) : new Color(1.0f, 0.4f, 0.4f);
        InformationManager.DisplayMessage(new InformationMessage(
            $"[TacticsEditor] {result.Message}", color));
        DiagLog.Log(Tag, $"export result: success={result.Success}, path={result.Path}, msg={result.Message}");
    }

    private FormationSlot GetOrCreateSlot(int slotIndex)
    {
        var slot = Plan.Formations.FirstOrDefault(s => s.Slot == slotIndex);
        if (slot == null)
        {
            slot = new FormationSlot { Slot = slotIndex };
            Plan.Formations.Add(slot);
        }
        return slot;
    }

    // RaycastFromCursor: pure-reflection probe so we don't compile-bind to
    // TaleWorlds.MountAndBlade.View.dll (it's not on NuGet). We ask
    // ScreenManager.TopScreen for its current top screen as `object`,
    // check its type name to be sure it's a MissionScreen, then reflect
    // GetProjectedMousePositionOnGround off it. Signature has shifted
    // across game versions (sometimes (), sometimes (out Vec3, out bool)),
    // so we probe parameters and fill defaults regardless.
    private bool RaycastFromCursor(out Vec3 worldPos)
    {
        worldPos = Vec3.Zero;
        try
        {
            object? screen = ScreenManager.TopScreen;
            if (screen == null) return false;

            var screenType = screen.GetType();
            // Sanity guard: only proceed if this looks like a MissionScreen.
            // The full type name from the View DLL is
            // TaleWorlds.MountAndBlade.View.Screens.MissionScreen.
            if (!screenType.FullName?.EndsWith("MissionScreen", StringComparison.Ordinal) ?? true)
                return false;

            var m = screenType.GetMethod("GetProjectedMousePositionOnGround");
            if (m == null) return false;

            var ps = m.GetParameters();
            var args = new object?[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                if (ps[i].IsOut)
                {
                    args[i] = ps[i].ParameterType.GetElementType()!.IsValueType
                        ? Activator.CreateInstance(ps[i].ParameterType.GetElementType()!)
                        : null;
                }
                else if (ps[i].ParameterType == typeof(bool)) args[i] = false;
                else if (ps[i].ParameterType.IsValueType) args[i] = Activator.CreateInstance(ps[i].ParameterType);
                else args[i] = null;
            }
            var ret = m.Invoke(screen, args);
            if (ret is Vec3 hit && hit.LengthSquared > 0.0001f)
            {
                worldPos = hit;
                return true;
            }
            for (int i = 0; i < ps.Length; i++)
            {
                if (ps[i].IsOut && args[i] is Vec3 v && v.LengthSquared > 0.0001f)
                {
                    worldPos = v;
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "RaycastFromCursor", ex);
        }
        return false;
    }
}

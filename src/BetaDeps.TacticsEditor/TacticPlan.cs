// BetaDeps Tactics Editor -- data model.
//
// A TacticPlan is the on-disk representation of a battle plan: a list of
// formation slots, each with a world position, facing, formation class,
// arrangement, and movement behavior. Exported as JSON next to a
// SubModule.xml so the whole package is a drop-in standalone mod that
// applies the saved plan on every battle start.
//
// Position is world XYZ in meters. FacingYaw is radians, 0 = +X axis,
// clockwise viewed from above. FormationClass + Arrangement + Behavior
// are stored as enum-string names so a hand-edited JSON stays readable
// and old plans survive enum churn.
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using System;
using System.Collections.Generic;

using Newtonsoft.Json;

namespace BetaDeps.TacticsEditor;

/// <summary>
/// A single formation's planned placement and behavior at battle start.
/// </summary>
public sealed class FormationSlot
{
    /// <summary>Slot index 0-9 (matches the player's number-key selection).</summary>
    [JsonProperty("slot")]
    public int Slot { get; set; }

    /// <summary>World position in meters [x, y, z]. Z is up in TaleWorlds.</summary>
    [JsonProperty("position")]
    public float[] Position { get; set; } = new float[3];

    /// <summary>Facing yaw in radians. 0 = looking down +X axis.</summary>
    [JsonProperty("facingYaw")]
    public float FacingYaw { get; set; }

    /// <summary>FormationClass enum name (Infantry, Ranged, Cavalry, HorseArcher, etc.).</summary>
    [JsonProperty("formationClass")]
    public string FormationClass { get; set; } = "Infantry";

    /// <summary>ArrangementOrderEnum name (Line, Loose, Circle, ShieldWall, Skein, Column).</summary>
    [JsonProperty("arrangement")]
    public string Arrangement { get; set; } = "Line";

    /// <summary>Movement behavior keyword (Defend, Charge, FollowMe, Hold, Retreat).</summary>
    [JsonProperty("behavior")]
    public string Behavior { get; set; } = "Hold";

    /// <summary>Optional human-readable label (e.g. "Left flank pikes").</summary>
    [JsonProperty("label", NullValueHandling = NullValueHandling.Ignore)]
    public string? Label { get; set; }
}

/// <summary>
/// A complete tactic plan. Serializes to/from JSON; one file per tactic mod.
/// </summary>
public sealed class TacticPlan
{
    [JsonProperty("name")]
    public string Name { get; set; } = "Untitled Tactic";

    [JsonProperty("author")]
    public string Author { get; set; } = "Unknown";

    [JsonProperty("version")]
    public string Version { get; set; } = "1.0";

    [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
    public string? Description { get; set; }

    /// <summary>ISO 8601 UTC timestamp when the plan was last saved.</summary>
    [JsonProperty("savedAt")]
    public string SavedAt { get; set; } = DateTime.UtcNow.ToString("o");

    /// <summary>Which team the plan targets: Player, Defender, Attacker, or Any.</summary>
    [JsonProperty("targetTeam")]
    public string TargetTeam { get; set; } = "Player";

    /// <summary>Formation slots in placement order. Up to 10 slots (0-9).</summary>
    [JsonProperty("formations")]
    public List<FormationSlot> Formations { get; set; } = new();

    /// <summary>Schema version. Bump if breaking changes.</summary>
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;
}

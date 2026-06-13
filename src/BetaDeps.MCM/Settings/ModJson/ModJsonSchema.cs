// BetaDeps clean-room implementation. MIT, copyright 2026 Maxfield Management Group.
//
// ModJsonSchema -- the neutral, parsed representation of a consumer mod's
// `mod.json` declarative-settings file (BetaDeps v1.x modder layer). A mod
// author drops a mod.json next to their module and gets an MCM settings page
// with persistence, zero C#. ModJsonParser produces this model; ModJsonLoader
// turns it into a live fluent settings instance.

using System.Collections.Generic;

namespace BetaDeps.Framework
{
    /// <summary>Parsed `mod.json` settings declaration.</summary>
    public sealed class ModJsonSchema
    {
        /// <summary>Settings id (used as the JSON filename + MCM registry key).</summary>
        public string Id { get; set; } = "";
        /// <summary>Display name shown in the Mod Config list.</summary>
        public string Name { get; set; } = "";
        /// <summary>"global" (default), "percampaign", or "persave".</summary>
        public string Scope { get; set; } = "global";
        public List<ModJsonGroup> Groups { get; } = new();
    }

    public sealed class ModJsonGroup
    {
        public string Name { get; set; } = "General";
        public int Order { get; set; }
        public List<ModJsonProperty> Properties { get; } = new();
    }

    /// <summary>One declared setting. <see cref="Type"/> is bool|int|float|text.</summary>
    public sealed class ModJsonProperty
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public object? Default { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public string Hint { get; set; } = "";
        public bool RequireRestart { get; set; }
    }

    /// <summary>Outcome of parsing and/or loading a mod.json.</summary>
    public sealed class ModJsonResult
    {
        public bool Ok { get; set; }
        public string? Id { get; set; }
        public ModJsonSchema? Schema { get; set; }
        /// <summary>The live settings instance after a successful Load (null after Parse-only).</summary>
        public MCM.Abstractions.BaseSettings? Settings { get; set; }
        /// <summary>Resolved on-disk JSON path of the built settings (for inspection/cleanup).</summary>
        public string? SettingsFilePath { get; set; }
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();
    }
}

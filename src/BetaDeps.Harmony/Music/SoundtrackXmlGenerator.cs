// BetaDeps.Harmony -- SoundtrackXmlGenerator
//
// Turns the active BYO pools (MusicConfig) into the runtime PSAI project at
// Modules\BetaDeps\Music\soundtrack.xml, then leaves it for
// PsaiRedirectManager to hand to PsaiCore.LoadSoundtrackFromProjectFile.
//
// One PSAI Theme is emitted per active PSAI-path context (id = 9000N), and
// within it one Group per user track (each group = a single Segment). PSAI
// then rotates across groups for variety, exactly as Native's soundtrack.xml
// does (campaign theme = many single-segment groups). The element layout
// mirrors Native's file field-for-field so PSAI's XmlSerializer round-trips it.
//
// Path resolution constraint (decompiled PSAI PlatformLayerStandalone):
//   final clip path = Modules\BetaDeps\Music\<dir>\PC\<name>.ogg
// PSAI inserts a "PC/" folder before the filename and forces the .ogg
// extension. So we (a) keep the user's files where they dropped them
// (Music\BYO\<Context>\*.ogg) and stage a same-volume HARDLINK into
// Music\BYO\<Context>\PC\ so the converted path resolves with zero byte
// duplication, and (b) skip .wav in PSAI contexts because the forced .ogg
// extension can't reach them (the settlement/Engine.Music path in C3 handles
// .wav loosely).
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

using BetaDeps.Foundation;

namespace BetaDeps.Harmony.Music;

public sealed class SoundtrackGenResult
{
    public bool Success { get; set; }
    public int ThemeCount { get; set; }
    public int SegmentCount { get; set; }
    public string OutputPath { get; set; } = "";
}

public static class SoundtrackXmlGenerator
{
    private const string Tag = "SoundtrackXmlGen";

    // IMPORTANT: ids written here are LOCAL ids. PSAI prepends ModuleIdPrefix
    // ("9000") to every theme/group/segment id at load time and reparses --
    //     effectiveId = int.Parse("9000" + idInFile)
    // (decompiled PsaiProject, "theme.Id = int.Parse(ModuleIdPrefix + theme.Id)").
    // So a theme written as <Id>2</Id> becomes effective id 90002, matching
    // MusicContext.CustomThemeId(). Writing the full 90002 here would double the
    // prefix (900090002) and writing a large group/segment id would overflow
    // int32 and make PSAI throw, skipping the whole project. Keep these SMALL.
    private const int GroupIdBase = 0;
    private const int SegmentIdBase = 0;

    /// <summary>
    /// Generate Music\soundtrack.xml from the config's active PSAI contexts and
    /// stage hardlinks so every referenced .ogg resolves. Returns a result with
    /// Success=false (and writes nothing harmful) on any failure.
    /// </summary>
    public static SoundtrackGenResult Generate(MusicConfig cfg)
    {
        var outPath = Path.Combine(cfg.ModuleDir, "Music", "soundtrack.xml");
        try
        {
            int groupId = GroupIdBase;
            int segId = SegmentIdBase;
            int themeCount = 0, segCount = 0;

            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
            sb.Append("<!-- GENERATED AT RUNTIME by BetaDeps SoundtrackXmlGenerator. Do not edit; ")
              .Append("regenerated every launch from Music\\BYO\\. -->\n");
            sb.Append("<PsaiProject xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">\n");
            sb.Append("  <SerializedByProtocolVersion>1.0</SerializedByProtocolVersion>\n");
            sb.Append("  <Properties>\n");
            sb.Append("    <ModuleIdPrefix>9000</ModuleIdPrefix>\n");
            sb.Append("    <WarningThresholdPreBeatMillis>1500</WarningThresholdPreBeatMillis>\n");
            sb.Append("    <DefaultCalculatePostAndPrebeatLengthBasedOnBeats>false</DefaultCalculatePostAndPrebeatLengthBasedOnBeats>\n");
            sb.Append("    <DefaultSegmentSuitabilites>3</DefaultSegmentSuitabilites>\n");
            sb.Append("    <ForceFullRebuild>true</ForceFullRebuild>\n");
            sb.Append("    <VolumeBoost>0</VolumeBoost>\n");
            sb.Append("    <ExportSoundQualityInPercent>100</ExportSoundQualityInPercent>\n");
            sb.Append("    <DefaultPrebeats>1</DefaultPrebeats>\n");
            sb.Append("    <DefaultPostbeats>4</DefaultPostbeats>\n");
            sb.Append("    <DefaultBpm>100</DefaultBpm>\n");
            sb.Append("    <DefaultPrebeatLengthInSamples>88200</DefaultPrebeatLengthInSamples>\n");
            sb.Append("    <DefaultPostbeatLengthInSamples>176400</DefaultPostbeatLengthInSamples>\n");
            sb.Append("  </Properties>\n");
            sb.Append("  <Themes>\n");

            foreach (var ctx in cfg.ActivePsaiContexts())
            {
                var pool = cfg.PoolFor(ctx);
                if (pool == null || pool.IsEmpty) continue;

                var segmentsXml = new StringBuilder();
                int ctxSegments = 0;

                foreach (var trackPath in pool.Tracks)
                {
                    if (!string.Equals(Path.GetExtension(trackPath), ".ogg", StringComparison.OrdinalIgnoreCase))
                    {
                        DiagLog.Log(Tag, $"  skip non-.ogg on PSAI path ({ctx}): {Path.GetFileName(trackPath)} " +
                                         "— PSAI forces .ogg; use the settlement path for .wav.");
                        continue;
                    }

                    // Stage a hardlink so Music\BYO\<Context>\PC\<name>.ogg exists.
                    if (!StagePcLink(cfg.ByoRoot, ctx, trackPath, out string relPathNoExt))
                        continue;

                    var info = AudioMetadata.Read(trackPath);
                    AppendSegment(segmentsXml, ref groupId, ref segId, ctx, trackPath, relPathNoExt, info);
                    ctxSegments++;
                }

                if (ctxSegments == 0) continue;
                AppendTheme(sb, ctx, segmentsXml.ToString());
                themeCount++;
                segCount += ctxSegments;
                DiagLog.Log(Tag, $"  theme {ctx.CustomThemeId()} ({ctx}): {ctxSegments} segment(s)");
            }

            sb.Append("  </Themes>\n");
            sb.Append("</PsaiProject>\n");

            if (themeCount == 0)
            {
                DiagLog.Log(Tag, "no active PSAI-path tracks; writing empty soundtrack (vanilla plays).");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            DiagLog.Log(Tag, $"wrote {outPath}: {themeCount} theme(s), {segCount} segment(s).");

            return new SoundtrackGenResult
            {
                Success = true,
                ThemeCount = themeCount,
                SegmentCount = segCount,
                OutputPath = outPath,
            };
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, "Generate", ex);
            return new SoundtrackGenResult { Success = false, OutputPath = outPath };
        }
    }

    // ---- XML emission --------------------------------------------------

    private static void AppendTheme(StringBuilder sb, MusicContext ctx, string segmentsXml)
    {
        int localId = ctx.LocalThemeId();   // PSAI prepends "9000" -> effective CustomThemeId()
        int groupId = 0; // group ids are embedded in segmentsXml already
        sb.Append("    <Theme>\n");
        sb.Append("      <Name>").Append(Esc($"BetaDeps_{ctx}")).Append("</Name>\n");
        sb.Append("      <Id>").Append(localId).Append("</Id>\n");
        sb.Append("      <ThemeTypeInt>1</ThemeTypeInt>\n");                 // basicMood = looping mood theme
        sb.Append("      <Serialization_ManuallyBlockedThemeIds />\n");
        sb.Append("      <IntensityAfterRest>0.5</IntensityAfterRest>\n");
        sb.Append("      <MusicPhaseSecondsAfterRest>600</MusicPhaseSecondsAfterRest>\n");
        sb.Append("      <MusicPhaseSecondsGeneral>600</MusicPhaseSecondsGeneral>\n");
        sb.Append("      <RestSecondsMin>0</RestSecondsMin>\n");             // no silent gaps: play continuously
        sb.Append("      <RestSecondsMax>0</RestSecondsMax>\n");
        sb.Append("      <FadeoutMs>2000</FadeoutMs>\n");
        sb.Append("      <Priority>1</Priority>\n");
        sb.Append("      <WeightingSwitchGroups>1</WeightingSwitchGroups>\n"); // favor switching tracks = variety
        sb.Append("      <WeightingIntensityVsVariance>0.5</WeightingIntensityVsVariance>\n");
        sb.Append("      <WeightingLowPlaycountVsRandom>0.5</WeightingLowPlaycountVsRandom>\n");
        sb.Append("      <Groups>\n");
        sb.Append(segmentsXml);
        sb.Append("      </Groups>\n");
        sb.Append("    </Theme>\n");
        _ = groupId;
    }

    /// <summary>
    /// Append one Group (single Segment) for a track. Group/segment ids are
    /// advanced via ref so the whole project stays collision-free.
    /// </summary>
    private static void AppendSegment(
        StringBuilder sb, ref int groupId, ref int segId,
        MusicContext ctx, string trackPath, string relPathNoExt, AudioInfo info)
    {
        int g = ++groupId;
        int s = ++segId;
        int themeLocalId = ctx.LocalThemeId();   // segment's parent theme (local id; PSAI re-parents on load)
        string name = Path.GetFileNameWithoutExtension(trackPath);
        // Post-beat = ~2s tail for a clean transition; pre-beat 0 = start immediately.
        long postBeat = Math.Min(info.SampleRate * 2L, Math.Max(0, info.TotalSamples - 1));
        string total = info.TotalSamples.ToString(CultureInfo.InvariantCulture);
        string rate = info.SampleRate.ToString(CultureInfo.InvariantCulture);

        sb.Append("        <Group>\n");
        sb.Append("          <Name>").Append(Esc(name)).Append("</Name>\n");
        sb.Append("          <Segments>\n");
        sb.Append("            <Segment>\n");
        sb.Append("              <Name>").Append(Esc(name)).Append("</Name>\n");
        sb.Append("              <Id>").Append(s).Append("</Id>\n");
        sb.Append("              <IsAutomaticBridgeSegment>false</IsAutomaticBridgeSegment>\n");
        sb.Append("              <Intensity>0.5</Intensity>\n");
        sb.Append("              <IsUsableAtStart>true</IsUsableAtStart>\n");
        sb.Append("              <IsUsableInMiddle>true</IsUsableInMiddle>\n");
        sb.Append("              <IsUsableAtEnd>true</IsUsableAtEnd>\n");
        sb.Append("              <AudioData>\n");
        sb.Append("                <_prebeatLengthInSamplesEnteredManually>0</_prebeatLengthInSamplesEnteredManually>\n");
        sb.Append("                <_postbeatLengthInSamplesEnteredManually>").Append(postBeat).Append("</_postbeatLengthInSamplesEnteredManually>\n");
        sb.Append("                <Path>").Append(Esc(relPathNoExt)).Append("</Path>\n");
        sb.Append("                <Bpm>100</Bpm>\n");
        sb.Append("                <PreBeats>1</PreBeats>\n");
        sb.Append("                <PostBeats>4</PostBeats>\n");
        sb.Append("                <CalculatePostAndPrebeatLengthBasedOnBeats>false</CalculatePostAndPrebeatLengthBasedOnBeats>\n");
        sb.Append("                <PreBeatLengthInSamples>0</PreBeatLengthInSamples>\n");
        sb.Append("                <PostBeatLengthInSamples>").Append(postBeat).Append("</PostBeatLengthInSamples>\n");
        sb.Append("                <TotalLengthInSamples>").Append(total).Append("</TotalLengthInSamples>\n");
        sb.Append("                <SampleRate>").Append(rate).Append("</SampleRate>\n");
        sb.Append("              </AudioData>\n");
        sb.Append("              <CalculatePostAndPrebeatLengthBasedOnBeats>false</CalculatePostAndPrebeatLengthBasedOnBeats>\n");
        sb.Append("              <PreBeatLengthInSamples>0</PreBeatLengthInSamples>\n");
        sb.Append("              <PostBeatLengthInSamples>").Append(postBeat).Append("</PostBeatLengthInSamples>\n");
        sb.Append("              <PreBeats>1</PreBeats>\n");
        sb.Append("              <PostBeats>4</PostBeats>\n");
        sb.Append("              <Bpm>100</Bpm>\n");
        sb.Append("              <SampleRate>").Append(rate).Append("</SampleRate>\n");
        sb.Append("              <BitsPerSample>16</BitsPerSample>\n");
        sb.Append("              <ThemeId>").Append(themeLocalId).Append("</ThemeId>\n");
        sb.Append("              <Serialization_ManuallyBlockedSegmentIds />\n");
        sb.Append("              <Serialization_ManuallyLinkedSegmentIds />\n");
        sb.Append("              <DefaultCompatibiltyAsFollower>allowed_implicitly</DefaultCompatibiltyAsFollower>\n");
        sb.Append("            </Segment>\n");
        sb.Append("          </Segments>\n");
        sb.Append("          <Description />\n");
        sb.Append("          <Serialization_Id>").Append(g).Append("</Serialization_Id>\n");
        sb.Append("          <Serialization_ManuallyBlockedGroupIds />\n");
        sb.Append("          <Serialization_ManuallyLinkedGroupIds />\n");
        sb.Append("          <Serialization_ManualBridgeSegmentIds />\n");
        sb.Append("        </Group>\n");
    }

    // ---- hardlink staging ---------------------------------------------

    /// <summary>
    /// Ensure Music\BYO\<Context>\PC\<name>.ogg exists as a hardlink to the
    /// user's track, and return the project-relative path (no extension) for
    /// the &lt;Path&gt; element, e.g. "BYO/Menu/mytrack". PSAI's platform layer
    /// will convert that to "BYO/Menu/PC/mytrack.ogg".
    /// </summary>
    private static bool StagePcLink(string byoRoot, MusicContext ctx, string trackPath, out string relPathNoExt)
    {
        relPathNoExt = "";
        try
        {
            string name = Path.GetFileName(trackPath);                      // mytrack.ogg
            string ctxRel = ctx.FolderRelativePath();                       // e.g. "Menu" or "Settlement/Town"
            string pcDir = Path.Combine(byoRoot, ctxRel.Replace('/', Path.DirectorySeparatorChar), "PC");
            Directory.CreateDirectory(pcDir);
            string linkPath = Path.Combine(pcDir, name);

            // Always refresh: if the user replaced BYO\<ctx>\<name>.ogg with a
            // different file of the same name, the existing PC\ link is stale
            // (it still points at the old file's content). linkPath is always in
            // the PC\ subdir, never the source track, so deleting it is safe.
            try { if (File.Exists(linkPath)) File.Delete(linkPath); } catch { /* fall through; create may still succeed */ }
            if (!CreateHardLink(linkPath, trackPath, IntPtr.Zero))
            {
                // Cross-volume or permission failure: fall back to a copy so
                // the track still resolves. Wasteful but correct.
                File.Copy(trackPath, linkPath, overwrite: true);
                DiagLog.Log(Tag, $"  hardlink failed for {name}; copied into PC\\ instead.");
            }

            // <Path> is project-relative (to Music\), extension stripped (PSAI
            // re-adds .ogg). Forward slashes per the schema.
            relPathNoExt = $"BYO/{ctxRel}/{Path.GetFileNameWithoutExtension(name)}";
            return true;
        }
        catch (Exception ex)
        {
            DiagLog.LogCaught(Tag, $"StagePcLink({Path.GetFileName(trackPath)})", ex);
            return false;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    // ---- helpers ------------------------------------------------------

    private static string Esc(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}

// BetaDeps clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
// v0.4.19: reverted to 10-slot inline pagination (v0.4.16 working pattern).
// NavigatableListPanel + ItemTemplate failed to iterate CurrentRows twice
// (v0.4.5 with MCMOptionRow prefab include, v0.4.18 with inline ItemTemplate).
// UIExtenderEx mixin DataSourceProperty doesn't seem to surface MBBindingList<T>
// in a way NavigatableListPanel recognizes. Sticking with what works.

using System.Text;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;

namespace MCM.UI.PrefabExtensions;

[PrefabExtension("Options", "descendant::ListPanel[@Id='TabToggleList']")]
internal sealed class MCMTabTogglePatch : PrefabExtensionInsertPatch
{
    public override InsertType Type => InsertType.Child;

    [PrefabExtensionText]
    public string XmlContent =>
        "<OptionsTabToggle Id=\"BetaDepsModConfigTabToggle\" Parameter.TabName=\"BetaDepsModConfigPage\">\n"
        + "  <Children>\n"
        + "    <TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\"\n"
        + "                HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"\n"
        + "                DoNotAcceptEvents=\"true\" Text=\"Mod Config\" />\n"
        + "  </Children>\n"
        + "</OptionsTabToggle>";
}

/// <summary>
/// v1.0 (task #5): inject the hover hint into the vanilla DescriptionsRightPanel
/// so it appears on the right side of the Options screen exactly like Video/
/// Audio/Gameplay options do. Sits above the empty vanilla
/// CurrentOptionNameWidget so when the user is on the BetaDeps tab and hovers
/// a property row, the hint text fills the same screen area Bannerlord trains
/// users to look at. IsVisible="@IsHintVisible" keeps it hidden when no row
/// is hovered, and naturally hides when the user switches away from our tab
/// (the OptionsVM mixin's static HoverEndCallback clears the state on tab
/// changes via the same Q/E suppression path).
/// </summary>
[PrefabExtension("Options", "descendant::Widget[@Id='DescriptionsRightPanel']")]
internal sealed class MCMDescriptionsRightPanelPatch : PrefabExtensionInsertPatch
{
    public override InsertType Type => InsertType.Child;

    [PrefabExtensionText]
    public string XmlContent =>
        // ListPanel sits at the top of DescriptionsRightPanel, sized to the
        // same 650-px width vanilla uses for its CurrentOptionNameWidget /
        // CurrentOptionDescriptionWidget pair. MarginTop=50 leaves a gap from
        // the top of the right-side area so the name doesn't smack against
        // the panel edge.
        "<ListPanel Id=\"BetaDepsHintPanel\" IsVisible=\"@IsHintVisible\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Top\" MarginTop=\"50\" StackLayout.LayoutMethod=\"VerticalTopToBottom\">\n"
        + "  <Children>\n"
        + "    <RichTextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" Brush=\"SPOptions.Description.Title.Text\" Text=\"@HoveredOptionName\" />\n"
        + "    <RichTextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" MarginTop=\"25\" Brush=\"SPOptions.Description.Text\" Text=\"@HoveredHintText\" />\n"
        + "  </Children>\n"
        + "</ListPanel>";
}

[PrefabExtension("Options", "descendant::TabControl[@Id='TabControl']")]
internal sealed class MCMTabContentPatch : PrefabExtensionInsertPatch
{
    public override InsertType Type => InsertType.Child;
    // v1.0 (task #12): bumped 10 → 100 alongside the OptionsVMMixin
    // SlotCount bump, so the unpaginated scrollable mod page can show every
    // property at once. Slot rows 0-9 use the per-mixin-property template
    // (with sliders for slots 0-5 thanks to the 6-slider ceiling); rows
    // 10-99 use the VM-context template in BuildVmSlotRow (text-only for
    // numerics, no slider). Mods with >100 settings overflow silently; bump
    // higher here AND in the mixin if needed.
    private const int SlotCount = 20;

    // v0.5.0 perf: XML is identical between Options-open events (slot bindings
    // make it data-driven; the XML template never changes per-session). Build
    // once, cache the result, and skip the dump-file write when the content
    // hasn't changed since last write.
    private static string? _cachedXml;
    private static long _lastDumpHash;

    /// <summary>
    /// FNV-1a 64-bit string hash. Stable across runtimes / processes
    /// (unlike string.GetHashCode() which is randomized on .NET Core+).
    /// Used only for change-detection on the PatchedOptions.dump.xml file;
    /// no security or cryptographic claims.
    /// </summary>
    private static long StableHash(string s)
    {
        if (s == null) return 0;
        const ulong fnvOffset = 14695981039346656037UL;
        const ulong fnvPrime  =        1099511628211UL;
        ulong h = fnvOffset;
        for (int i = 0; i < s.Length; i++)
        {
            h ^= (byte)(s[i] & 0xff);
            h *= fnvPrime;
            h ^= (byte)((s[i] >> 8) & 0xff);
            h *= fnvPrime;
        }
        return unchecked((long)h);
    }

    [PrefabExtensionText]
    public string XmlContent
    {
        get
        {
            if (_cachedXml != null) return _cachedXml;

            var sb = new StringBuilder(65536);
            sb.Append(HeaderXml);
            for (int i = 0; i < SlotCount; i++) sb.Append(BuildSlotRow(i));
            sb.Append(FooterXml);
            var xml = sb.ToString();
            _cachedXml = xml;

            // Write the dump file only if its contents would actually change.
            // v0.6 fix: previously used string.GetHashCode() which is
            // randomized per-process on .NET Core+. Currently fine on
            // net472 (stable) but would silently break dump-skip on
            // any future retarget. FNV-1a is stable across runtimes
            // and allocation-free.
            try
            {
                var rtPath = BetaDeps.Foundation.RuntimeLog.Path;
                var dir = System.IO.Path.GetDirectoryName(rtPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    var dumpPath = System.IO.Path.Combine(dir, "PatchedOptions.dump.xml");
                    long h = StableHash(xml);
                    if (h != _lastDumpHash)
                    {
                        System.IO.File.WriteAllText(dumpPath, xml);
                        _lastDumpHash = h;
                        BetaDeps.Foundation.RuntimeLog.Write("MCMTabContentPatch", "wrote PatchedOptions.dump.xml (" + xml.Length + " chars)");
                    }
                }
            }
            catch { }
            return xml;
        }
    }

    private const string HeaderXml =
        "<ListPanel Id=\"BetaDepsModConfigPage\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Top\" MarginTop=\"10\" MarginBottom=\"10\" StackLayout.LayoutMethod=\"VerticalTopToBottom\">\n"
        + "  <Children>\n"
        + "    <RichTextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" Brush=\"SPOptions.Description.Title.Text\" Text=\"@BetaDepsModConfigTitle\" />\n"
        + "    <RichTextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" MarginTop=\"8\" Brush=\"SPOptions.Group.Title.Text\" Text=\"@BetaDepsModConfigSummary\" />\n"
        // v1.0: live mod-list search/filter. Inline EditableTextWidget binds
        // two-way to BetaDepsModSearchText; the setter calls ApplyFilter() on
        // every keystroke, so the Prev/Next cycler narrows as the user types.
        //
        // The Q/E tab-switching conflict (OptionsVM.SelectPreviousCategory /
        // SelectNextCategory fire from a game-input hotkey, independent of UI
        // focus) is handled by a Harmony patch in TabSwitchGuardPatch.cs that
        // suppresses tab switches when any EditableTextWidget has focus.
        + "    <ListPanel WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" MarginTop=\"12\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">\n"
        + "      <Children>\n"
        + "        <RichTextWidget WidthSizePolicy=\"Fixed\" SuggestedWidth=\"100\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"36\" HorizontalAlignment=\"Right\" VerticalAlignment=\"Center\" TextHorizontalAlignment=\"Right\" Brush=\"SPOptions.OptionName.Text\" Text=\"Search:\" />\n"
        + "        <EditableTextWidget Id=\"BetaDepsSearchInput\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"480\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"36\" MarginLeft=\"10\" VerticalAlignment=\"Center\" Brush=\"CustomBattle.Search.TextBox\" Text=\"@BetaDepsModSearchText\" GamepadNavigationIndex=\"0\" />\n"
        + "        <ButtonWidget Command.Click=\"ExecuteClearSearch\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"100\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"36\" MarginLeft=\"10\" VerticalAlignment=\"Center\" Brush=\"Popup.Cancel.Button\" UpdateChildrenStates=\"true\" IsVisible=\"@BetaDepsSearchClearVisible\">\n"
        + "          <Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" DoNotAcceptEvents=\"true\" Brush=\"Popup.Button.Text\" Text=\"Clear\" /></Children>\n"
        + "        </ButtonWidget>\n"
        + "      </Children>\n"
        + "    </ListPanel>\n"
        + "    <ListPanel WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" MarginTop=\"12\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">\n"
        + "      <Children>\n"
        + "        <ButtonWidget Command.Click=\"ExecutePrevMod\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"160\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"48\" VerticalAlignment=\"Center\" Brush=\"Popup.Cancel.Button\" UpdateChildrenStates=\"true\">\n"
        + "          <Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" DoNotAcceptEvents=\"true\" Brush=\"Popup.Button.Text\" Text=\"&lt; Prev Mod\" /></Children>\n"
        + "        </ButtonWidget>\n"
        // Mod-name / summary panel. The middle container is a fixed-width
        // ListPanel with a vertical stack layout, and each text inside is
        // WidthSizePolicy="CoverChildren" with HorizontalAlignment="Center" so
        // the widget itself shrinks to its text and then centers within the
        // 900-px parent. We can't rely on TextHorizontalAlignment on a
        // StretchToParent RichTextWidget — RichTextWidget honors the brush's
        // baked-in alignment (which is Left for SPOptions.Description.Title.Text),
        // so the title used to drift toward the Prev Mod button on the left.
        + "        <ListPanel WidthSizePolicy=\"Fixed\" SuggestedWidth=\"900\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"52\" MarginLeft=\"20\" MarginRight=\"20\" VerticalAlignment=\"Center\" StackLayout.LayoutMethod=\"VerticalTopToBottom\">\n"
        + "          <Children>\n"
        + "            <RichTextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" Brush=\"SPOptions.Description.Title.Text\" Text=\"@SelectedModName\" />\n"
        + "            <RichTextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" MarginTop=\"2\" Brush=\"SPOptions.Group.Title.Text\" Text=\"@SelectedModSummary\" />\n"
        + "          </Children>\n"
        + "        </ListPanel>\n"
        + "        <ButtonWidget Command.Click=\"ExecuteNextMod\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"160\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"48\" VerticalAlignment=\"Center\" Brush=\"Popup.Cancel.Button\" UpdateChildrenStates=\"true\">\n"
        + "          <Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" DoNotAcceptEvents=\"true\" Brush=\"Popup.Button.Text\" Text=\"Next Mod &gt;\" /></Children>\n"
        + "        </ButtonWidget>\n"
        + "      </Children>\n"
        + "    </ListPanel>\n"
        // v1.0 (task #5 v2): hint panel moved AGAIN to the vanilla
        // DescriptionsRightPanel on the right side of the Options screen
        // (see MCMDescriptionsRightPanelPatch above). That's where vanilla
        // puts Video/Audio option descriptions, and matches what users
        // already expect. The previous attempt to put it inline above the
        // slot list took 60 px of vertical space that's better used by the
        // slot rows themselves.
        + "    <ListPanel WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" MarginTop=\"20\" StackLayout.LayoutMethod=\"VerticalTopToBottom\">\n"
        + "      <Children>\n";

    private const string FooterXml =
        "      </Children>\n"
        + "    </ListPanel>\n"
        // v0.5.9 (task #13 perf): soft pagination restored. With SlotCount=20
        // ROT-class mods (200+ properties) get sliced into pages. Prev/Next
        // Page buttons + "Page X of Y" indicator are gated on
        // @PaginationVisible so single-page mods (<20 properties, the
        // common case) don't show pagination clutter.
        + "    <RichTextWidget IsVisible=\"@PaginationVisible\" WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" MarginTop=\"15\" Brush=\"SPOptions.Group.Title.Text\" Text=\"@SelectedPageSummary\" />\n"
        + "    <ListPanel IsVisible=\"@PaginationVisible\" WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" MarginTop=\"10\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">\n"
        + "      <Children>\n"
        + "        <ButtonWidget Command.Click=\"ExecutePrevPage\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"160\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"48\" Brush=\"Popup.Cancel.Button\" UpdateChildrenStates=\"true\">\n"
        + "          <Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" DoNotAcceptEvents=\"true\" Brush=\"Popup.Button.Text\" Text=\"&lt; Prev Page\" /></Children>\n"
        + "        </ButtonWidget>\n"
        + "        <ButtonWidget Command.Click=\"ExecuteNextPage\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"160\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"48\" MarginLeft=\"20\" Brush=\"Popup.Cancel.Button\" UpdateChildrenStates=\"true\">\n"
        + "          <Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" DoNotAcceptEvents=\"true\" Brush=\"Popup.Button.Text\" Text=\"Next Page &gt;\" /></Children>\n"
        + "        </ButtonWidget>\n"
        + "      </Children>\n"
        + "    </ListPanel>\n"
        + "    <ListPanel WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" MarginTop=\"15\" MarginBottom=\"15\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">\n"
        + "      <Children>\n"
        + "        <ButtonWidget Command.Click=\"ExecuteResetDefaults\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"260\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"52\" Brush=\"Popup.Done.Button.NineGrid\" UpdateChildrenStates=\"true\">\n"
        + "          <Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" DoNotAcceptEvents=\"true\" Brush=\"Popup.Button.Text\" Text=\"Reset to Defaults\" /></Children>\n"
        + "        </ButtonWidget>\n"
        + "        <ButtonWidget Command.Click=\"ExecuteRunSelfTest\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"260\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"52\" MarginLeft=\"20\" Brush=\"Popup.Done.Button.NineGrid\" UpdateChildrenStates=\"true\">\n"
        + "          <Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" DoNotAcceptEvents=\"true\" Brush=\"Popup.Button.Text\" Text=\"@SelfTestButtonText\" /></Children>\n"
        + "        </ButtonWidget>\n"
        + "        <ButtonWidget Command.Click=\"ExecuteSendToGitHub\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"260\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"52\" MarginLeft=\"20\" Brush=\"Popup.Done.Button.NineGrid\" UpdateChildrenStates=\"true\">\n"
        + "          <Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" DoNotAcceptEvents=\"true\" Brush=\"Popup.Button.Text\" Text=\"Send to GitHub\" /></Children>\n"
        + "        </ButtonWidget>\n"
        + "        <ButtonWidget Command.Click=\"ExecuteToggleAutoDisable\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"260\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"52\" MarginLeft=\"20\" Brush=\"Popup.Done.Button.NineGrid\" UpdateChildrenStates=\"true\">\n"
        + "          <Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" DoNotAcceptEvents=\"true\" Brush=\"Popup.Button.Text\" Text=\"Toggle Auto-Disable\" /></Children>\n"
        + "        </ButtonWidget>\n"
        + "        <ButtonWidget Command.Click=\"ExecuteTogglePatchShield\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"260\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"52\" MarginLeft=\"20\" Brush=\"Popup.Done.Button.NineGrid\" UpdateChildrenStates=\"true\">\n"
        + "          <Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" DoNotAcceptEvents=\"true\" Brush=\"Popup.Button.Text\" Text=\"Toggle PatchShield\" /></Children>\n"
        + "        </ButtonWidget>\n"
        + "        <ButtonWidget Command.Click=\"ExecuteToggleSaveShieldSwallow\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"260\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"52\" MarginLeft=\"20\" Brush=\"Popup.Done.Button.NineGrid\" UpdateChildrenStates=\"true\">\n"
        + "          <Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" DoNotAcceptEvents=\"true\" Brush=\"Popup.Button.Text\" Text=\"Toggle SaveShield Swallow\" /></Children>\n"
        + "        </ButtonWidget>\n"
        + "      </Children>\n"
        + "    </ListPanel>\n"
        // v1.0: dead space after the action button row so the Cancel/Done bar
        // at the very bottom of the Options screen doesn't sit on top of
        // Reset to Defaults / Run Self-Test when the user scrolls to the
        // bottom of a tall mod. 120 px is roughly the height of the vanilla
        // bottom button bar plus a small margin.
        + "    <Widget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"1\" SuggestedHeight=\"120\" />\n"
        + "  </Children>\n"
        + "</ListPanel>";

    // v0.6 RC8: extend vanilla slider+nav-siblings to all 10 slots after RC7
    // confirmed slot 0 didn't crash the page. The KEY pattern (vs RC2-RC6) is
    // the NavigationTargetSwitcher + NavigationAutoScrollWidget pair as the
    // FIRST two children of the slider's ListPanel -- a piece vanilla
    // OptionItem.xml has and that every prior bisect was missing.
    private static string BuildSlotRow(int n)
    {
        // v1.0 (task #12): two `DataSource="{Slot{n}_VM}"` experiments both
        // failed — Gauntlet's DataSource scope only propagates to children
        // through ItemTemplate iteration, and dot-notation `@Slot10_VM.X`
        // didn't resolve either. So every slot 0-249 uses the same per-mixin-
        // property template; the Slot{n}_X accessors for slots 10-249 are
        // generated as a partial class in OptionsVMMixin.SlotProperties.g.cs.

        var s = n.ToString();
        // Numeric display: slot 0 -> slider with vanilla nav-siblings pattern,
        //                 slots 1-9 -> read-only RichTextWidget (current behavior).
        // v0.6 RC13: RC12 confirmed all 3 slider bindings work in isolation.
        // RC13 tests the IsVisible="@Slot0_IsInteger" binding on the wrapping
        // ListPanel — that was the OTHER big difference between working RC12
        // and crashing RC8. In RC8 every slot wrapped its slider in
        // <ListPanel IsVisible="@Slot{n}_IsInteger">; if that binding causes
        // a construction-time crash (because the binding system tries to
        // resolve it before the data source is ready), then the IsVisible
        // approach is poisoned and we have to find another way to hide
        // sliders for non-numeric slots.
        // Slot 0 will usually land on a group header, so Slot0_IsInteger=false
        // and the slider will be hidden. The question is: does the BINDING
        // resolution itself crash, regardless of the value?
        // v0.6 RC17: RC15 (5) worked, RC16 (7) crashed. Try 6 sliders.
        // Slots 1-5 use BuildSliderBlock (5 sliders) + slot 0 hardcoded (1) = 6.
        // If RC17 works: threshold is 6 exactly; 7 is too many.
        // If RC17 crashes: threshold is 5 exactly; 6 is too many.
        // v0.5.5 unified-binding ship: ONE slider per slot covers BOTH int
        // and float settings. Slot{n}_FloatValue dispatches internally on
        // IsInteger vs IsFloating, and Slot{n}_IsNumeric (new) controls
        // visibility. Net result: 6 sliders per page (within the safe
        // ceiling), but those 6 sliders now cover both int AND float
        // numerics, not just int. Slots 6-9 still text-only.
        // v1.0 (task #7 + #13 perf): the 6-slider crash ceiling is lifted
        // (UpdateValueContinuously was the missing attribute) but each
        // SliderWidget is still a heavy widget — 5 sprite layers, native
        // handle allocation, per-frame layout. With 50 slot rows and a
        // slider per row, scrolling stutters because Gauntlet relays out
        // every slider on every scroll tick. Gate slider rendering to the
        // first 20 numeric slots (sliders for the most-likely-edited
        // settings, text fallback for the tail). 20 is empirical — feels
        // smooth in testing and covers >95% of consumer-mod use cases.
        string numericDisplay = (n < 20)
            ? BuildUnifiedSliderBlock(s)
            : "                <RichTextWidget IsVisible=\"@Slot" + s + "_IsNumeric\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" MarginLeft=\"20\" Brush=\"SPOptions.Dropdown.Center.Text\" Text=\"@Slot" + s + "_ValueText\" />\n";

        // v1.0 (task #5): Command.HoverBegin / Command.HoverEnd on each slot
        // row's outer Widget invoke ExecuteSlot{n}Hover / ExecuteSlot{n}HoverEnd
        // on the mixin, which populate HoveredOptionName + HoveredHintText.
        // Those bindings drive the hint panel in FooterXml (gated on
        // IsHintVisible). Wired per-slot because Gauntlet can't dynamically
        // dispatch one handler with the slot index as an argument.
        return
            "        <Widget IsVisible=\"@Slot" + s + "_IsVisible\" Command.HoverBegin=\"ExecuteSlot" + s + "Hover\" Command.HoverEnd=\"ExecuteSlot" + s + "HoverEnd\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"1000\" SuggestedHeight=\"50\" MarginTop=\"4\">\n"
            + "          <Children>\n"
            + "            <RichTextWidget IsVisible=\"@Slot" + s + "_IsHeader\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Brush=\"SPOptions.GameKeysGroup.Title.Text\" Text=\"@Slot" + s + "_GroupHeader\" />\n"
            + "            <ListPanel IsVisible=\"@Slot" + s + "_IsProperty\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">\n"
            + "              <Children>\n"
            + "                <TextWidget DoNotAcceptEvents=\"true\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"500\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Right\" VerticalAlignment=\"Center\" Brush=\"SPOptions.OptionName.Text\" Text=\"@Slot" + s + "_DisplayName\" />\n"
            // v1.0 (task #5): Command.HoverBegin/HoverEnd added to interactive
            // child widgets too — ButtonWidget consumes hover events for its
            // own brush state changes and doesn't bubble them to the outer
            // Widget, so the hint panel never fired when the user hovered the
            // toggle/button directly. Wiring it on the button itself routes
            // the same ExecuteSlot{n}Hover call.
            + "                <ButtonWidget IsVisible=\"@Slot" + s + "_IsBool\" Command.Click=\"ExecuteSlot" + s + "ToggleBool\" Command.HoverBegin=\"ExecuteSlot" + s + "Hover\" Command.HoverEnd=\"ExecuteSlot" + s + "HoverEnd\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"120\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"40\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" MarginLeft=\"20\" Brush=\"Popup.Cancel.Button\" UpdateChildrenStates=\"true\">\n"
            + "                  <Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" DoNotAcceptEvents=\"true\" Brush=\"Popup.Button.Text\" Text=\"@Slot" + s + "_BoolText\" /></Children>\n"
            + "                </ButtonWidget>\n"
            // v0.5.6: action-button widget for IsButton settings (Discord
            // links, etc.). Bound to ExecuteSlot{n}ActionButton, hidden for
            // non-button slots via Slot{n}_IsButton visibility binding.
            // v1.0 (BEW shim): button text reads from Slot{n}_ButtonText so
            // fluent buttons added via AddButton(...) show their proper
            // `content` label (e.g. "Reset") instead of a generic "Run".
            + "                <ButtonWidget IsVisible=\"@Slot" + s + "_IsButton\" Command.Click=\"ExecuteSlot" + s + "ActionButton\" Command.HoverBegin=\"ExecuteSlot" + s + "Hover\" Command.HoverEnd=\"ExecuteSlot" + s + "HoverEnd\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"240\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"40\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" MarginLeft=\"20\" Brush=\"Popup.Done.Button.NineGrid\" UpdateChildrenStates=\"true\">\n"
            + "                  <Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" DoNotAcceptEvents=\"true\" Brush=\"Popup.Button.Text\" Text=\"@Slot" + s + "_ButtonText\" /></Children>\n"
            + "                </ButtonWidget>\n"
            + numericDisplay
            + "                <RichTextWidget IsVisible=\"@Slot" + s + "_IsText\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" MarginLeft=\"20\" Brush=\"SPOptions.Dropdown.Center.Text\" Text=\"@Slot" + s + "_TextValue\" />\n"
            + "                <RichTextWidget IsVisible=\"@Slot" + s + "_IsDropdown\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" MarginLeft=\"20\" Brush=\"SPOptions.Dropdown.Center.Text\" Text=\"@Slot" + s + "_DropdownText\" />\n"
            + "              </Children>\n"
            + "            </ListPanel>\n"
            + "          </Children>\n"
            + "        </Widget>\n";
    }

    /// <summary>
    /// v1.0 (task #12): VM-context slot template used for slots 10-99 in the
    /// unpaginated scrollable mod page. Outer Widget keeps mixin context so
    /// Command.HoverBegin/HoverEnd hit the mixin's ExecuteSlot{n}Hover handlers
    /// (which feed the HoveredOptionName/HintText panel below the list); the
    /// inner Widget switches DataSource to Slot{n}_VM so every binding inside
    /// (IsHeader, IsProperty, DisplayName, IsBool, BoolValue, IsButton,
    /// ButtonContentText, IsNumeric, ValueText, IsText, TextValue, IsDropdown,
    /// DropdownText) reads from the SettingsPropertyVM directly. No slider —
    /// numeric properties in slots 10-99 fall back to text-only (lifted when
    /// task #7 raises the 6-slider ceiling).
    /// </summary>
    private static string BuildVmSlotRow(int n)
    {
        var s = n.ToString();
        // v1.0 (task #12 debug): minimal template — just display the
        // DisplayName and a one-character type-kind indicator so we can see
        // whether the DataSource scope is actually propagating bindings to
        // VM context. If `@DisplayName` renders correctly we know the scope
        // works and the broken rendering was something else; if it shows
        // blank we know the scope itself doesn't propagate and we need a
        // different approach.
        // Diagnostic 2: try dot-notation path binding `Slot{n}_VM.DisplayName`
        // without a DataSource attribute. If Gauntlet supports nested-property
        // syntax, we can route every binding through the VM by name without
        // needing the scope to "stick" to child elements (which it apparently
        // doesn't outside of ItemTemplate).
        return
            "        <Widget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"1000\" SuggestedHeight=\"40\" MarginTop=\"4\">\n"
            + "          <Children>\n"
            + "            <RichTextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Brush=\"SPOptions.OptionName.Text\" Text=\"@Slot" + s + "_VM.DisplayName\" />\n"
            + "          </Children>\n"
            + "        </Widget>\n";
    }

    // v1.0 (task #12): touched to force the harness to flush its cached
    // copy of this file back to disk — bash-side Python writes were getting
    // truncated by the remote-mount layer on the way out.
    //
    // v0.6 RC7: vanilla OptionItem.xml NumericOption block, slot-prefixed for
    // unique widget IDs. The KEY difference vs all previous attempts is the
    // NavigationTargetSwitcher + NavigationAutoScrollWidget pair as the FIRST
    // two children of the ListPanel, before SliderWidget. CLAUDE.md notes this
    // pattern was missing from every prior bisect.
    // v0.5.5 unified slider block. One slider per slot covering int and float.
    // - Bound to Slot{n}_FloatValue (the mixin getter dispatches int vs float)
    // - IsDiscrete bound to Slot{n}_IsInteger (true snaps to whole numbers, false continuous)
    // - DiscreteIncrementInterval is a literal "1" — works for ints; floats ignore it when IsDiscrete=false
    // - IsVisible bound to Slot{n}_IsNumeric (IsInteger || IsFloating)
    private static string BuildUnifiedSliderBlock(string s)
    {
        string sliderId = "Slot" + s + "NumSlider";
        string fillerId = "Slot" + s + "NumFiller";
        string handleId = "Slot" + s + "NumSliderHandle";
        return
            "                <ListPanel IsVisible=\"@Slot" + s + "_IsNumeric\" WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" MarginLeft=\"20\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">\n"
            + "                  <Children>\n"
            + "                    <NavigationTargetSwitcher FromTarget=\"..\\.\" ToTarget=\"..\\" + sliderId + "\\" + handleId + "\" />\n"
            + "                    <NavigationAutoScrollWidget TrackedWidget=\"..\\" + sliderId + "\\" + handleId + "\" AutoScrollTopOffset=\"90\" AutoScrollBottomOffset=\"90\" />\n"
            // v1.0 (task #7): added UpdateValueContinuously="@SliderUpdateFalse"
            // — vanilla OptionItem.xml binds this attribute (we missed it
            // entirely), and the SliderWidget construction path may treat its
            // absence as an uninitialized native handle that runs out of room
            // past 6 instances per page. Bound to a static `false` mixin
            // property so the slider only fires its ValueFloat change after
            // the user releases the handle (matching vanilla behavior for
            // game-options sliders). DiscreteIncrementInterval also now a
            // binding — vanilla binds it, we had a literal "1" which the
            // construction may dislike when widgets compete for whatever
            // shared resource the 6-slider ceiling implies.
            + "                    <SliderWidget Id=\"" + sliderId + "\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"338\" SuggestedHeight=\"42\" VerticalAlignment=\"Center\" DoNotUpdateHandleSize=\"true\" Filler=\"" + fillerId + "\" Handle=\"" + handleId + "\" IsDiscrete=\"@Slot" + s + "_IsInteger\" DiscreteIncrementInterval=\"@SliderIncrementOne\" MaxValueFloat=\"@Slot" + s + "_MaxValue\" MinValueFloat=\"@Slot" + s + "_MinValue\" ValueFloat=\"@Slot" + s + "_FloatValue\" UpdateValueContinuously=\"@SliderUpdateFalse\">\n"
            + "                      <Children>\n"
            + "                        <Widget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"362\" SuggestedHeight=\"38\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Sprite=\"SPGeneral\\SPOptions\\standart_slider_canvas\" DoNotAcceptEvents=\"true\" />\n"
            + "                        <Widget Id=\"" + fillerId + "\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"345\" SuggestedHeight=\"35\" VerticalAlignment=\"Center\" Sprite=\"SPGeneral\\SPOptions\\standart_slider_fill\" ClipContents=\"true\">\n"
            + "                          <Children>\n"
            + "                            <Widget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"345\" SuggestedHeight=\"35\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" Sprite=\"SPGeneral\\SPOptions\\standart_slider_fill\" />\n"
            + "                          </Children>\n"
            + "                        </Widget>\n"
            + "                        <Widget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"400\" SuggestedHeight=\"65\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Sprite=\"SPGeneral\\SPOptions\\standart_slider_frame\" DoNotAcceptEvents=\"true\" />\n"
            + "                        <ImageWidget Id=\"" + handleId + "\" DoNotAcceptEvents=\"true\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"14\" SuggestedHeight=\"38\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" Brush=\"SPOptions.Slider.Handle\" />\n"
            + "                      </Children>\n"
            + "                    </SliderWidget>\n"
            + "                    <RichTextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" MarginLeft=\"20\" Brush=\"SPOptions.Slider.Value.Text\" IsEnabled=\"false\" Text=\"@Slot" + s + "_ValueText\" />\n"
            + "                  </Children>\n"
            + "                </ListPanel>\n";
    }
}

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
        // Polish #10: tab label is "Mod Configuration" (was "Mod Config"),
        // matching the in-page title verbatim. Longer tab text reads as
        // "this is a different kind of tab" against the vanilla
        // Video / Audio / Gameplay / Keybindings labels and pulls the
        // user's eye to it without needing an icon prefix.
        "<OptionsTabToggle Id=\"BetaDepsModConfigTabToggle\" Parameter.TabName=\"BetaDepsModConfigPage\">\n"
        + "  <Children>\n"
        + "    <TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\"\n"
        + "                HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"\n"
        + "                DoNotAcceptEvents=\"true\" Text=\"Mod Configuration\" />\n"
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
        // Polish (post-v0.8): right-side hint panel narrowed AND visually
        // separated from the main settings column via a vertical divider line.
        //
        // Structural fix vs the first attempt: instead of a no-layout Widget
        // wrapper with overlapping children (which rendered the divider but
        // suppressed the text — the inner StretchToParent + HorizontalAlignment
        // combination didn't produce a visible bounding box for the
        // RichTextWidgets), use an explicit HorizontalLeftToRight ListPanel
        // with two sibling columns: a 2-px separator column on the left
        // and a 338-px text column on the right (2 + 20 margin + 338 = 360).
        //
        //   [ separator | <margin> | name + description text ]
        //
        // 360 px total replaces the previous StretchToParent which let
        // the panel eat ~600+ px of horizontal screen space.
        "<ListPanel Id=\"BetaDepsHintPanelWrapper\" IsVisible=\"@IsHintVisible\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"360\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Right\" VerticalAlignment=\"Top\" MarginTop=\"50\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">\n"
        + "  <Children>\n"
        + "    <ListPanel WidthSizePolicy=\"Fixed\" SuggestedWidth=\"338\" HeightSizePolicy=\"CoverChildren\" MarginLeft=\"8\" VerticalAlignment=\"Top\" HorizontalAlignment=\"Left\" StackLayout.LayoutMethod=\"VerticalTopToBottom\">\n"
        + "      <Children>\n"
        // Post-screenshot fix: text now explicitly left-aligned via
        // TextHorizontalAlignment + HorizontalAlignment=Left. Previously
        // the brush's baked-in alignment was right-aligning long titles like
        // "Join Discord - Support & Community" against the right edge of
        // the 338-px column, causing the wrap to look like the text was
        // getting cut off. Left-aligned wrapping reads as a natural paragraph.
        + "        <RichTextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Left\" TextHorizontalAlignment=\"Left\" Brush=\"SPOptions.Description.Title.Text\" Text=\"@HoveredOptionName\" />\n"
        + "        <RichTextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Left\" TextHorizontalAlignment=\"Left\" MarginTop=\"25\" Brush=\"SPOptions.Description.Text\" Text=\"@HoveredHintText\" />\n"
        + "      </Children>\n"
        + "    </ListPanel>\n"
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
            // slice 4d: the {RowList} single-page scrollable list is now the
            // unconditional Mod Configuration UI. The old fixed 20-slot fan and
            // its pagination are retired. (Dead slot-builder methods + the
            // RowListProbeEnabled flag check remain in this file pending the
            // step-2 cleanup deletion, but are no longer invoked.)
            sb.Append(RowListXml);
            sb.Append(FooterCloseXml);
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

    /// <summary>
    /// slice 4c proof gate. True when Modules\BetaDeps\rowlist-probe.flag exists
    /// (same directory as runtime.log). Lets us A/B the {RowList} ItemTemplate
    /// list against the working slot rows on a single Quick-Test without shipping
    /// it. Drop the flag, launch, open Mod Configuration. Delete the flag to hide.
    /// </summary>
    private static bool RowListProbeEnabled()
    {
        try
        {
            var rtPath = BetaDeps.Foundation.RuntimeLog.Path;
            var dir = System.IO.Path.GetDirectoryName(rtPath);
            if (string.IsNullOrEmpty(dir)) return false;
            return System.IO.File.Exists(System.IO.Path.Combine(dir, "rowlist-probe.flag"));
        }
        catch { return false; }
    }

    /// <summary>
    /// Minimal ItemTemplate list bound to the mixin RowList. One RichTextWidget
    /// per row: group-header rows show @GroupHeader, property rows show
    /// @DisplayName. Both gate on the PresentationRowVM's own [DataSourceProperty]
    /// flags (@IsHeader / @IsProperty), which resolve natively because each row is
    /// a real ViewModel instantiated by ItemTemplate (no mixin at row level).
    /// Plain ListPanel (not NavigatableListPanel) to avoid needing nav-scope
    /// scaffolding for the proof.
    /// </summary>
    // slice 4c: the full single-page scrollable mod settings list. One
    // NavigatableListPanel bound to the mixin's MBBindingList<PresentationRowVM>
    // RowList via the data-source sigil {RowList}; the ItemTemplate renders one
    // row per setting, switching widget variant on the row VM's own
    // [DataSourceProperty] flags (@IsHeader / @IsBool / @IsNumeric / @IsText /
    // @IsDropdown / @IsButton). Each row is a real PresentationRowVM, so its
    // @bindings and Command.Click="ExecuteToggleBool" etc. resolve natively once
    // ItemTemplate hands the row VM to the widget. The {RowList} item-source +
    // per-row index resolution is what the GetViewModelAtPath postfix in
    // ViewModelBindingPatch.cs enables (see that file's slice-4c comment).
    //
    // Numerics use an editable text field, NOT a SliderWidget: this list
    // instantiates every row (no virtualization), so per-row sliders would
    // recreate the historical 6-sliders-per-page Gauntlet crash ceiling on
    // settings-heavy mods. Whether/how to reintroduce sliders is a separate
    // decision tracked for after the list is confirmed.
    private const string RowListXml =
        "    <NavigatableListPanel Id=\"BetaDepsModRowList\" DataSource=\"{RowList}\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\"  VerticalAlignment=\"Top\" StackLayout.LayoutMethod=\"VerticalTopToBottom\">\n"
        + "      <ItemTemplate>\n"
        // Slice 7: tighter row spacing (50->44 tall, 4->2 top margin) so more
        // settings fit per screen without feeling cramped.
        + "        <Widget IsVisible=\"@IsVisible\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"960\" SuggestedHeight=\"44\" HorizontalAlignment=\"Left\" MarginTop=\"2\">\n"
        + "          <Children>\n"
        // Hover highlight overlay — solid amber, shown only while @IsHovered. The
        // flag is set by the HintWidget's HoverBegin/End at the row's end, which
        // is the ONLY hover signal that fires reliably across the whole row (the
        // event-accepting controls swallow Command.HoverBegin on the container).
        + "            <BrushWidget IsVisible=\"@IsHovered\" DoNotAcceptEvents=\"true\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"BetaDeps.RowHover.Solid\" />\n"
        // No full-width header background: native SPOptions group headers are
        // plain title text + a separator, with no box behind them. The parent
        // tier still reads clearly via the larger chevron and the divider below
        // (parent-only); child tier via the indent + a bold chevron. The old
        // BetaDeps.ParentHeaderBg gold wash was non-native and read as a heavy box.
        // ----- group-header row: centered title + thin divider -----
        + "            <ListPanel IsVisible=\"@IsHeader\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" StackLayout.LayoutMethod=\"VerticalTopToBottom\">\n"
        + "              <Children>\n"
        // Slice 1: the WHOLE header row is a flat clickable button that toggles
        // collapse (chevron + title inside, DoNotPassEventsToChildren so the
        // button gets the click). Left-aligned to read like a list section.
        // v0.9.2: header rows now drive the same @IsHovered overlay as property
        // rows via Command.HoverBegin/End on the button, so the subtle hover wash
        // applies to headers too.
        + "                <ButtonWidget Command.Click=\"ExecuteToggleCollapse\" Command.HoverBegin=\"ExecuteHoverBegin\" Command.HoverEnd=\"ExecuteHoverEnd\" DoNotPassEventsToChildren=\"true\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" UpdateChildrenStates=\"true\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">\n"
        + "                  <Children>\n"
        // Slice 8: indent spacer (width = nesting level x 28) so child sub-groups
        // sit visually under their parent header.
        + "                    <Widget DoNotAcceptEvents=\"true\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"@IndentPixels\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"1\" />\n"
        // v0.9.2: the gold vertical accent bar that used to mark child sub-section
        // headers is gone — the nesting cue is now the indent spacer above plus a
        // BOLD chevron (same size as the parent's), which reads cleaner than a
        // colored line at the left edge.
        // Slice 3: native collapse chevron sprites (closed when collapsed, open
        // when expanded), toggled by visibility. Replaces the text ">" / "v".
        // Hidden widgets collapse out of the HorizontalLeftToRight layout so only
        // the active chevron reserves space before the title.
        // Parent chevrons: LARGE (27px) so top-level sections read boldly.
        + "                    <BrushWidget DoNotAcceptEvents=\"true\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"27\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"27\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" MarginRight=\"9\" Brush=\"BetaDeps.Chevron.Closed\" IsVisible=\"@IsParentCollapsed\" />\n"
        + "                    <BrushWidget DoNotAcceptEvents=\"true\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"27\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"27\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" MarginRight=\"9\" Brush=\"BetaDeps.Chevron.Open\" IsVisible=\"@IsParentExpanded\" />\n"
        // Child chevrons: now BOLD (24px) — the primary nesting cue in place of the
        // removed left accent bar. Slightly smaller than the parent's 27px so the
        // tier is still legible, but clearly a real chevron, not a thin line.
        + "                    <BrushWidget DoNotAcceptEvents=\"true\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"24\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"24\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" MarginRight=\"9\" Brush=\"BetaDeps.Chevron.Closed\" IsVisible=\"@IsChildCollapsed\" />\n"
        + "                    <BrushWidget DoNotAcceptEvents=\"true\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"24\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"24\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" MarginRight=\"9\" Brush=\"BetaDeps.Chevron.Open\" IsVisible=\"@IsChildExpanded\" />\n"
        // Parent section title uses the NATIVE option-group title brush — the same
        // static brush vanilla's SPOptions group headers use — so top-level sections
        // match the game's other settings screens. The old InitialMenuButtonBrush
        // (the main-menu button brush) carried idle/hover animation layers that ran
        // every frame for every parent header in this non-virtualized list, which
        // both looked off-screen and cost real frame time. FontSize 30 keeps the
        // native default (36) from reading blocky. v0.9.2: FontColor forced to the
        // accent orange used by the checkbox/slider fills so parent sections read
        // as the warm accent tier (TextWidget, not RichText, so the override applies).
        + "                    <TextWidget IsVisible=\"@IsParentHeader\" DoNotAcceptEvents=\"true\" WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" VerticalAlignment=\"Center\" MarginLeft=\"4\" Brush=\"SPOptions.GameKeysGroup.Title.Text\" Brush.FontSize=\"30\" Brush.FontColor=\"#D9772BFF\" Text=\"@GroupHeader\" />\n"
        // Child header text. Brush.ColorFactor does NOT dim text (it multiplies
        // sprite-layer color, not the font channel); Brush.FontColor is what
        // recolors glyphs. Use a muted tan that's clearly subordinate to the
        // parent's bright gold. (Structural cues above — accent bar + no title
        // bar — are the primary differentiator; this is reinforcement.)
        + "                    <TextWidget IsVisible=\"@IsChildHeader\" DoNotAcceptEvents=\"true\" WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" VerticalAlignment=\"Center\" MarginLeft=\"4\" Brush=\"SPOptions.GameKeysGroup.Title.Text\" Brush.FontSize=\"26\" Brush.FontColor=\"#9C742AFF\" Text=\"@GroupHeader\" />\n"
        + "                  </Children>\n"
        + "                </ButtonWidget>\n"
        // Divider line only under PARENT headers; child sub-sections have none.
        + "                <Widget IsVisible=\"@IsParentHeader\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"1\" HorizontalAlignment=\"Left\" MarginTop=\"3\" MarginRight=\"40\" Brush=\"SPOptions.Group.Title.Separator\" />\n"
        + "              </Children>\n"
        + "            </ListPanel>\n"
        // ----- property row: name on the left, variant control on the right -----
        + "            <ListPanel IsVisible=\"@IsProperty\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">\n"
        + "              <Children>\n"
        // Name label carries the hover hint. DoNotAcceptEvents removed so it
        // actually receives hover (with it set, the label is transparent to the
        // mouse and Command.HoverBegin never fires -- which is why the outer
        // Widget and the HintWidget overlay both reported 0 invocations).
        // Slice 8: indent spacer so a nested sub-group's settings line up under it.
        + "                <Widget DoNotAcceptEvents=\"true\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"@IndentPixels\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"1\" />\n"
        + "                <TextWidget Command.HoverBegin=\"ExecuteHoverBegin\" Command.HoverEnd=\"ExecuteHoverEnd\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"380\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Left\" TextHorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" Brush=\"SPOptions.OptionName.Text\" Text=\"@DisplayName\" />\n"
        // bool checkbox (Phase 2.2). Vanilla SPOptions checkbox: a toggle button
        // with the Empty brush as the box and a Full-brush ToggleIndicator that
        // shows when checked. IsSelected two-way to @BoolValue drives the
        // BoolValue setter (-> WriteBack) on click, so no separate command needed.
        + "                <ButtonWidget IsVisible=\"@IsBool\" ButtonType=\"Toggle\" IsSelected=\"@BoolValue\" DoNotPassEventsToChildren=\"true\" Command.HoverBegin=\"ExecuteHoverBegin\" Command.HoverEnd=\"ExecuteHoverEnd\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"40\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"40\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" MarginLeft=\"20\" Brush=\"SPOptions.Checkbox.Empty.Button\" ToggleIndicator=\"BoolCheckIndicator\" UpdateChildrenStates=\"true\">\n"
        + "                  <Children><BrushWidget Id=\"BoolCheckIndicator\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"SPOptions.Checkbox.Full.Button\" /></Children>\n"
        + "                </ButtonWidget>\n"
        // action button
        + "                <ButtonWidget IsVisible=\"@IsButton\" Command.Click=\"ExecuteAction\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"240\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"40\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" MarginLeft=\"20\" Brush=\"Popup.Done.Button.NineGrid\" UpdateChildrenStates=\"true\">\n"
        + "                  <Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" DoNotAcceptEvents=\"true\" Brush=\"Popup.Button.Text\" Text=\"@ButtonText\" /></Children>\n"
        + "                </ButtonWidget>\n"
        // numeric: draggable slider + click-to-type value field. Mirrors vanilla
        // OptionItem.xml NumericOption (SliderWidget with Filler/Handle + nav
        // siblings) bound to the row VM. IDs reuse per ItemTemplate instance,
        // exactly as vanilla does. UpdateValueContinuously=@UpdateContinuously
        // (false) is the attribute that lifted the old slot-fan slider ceiling.
        + "                <ListPanel IsVisible=\"@IsNumeric\" WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" MarginLeft=\"20\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">\n"
        + "                  <Children>\n"
        + "                    <NavigationTargetSwitcher FromTarget=\"..\\.\" ToTarget=\"..\\NumSlider\\NumSliderHandle\" />\n"
        + "                    <NavigationAutoScrollWidget TrackedWidget=\"..\\NumSlider\\NumSliderHandle\" AutoScrollTopOffset=\"90\" AutoScrollBottomOffset=\"90\" />\n"
        + "                    <SliderWidget Id=\"NumSlider\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"338\" SuggestedHeight=\"42\" VerticalAlignment=\"Center\" DoNotUpdateHandleSize=\"true\" Filler=\"NumFiller\" Handle=\"NumSliderHandle\" IsDiscrete=\"@IsInteger\" DiscreteIncrementInterval=\"@DiscreteIncrementInterval\" MaxValueFloat=\"@MaxValue\" MinValueFloat=\"@MinValue\" ValueFloat=\"@FloatValue\" UpdateValueContinuously=\"@UpdateContinuously\">\n"
        + "                      <Children>\n"
        + "                        <Widget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"362\" SuggestedHeight=\"38\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Sprite=\"SPGeneral\\SPOptions\\standart_slider_canvas\" DoNotAcceptEvents=\"true\" />\n"
        + "                        <Widget Id=\"NumFiller\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"345\" SuggestedHeight=\"35\" VerticalAlignment=\"Center\" Sprite=\"SPGeneral\\SPOptions\\standart_slider_fill\" ClipContents=\"true\">\n"
        + "                          <Children>\n"
        + "                            <Widget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"345\" SuggestedHeight=\"35\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" Sprite=\"SPGeneral\\SPOptions\\standart_slider_fill\" />\n"
        + "                          </Children>\n"
        + "                        </Widget>\n"
        + "                        <Widget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"400\" SuggestedHeight=\"65\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Sprite=\"SPGeneral\\SPOptions\\standart_slider_frame\" DoNotAcceptEvents=\"true\" />\n"
        + "                        <ImageWidget Id=\"NumSliderHandle\" DoNotAcceptEvents=\"true\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"18\" SuggestedHeight=\"42\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" Brush=\"SPOptions.Slider.Handle\" />\n"
        + "                      </Children>\n"
        + "                    </SliderWidget>\n"
        + "                    <EditableTextWidget Id=\"NumValueInput\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"150\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"38\" MarginLeft=\"30\" VerticalAlignment=\"Center\" Brush=\"SPOptions.Slider.Value.Text\" Text=\"@EditableValueText\" />\n"
        + "                  </Children>\n"
        + "                </ListPanel>\n"
        // free text -- v0.9.2: was a read-only RichTextWidget, which made every
        // string setting (API keys, model names, etc.) impossible to edit even
        // though the TextValue VM setter was already write-through. Same
        // click-to-edit pattern as NumValueInput above; brush matches the
        // sidebar search box so the field visibly affords typing.
        // TabSwitchGuardPatch already suppresses Q/E tab-switching while any
        // EditableTextWidget has focus, so no extra guard is needed here.
        + "                <EditableTextWidget Id=\"TextValueInput\" IsVisible=\"@IsText\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"360\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"38\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" MarginLeft=\"20\" Brush=\"CustomBattle.Search.TextBox\" Text=\"@TextValue\" />\n"
        // dropdown: [<] value [>]
        + "                <ListPanel IsVisible=\"@IsDropdown\" WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" MarginLeft=\"20\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">\n"
        + "                  <Children>\n"
        + "                    <ButtonWidget Command.Click=\"ExecuteDropdownPrev\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"42\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"40\" VerticalAlignment=\"Center\" Brush=\"Popup.Cancel.Button\" UpdateChildrenStates=\"true\">\n"
        + "                      <Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" DoNotAcceptEvents=\"true\" Brush=\"Popup.Button.Text\" Text=\"&lt;\" /></Children>\n"
        + "                    </ButtonWidget>\n"
        + "                    <RichTextWidget WidthSizePolicy=\"Fixed\" SuggestedWidth=\"260\" HeightSizePolicy=\"CoverChildren\" VerticalAlignment=\"Center\" MarginLeft=\"8\" MarginRight=\"8\" Brush=\"SPOptions.Dropdown.Center.Text\" Text=\"@DropdownText\" />\n"
        + "                    <ButtonWidget Command.Click=\"ExecuteDropdownNext\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"42\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"40\" VerticalAlignment=\"Center\" Brush=\"Popup.Cancel.Button\" UpdateChildrenStates=\"true\">\n"
        + "                      <Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" DoNotAcceptEvents=\"true\" Brush=\"Popup.Button.Text\" Text=\"&gt;\" /></Children>\n"
        + "                    </ButtonWidget>\n"
        + "                  </Children>\n"
        + "                </ListPanel>\n"
        + "              </Children>\n"
        + "            </ListPanel>\n"
        // Hover hint: a HintWidget overlay is how vanilla OptionItem.xml detects
        // row hover (Command.HoverBegin on a plain Widget does NOT fire here --
        // confirmed 0 invocations in runtime.log -- because the row's
        // event-accepting children consume the mouse). StretchToParent +
        // IsEnabled=false makes it a transparent overlay that reports hover but
        // passes clicks through to the controls beneath. Inherits the row VM as
        // its context, so ExecuteHoverBegin/End resolve on PresentationRowVM,
        // which fires SettingsPropertyVM.HoverCallback into the mixin's panel.
        + "            <HintWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Command.HoverBegin=\"ExecuteHoverBegin\" Command.HoverEnd=\"ExecuteHoverEnd\" IsEnabled=\"false\" />\n"
        + "          </Children>\n"
        + "        </Widget>\n"
        + "      </ItemTemplate>\n"
        + "    </NavigatableListPanel>\n";

    private const string HeaderXml =
        // Phase 2.1: 2-column layout. The page is now HORIZONTAL -- a fixed-width
        // left "Mods" sidebar (search + clickable {ModList}) and a stretch right
        // column (mod header + preset + settings + footer). The sidebar replaces
        // the old Prev/Next cycler.
        "<ListPanel Id=\"BetaDepsModConfigPage\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Top\" MarginTop=\"10\" MarginBottom=\"10\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">\n"
        + "  <Children>\n"
        // ----- LEFT SIDEBAR: search field + clickable Mods list -----
        + "    <ListPanel Id=\"BetaDepsModSidebar\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"380\" HeightSizePolicy=\"CoverChildren\" VerticalAlignment=\"Top\" MarginRight=\"24\" StackLayout.LayoutMethod=\"VerticalTopToBottom\">\n"
        + "      <Children>\n"
        + "        <RichTextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Left\" MarginLeft=\"6\" MarginBottom=\"8\" Brush=\"SPOptions.Tab.Text\" Text=\"Mods\" />\n"
        + "        <ListPanel WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" MarginBottom=\"10\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">\n"
        + "          <Children>\n"
        // Slice 6 (search magnifier) reverted: the only magnifier sprite found,
        // MPLobby\...\filters_search_icon, is a multiplayer-lobby sprite that
        // isn't loaded in the SP Options context, so it drew nothing. The verify
        // loop's screenshot caught it (gate stayed green). Left as the clean text
        // "Search..." field; revisit if an SP-loaded magnifier sprite turns up.
        + "            <EditableTextWidget Id=\"BetaDepsSearchInput\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"300\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"36\" VerticalAlignment=\"Center\" Brush=\"CustomBattle.Search.TextBox\" Text=\"@BetaDepsModSearchText\" GamepadNavigationIndex=\"0\">\n"
        + "              <Children>\n"
        + "                <RichTextWidget IsVisible=\"@BetaDepsSearchPlaceholderVisible\" DoNotAcceptEvents=\"true\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Brush=\"SPOptions.Group.Title.Text\" Text=\"Search...\" />\n"
        + "              </Children>\n"
        + "            </EditableTextWidget>\n"
        + "            <ButtonWidget Command.Click=\"ExecuteClearSearch\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"56\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"36\" MarginLeft=\"6\" VerticalAlignment=\"Center\" Brush=\"Popup.Cancel.Button\" UpdateChildrenStates=\"true\" IsVisible=\"@BetaDepsSearchClearVisible\">\n"
        + "              <Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" DoNotAcceptEvents=\"true\" Brush=\"Popup.Button.Text\" Text=\"X\" /></Children>\n"
        + "            </ButtonWidget>\n"
        + "          </Children>\n"
        + "        </ListPanel>\n"
        + "        <NavigatableListPanel Id=\"BetaDepsModListPanel\" DataSource=\"{ModList}\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" VerticalAlignment=\"Top\" StackLayout.LayoutMethod=\"VerticalTopToBottom\">\n"
        + "          <ItemTemplate>\n"
        // Flat selectable row (Phase 2.5): no button background. The selected
        // row shows a full-width highlight panel + a left accent bar.
        + "            <ButtonWidget Command.Click=\"ExecuteSelect\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"42\" MarginBottom=\"1\" UpdateChildrenStates=\"true\">\n"
        + "              <Children>\n"
        // Slice 4: hover wash (transparent until the row is hovered). Behind the
        // selection canvas so a selected row still reads as selected.
        + "                <BrushWidget DoNotAcceptEvents=\"true\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"BetaDeps.RowHover\" />\n"
        + "                <BrushWidget IsVisible=\"@IsSelected\" DoNotAcceptEvents=\"true\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"SPOptions.GameKey.Button.Canvas\" />\n"
        + "                <Widget IsVisible=\"@IsSelected\" DoNotAcceptEvents=\"true\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"4\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" Brush=\"SPOptions.Group.Title.Separator\" />\n"
        + "                <TextWidget DoNotAcceptEvents=\"true\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" MarginLeft=\"16\" MarginRight=\"8\" Brush=\"SPOptions.OptionName.Text\" Text=\"@DisplayName\" />\n"
        + "              </Children>\n"
        + "            </ButtonWidget>\n"
        + "          </ItemTemplate>\n"
        + "        </NavigatableListPanel>\n"
        + "      </Children>\n"
        + "    </ListPanel>\n"
        // vertical divider between the sidebar and the settings (Phase 2.5).
        + "    <Widget WidthSizePolicy=\"Fixed\" SuggestedWidth=\"2\" HeightSizePolicy=\"StretchToParent\" VerticalAlignment=\"Center\" MarginRight=\"24\" Brush=\"SPOptions.Group.Title.Separator\" />\n"
        // ----- RIGHT COLUMN: settings for the selected mod -----
        // Slice 5: contained-panel backing behind the settings column (subtle dark
        // fill so the text reads cleanly over the parallax). Small inner padding via
        // MarginTop/Bottom so the panel breathes around the content.
        + "    <ListPanel Id=\"BetaDepsModRightCol\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" VerticalAlignment=\"Top\" StackLayout.LayoutMethod=\"VerticalTopToBottom\" Brush=\"BetaDeps.Panel.Background\">\n"
        + "      <Children>\n"
        // Post-screenshot polish: "Mod Configuration · 35 mods" title row was
        // removed. The tab itself reads "Mod Configuration" (see
        // MCMTabTogglePatch) so a duplicate title underneath added no
        // information; the divider that used to sit beneath it was also part
        // of that same header block and is gone with it. Page now opens
        // directly into the search field, which becomes the visual entry point.
        // The BetaDepsModConfigTitle / BetaDepsModConfigSummary VM properties
        // are kept (other prefab patches or future scenarios may still use
        // them) but no longer rendered here.

        // v0.7.6 visual change #4: empty-state hint. Visible only when no
        // mods have registered MCM settings. BetaDepsModConfigIsEmpty is
        // the inverse of HasMods; bound on the VM side. When true, the
        // settings slot panel below renders nothing but at least the user
        // sees a friendly explanation instead of an empty page.
        // Polish #11: empty-state text rewritten from "No mod settings found.
        // Mods that expose settings will appear here automatically." to a
        // friendlier two-line message that tells the user (a) nothing is
        // wrong, (b) what to do next. CoverChildren means the parent panel
        // collapses to zero height once mods register, so this only
        // occupies vertical space when actually needed.
        + "    <ListPanel IsVisible=\"@BetaDepsModConfigIsEmpty\" WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" MarginTop=\"24\" StackLayout.LayoutMethod=\"VerticalTopToBottom\">\n"
        + "      <Children>\n"
        + "        <RichTextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" Brush=\"SPOptions.Description.Title.Text\" Text=\"No mod settings registered yet.\" />\n"
        + "        <RichTextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" MarginTop=\"6\" Brush=\"SPOptions.Group.Title.Text\" Text=\"Install any BUTR / BetaDeps-compatible mod that exposes options and it will appear here on next launch.\" />\n"
        + "      </Children>\n"
        + "    </ListPanel>\n"
        // Polish (post-v0.8) #4 + #5: search and cycler back on SEPARATE rows.
        // ROW A: search input + (conditional) Clear button, centered.
        //        Wider than before (340 px) so users perceive it as the primary
        //        action and the Search mods… placeholder reads at a glance.
        // ROW B: [< Prev Mod] [Mod Name + Mod-N-of-N indicator] [Next Mod >].
        //        SelectedModName now uses a heading brush distinct from the
        //        page-level "Mod Configuration" title above, so the visual
        //        hierarchy reads cleanly:
        //          page title  (largest / most weight)
        //          → search row
        //          → mod-name + nav (largest within the cycler)
        //          → settings rows below
        //
        // Live mod-list search/filter still binds the same way:
        // EditableTextWidget two-way to BetaDepsModSearchText; the setter
        // calls ApplyFilter() on every keystroke. Q/E tab-switching conflict
        // (OptionsVM.SelectPreviousCategory / SelectNextCategory fire from a
        // game-input hotkey independent of UI focus) is handled by
        // TabSwitchGuardPatch.cs which suppresses tab switches when any
        // EditableTextWidget has focus.

        // (ROW A search field relocated into the left sidebar above.)

        // Mod name + "Mod N of M" header for the selected mod (no Prev/Next --
        // selection is via the left sidebar now). Vertical wrapper.
        + "    <ListPanel WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" MarginTop=\"4\" StackLayout.LayoutMethod=\"VerticalTopToBottom\">\n"
        + "      <Children>\n"
        // Mod-name / mod-index panel. Polish #5 + post-screenshot adjustment:
        // SelectedModName uses SPOptions.Tab.Text (heading-weight, larger) so
        // it visually outranks the per-row labels but doesn't compete with
        // the page title above. Container height bumped 52 -> 72 and the
        // MarginTop between the two rows bumped 2 -> 10 so the mod name
        // and the "Mod N of M" subtitle have actual breathing room instead
        // of stacking like one cramped block.
        + "        <RichTextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" TextHorizontalAlignment=\"Center\" Brush=\"SPOptions.Tab.Text\" Text=\"@SelectedModName\" />\n"
        // "Mod N of 9" subtitle dropped (Phase 2 refinement): redundant with the
        // left sidebar's position + matches the target's clean mod-name title.
        // Shown only when a search filter is active (so the user knows the list
        // is narrowed). SelectedModSummary stays bound elsewhere / for that case.
        + "        <RichTextWidget IsVisible=\"@BetaDepsSearchClearVisible\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" TextHorizontalAlignment=\"Center\" MarginTop=\"4\" Brush=\"SPOptions.Group.Title.Text\" Text=\"@SelectedModSummary\" />\n"
        + "      </Children>\n"
        + "    </ListPanel>\n"
        // v0.8.2 Suberfudge feature: preset cycle row. Lives between the
        // mod-name/cycler header and the property list. Hidden when
        // PresetCycleVisible is false (defensive — covers the edge case
        // where no mod is selected yet). Layout: [Preset:] [< ] [name] [ >] [Apply]
        // Phase 2.4: preset "Custom" dropdown. Button shows the active preset;
        // clicking toggles a popup list ({PresetOptions}) that inline-expands
        // below it. Selecting a row applies that preset (or the Current/Save
        // sentinels) and closes the popup.
        + "    <ListPanel IsVisible=\"@PresetCycleVisible\" WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" MarginTop=\"10\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">\n"
        + "      <Children>\n"
        + "        <RichTextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" VerticalAlignment=\"Center\" MarginRight=\"12\" Brush=\"SPOptions.Group.Title.Text\" Text=\"Preset:\" />\n"
        + "        <ButtonWidget Command.Click=\"ExecuteTogglePresetDropdown\" DoNotPassEventsToChildren=\"true\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"360\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"42\" VerticalAlignment=\"Center\" Brush=\"SPOptions.Dropdown.Center\" UpdateChildrenStates=\"true\">\n"
        + "          <Children>\n"
        + "            <ListPanel WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">\n"
        + "              <Children>\n"
        + "                <RichTextWidget DoNotAcceptEvents=\"true\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" MarginLeft=\"16\" Brush=\"SPOptions.Dropdown.Center.Text\" Text=\"@PresetButtonText\" />\n"
        + "                <RichTextWidget DoNotAcceptEvents=\"true\" WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Right\" VerticalAlignment=\"Center\" MarginRight=\"14\" Brush=\"SPOptions.Dropdown.Center.Text\" Text=\"▼\" />\n"
        + "              </Children>\n"
        + "            </ListPanel>\n"
        + "          </Children>\n"
        + "        </ButtonWidget>\n"
        + "      </Children>\n"
        + "    </ListPanel>\n"
        // IsVisible (mixin context) MUST be on a separate widget from the
        // DataSource="{PresetOptions}" list -- a widget with a {} DataSource
        // changes its own binding context, so @PresetDropdownOpen on the same
        // widget would resolve against the PresetOptions list (no such property)
        // and the popup would show permanently. Outer Widget gates visibility;
        // inner ListPanel iterates the options.
        + "    <Widget IsVisible=\"@PresetDropdownOpen\" WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" MarginTop=\"2\">\n"
        + "      <Children>\n"
        + "        <ListPanel DataSource=\"{PresetOptions}\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"360\" HeightSizePolicy=\"CoverChildren\" Brush=\"SPOptions.Dropdown.Extension\" StackLayout.LayoutMethod=\"VerticalTopToBottom\">\n"
        + "          <ItemTemplate>\n"
        + "            <ButtonWidget Command.Click=\"ExecuteSelect\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"38\" UpdateChildrenStates=\"true\">\n"
        + "              <Children><TextWidget DoNotAcceptEvents=\"true\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" MarginLeft=\"16\" MarginRight=\"12\" Brush=\"SPOptions.Dropdown.Item.Text\" Text=\"@DisplayName\" /></Children>\n"
        + "            </ButtonWidget>\n"
        + "          </ItemTemplate>\n"
        + "        </ListPanel>\n"
        + "      </Children>\n"
        + "    </Widget>\n"
        // v1.0 (task #5 v2): hint panel moved AGAIN to the vanilla
        // DescriptionsRightPanel on the right side of the Options screen
        // (see MCMDescriptionsRightPanelPatch above). That's where vanilla
        // puts Video/Audio option descriptions, and matches what users
        // already expect. The previous attempt to put it inline above the
        // slot list took 60 px of vertical space that's better used by the
        // slot rows themselves.
        // Phase 2 refinement: contained settings panel. CoverChildren width keeps
        // the box hugging the row content (no alignment shift) with a subtle
        // inset background brush + padding for the "contained panel" look.
        + "    <ListPanel WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" MarginTop=\"16\" MarginBottom=\"8\" Brush=\"SPOptions.GameKey.Button.Canvas\" StackLayout.LayoutMethod=\"VerticalTopToBottom\">\n"
        + "      <Children>\n"
        + "        <Widget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"1\" SuggestedHeight=\"10\" />\n";

    // slice 4c: the settings content-panel close, split out so list mode can
    // skip the slot pagination block that immediately follows it.
    private const string FooterCloseXml =
        "      </Children>\n"
        + "    </ListPanel>\n";

    // Slot-mode soft pagination (Page X of Y + Prev/Next Page), emitted ONLY in
    // slot mode. The {RowList} list is a single continuous scroll with no pages,
    // so list mode omits this block entirely (it was showing a vestigial
    // "Page 1 of 2" driven by the still-running RefreshSlots page math).
    private const string FooterPaginationXml =
        "    <RichTextWidget IsVisible=\"@PaginationVisible\" WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" MarginTop=\"15\" Brush=\"SPOptions.Group.Title.Text\" Text=\"@SelectedPageSummary\" />\n"
        + "    <ListPanel IsVisible=\"@PaginationVisible\" WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" MarginTop=\"10\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">\n"
        + "      <Children>\n"
        + "        <ButtonWidget Command.Click=\"ExecutePrevPage\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"160\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"48\" Brush=\"Popup.Cancel.Button\" UpdateChildrenStates=\"true\">\n"
        + "          <Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" DoNotAcceptEvents=\"true\" Brush=\"Popup.Button.Text\" Text=\"&lt; Prev Page\" /></Children>\n"
        + "        </ButtonWidget>\n"
        + "        <ButtonWidget Command.Click=\"ExecuteNextPage\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"160\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"48\" MarginLeft=\"20\" Brush=\"Popup.Cancel.Button\" UpdateChildrenStates=\"true\">\n"
        + "          <Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" DoNotAcceptEvents=\"true\" Brush=\"Popup.Button.Text\" Text=\"Next Page &gt;\" /></Children>\n"
        + "        </ButtonWidget>\n"
        + "      </Children>\n"
        + "    </ListPanel>\n";

    private const string FooterXml =
        // Footer hint text (Q/E + slider tip) removed per user request.
        ""
        // v0.7.6 visual change #1: split the 6-button action bar into two rows.
        // Row A: per-mod / diagnostic actions (Reset to Defaults, Run Self-Test,
        // Send to GitHub) -- 3 x 260 = 820 px, easy fit.
        // Row B: global toggle settings (Auto-Disable, PatchShield, SaveShield
        // v0.8 UI cleanup: two button rows (Reset/Self-Test/SendToGitHub plus
        // AutoDisable/PatchShield/SaveShield toggles) collapsed into ONE row
        // with two buttons: Reset to Defaults + Report a Bug. The diagnostic
        // toggles (PatchShield/SaveShield/AutoDisable) were dev-tier surfaces
        // that just confused end users; they remain accessible via flag files
        // in Modules\BetaDeps\ for modders who need to debug (see
        // BETADEPS-NATIVE-API.md "Debugging" section).
        //
        // Run Self-Test is folded INTO Report-a-Bug: clicking the bug button
        // auto-runs self-test as its first step, then opens a GitHub issue
        // draft pre-filled with the results. Single click for an end-user
        // bug report, no need to know "self-test" exists as a concept.
        + "    <ListPanel WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" MarginTop=\"10\" MarginBottom=\"15\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">\n"
        + "      <Children>\n"
        + "        <ButtonWidget Command.Click=\"ExecuteResetDefaults\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"260\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"52\" Brush=\"Popup.Done.Button.NineGrid\" UpdateChildrenStates=\"true\">\n"
        + "          <Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" DoNotAcceptEvents=\"true\" Brush=\"Popup.Button.Text\" Text=\"Reset to Defaults\" /></Children>\n"
        + "        </ButtonWidget>\n"
        // Polish #12: button reads "Send Bug Report" (was "Report a Bug").
        // The new label is action-oriented (verb-first) and signals what
        // actually happens — the click runs Self-Test, builds a pre-filled
        // GitHub issue body, and opens the browser. "Report a Bug" sounded
        // descriptive; "Send Bug Report" sounds active.
        + "        <ButtonWidget Command.Click=\"ExecuteSendToGitHub\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"260\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"52\" MarginLeft=\"20\" Brush=\"Popup.Done.Button.NineGrid\" UpdateChildrenStates=\"true\">\n"
        + "          <Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" DoNotAcceptEvents=\"true\" Brush=\"Popup.Button.Text\" Text=\"Send Bug Report\" /></Children>\n"
        + "        </ButtonWidget>\n"
        + "      </Children>\n"
        + "    </ListPanel>\n"
        // v1.0: dead space after the action button row so the Cancel/Done bar
        // at the very bottom of the Options screen doesn't sit on top of
        // Reset to Defaults / Report a Bug when the user scrolls to the
        // bottom of a tall mod. 120 px is roughly the height of the vanilla
        // bottom button bar plus a small margin.
        + "    <Widget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"1\" SuggestedHeight=\"120\" />\n"
        // close BetaDepsModRightCol (Phase 2.1 right column)
        + "      </Children>\n"
        + "    </ListPanel>\n"
        // close BetaDepsModConfigPage (now horizontal: sidebar + right column)
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
            // Polish #8: when this slot is a group-header row, render the
            // header text AND a thin divider line beneath it. The divider
            // visually separates the group from the slot rows that follow,
            // matching vanilla SP-options' Video/Audio/Gameplay section
            // dividers. ListPanel container stacks the two children
            // vertically; both are gated on the same Slot{n}_IsHeader flag.
            + "            <ListPanel IsVisible=\"@Slot" + s + "_IsHeader\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" StackLayout.LayoutMethod=\"VerticalTopToBottom\">\n"
            + "              <Children>\n"
            + "                <RichTextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" Brush=\"SPOptions.GameKeysGroup.Title.Text\" Text=\"@Slot" + s + "_GroupHeader\" />\n"
            + "                <Widget WidthSizePolicy=\"Fixed\" SuggestedWidth=\"600\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"1\" HorizontalAlignment=\"Center\" MarginTop=\"3\" Brush=\"SPOptions.Group.Title.Separator\" />\n"
            + "              </Children>\n"
            + "            </ListPanel>\n"
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
            // v0.9.2: editable text field (was a read-only RichTextWidget --
            // string settings could never be typed into). Mirrors the RowList
            // ItemTemplate fix; Slot{n}_TextValue setter is already write-through.
            + "                <EditableTextWidget Id=\"Slot" + s + "TextValueInput\" IsVisible=\"@Slot" + s + "_IsText\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"360\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"38\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" MarginLeft=\"20\" Brush=\"CustomBattle.Search.TextBox\" Text=\"@Slot" + s + "_TextValue\" />\n"
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
    // two children of the ListPanel, before SliderWidget. This pattern was
    // missing from every prior bisect.
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
            // Polish #9: slider handle widened from 14 to 18 px and given a
            // small vertical bump (38 -> 42) so it sits visually proud of the
            // track and is easier to click on without sub-pixel precision.
            // Vanilla SP Options sliders use roughly this footprint; the
            // pre-polish 14×38 was a hair smaller than the surrounding
            // controls and looked under-sized next to the click-to-edit
            // numeric field to the right.
            + "                        <ImageWidget Id=\"" + handleId + "\" DoNotAcceptEvents=\"true\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"18\" SuggestedHeight=\"42\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" Brush=\"SPOptions.Slider.Handle\" />\n"
            + "                      </Children>\n"
            + "                    </SliderWidget>\n"
            // v0.7.6 click-to-edit: replaced read-only RichTextWidget with an
            // EditableTextWidget bound two-way to Slot{n}_EditableValueText.
            // User can click the number, type a new value, and the setter
            // parses+clamps+writes IntValue/FloatValue. Slider drag still
            // updates this text via the FloatValue setter notifying
            // EditableValueText. Brush matches the search box so the field
            // visibly affords typing.
            + "                    <EditableTextWidget Id=\"Slot" + s + "ValueInput\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"90\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"38\" MarginLeft=\"20\" VerticalAlignment=\"Center\" Brush=\"CustomBattle.Search.TextBox\" Text=\"@Slot" + s + "_EditableValueText\" />\n"
            + "                  </Children>\n"
            + "                </ListPanel>\n";
    }
}

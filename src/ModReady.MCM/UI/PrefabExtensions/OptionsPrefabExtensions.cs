// ModReady clean-room re-implementation. MIT, copyright 2026 Maxfield Management Group.
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
        "<OptionsTabToggle Id=\"ModReadyModConfigTabToggle\" Parameter.TabName=\"ModReadyModConfigPage\">\n"
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
/// CurrentOptionNameWidget so when the user is on the ModReady tab and hovers
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
        "<ListPanel Id=\"ModReadyHintPanelWrapper\" IsVisible=\"@IsHintVisible\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"360\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Right\" VerticalAlignment=\"Top\" MarginTop=\"50\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">\n"
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
            // slice 4d: the {RowList} single-page scrollable list is the
            // unconditional Mod Configuration UI. The old fixed 20-slot fan, its
            // pagination, and all the dead slot-builder methods were removed in
            // Phase 5 (M13).
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
                var rtPath = ModReady.Foundation.RuntimeLog.Path;
                var dir = System.IO.Path.GetDirectoryName(rtPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    var dumpPath = System.IO.Path.Combine(dir, "PatchedOptions.dump.xml");
                    long h = StableHash(xml);
                    if (h != _lastDumpHash)
                    {
                        System.IO.File.WriteAllText(dumpPath, xml);
                        _lastDumpHash = h;
                        ModReady.Foundation.RuntimeLog.Write("MCMTabContentPatch", "wrote PatchedOptions.dump.xml (" + xml.Length + " chars)");
                    }
                }
            }
            catch { }
            return xml;
        }
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
        "    <NavigatableListPanel Id=\"ModReadyModRowList\" DataSource=\"{RowList}\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\"  VerticalAlignment=\"Top\" StackLayout.LayoutMethod=\"VerticalTopToBottom\">\n"
        + "      <ItemTemplate>\n"
        // Slice 7: tighter row spacing (50->44 tall, 4->2 top margin) so more
        // settings fit per screen without feeling cramped.
        + "        <Widget IsVisible=\"@IsVisible\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"960\" SuggestedHeight=\"44\" HorizontalAlignment=\"Left\" MarginTop=\"2\">\n"
        + "          <Children>\n"
        // Hover highlight overlay — solid amber, shown only while @IsHovered. The
        // flag is set by the HintWidget's HoverBegin/End at the row's end, which
        // is the ONLY hover signal that fires reliably across the whole row (the
        // event-accepting controls swallow Command.HoverBegin on the container).
        + "            <BrushWidget IsVisible=\"@IsHovered\" DoNotAcceptEvents=\"true\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"ModReady.RowHover.Solid\" />\n"
        // No full-width header background: native SPOptions group headers are
        // plain title text + a separator, with no box behind them. The parent
        // tier still reads clearly via the larger chevron and the divider below
        // (parent-only); child tier via the indent + a bold chevron. The old
        // ModReady.ParentHeaderBg gold wash was non-native and read as a heavy box.
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
        + "                    <BrushWidget DoNotAcceptEvents=\"true\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"27\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"27\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" MarginRight=\"9\" Brush=\"ModReady.Chevron.Closed\" IsVisible=\"@IsParentCollapsed\" />\n"
        + "                    <BrushWidget DoNotAcceptEvents=\"true\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"27\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"27\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" MarginRight=\"9\" Brush=\"ModReady.Chevron.Open\" IsVisible=\"@IsParentExpanded\" />\n"
        // Child chevrons: now BOLD (24px) — the primary nesting cue in place of the
        // removed left accent bar. Slightly smaller than the parent's 27px so the
        // tier is still legible, but clearly a real chevron, not a thin line.
        + "                    <BrushWidget DoNotAcceptEvents=\"true\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"24\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"24\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" MarginRight=\"9\" Brush=\"ModReady.Chevron.Closed\" IsVisible=\"@IsChildCollapsed\" />\n"
        + "                    <BrushWidget DoNotAcceptEvents=\"true\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"24\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"24\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" MarginRight=\"9\" Brush=\"ModReady.Chevron.Open\" IsVisible=\"@IsChildExpanded\" />\n"
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
        "<ListPanel Id=\"ModReadyModConfigPage\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Top\" MarginTop=\"10\" MarginBottom=\"10\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">\n"
        + "  <Children>\n"
        // ----- LEFT SIDEBAR: search field + clickable Mods list -----
        + "    <ListPanel Id=\"ModReadyModSidebar\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"380\" HeightSizePolicy=\"CoverChildren\" VerticalAlignment=\"Top\" MarginRight=\"24\" StackLayout.LayoutMethod=\"VerticalTopToBottom\">\n"
        + "      <Children>\n"
        + "        <RichTextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Left\" MarginLeft=\"6\" MarginBottom=\"8\" Brush=\"SPOptions.Tab.Text\" Text=\"Mods\" />\n"
        + "        <ListPanel WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" MarginBottom=\"10\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">\n"
        + "          <Children>\n"
        // Slice 6 (search magnifier) reverted: the only magnifier sprite found,
        // MPLobby\...\filters_search_icon, is a multiplayer-lobby sprite that
        // isn't loaded in the SP Options context, so it drew nothing. The verify
        // loop's screenshot caught it (gate stayed green). Left as the clean text
        // "Search..." field; revisit if an SP-loaded magnifier sprite turns up.
        + "            <EditableTextWidget Id=\"ModReadySearchInput\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"300\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"36\" VerticalAlignment=\"Center\" Brush=\"CustomBattle.Search.TextBox\" Text=\"@ModReadyModSearchText\" GamepadNavigationIndex=\"0\">\n"
        + "              <Children>\n"
        + "                <RichTextWidget IsVisible=\"@ModReadySearchPlaceholderVisible\" DoNotAcceptEvents=\"true\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Brush=\"SPOptions.Group.Title.Text\" Text=\"Search...\" />\n"
        + "              </Children>\n"
        + "            </EditableTextWidget>\n"
        + "            <ButtonWidget Command.Click=\"ExecuteClearSearch\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"56\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"36\" MarginLeft=\"6\" VerticalAlignment=\"Center\" Brush=\"Popup.Cancel.Button\" UpdateChildrenStates=\"true\" IsVisible=\"@ModReadySearchClearVisible\">\n"
        + "              <Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" DoNotAcceptEvents=\"true\" Brush=\"Popup.Button.Text\" Text=\"X\" /></Children>\n"
        + "            </ButtonWidget>\n"
        + "          </Children>\n"
        + "        </ListPanel>\n"
        + "        <NavigatableListPanel Id=\"ModReadyModListPanel\" DataSource=\"{ModList}\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" VerticalAlignment=\"Top\" StackLayout.LayoutMethod=\"VerticalTopToBottom\">\n"
        + "          <ItemTemplate>\n"
        // Flat selectable row (Phase 2.5): no button background. The selected
        // row shows a full-width highlight panel + a left accent bar.
        + "            <ButtonWidget Command.Click=\"ExecuteSelect\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"42\" MarginBottom=\"1\" UpdateChildrenStates=\"true\">\n"
        + "              <Children>\n"
        // Slice 4: hover wash (transparent until the row is hovered). Behind the
        // selection canvas so a selected row still reads as selected.
        + "                <BrushWidget DoNotAcceptEvents=\"true\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"ModReady.RowHover\" />\n"
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
        + "    <ListPanel Id=\"ModReadyModRightCol\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" VerticalAlignment=\"Top\" StackLayout.LayoutMethod=\"VerticalTopToBottom\" Brush=\"ModReady.Panel.Background\">\n"
        + "      <Children>\n"
        // Post-screenshot polish: "Mod Configuration · 35 mods" title row was
        // removed. The tab itself reads "Mod Configuration" (see
        // MCMTabTogglePatch) so a duplicate title underneath added no
        // information; the divider that used to sit beneath it was also part
        // of that same header block and is gone with it. Page now opens
        // directly into the search field, which becomes the visual entry point.
        // The ModReadyModConfigTitle / ModReadyModConfigSummary VM properties
        // are kept (other prefab patches or future scenarios may still use
        // them) but no longer rendered here.

        // v0.7.6 visual change #4: empty-state hint. Visible only when no
        // mods have registered MCM settings. ModReadyModConfigIsEmpty is
        // the inverse of HasMods; bound on the VM side. When true, the
        // settings slot panel below renders nothing but at least the user
        // sees a friendly explanation instead of an empty page.
        // Polish #11: empty-state text rewritten from "No mod settings found.
        // Mods that expose settings will appear here automatically." to a
        // friendlier two-line message that tells the user (a) nothing is
        // wrong, (b) what to do next. CoverChildren means the parent panel
        // collapses to zero height once mods register, so this only
        // occupies vertical space when actually needed.
        + "    <ListPanel IsVisible=\"@ModReadyModConfigIsEmpty\" WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" MarginTop=\"24\" StackLayout.LayoutMethod=\"VerticalTopToBottom\">\n"
        + "      <Children>\n"
        + "        <RichTextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" Brush=\"SPOptions.Description.Title.Text\" Text=\"No mod settings registered yet.\" />\n"
        + "        <RichTextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" MarginTop=\"6\" Brush=\"SPOptions.Group.Title.Text\" Text=\"Install any BUTR / ModReady-compatible mod that exposes options and it will appear here on next launch.\" />\n"
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
        // EditableTextWidget two-way to ModReadyModSearchText; the setter
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
        + "        <RichTextWidget IsVisible=\"@ModReadySearchClearVisible\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" TextHorizontalAlignment=\"Center\" MarginTop=\"4\" Brush=\"SPOptions.Group.Title.Text\" Text=\"@SelectedModSummary\" />\n"
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
        // in Modules\ModReady\ for modders who need to debug (see
        // MODREADY-NATIVE-API.md "Debugging" section).
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
        // close ModReadyModRightCol (Phase 2.1 right column)
        + "      </Children>\n"
        + "    </ListPanel>\n"
        // close ModReadyModConfigPage (now horizontal: sidebar + right column)
        + "  </Children>\n"
        + "</ListPanel>";

}

// BetaDeps.MCM -- MusicOptionsPatch
//
// Injects the BYO music-picker panel into the native Options > Sound tab, below
// the volume sliders.
//
// Target: the OptionsGroupedPage PREFAB definition -- inserted as the sibling
// immediately AFTER the BaseOptions list (the volume sliders), NOT the
// <OptionsGroupedPage> instance in the "Options" movie. We first injected as a
// child of the AudioOptionsPage *instance*, but Gauntlet does not instantiate
// children placed inside a prefab-instance tag -- the patch applied to the movie
// XML yet the widgets were never created. Patching the prefab's OWN definition
// fixed that.
//
// Then a second trap: OptionsInnerPanel (the prefab root) is a bare <Widget>,
// which does NOT run a stack layout -- appending a section there made it overlap
// the volume sliders. BaseOptions' parent <ListPanel Id="@Name"> DOES stack, so
// we Append our section as BaseOptions' next sibling: it flows directly below the
// sliders, above the (optional) grouped options.
//
// OptionsGroupedPage is shared by all four grouped pages (Video / Performance /
// Audio / Gameplay), so the section is instantiated on every one -- but
// @BetaDepsMusicVisible (reference-equality to the captured AudioOptions category)
// resolves true ONLY on the audio page, hiding it everywhere else.
//
// The panel binds to MusicCategoryMixin (attached to the audio GroupedOptionCategoryVM):
//   {BetaDepsMusicRows}    -> one MusicRowVM per context (enable/mode/volume/count)
//   @BetaDepsMusicVisible  -> hides the whole section unless this is the audio category
//
// Original work. MIT, copyright 2026 Maxfield Management Group.

using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;

namespace MCM.UI.PrefabExtensions;

// v1.1 BYO music picker -- SHELVED for the v1.0.0 ship (feature-complete + verified,
// held back as a future add-on). Re-enable by uncommenting this attribute, the two
// [ViewModelMixin] attributes in MusicOptionsMixin.cs, and flipping EnableMusicPicker
// in BetaDepsHarmonySubModule.cs (plus $IncludeMusicPicker in scripts\Build-Phase1.ps1).
// [PrefabExtension("OptionsGroupedPage", "descendant::NavigatableListPanel[@Id='BaseOptions']")]
internal sealed class MusicOptionsPatch : PrefabExtensionInsertPatch
{
    // Insert as the sibling immediately AFTER BaseOptions (inside the page's
    // vertical ListPanel stack), so the section flows directly below the volume
    // sliders. Append == sibling-after-target in this patcher.
    public override InsertType Type => InsertType.Append;

    [PrefabExtensionText]
    public string XmlContent =>
        "<ListPanel Id=\"BetaDepsMusicSection\" IsVisible=\"@BetaDepsMusicVisible\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Top\" MarginTop=\"24\" MarginBottom=\"40\" StackLayout.LayoutMethod=\"VerticalTopToBottom\">\n"
        + "  <Children>\n"
        // ---- section header + divider ----
        + "    <RichTextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" TextHorizontalAlignment=\"Center\" Brush=\"SPOptions.GameKeysGroup.Title.Text\" Text=\"Custom Music  (Bring Your Own)\" />\n"
        + "    <Widget WidthSizePolicy=\"Fixed\" SuggestedWidth=\"740\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"2\" HorizontalAlignment=\"Center\" MarginTop=\"6\" MarginBottom=\"12\" Brush=\"SPOptions.Group.Title.Separator\" />\n"
        // ---- per-context rows ----
        + "    <ListPanel DataSource=\"{BetaDepsMusicRows}\" WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" StackLayout.LayoutMethod=\"VerticalTopToBottom\">\n"
        + "      <ItemTemplate>\n"
        + "        <ListPanel IsVisible=\"@IsRowVisible\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"1000\" HeightSizePolicy=\"CoverChildren\" MarginBottom=\"4\" HorizontalAlignment=\"Center\" StackLayout.LayoutMethod=\"HorizontalLeftToRight\">\n"
        + "          <Children>\n"
        // context name
        + "            <TextWidget DoNotAcceptEvents=\"true\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"270\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Left\" TextHorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" Brush=\"SPOptions.OptionName.Text\" Text=\"@DisplayName\" />\n"
        // enable checkbox (vanilla SPOptions checkbox)
        + "            <ButtonWidget ButtonType=\"Toggle\" IsSelected=\"@EnableValue\" DoNotPassEventsToChildren=\"true\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"40\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"40\" VerticalAlignment=\"Center\" MarginLeft=\"10\" Brush=\"SPOptions.Checkbox.Empty.Button\" ToggleIndicator=\"MusicCheckIndicator\" UpdateChildrenStates=\"true\">\n"
        + "              <Children><BrushWidget Id=\"MusicCheckIndicator\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"SPOptions.Checkbox.Full.Button\" /></Children>\n"
        + "            </ButtonWidget>\n"
        // shuffle / sequential toggle (text reflects @ModeText)
        + "            <ButtonWidget ButtonType=\"Toggle\" IsSelected=\"@SequentialValue\" DoNotPassEventsToChildren=\"true\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"150\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"40\" VerticalAlignment=\"Center\" MarginLeft=\"20\" Brush=\"Popup.Cancel.Button\" UpdateChildrenStates=\"true\">\n"
        + "              <Children><TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" DoNotAcceptEvents=\"true\" Brush=\"Popup.Button.Text\" Text=\"@ModeText\" /></Children>\n"
        + "            </ButtonWidget>\n"
        // volume slider 0..100 (native SPOptions slider sprites; discrete 5% steps)
        + "            <SliderWidget Id=\"MusicVolSlider\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"240\" SuggestedHeight=\"42\" VerticalAlignment=\"Center\" MarginLeft=\"20\" DoNotUpdateHandleSize=\"true\" Filler=\"MusicVolFiller\" Handle=\"MusicVolHandle\" IsDiscrete=\"true\" DiscreteIncrementInterval=\"5\" MaxValueFloat=\"100\" MinValueFloat=\"0\" ValueFloat=\"@VolumeValue\" UpdateValueContinuously=\"false\">\n"
        + "              <Children>\n"
        + "                <Widget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"260\" SuggestedHeight=\"38\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Sprite=\"SPGeneral\\SPOptions\\standart_slider_canvas\" DoNotAcceptEvents=\"true\" />\n"
        + "                <Widget Id=\"MusicVolFiller\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"245\" SuggestedHeight=\"35\" VerticalAlignment=\"Center\" Sprite=\"SPGeneral\\SPOptions\\standart_slider_fill\" ClipContents=\"true\">\n"
        + "                  <Children><Widget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"245\" SuggestedHeight=\"35\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" Sprite=\"SPGeneral\\SPOptions\\standart_slider_fill\" /></Children>\n"
        + "                </Widget>\n"
        + "                <Widget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"300\" SuggestedHeight=\"60\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Sprite=\"SPGeneral\\SPOptions\\standart_slider_frame\" DoNotAcceptEvents=\"true\" />\n"
        + "                <ImageWidget Id=\"MusicVolHandle\" DoNotAcceptEvents=\"true\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"18\" SuggestedHeight=\"42\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" Brush=\"SPOptions.Slider.Handle\" />\n"
        + "              </Children>\n"
        + "            </SliderWidget>\n"
        // track count
        + "            <TextWidget DoNotAcceptEvents=\"true\" WidthSizePolicy=\"Fixed\" SuggestedWidth=\"260\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Left\" TextHorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" MarginLeft=\"20\" Brush=\"SPOptions.Slider.Value.Text\" Text=\"@TrackCountText\" />\n"
        + "          </Children>\n"
        + "        </ListPanel>\n"
        + "      </ItemTemplate>\n"
        + "    </ListPanel>\n"
        // ---- footer hint (drop-files guidance, shown once) ----
        + "    <RichTextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"CoverChildren\" HorizontalAlignment=\"Center\" TextHorizontalAlignment=\"Center\" MarginTop=\"14\" Brush=\"SPOptions.Group.Title.Text\" Text=\"Drop .ogg or .wav files into the BYO folder shown for each empty row, then relaunch to load them.\" />\n"
        + "  </Children>\n"
        + "</ListPanel>";
}

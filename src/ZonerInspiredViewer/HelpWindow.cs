using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace ZonerInspiredViewer
{
    [DataContract]
    internal sealed class HelpBuild
    {
        [DataMember] public string Version { get; set; }
        [DataMember] public string StartedUtc { get; set; }
        [DataMember] public string CompletedUtc { get; set; }
        [DataMember] public string Result { get; set; }
        [DataMember] public string Note { get; set; }
        public string Label { get { return "v" + Version + (Version == BuildInfo.Version ? "  (current)" : "  - " + Result); } }
        public override string ToString() { return Label; }
    }

    internal sealed class HelpWindow : Window
    {
        internal const string Shortcuts =
            "GENERAL\r\n"
            + "F1                         Help, version and changelog\r\n"
            + "Ctrl+,                    Configure the viewer\r\n"
            + "Ctrl+F                   Filter names in the current folder\r\n"
            + "Ctrl+Shift+F           Advanced search in the current folder and all subfolders\r\n"
            + "Ctrl+T                   New tab using the latest browser view\r\n"
            + "Ctrl+Tab               Cycle through tabs\r\n"
            + "Ctrl+W                  Close the active unpinned tab\r\n"
            + "F11                       Toggle fullscreen\r\n"
            + "H                          Toggle compact / normal layout\r\n"
            + "J                           Hide / show the folder tree\r\n\r\n"
            + "THUMBNAIL BROWSER\r\n"
            + "T                          Toggle the experimental thumbnail minimap\r\n"
            + "Arrow keys           Select a thumbnail; Up / Down move by visual row\r\n"
            + "Shift+arrows         Extend or contract the selection\r\n"
            + "Home / End          Select the first / last image under the current sort and filter\r\n"
            + "Page Up / Down   Scroll one viewport without changing selection\r\n"
            + "Enter / double-click   Open the selected image or folder\r\n"
            + "Backspace / Alt+Up     Go to the parent folder\r\n"
            + "Mouse Back          Go to the parent folder\r\n"
            + "Mouse Forward     Go down the saved folder history or into the selected folder\r\n"
            + "Ctrl+click / Shift+click   Toggle selection / select a range\r\n"
            + "Ctrl+A                   Select all items\r\n"
            + "Ctrl+C / X / V         Copy / cut / paste selected files and folders\r\n"
            + "F2 / Delete           Rename one selected file/folder / recycle selected items\r\n\r\n"
            + "Folder thumbnails support the same Rename, Move, Delete, Copy, Cut and Paste actions as files. Move and Recycle allow mixed multi-selection; selected folders include their contents. Recycling is confirmed and uses the Recycle Bin. Rename/move updates descendant open tabs and favorite paths while retaining aliases. Recycled descendant tabs become browsers at the nearest surviving parent.\r\n\r\n"
            + "THUMBNAIL MINIMAP (EXPERIMENTAL)\r\n"
            + "Drag the blue band   Scroll quickly through the current thumbnail view\r\n"
            + "Click a miniature     Scroll to that image's row without opening it\r\n"
            + "Mouse wheel           Scroll the thumbnail browser normally\r\n"
            + "T / minimap arrow   Hide / show the minimap; the arrow remains available\r\n"
            + "The map follows the current sort/filter. Long folders use a moving miniature window, not a compressed or sampled contact sheet. Folder markers preserve folder positions. Its on/off setting is saved with the viewer configuration.\r\n\r\n"
            + "IMAGE VIEWER\r\n"
            + "Right / Page Down / Space   Next image, wrapping at the end\r\n"
            + "Left / Page Up / Backspace   Previous image, wrapping at the start\r\n"
            + "Mouse wheel         Previous / next image with nearby thumbnail previews\r\n"
            + "Home / End           First / last image in the current sequence\r\n"
            + "Right button + wheel   Zoom in / out\r\n"
            + "+ / -                       Zoom in / out\r\n"
            + "0                           Fit in frame without enlarging small images\r\n"
            + "Left-button drag   Pan an image larger than the frame\r\n"
            + "P                           Hide / show the image pan navigator, bottom-right\r\n"
            + "I                           Toggle translucent image metadata, bottom-left\r\n"
            + "[ / ]                       Rotate left / right 90 degrees (view-only unless auto-save enabled)\r\n"
            + "Enter / double-click   Return to thumbnails in the same tab\r\n"
            + "Esc                        Stop slideshow, exit fullscreen and return to thumbnails\r\n"
            + "Delete                   Recycle the displayed image and advance without closing the tab\r\n\r\n"
            + "ADVANCED SEARCH\r\n"
            + "Type a name          Case-insensitive image / folder name match, including subfolders\r\n"
            + "Arrows / Home / End / Page Up / Down   Navigate result thumbnails\r\n"
            + "Enter / double-click   Open the selected result in the main window\r\n"
            + "Right-click            Open result or its containing folder\r\n"
            + "Esc                        Close search and cancel its background work\r\n\r\n"
            + "WORKSPACE AND SETTINGS\r\n"
            + "Batch tools: select thumbnails, then use Batch on the browser toolbar, File > Batch or the right-click menu. Review current/new names, dimensions and status; uncheck Use to exclude rows. Rename, Resize or Convert asks before applying the preview. Esc requests cancellation during processing.\r\n"
            + "Batch rename protects file extensions. * inserts the original name; ### inserts a three-digit sequence. Start/Step, letter numbering, metadata tokens, find/replace, removal, prefix/suffix, whitespace and case rules are available. Swaps use staging and attempted rollback; the result provides a JSON recovery log, not an automatic undo.\r\n"
            + "Batch resize supports pixels, percentage, print size/DPI and long/short edge, with aspect/fit and enlarge/reduce options. Resize/conversion create copies by default; explicit overwrite keeps backups in .zen-batch-backups. They use on-disk originals, exclude viewer-only adjustments, output 8-bit pixels and skip animated/multi-image inputs. JPEG transparency becomes white. HEIC/HEIF input can use another output format; HEIC encoding is not bundled. Metadata retention depends on the output format.\r\n"
            + "Batch presets are saved in this window's workspace and included in sessions/backups/reset. A blank output Folder uses each source folder. The preview is asynchronous and virtualized; encoding uses bounded CPU workers.\r\n"
            + "The global toolbar contains Open folder, Sessions, File, View and Configure, with search on the right. The second row follows the current view: file actions while browsing, and navigation/zoom/Enhance/AI/print while viewing. Back to folder returns to thumbnails in the same tab. Quick Enhance remains window-wide.\r\n"
            + "Folder breadcrumbs open parent folders; the overflow menu includes omitted ancestors. Ctrl+L or the pencil edits the address. Enter opens an existing absolute/relative path (environment variables are supported); Escape cancels. Ctrl+O opens the native folder chooser. Existing custom key assignments are preserved. Search's X clears only the current filter; Escape clears it while typing.\r\n"
            + "File includes Copy/Cut/Paste, Rename/Move/Recycle, Show in Explorer and Copy full path. Path/Explorer use the active image/selection, or the current folder if none is selected. View groups panels, overlays, compact/fullscreen, maximum window height and Performance details. Thumbnails beside Sort opens the size/shape settings.\r\n"
            + "Configure has a category sidebar and Find setting search across labels and commands. Ctrl+F focuses settings search while this window is active. Escape clears a search before closing. Settings apply immediately and the focused control stays visible when resizing. Performance details can be expanded at the bottom of the viewer; this choice is saved in sessions/backups.\r\n"
            + "Configure > Performance selects the AI enhancement GPU (Auto prefers a discrete GPU) and background thumbnail processing (Auto, CPU or Integrated GPU). Auto compares three uncached samples per format/size class and uses GPU only when the measured path is at least 20% faster. Recheck performance restarts comparison. Existing thumbnails stay cached until rebuilt. CPU still handles indexing, decoding and cache encoding; WPF's display adapter is controlled separately by Windows.\r\n"
            + "AI GPU soft limit checks DXGI/process memory before starting model work; it is not a hard cap on WPF or ONNX allocations. Thumbnail GPU limit bounds estimated texture working memory. Pressure, unsupported or missing devices fall back to CPU. Configure reports the current processing path; GPU preferences persist in sessions/backups and reset to Auto.\r\n"
            + "Configure > Performance > Image renderer uses GPU-resident shared textures. Image GPU selects the adapter; Image VRAM limit defaults to 4096 MB, separate from the CPU decoded cache. Live Windows GPU budgets can reduce this allowance. Inactive textures are evicted first; displayed textures are released only after switching to the CPU mirror. The status row identifies GPU/WPF presentation, and Configure reports hits, uploads, GPU JPEGs and evictions. Settings persist in sessions/backups.\r\n"
            + "NVIDIA GPU-assisted JPEG decoding uses bundled nvJPEG hybrid CPU/GPU processing, not a dedicated GeForce JPEG engine. ICC-enabled/unsupported JPEGs and other formats use CPU decode followed by GPU upload. Animation, oversize surfaces and device failures use WPF fallback. A CPU mirror supports editing, print and export. GPU limits do not include all CUDA, driver, WPF or AI allocations. No CUDA toolkit installation is required.\r\n"
            + "The expanded Performance bar shows each GPU's dedicated VRAM and shared RAM usage separately, across all applications. The integrated GPU uses shared system RAM as well as any reserved dedicated memory. Shared RAM is not extra RTX VRAM. Unavailable counters are not reported as zero.\r\n"
            + "Configure > Shortcuts shows a keyboard using the active Windows input layout. Hover for bindings/descriptions; double-click a key to record, save or remove an assignment. Reset all keys restores defaults after confirmation. Browser/Image filters the view. The logical diagram does not detect physical keyboard shape or hardware Fn macros.\r\n"
            + "Wheel or arrow navigation shows the current image plus three previous and next thumbnails in the current sort/filter order, wrapping at folder ends (small folders repeat). Left/Up show the previous image; Right/Down show the next. Click to open a preview; hover to keep the strip visible. It hides after two seconds. Configure > Images chooses bottom center, left center, right center, or turns previews off. Right-button + wheel still zooms.\r\n"
            + "Preview size in the image's bottom status bar adjusts the carousel from 96 to 256 px (default 160). The center is largest and fully opaque; previews shrink and fade toward both edges. All seven scale down together when space is limited. Size is separate from browser thumbnails and saved in sessions/backups. Larger, DPI-aware previews use their own thumbnail-cache sizes.\r\n"
            + "Configure > Reset viewer clears settings, tabs/pins, all saved sessions, favorites, expanded tree state, recent searches and custom shortcuts. Photos are untouched and a recovery JSON backup is created under the profile's backups folder before reset. Close other viewer windows first. Thumbnail cache is kept unless Also clear thumbnail cache is checked; Performance > Clear cache clears all sizes independently. Thumbnails regenerate as you browse. Prior backups and downloaded models are retained; this is not secure erasure.\r\n"
            + "The bottom status bar shows the displayed image's full path, type, filename, modified date/time, original pixel resolution, bit depth and file size. Long paths/names have full-text hover tooltips; details wrap in narrow windows. This stays visible independently of the I overlay and right metadata panel, and follows navigation, renames and saved file changes.\r\n"
            + "Name sorting uses natural numeric order: image1, image2, image11. Number in name remains available with the same ordering. Folder names and equal-metadata name ties are natural too; folders remain first in ascending order even when images descend. Image navigation and search results follow the chosen sort.\r\n"
            + "Enhance opens ten live manual sliders at the lower-left: vibrance, exposure, brilliance, contrast, brightness, black point, saturation, sharpness, highlights and shadows. Numeric values are inside the handles. Sharpness is 0-100; others are -100 to +100 with 0 neutral. Exposure spans +/-3 stops. Each row resets independently; the header offers Reset all and Close. Closing keeps adjustments.\r\n"
            + "E toggles Enhance in image mode and focuses the first slider when opening. Left/Right adjust the focused slider by one; click or Tab to another slider. E again returns focus to the image while keeping adjustments. Existing custom E assignments are preserved; change Enhance adjustments in Configure > Shortcuts.\r\n"
            + "The arrow attached to AI upscale selects Basic, Real-ESRGAN, HAT / Real-HAT, SwinIR-M Real-World x4, or optional SUPIR. Basic and the three larger ONNX models are bundled; selection persists in sessions/backups. SwinIR-M uses the official medium real-world GAN checkpoint, not the large, lightweight or classical variant. Switching active previews cancels old work and rebuilds from original pixels. SUPIR setup connects your trusted local CUDA/Python/SUPIR installation; checkpoints are not bundled or downloaded. SUPIR needs at least 12288 MB AI allowance, is limited to 4.2 MP output, and its full inference is unverified in this release. Missing/failed models never substitute Basic silently.\r\n"
            + "S or the top-row AI upscale button automatically detects images smaller than the current frame at the Windows display scaling, and applies a view-only upscale up to 3x with the selected model. Images already large enough are unchanged. Press again to cancel or restore original pixels. Enhance stays live; no file is written automatically. Navigation/reload discards the preview. S can be rebound, and existing custom S assignments are preserved.\r\n"
            + "Print preview: the printer toolbar icon or Ctrl+P in image mode opens a full-image preview with visible Enhance, rotation and applied AI upscale. Choose a Windows printer, paper size, portrait/landscape, fit or crop-to-fill, margins and copies. Printer hardware margins also apply. Preview zoom changes only the preview. Print sends the shown page to the Windows queue; opening the preview does not print. Transparent pixels use white paper, and animation prints the captured frame. Files are unchanged. Ctrl+P can be rebound; existing custom assignments are preserved.\r\n"
            + "Print preview renders and reads printer settings in the background. No printer means preview only; Print is disabled. PDF printers may ask for a filename. Submission success is not physical-print confirmation, and cancel cannot recall pages already accepted by the printer. Initial printing is one current image per page, without contact sheets or ICC soft-proofing. Color/quality otherwise use the driver's defaults.\r\n"
            + "While inline upscale is active, the bottom status bar has an AI upscale slider (1x, 1.25x, 1.4x, 1.6x, 1.8x, 2x, 2.5x, 3x, capped at 32 MP). Changes rebuild from original pixels after a short pause; the previous result stays visible until ready. 1x bypasses AI while keeping the slider available. Focused arrow/Home/End keys adjust scale without switching images. S cancels a pending change or restores the original when idle. Enhance > Save As keeps the applied size. The slider hides on navigation and its factor is temporary.\r\n"
            + "Configure > Image tools > Super-resolution retains manual 1x, 1.25x, 1.4x, 1.6x, 1.8x, 2x, 2.5x and 3x previews. It starts from original pixels and includes current Quick Enhance, all ten sliders and rotation. Original compares at equal display size; 1:1 shows actual output pixels. Save As preserves the original and opens a separate result tab. Esc cancels dialog processing. The manual factor persists, but unsaved previews do not.\r\n"
            + "Upscale uses local AI reconstruction with subject/background masks and gentler skin-color detail. It preserves source chroma before your adjustments; no complexion whitening or face identity processing. Detection and detail estimates can be wrong. The Basic model reconstructs at 3x; Real-ESRGAN/Real-HAT/SwinIR-M at 4x, filtered to fractional sizes. Source/output limits are 8/32 megapixels, with 8-bit sRGB export. ONNX models use the configured AI GPU/soft limit with same-model CPU fallback under pressure. Optional SUPIR requires CUDA, has a lower output cap, and may generate different real details.\r\n"
            + "Enhance stacks above Info, which stays in its normal position; short windows scroll the sliders. Focused slider arrows/Home/End change values instead of navigating images. Slideshow advancement is suspended while adjusting.\r\n"
            + "Manual adjustments apply after Quick Enhance and remain view-only until Enhance > Save or Save As. Save confirms overwriting and keeps original bytes in .viewer-enhance-backups. Save As opens another file in a new tab. Both save the full image with visible Quick Enhance and rotation, not the zoomed crop or overlays. The separate rotation-only Save command excludes enhancement.\r\n"
            + "Configure > Image tools > Enhance saving > Ask before overwriting controls replacement confirmations for Enhance Save and Save As. Turn it off to save without asking; original backups and changed-file checks remain enabled. The choice is remembered in sessions and JSON backups, and defaults to on for older sessions. Windows security/folder permissions still apply. Save As still opens a file picker; rotation saving and file-copy collisions are separate.\r\n"
            + "Enhance saving uses 8-bit sRGB output: PNG/WebP/JPEG XL lossless settings, JPEG/AVIF/HEIC quality 95, and a white background for JPEG transparency. Common EXIF/IPTC are retained where available; original ICC, XMP and vendor-specific metadata are not guaranteed. Animations cannot be overwritten; Save As exports the displayed frame. Unsupported encoders or changed/read-only files fail without overwriting. Saving can be canceled before replacement.\r\n"
            + "Successful Enhance saves reload the file and clear its baked slider/rotation values. Quick Enhance is not applied twice to that saved revision, including after session/backup restore; explicitly toggle it to process afresh. Unsaved values remain independent per tab and in sessions/backups. Returning from the browser to the same image retains them; another image in that tab starts neutral. Panel visibility is window-wide.\r\n"
            + "The image pan navigator appears at the bottom-right when an image extends beyond the frame at its current zoom. Drag its outlined visible area to pan, or click elsewhere in the preview to center there. It follows zoom, rotation and animated GIF frames. P or Configure > Appearance > Pan navigator toggles it for this window, saved in sessions/backups. It hides in the browser and when the image fits. During slideshow it sits above playback controls; dragging suspends advancement until release. Its preview uses the original decoded image without Quick Enhance.\r\n"
            + "The toolbar's Zoom lock keeps the zoom level across images, tabs, rotation and resizing. It highlights when enabled and is saved in sessions/backups. Manual zoom still changes the locked level; AI-resolution changes preserve apparent size. W fits width and H fits height, centering the image without distortion; drag to pan the overflowing dimension. Original size (1:1) shows current pixels at 100%. Normal Fit / 0 still avoids enlarging small images. U toggles compact mode. Configure > Shortcuts can change these keys.\r\n"
            + "A translucent zoom factor appears in the image area's top-right corner for one second after zoom changes above or below 1x, including Fit and restored views. It stays hidden at 1x and never blocks mouse gestures.\r\n"
            + "I toggles six image metadata rows: file size (automatic units), file type, file name, local modified date/time, pixel resolution and bit depth. It stays on across images/tabs until toggled off, hides in the browser and is saved in sessions/backups. Configure > Appearance also has this toggle, independent of the right-side Details panel.\r\n"
            + "Bit depth uses native WIC bits/pixel (with pixel format) for JPEG/PNG and header-reported bits/channel for modern codecs, including palette channel depth for GIF. This is not compressed bits/pixel or the converted display buffer. Unavailable fields are marked explicitly.\r\n"
            + "Text fields, sliders, dropdowns and the folder tree keep their own editing / navigation keys.\r\n"
            + "Tab right-click menus include duplicate and pin / unpin. The + button opens a new browser tab.\r\n"
            + "Drag tabs left/right to reorder, including pinned tabs. Drag favorite headers up/down to reorder shortcuts without moving folders. Escape cancels a drag; edges auto-scroll. Order is saved.\r\n"
            + "Tab stacks: right-click a tab > Tab stack > New stack, name it and check the tabs to combine. Use Move to stack or Manage tabs to change membership. Click a stack header/arrow to collapse or expand; its options menu switches to any member, renames the stack or unstacks all tabs without closing them. Pinned tabs retain their protection. Drag a stack header to move the whole group; drop a tab before a member to join that stack, or outside a group to unstack it. Duplicate stays in its source stack.\r\n"
            + "Stack names, order, members and collapsed state are saved in automatic/named sessions and JSON backups. Missing image tabs are removed on backup import, and empty stacks disappear. Reset clears stacks too. Older sessions/backups open as unstacked tabs. Stacks are window-local, one level deep; the fixed Browser command is not a stackable tab (duplicate it first).\r\n"
            + "The vertical-arrow toolbar button maximizes window height using the current monitor's available area, keeping its width. Press again to restore height. It respects the taskbar work area and does not change taskbar settings.\r\n"
            + "Configure contains Appearance, Images, Performance, Image tools, Backgrounds and Shortcuts. Changes apply immediately; Close or Esc returns to the workspace.\r\n"
            + "Backgrounds changes only the top toolbar/status band: None, Color fade, Contours, Facets, Ribbons or Crosshatch. The default is Crosshatch, Teal and 100% intensity with right-edge fading; Reset background restores these defaults. Choose None for a plain toolbar. The preview is live. Choices are saved per window in sessions/backups, and previously saved backgrounds are preserved.\r\n"
            + "Background line art is original, static and resolution-independent. Dark/light mode adjusts it automatically; high contrast suppresses decoration. Compact mode hides it with the toolbar. Photos, thumbnails, tabs and the native Windows title bar are unchanged.\r\n"
            + "Image tools rebuilds current-folder thumbnails at the chosen size, plus minimap/tab previews and bounded child-folder previews. Cancel keeps completed thumbnails. Unsupported codecs or damaged originals may still fail; hover the rebuild status for its latest error.\r\n"
            + "Image tools also has left/right rotation, Save and original-backup buttons. Rotation is view-only by default. Opt-in automatic saving applies to buttons and [ / ] without repeated app prompts; Windows access restrictions still apply.\r\n"
            + "After rotation or Enhance saving finishes, focus returns to the image so Left/Right navigate without clicking again, including after a failed or canceled save. Deliberately focused sliders still use their own arrow controls. A save finishing in the background does not activate the viewer over another window.\r\n"
            + "Rotation saving supports single-frame JPEG/PNG, retaining a separate original .bak in .viewer-rotation-backups beside the image each time. JPEG is re-encoded at quality 95; PNG pixels are lossless. Animations/other formats remain view-only. Standard WIC metadata is preserved where supported; vendor-specific metadata is not guaranteed. Quick Enhance is never saved.\r\n"
            + "Inactive image/folder tabs share a compact width based on the narrowest measured open title and available space. The active tab keeps its own normal title-based width, within 96-280 DIP, including when pinned or in an overflowing row. Activating another tab moves this exception to it. Longer names retain ellipsis and hover previews; Browser and + keep their fixed sizes.\r\n"
            + "Search and Advanced search are aligned to the right of the toolbar, on a separate right-aligned row in narrower windows. Filtering and keyboard shortcuts are unchanged.\r\n"
            + "The arrow beside either search field shows five recent distinct queries, newest first. Choose one to reuse it, or Clear history to reset the list without changing the current filter. Enter or leaving an edited search records it; Alt+Down opens the dropdown. History is shared within this window and retained in sessions/backups.\r\n"
            + "Images includes thumbnail size/shape, Basic/Advanced mode, ICC and slideshow start/stop, timing and shuffle. Starting playback closes Configure. Translucent controls stay at the bottom-right during playback and pause: previous, pause/resume, next, shuffle, stop and seconds per image. Editing the interval pauses the timer until the field loses focus or Enter is pressed. Escape stops and returns to thumbnails; Stop keeps the displayed image. Playback never starts automatically after restore.\r\n"
            + "Shortcuts can be added, replaced, removed or reset per command. Assignments apply to this window, are saved in sessions/backups and appear in current-key help and button tooltips. Conflicts in the same view are rejected. Escape stays available for cancel/exit; Windows shortcuts and native input/dialog controls are unchanged.\r\n"
            + "Performance separates the decoded cache budget from system-wide dedicated GPU memory usage/capacity. Unavailable telemetry is not shown as zero.\r\n"
            + "Quick Enhance is non-destructive and remains enabled across this window's tabs until turned off.\r\n"
            + "Advanced also checks native-resolution subject samples for soft edges and adapts sharpening separately from the background. Noise, uncertain detail, skin colors and transparency receive protection. Hover Quick Enhance for the subject/background sharpening estimate. This improves edge contrast, not lost detail from severe blur.";

        internal readonly List<HelpBuild> Builds;
        internal readonly string ReleaseNotes;
        private readonly ComboBox _versions;
        private readonly RichTextBox _buildText;
        private readonly List<FrameworkElement> _pages = new List<FrameworkElement>();
        private readonly List<ToggleButton> _sections = new List<ToggleButton>();
        private readonly RichTextBox _shortcutText;

        internal void RefreshShortcuts(ShortcutMap shortcuts)
        {
            _shortcutText.Document = HelpDocument.Shortcuts(shortcuts.HelpText() + "DEFAULT KEY AND MOUSE REFERENCE\r\n"
                + "The reference below describes factory defaults. Your current assignments are listed above.\r\n\r\n" + Shortcuts);
        }

        public HelpWindow()
        {
            Title = "Help - " + BuildInfo.WindowTitle;
            Width = 860; Height = 740; MinWidth = 580; MinHeight = 440;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ThemeManager.PrepareWindow(this);
            ReleaseNotes = ReadResource("Viewer.Changelog.md");
            using (Stream stream = typeof(HelpWindow).Assembly.GetManifestResourceStream("Viewer.BuildHistory.json"))
                Builds = stream == null ? new List<HelpBuild>() : (List<HelpBuild>)new DataContractJsonSerializer(typeof(List<HelpBuild>)).ReadObject(stream);
            Builds.Reverse();
            var panel = new DockPanel { Margin = new Thickness(16) };
            ThemeManager.Bind(panel, Panel.BackgroundProperty, ThemeKeys.WindowBackground);
            Content = panel;
            var heading = new TextBlock { Text = BuildInfo.AppName + "  /  v" + BuildInfo.Version, FontSize = 22,
                FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
            ThemeManager.Bind(heading, TextBlock.ForegroundProperty, ThemeKeys.Text);
            DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
            var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            DockPanel.SetDock(tabs, Dock.Top); panel.Children.Add(tabs);
            var content = new Grid(); panel.Children.Add(content);
            _shortcutText = HelpDocument.View(HelpDocument.Shortcuts(""));
            _pages.Add(_shortcutText);
            RefreshShortcuts(new ShortcutMap());
            var history = new DockPanel();
            _versions = new ComboBox { ItemsSource = Builds, DisplayMemberPath = "Label", Height = 32, Margin = new Thickness(0, 0, 0, 10) };
            AutomationProperties.SetName(_versions, "Build version");
            DockPanel.SetDock(_versions, Dock.Top); history.Children.Add(_versions);
            _buildText = HelpDocument.View(HelpDocument.Create()); history.Children.Add(_buildText);
            _versions.SelectionChanged += delegate { ShowBuild(); };
            _pages.Add(history);
            _pages.Add(HelpDocument.View(HelpDocument.Markdown(ReleaseNotes)));
            _pages.Add(HelpDocument.View(HelpDocument.Markdown(
                "# Supported formats\n"
                + "## Photos and graphics\n"
                + "- **JPEG / JPG** (.jpg, .jpeg): Windows decoder.\n"
                + "- **PNG** (.png): Windows decoder, including transparency.\n"
                + "- **WebP** (.webp): bundled decoder; still image / first animation frame.\n"
                + "- **AVIF** (.avif): bundled decoder; primary still image.\n"
                + "- **JPEG XL** (.jxl): bundled decoder; still image / first animation frame.\n"
                + "- **HEIC / HEIF** (.heic, .heif): bundled HEVC decoder; primary image. No Windows extension required.\n"
                + "- **GIF** (.gif): still and animated images, with frame timing and disposal.\n"
                + "## Display and playback\n"
                + "Modern formats are converted to 8-bit display pixels. HDR output, RAW and multi-page browsing are not supported. Embedded ICC conversion is optional under Configure > Images.\n"
                + "GIF playback runs only in the active image tab. It uses up to one quarter of the decoded cache budget (32-256 MB), separately from the still-image cache, with a 512-frame limit. Oversized animations show their first frame with a status message. Frame delays below 20 ms are clamped to 20 ms.\n"
                + "## Saving rotations\n"
                + "Rotation is non-destructive by default for every format. Saving a rotation supports single-frame JPEG and PNG only, with an original-file backup. Other formats and animations remain view-only.\n"
                + "## Decoder components\n"
                + "Bundled Magick.NET 14.16.0, libheif, libde265, libjxl and libwebp. See the Licenses folder beside the executable for third-party notices. Decoding runs on bounded CPU workers; WPF renders the resulting pixels on the GPU where available.")));
            string[] names = { "Shortcuts", "Version history", "Release notes", "Formats" };
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                var tab = new ToggleButton { Content = names[i], Height = 32, MinWidth = 92, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 0) };
                AutomationProperties.SetName(tab, names[i]);
                tab.Click += delegate { SelectSection(index); };
                tabs.Children.Add(tab); _sections.Add(tab); content.Children.Add(_pages[i]);
            }
            _versions.SelectedIndex = 0; SelectSection(0);
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        }

        internal void SelectSection(int index)
        {
            for (int i = 0; i < _pages.Count; i++)
            {
                _pages[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
                _sections[i].IsChecked = i == index;
            }
        }

        private void ShowBuild()
        {
            HelpBuild build = _versions.SelectedItem as HelpBuild;
            if (build == null) return;
            _buildText.Document = HelpDocument.Markdown("# Version " + build.Version + "\r\n"
                + (build.Version == BuildInfo.Version ? "Current executable" : build.Result) + "\r\n"
                + "Build started (UTC): " + build.StartedUtc + "\r\n\r\n" + build.Note + "\r\n\r\n"
                + RelatedNotes(build.Version));
            _buildText.ScrollToHome();
        }

        internal string RelatedNotes(string version)
        {
            Version target;
            if (!Version.TryParse(version, out target)) return "";
            MatchCollection sections = Regex.Matches(ReleaseNotes, @"^## (\d+\.\d+\.\d+)(?:-(\d+\.\d+\.\d+))?", RegexOptions.Multiline);
            for (int i = 0; i < sections.Count; i++)
            {
                Match section = sections[i];
                Version first = new Version(section.Groups[1].Value);
                Version last = section.Groups[2].Success ? new Version(section.Groups[2].Value) : first;
                if (target >= first && target <= last)
                    return ReleaseNotes.Substring(section.Index, (i + 1 < sections.Count ? sections[i + 1].Index : ReleaseNotes.Length) - section.Index).Trim();
            }
            return "See Release notes for earlier prototype changes.";
        }

        private static string ReadResource(string name)
        {
            using (Stream stream = typeof(HelpWindow).Assembly.GetManifestResourceStream(name))
            using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
        }

    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Windows.Input;

namespace ZonerInspiredViewer
{
    [DataContract]
    internal sealed class ShortcutGesture
    {
        [DataMember] public int KeyCode { get; set; }
        [DataMember] public int Modifiers { get; set; }
        public ShortcutGesture Copy() { return (ShortcutGesture)MemberwiseClone(); }
        public override string ToString()
        {
            string name = WindowsKeyboardLayout.Current.KeyName((Key)KeyCode);
            return (((ModifierKeys)Modifiers & ModifierKeys.Control) != 0 ? "Ctrl+" : "")
                + (((ModifierKeys)Modifiers & ModifierKeys.Alt) != 0 ? "Alt+" : "")
                + (((ModifierKeys)Modifiers & ModifierKeys.Shift) != 0 ? "Shift+" : "") + name;
        }
    }

    [DataContract]
    internal sealed class ShortcutOverride
    {
        [DataMember] public string Action { get; set; }
        [DataMember] public List<ShortcutGesture> Keys { get; set; }
    }

    internal sealed class ShortcutCommand
    {
        public string Id, Name;
        public int Scope; // 1 = browser, 2 = image, 3 = both.
        public bool Repeat, TextInput, Selection;
        public Key NavigationKey;
        public List<ShortcutGesture> Defaults;
        public string Description
        {
            get
            {
                if (Selection) return "Move the active thumbnail or scroll the browser. Ctrl/Shift also adjust the selection.";
                switch (Id)
                {
                    case "help": return "Open version information, current shortcuts and release history.";
                    case "configure": return "Open viewer preferences.";
                    case "search": return "Focus the filename filter for the current folder.";
                    case "folderAddress": return "Enter or paste a folder path in the current tab.";
                    case "openFolder": return "Choose a folder to browse in the current tab.";
                    case "advancedSearch": return "Search recursively from the current folder.";
                    case "newTab": return "Open the latest folder browser view in a new tab.";
                    case "nextTab": return "Activate the next open tab.";
                    case "closeTab": return "Close the active tab.";
                    case "fullscreen": return "Enter or leave fullscreen mode.";
                    case "compact": return "Toggle the toolbar and folder tree for a compact workspace.";
                    case "tree": return "Show or hide the folder navigation tree.";
                    case "minimap": return "Show or hide the browser's thumbnail minimap.";
                    case "metadata": return "Show or hide image details in the lower-left overlay.";
                    case "navigator": return "Show or hide the overview used to pan a zoomed image.";
                    case "manualEnhance": return "Open or close manual adjustment sliders for the current image.";
                    case "upscale": return "Automatically upscale a small image for the viewing frame. Press again to restore the original or cancel processing.";
                    case "print": return "Preview and print the current image with visible rotation, Enhance and applied AI upscale.";
                    case "open": return "Open the selected image or folder from the thumbnail browser.";
                    case "browse": return "Return to the folder's thumbnails in this tab.";
                    case "next": return "Show the next image, wrapping at the end of the folder.";
                    case "previous": return "Show the previous image, wrapping at the start of the folder.";
                    case "first": return "Show the first image in the current sort and filter order.";
                    case "last": return "Show the last image in the current sort and filter order.";
                    case "zoomIn": return "Increase the image's zoom around the frame center.";
                    case "zoomOut": return "Decrease the image's zoom around the frame center.";
                    case "fit": return "Fit the image inside the viewing frame without enlarging small images.";
                    case "fitWidth": return "Fit the image to the frame width, preserving proportions. Small images may be enlarged.";
                    case "fitHeight": return "Fit the image to the frame height, preserving proportions. Small images may be enlarged.";
                    case "zoomLock": return "Keep the zoom level when changing images and tabs. Zoom controls change the locked level.";
                    case "actual": return "Show the image at one display pixel per image pixel.";
                    case "rotateLeft": return "Rotate the current image counterclockwise by 90 degrees.";
                    case "rotateRight": return "Rotate the current image clockwise by 90 degrees.";
                    case "rename": return "Rename the current image or the selected browser item.";
                    case "delete": return "Move selected items or the current image to the Recycle Bin.";
                    case "copy": return "Copy selected files and folders to the clipboard.";
                    case "cut": return "Prepare selected files and folders for moving with Paste.";
                    case "paste": return "Paste files and folders into the current folder.";
                    case "selectAll": return "Select every visible item in the thumbnail browser.";
                    case "parent": case "browserParent": return "Open the parent of the current folder.";
                    case "forward": return "Return to the child folder in forward navigation history.";
                    case "enhance": return "Toggle automatic enhancement across all tabs in this window.";
                    case "slideshow": return "Start or stop the current folder's windowed slideshow.";
                    case "pauseSlideshow": return "Pause or resume an active slideshow.";
                    default: return Name;
                }
            }
        }
        public override string ToString() { return Name + "  (" + (Scope == 1 ? "Browser" : Scope == 2 ? "Image" : "Both views") + ")"; }
    }

    internal sealed class ShortcutMap
    {
        internal readonly List<ShortcutCommand> Commands = new List<ShortcutCommand>();
        private Dictionary<string, List<ShortcutGesture>> _keys;
        internal ShortcutMap()
        {
            Add("help", "Help", 3, Key.F1).TextInput = true;
            Add("configure", "Configure", 3, Key.OemComma, ModifierKeys.Control).TextInput = true;
            Add("search", "Search this folder", 3, Key.F, ModifierKeys.Control).TextInput = true;
            Add("folderAddress", "Folder address", 3, Key.L, ModifierKeys.Control).TextInput = true;
            Add("openFolder", "Open folder", 3, Key.O, ModifierKeys.Control).TextInput = true;
            Add("advancedSearch", "Advanced search", 3, Key.F, ModifierKeys.Control | ModifierKeys.Shift).TextInput = true;
            Add("newTab", "New browser tab", 3, Key.T, ModifierKeys.Control).TextInput = true;
            Add("nextTab", "Next tab", 3, Key.Tab, ModifierKeys.Control);
            Add("closeTab", "Close tab", 3, Key.W, ModifierKeys.Control);
            Add("fullscreen", "Fullscreen", 3, Key.F11);
            Add("compact", "Compact mode", 3, Key.H);
            Add("tree", "Folder tree", 3, Key.J);
            Add("minimap", "Thumbnail minimap", 1, Key.T);
            Add("metadata", "Image metadata overlay", 2, Key.I);
            Add("navigator", "Image navigator", 2, Key.P);
            Add("manualEnhance", "Enhance adjustments", 2, Key.E);
            Add("upscale", "AI upscale", 2, Key.S);
            Add("print", "Print preview", 2, Key.P, ModifierKeys.Control);
            Add("open", "Open selected item", 1, Key.Enter);
            Add("browse", "Return to thumbnails", 2, Key.Enter);
            Add("next", "Next image", 2, Key.Right, ModifierKeys.None, Key.PageDown, Key.Space).Repeat = true;
            Add("previous", "Previous image", 2, Key.Left, ModifierKeys.None, Key.PageUp, Key.Back).Repeat = true;
            Add("first", "First image", 2, Key.Home);
            Add("last", "Last image", 2, Key.End);
            Add("zoomIn", "Zoom in", 2, Key.OemPlus, ModifierKeys.None, Key.Add).Repeat = true;
            Commands.Last().Defaults.Add(Gesture(Key.OemPlus, ModifierKeys.Shift));
            Add("zoomOut", "Zoom out", 2, Key.OemMinus, ModifierKeys.None, Key.Subtract).Repeat = true;
            Add("fit", "Fit image", 2, Key.D0, ModifierKeys.None, Key.NumPad0);
            Add("actual", "Actual size (100%)", 2, Key.None);
            Add("fitWidth", "Fit width", 2, Key.None);
            Add("fitHeight", "Fit height", 2, Key.None);
            Add("zoomLock", "Zoom lock", 3, Key.None);
            Add("rotateLeft", "Rotate left 90 degrees", 2, Key.Oem4);
            Add("rotateRight", "Rotate right 90 degrees", 2, Key.Oem6);
            Add("rename", "Rename", 3, Key.F2);
            Add("delete", "Recycle", 3, Key.Delete);
            Add("copy", "Copy", 3, Key.C, ModifierKeys.Control);
            Add("cut", "Cut", 3, Key.X, ModifierKeys.Control);
            Add("paste", "Paste", 3, Key.V, ModifierKeys.Control);
            Add("selectAll", "Select all items", 1, Key.A, ModifierKeys.Control);
            Add("parent", "Parent folder", 3, Key.BrowserBack);
            Add("browserParent", "Up one folder", 1, Key.Back).Defaults.Add(Gesture(Key.Up, ModifierKeys.Alt));
            Add("forward", "Forward folder", 3, Key.BrowserForward);
            foreach (Key key in new[] { Key.Left, Key.Right, Key.Up, Key.Down, Key.Home, Key.End, Key.PageUp, Key.PageDown })
            {
                var command = Add("nav" + key, "Thumbnail " + Gesture(key, ModifierKeys.None), 1, key);
                command.Selection = true; command.Repeat = true; command.NavigationKey = key;
            }
            Add("enhance", "Quick Enhance", 3, Key.None);
            Add("slideshow", "Start / stop slideshow", 3, Key.F5);
            Add("pauseSlideshow", "Pause / resume slideshow", 2, Key.None);
            ResetAll();
        }

        private ShortcutCommand Add(string id, string name, int scope, Key key, ModifierKeys modifiers = ModifierKeys.None, params Key[] aliases)
        {
            var command = new ShortcutCommand { Id = id, Name = name, Scope = scope, Defaults = new List<ShortcutGesture>() };
            if (key != Key.None) command.Defaults.Add(Gesture(key, modifiers));
            foreach (Key alias in aliases) command.Defaults.Add(Gesture(alias, modifiers));
            Commands.Add(command); return command;
        }

        internal static ShortcutGesture Gesture(Key key, ModifierKeys modifiers)
        { return new ShortcutGesture { KeyCode = (int)key, Modifiers = (int)modifiers }; }
        internal List<ShortcutGesture> Keys(string id) { return _keys[id].Select(key => key.Copy()).ToList(); }
        internal string Display(string id) { return _keys[id].Count == 0 ? "Unassigned" : String.Join(" / ", _keys[id]); }
        internal void ResetAll() { _keys = Commands.ToDictionary(c => c.Id, c => c.Defaults.Select(g => g.Copy()).ToList()); }

        internal static bool IsModifier(Key key)
        { return key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LeftShift || key == Key.RightShift; }

        private static bool Valid(ShortcutGesture gesture)
        {
            if (gesture == null || !Enum.IsDefined(typeof(Key), gesture.KeyCode)) return false;
            Key key = (Key)gesture.KeyCode; var modifiers = (ModifierKeys)gesture.Modifiers;
            return key != Key.None && key != Key.Escape && !IsModifier(key) && key != Key.LWin && key != Key.RWin
                && key != Key.System && key != Key.ImeProcessed && key != Key.DeadCharProcessed
                && (modifiers & ~(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift)) == 0
                && !(key == Key.Tab && (modifiers == ModifierKeys.None || modifiers == ModifierKeys.Shift || (modifiers & ModifierKeys.Alt) != 0))
                && !((modifiers & ModifierKeys.Alt) != 0 && (key == Key.F4 || key == Key.Space))
                && !(key == Key.Delete && (modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == (ModifierKeys.Control | ModifierKeys.Alt));
        }

        private static bool Matches(ShortcutCommand command, ShortcutGesture gesture, Key key, ModifierKeys modifiers)
        {
            if (gesture.KeyCode != (int)key) return false;
            int extra = command.Selection ? (int)(ModifierKeys.Control | ModifierKeys.Shift) : 0;
            return (gesture.Modifiers & ~extra) == ((int)modifiers & ~extra)
                && (gesture.Modifiers & (int)modifiers) == gesture.Modifiers;
        }

        internal ShortcutCommand Match(Key key, ModifierKeys modifiers, bool browser)
        {
            return Commands.FirstOrDefault(c => (c.Scope & (browser ? 1 : 2)) != 0 && _keys[c.Id].Any(g => Matches(c, g, key, modifiers)));
        }

        private string Validate(Dictionary<string, List<ShortcutGesture>> keys)
        {
            if (Commands.Any(c => keys[c.Id] == null || keys[c.Id].Count > 8 || keys[c.Id].Any(g => !Valid(g))))
                return "Use a valid key combination. Escape, focus navigation and Windows shortcuts are reserved.";
            foreach (var command in Commands)
            {
                var seen = new HashSet<string>();
                foreach (var gesture in keys[command.Id])
                {
                    if (!seen.Add(gesture.KeyCode + ":" + gesture.Modifiers)) return "That shortcut is already assigned to this command.";
                    foreach (var other in Commands)
                    {
                        if (command == other || (command.Scope & other.Scope) == 0) continue;
                        foreach (var candidate in keys[other.Id])
                            for (int m = 0; m < 8; m++)
                                if (Matches(command, gesture, (Key)gesture.KeyCode, (ModifierKeys)m)
                                    && Matches(other, candidate, (Key)gesture.KeyCode, (ModifierKeys)m))
                                    return gesture + " conflicts with " + other.Name + ". Remove its assignment first.";
                    }
                }
            }
            return null;
        }

        internal bool Set(string id, List<ShortcutGesture> gestures, out string error)
        {
            if (!_keys.ContainsKey(id)) { error = "Unknown command."; return false; }
            var next = new Dictionary<string, List<ShortcutGesture>>(_keys); next[id] = gestures;
            error = Validate(next); if (error != null) return false;
            _keys[id] = gestures.Select(g => g.Copy()).ToList(); return true;
        }

        internal List<ShortcutOverride> Export()
        {
            return Commands.Where(c => !c.Defaults.Select(g => g.KeyCode + ":" + g.Modifiers)
                .SequenceEqual(_keys[c.Id].Select(g => g.KeyCode + ":" + g.Modifiers)))
                .Select(c => new ShortcutOverride { Action = c.Id, Keys = Keys(c.Id) }).ToList();
        }

        internal bool Load(List<ShortcutOverride> saved, out string error)
        {
            var next = Commands.ToDictionary(c => c.Id, c => c.Defaults.Select(g => g.Copy()).ToList());
            var seen = new HashSet<string>(); error = null;
            if (saved != null)
            {
                if (saved.Count > Commands.Count) { error = "Too many shortcut assignments."; return false; }
                foreach (var item in saved)
                {
                    if (item == null || item.Action == null || !next.ContainsKey(item.Action) || !seen.Add(item.Action))
                    { error = "Unknown or duplicate shortcut command."; return false; }
                    next[item.Action] = item.Keys;
                }
            }
            // A pre-existing custom P binding takes precedence over the newly introduced default.
            if (!seen.Contains("navigator") && Commands.Any(c => c.Id != "navigator" && (c.Scope & 2) != 0 && seen.Contains(c.Id)
                && next[c.Id] != null && next[c.Id].Any(g => g != null && Matches(c, g, Key.P, ModifierKeys.None))))
                next["navigator"] = new List<ShortcutGesture>();
            if (!seen.Contains("manualEnhance") && Commands.Any(c => c.Id != "manualEnhance" && (c.Scope & 2) != 0 && seen.Contains(c.Id)
                && next[c.Id] != null && next[c.Id].Any(g => g != null && Matches(c, g, Key.E, ModifierKeys.None))))
                next["manualEnhance"] = new List<ShortcutGesture>();
            if (!seen.Contains("upscale") && Commands.Any(c => c.Id != "upscale" && (c.Scope & 2) != 0 && seen.Contains(c.Id)
                && next[c.Id] != null && next[c.Id].Any(g => g != null && Matches(c, g, Key.S, ModifierKeys.None))))
                next["upscale"] = new List<ShortcutGesture>();
            if (!seen.Contains("print") && Commands.Any(c => c.Id != "print" && (c.Scope & 2) != 0 && seen.Contains(c.Id)
                && next[c.Id] != null && next[c.Id].Any(g => g != null && Matches(c, g, Key.P, ModifierKeys.Control))))
                next["print"] = new List<ShortcutGesture>();
            foreach (string introduced in new[] { "folderAddress", "openFolder" })
            {
                Key key = introduced == "folderAddress" ? Key.L : Key.O;
                if (!seen.Contains(introduced) && Commands.Any(c => c.Id != introduced && seen.Contains(c.Id)
                    && next[c.Id] != null && next[c.Id].Any(g => g != null && Matches(c, g, key, ModifierKeys.Control))))
                    next[introduced] = new List<ShortcutGesture>();
            }
            error = Validate(next); if (error != null) return false;
            _keys = next.ToDictionary(pair => pair.Key, pair => pair.Value.Select(g => g.Copy()).ToList()); return true;
        }

        internal string Presentation(string name)
        {
            switch (name)
            {
                case "Back to folder": name = "Return to thumbnails"; break;
                case "Show folder tree": case "Hide folder tree": name = "Folder tree"; break;
                case "Show thumbnail minimap": case "Hide thumbnail minimap": name = "Thumbnail minimap"; break;
                case "Advanced Quick Enhance": name = "Quick Enhance"; break;
                case "Pause slideshow": case "Resume slideshow": name = "Pause / resume slideshow"; break;
            }
            var command = Commands.FirstOrDefault(c => c.Name == name);
            return command == null ? null : Display(command.Id);
        }

        internal string HelpText()
        {
            return "CURRENT KEY ASSIGNMENTS\r\nEscape always stops playback, exits fullscreen and returns to thumbnails.\r\n"
                + "Edit assignments in Configure > Shortcuts. Browser navigation also accepts Ctrl/Shift for selection. Text fields and native dialogs keep their own keys.\r\n\r\n"
                + String.Join("\r\n", Commands.Select(c => Display(c.Id) + "     " + c)) + "\r\n\r\n";
        }
    }
}

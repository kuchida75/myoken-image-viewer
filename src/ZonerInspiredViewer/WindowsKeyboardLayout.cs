using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;
using Microsoft.Win32;

namespace ZonerInspiredViewer
{
    internal sealed class WindowsKeyboardLayout
    {
        [ThreadStatic] private static WindowsKeyboardLayout _current;
        private readonly Dictionary<Key, string> _names = new Dictionary<Key, string>();
        internal readonly IntPtr Handle;
        internal readonly string Id, Name;

        private WindowsKeyboardLayout(IntPtr handle, string id)
        {
            Handle = handle; Id = id; Name = ReadName(id, handle);
        }

        internal static WindowsKeyboardLayout Current
        {
            get
            {
                IntPtr handle = GetKeyboardLayout(0);
                if (_current == null || _current.Handle != handle)
                {
                    var id = new StringBuilder(9);
                    GetKeyboardLayoutName(id);
                    _current = new WindowsKeyboardLayout(handle, id.Length == 8 ? id.ToString() : "unknown");
                }
                return _current;
            }
        }

        internal Key KeyAt(uint scanCode)
        { return KeyInterop.KeyFromVirtualKey((int)MapVirtualKeyEx(scanCode, 3, Handle)); }

        internal string KeyName(Key key)
        {
            string value;
            if (_names.TryGetValue(key, out value)) return value;
            switch (key)
            {
                case Key.None: value = "Unavailable"; break;
                case Key.Back: value = "Backspace"; break;
                case Key.Return: value = "Enter"; break;
                case Key.Capital: value = "Caps Lock"; break;
                case Key.PageUp: value = "Page Up"; break;
                case Key.PageDown: value = "Page Down"; break;
                case Key.LeftCtrl: case Key.RightCtrl: value = "Ctrl"; break;
                case Key.LeftShift: case Key.RightShift: value = "Shift"; break;
                case Key.LeftAlt: case Key.RightAlt: value = "Alt"; break;
                case Key.LWin: case Key.RWin: value = "Windows"; break;
                case Key.Apps: value = "Menu"; break;
                case Key.Snapshot: value = "Print Screen"; break;
                case Key.Scroll: value = "Scroll Lock"; break;
                case Key.NumLock: value = "Num Lock"; break;
                case Key.Add: value = "Num +"; break;
                case Key.Subtract: value = "Num -"; break;
                case Key.Multiply: value = "Num *"; break;
                case Key.Divide: value = "Num /"; break;
                case Key.Decimal: value = "Num decimal"; break;
                case Key.BrowserBack: value = "Browser Back"; break;
                case Key.BrowserForward: value = "Browser Forward"; break;
                default:
                    if (key >= Key.NumPad0 && key <= Key.NumPad9) value = "Num " + ((int)key - (int)Key.NumPad0);
                    else if (key >= Key.F1 && key <= Key.F24) value = key.ToString();
                    else value = Character(key, false);
                    if (String.IsNullOrWhiteSpace(value)) value = key.ToString();
                    break;
            }
            _names[key] = value; return value;
        }

        internal string Character(Key key, bool shifted)
        {
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            if (vk == 0) return "";
            var state = new byte[256];
            if (shifted) state[0x10] = 0x80;
            var text = new StringBuilder(16);
            // Flag 4 reads dead-key labels without changing the Windows composition buffer (Windows 10 1607+).
            int count = ToUnicodeEx(vk, MapVirtualKeyEx(vk, 0, Handle), state, text, text.Capacity, 4, Handle);
            if (count == 0) return "";
            string value = text.ToString(0, Math.Min(text.Length, Math.Abs(count)));
            foreach (char c in value) if (Char.IsControl(c)) return "";
            return value.ToUpperInvariant();
        }

        private static string ReadName(string id, IntPtr handle)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Keyboard Layouts\" + id))
                {
                    if (key != null)
                    {
                        string resource = key.GetValue("Layout Display Name") as string;
                        var name = new StringBuilder(256);
                        if (!String.IsNullOrEmpty(resource) && SHLoadIndirectString(resource, name, (uint)name.Capacity, IntPtr.Zero) == 0 && name.Length > 0)
                            return name.ToString();
                        string plain = key.GetValue("Layout Text") as string;
                        if (!String.IsNullOrEmpty(plain)) return plain;
                    }
                }
            }
            catch (System.Security.SecurityException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.IO.IOException) { }
            try { return CultureInfo.GetCultureInfo((int)(handle.ToInt64() & 0xffff)).DisplayName; }
            catch (CultureNotFoundException) { return "Windows input layout"; }
        }

        [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint threadId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetKeyboardLayoutName(StringBuilder name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint MapVirtualKeyEx(uint code, uint type, IntPtr layout);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ToUnicodeEx(uint key, uint scan, byte[] state, StringBuilder text, int size, uint flags, IntPtr layout);
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int SHLoadIndirectString(string source, StringBuilder output, uint size, IntPtr reserved);
    }
}

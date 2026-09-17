using System;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace ZonerInspiredViewer
{
    internal static class FileClipboardData
    {
        public const string PreferredEffect = "Preferred DropEffect";
        [DllImport("user32.dll")]
        public static extern uint GetClipboardSequenceNumber();

        public static DataObject Create(string[] paths, bool cut)
        {
            var data = new DataObject();
            var collection = new StringCollection();
            collection.AddRange(paths);
            data.SetFileDropList(collection);
            data.SetData(PreferredEffect, new MemoryStream(BitConverter.GetBytes(cut ? 2 : 1)));
            return data;
        }

        public static bool IsCut(IDataObject data)
        {
            if (data == null || !data.GetDataPresent(PreferredEffect)) return false;
            object value = data.GetData(PreferredEffect);
            var stream = value as MemoryStream;
            byte[] bytes = stream != null ? stream.ToArray() : value as byte[];
            return bytes != null && bytes.Length >= 4 && (BitConverter.ToInt32(bytes, 0) & 2) != 0;
        }

        public static string[] Paths(IDataObject data)
        {
            return (data != null && data.GetDataPresent(DataFormats.FileDrop)
                ? data.GetData(DataFormats.FileDrop) as string[] : null) ?? new string[0];
        }
    }
}

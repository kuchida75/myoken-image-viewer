using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ZonerInspiredViewer
{
    internal enum BrowserSortField { Name, Number, Created, Modified, Size, Type }

    internal sealed class BrowserSort : IComparer<ImageFileItem>
    {
        private readonly BrowserSortField _field;
        private readonly bool _descending;

        public BrowserSort(BrowserSortField field, bool descending)
        {
            _field = Normalize(field);
            _descending = descending;
        }

        public static BrowserSortField Normalize(BrowserSortField field)
        {
            return Enum.IsDefined(typeof(BrowserSortField), field) ? field : BrowserSortField.Name;
        }

        public int Compare(ImageFileItem left, ImageFileItem right)
        {
            if (left.IsDirectory != right.IsDirectory) return left.IsDirectory ? -1 : 1;
            int result = 0;
            if (!left.IsDirectory)
            {
                switch (_field)
                {
                    case BrowserSortField.Name:
                    case BrowserSortField.Number: result = StrCmpLogicalW(left.Name, right.Name); break;
                    case BrowserSortField.Created: result = left.CreatedUtc.CompareTo(right.CreatedUtc); break;
                    case BrowserSortField.Modified: result = left.LastWriteUtc.CompareTo(right.LastWriteUtc); break;
                    case BrowserSortField.Size: result = left.Length.CompareTo(right.Length); break;
                    case BrowserSortField.Type: result = StringComparer.OrdinalIgnoreCase.Compare(left.Extension, right.Extension); break;
                }
            }
            if (result == 0) result = StrCmpLogicalW(left.Name, right.Name);
            if (result == 0) result = StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name);
            if (result == 0) result = StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path);
            return _descending && !left.IsDirectory ? -Math.Sign(result) : Math.Sign(result);
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int StrCmpLogicalW(string left, string right);
    }
}

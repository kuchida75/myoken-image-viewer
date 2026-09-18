using System;
using System.Collections.Generic;

namespace Myoken.Core
{
    // Portable deterministic filename ordering. Path identity is a separate policy.
    // Numeric runs are compared without parsing integers, including very long runs.
    public sealed class NaturalNameComparer : IComparer<string>
    {
        public static NaturalNameComparer Instance { get; } = new NaturalNameComparer();

        public int Compare(string? left, string? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return -1;
            if (right == null) return 1;
            int i = 0, j = 0;
            while (i < left.Length && j < right.Length)
            {
                if (Digit(left[i]) && Digit(right[j]))
                {
                    int a = i, b = j;
                    while (i < left.Length && Digit(left[i])) i++;
                    while (j < right.Length && Digit(right[j])) j++;
                    int az = a, bz = b;
                    while (az < i && left[az] == '0') az++;
                    while (bz < j && right[bz] == '0') bz++;
                    int n = (i - az).CompareTo(j - bz);
                    if (n != 0) return n;
                    for (int k = 0; k < i - az; k++)
                    {
                        n = left[az + k].CompareTo(right[bz + k]);
                        if (n != 0) return n;
                    }
                    n = (i - a).CompareTo(j - b);
                    if (n != 0) return n;
                }
                else
                {
                    int n = char.ToUpperInvariant(left[i]).CompareTo(char.ToUpperInvariant(right[j]));
                    if (n != 0) return n;
                    i++;
                    j++;
                }
            }
            int remaining = (left.Length - i).CompareTo(right.Length - j);
            return remaining != 0 ? remaining : StringComparer.Ordinal.Compare(left, right);
        }

        private static bool Digit(char c) => c >= '0' && c <= '9';
    }
}

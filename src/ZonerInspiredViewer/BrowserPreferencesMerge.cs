using System;
using System.Collections.Generic;
using System.Linq;

namespace ZonerInspiredViewer
{
    internal static class BrowserPreferencesMerge
    {
        private static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;

        public static BrowserPreferences Copy(BrowserPreferences value)
        {
            return new BrowserPreferences
            {
                FavoriteFolders = Clean(value.FavoriteFolders),
                ExpandedNodes = Clean(value.ExpandedNodes),
                FavoriteDetails = Details(value).Values.Select(item => item.Copy()).ToList()
            };
        }

        public static BrowserPreferences Merge(BrowserPreferences baseline, BrowserPreferences local,
            BrowserPreferences latest)
        {
            var result = new BrowserPreferences
            {
                FavoriteFolders = MergeFavorites(baseline.FavoriteFolders, local.FavoriteFolders, latest.FavoriteFolders),
                ExpandedNodes = MergePaths(baseline.ExpandedNodes, local.ExpandedNodes, latest.ExpandedNodes)
            };
            var oldDetails = Details(baseline);
            var localDetails = Details(local);
            var mergedDetails = Details(latest);
            var oldFolders = new HashSet<string>(Clean(baseline.FavoriteFolders), Paths);
            foreach (string path in Clean(local.FavoriteFolders))
            {
                FavoriteFolderDetails before, after;
                oldDetails.TryGetValue(path, out before);
                localDetails.TryGetValue(path, out after);
                if (!oldFolders.Contains(path) || !SameDetails(before, after))
                {
                    if (after == null) mergedDetails.Remove(path);
                    else mergedDetails[path] = after.Copy();
                }
            }
            result.FavoriteDetails = result.FavoriteFolders.Where(mergedDetails.ContainsKey)
                .Select(path => mergedDetails[path].Copy()).ToList();
            return result;
        }

        private static bool SameDetails(FavoriteFolderDetails left, FavoriteFolderDetails right)
        {
            return String.Equals(left == null ? "" : left.Name ?? "", right == null ? "" : right.Name ?? "", StringComparison.Ordinal)
                && String.Equals(left == null ? "" : left.Description ?? "", right == null ? "" : right.Description ?? "", StringComparison.Ordinal);
        }

        private static List<string> MergePaths(List<string> baseline, List<string> local, List<string> latest)
        {
            var before = new HashSet<string>(Clean(baseline), Paths);
            var after = new HashSet<string>(Clean(local), Paths);
            var result = Clean(latest).Where(path => !before.Contains(path) || after.Contains(path)).ToList();
            var present = new HashSet<string>(result, Paths);
            foreach (string path in Clean(local))
                if (!before.Contains(path) && present.Add(path)) result.Add(path);
            return result;
        }

        private static List<string> MergeFavorites(List<string> baseline, List<string> local, List<string> latest)
        {
            List<string> result = MergePaths(baseline, local, latest);
            List<string> before = Clean(baseline), after = Clean(local);
            var beforeSet = new HashSet<string>(before, Paths);
            var afterSet = new HashSet<string>(after, Paths);
            var expected = before.Where(afterSet.Contains).Concat(after.Where(path => !beforeSet.Contains(path)));
            if (expected.SequenceEqual(after, Paths)) return result;
            // Only an intentional local reorder changes relative order; remote additions keep their slots.
            var present = new HashSet<string>(result, Paths);
            var desired = new Queue<string>(after.Where(present.Contains));
            for (int i = 0; i < result.Count; i++) if (afterSet.Contains(result[i])) result[i] = desired.Dequeue();
            return result;
        }

        private static List<string> Clean(List<string> paths)
        {
            return (paths ?? new List<string>()).Where(path => !String.IsNullOrWhiteSpace(path)).Distinct(Paths).ToList();
        }

        private static Dictionary<string, FavoriteFolderDetails> Details(BrowserPreferences preferences)
        {
            var result = new Dictionary<string, FavoriteFolderDetails>(Paths);
            foreach (FavoriteFolderDetails item in preferences.FavoriteDetails ?? new List<FavoriteFolderDetails>())
                if (item != null && !String.IsNullOrWhiteSpace(item.Path)) result[item.Path] = item;
            return result;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ZonerInspiredViewer
{
    internal sealed class FaceLinkRefresh
    {
        internal FaceCrop[] Faces;
        internal string[] UnavailablePaths;
        internal int RemovedFaces;
    }

    internal static class FaceLinks
    {
        internal static FaceLinkRefresh Refresh(IList<FaceCrop> faces, int workers, CancellationToken token, Action<string> progress)
        {
            var paths = faces.Select(f => f.Source.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var exists = new bool[paths.Length]; int done = 0;
            Parallel.For(0, paths.Length, new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = Math.Max(1, Math.Min(8, workers)) }, i =>
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    FileTransferService.CheckAncestors(paths[i]);
                    // Check access without decoding, hashing, changing or locking the source for editing.
                    using (var input = new FileStream(paths[i], FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) exists[i] = input.Length > 0;
                }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
                catch (ArgumentException) { } catch (NotSupportedException) { }
                int count = Interlocked.Increment(ref done); if (progress != null) progress("Checking original links: " + count + " / " + paths.Length);
            });
            token.ThrowIfCancellationRequested();
            var missing = paths.Where((path, index) => !exists[index]).ToArray(); var excluded = new HashSet<string>(missing, StringComparer.OrdinalIgnoreCase);
            var kept = faces.Where(f => !excluded.Contains(f.Source.Path)).ToArray();
            return new FaceLinkRefresh { Faces = kept, UnavailablePaths = missing, RemovedFaces = faces.Count - kept.Length };
        }
    }
}

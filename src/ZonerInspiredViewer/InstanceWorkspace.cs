using System;
using System.IO;
using System.Collections.Generic;

namespace ZonerInspiredViewer
{
    internal sealed class InstanceWorkspace : IDisposable
    {
        private FileStream _lease;
        public int Number { get; private set; }
        public string SessionPath { get; private set; }

        public static InstanceWorkspace Acquire(string root)
        {
            root = Path.GetFullPath(root);
            string instances = Path.Combine(root, "instances");
            Directory.CreateDirectory(instances);
            for (int number = 1; number <= 256; number++)
            {
                FileStream lease;
                try
                {
                    // The OS releases this exclusive handle even after an unexpected exit.
                    lease = new FileStream(Path.Combine(instances, "window-" + number + ".lock"),
                        FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException ex)
                {
                    int code = ex.HResult & 0xffff;
                    if (code == 32 || code == 33) continue;
                    throw;
                }
                return new InstanceWorkspace
                {
                    _lease = lease, Number = number,
                    SessionPath = number == 1 ? Path.Combine(root, "session.json")
                        : Path.Combine(instances, "session-" + number + ".json")
                };
            }
            throw new IOException("All viewer window slots are in use. Close a window and try again.");
        }

        public void Dispose()
        {
            if (_lease == null) return;
            _lease.Dispose();
            _lease = null;
        }

        internal static IDisposable AcquireMaintenance(string root, int ownWindow)
        {
            var lease = new MaintenanceLease();
            string directory = Path.Combine(Path.GetFullPath(root), "instances");
            ProfileMaintenance.CheckPath(root, directory);
            Directory.CreateDirectory(directory);
            try
            {
                // Holding every other slot also prevents older viewer builds from opening this profile mid-reset.
                for (int number = 1; number <= 256; number++)
                    if (number != ownWindow) lease.Handles.Add(new FileStream(Path.Combine(directory, "window-" + number + ".lock"),
                        FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
                return lease;
            }
            catch (IOException error)
            {
                lease.Dispose();
                throw new IOException("Close other viewer windows using this profile before resetting or clearing its cache.", error);
            }
            catch { lease.Dispose(); throw; }
        }

        private sealed class MaintenanceLease : IDisposable
        {
            internal readonly List<FileStream> Handles = new List<FileStream>();
            public void Dispose() { foreach (var handle in Handles) handle.Dispose(); Handles.Clear(); }
        }
    }
}

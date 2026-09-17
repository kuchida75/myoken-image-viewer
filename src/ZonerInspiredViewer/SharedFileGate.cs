using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace ZonerInspiredViewer
{
    internal sealed class SharedFileGate : IDisposable
    {
        private readonly Mutex _mutex;

        public SharedFileGate(string path)
        {
            string key;
            using (var hash = SHA256.Create())
                key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(
                    Path.GetFullPath(path).ToUpperInvariant()))).Replace("-", "");
            _mutex = new Mutex(false, "Local\\ZonerViewer.Storage." + key);
            try
            {
                if (!_mutex.WaitOne(TimeSpan.FromSeconds(5)))
                    throw new IOException("Another viewer window is still saving. Please try again.");
            }
            catch (AbandonedMutexException) { }
            catch { _mutex.Dispose(); throw; }
        }

        public void Dispose()
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }
}

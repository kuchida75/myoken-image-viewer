using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace ZonerInspiredViewer
{
    internal sealed class SessionStore
    {
        private readonly string _path;
        private readonly string _profileRoot;
        private readonly string _namedRoot;
        private readonly string _browserPath;
        private readonly string _fallbackSessionPath;
        private BrowserPreferences _preferencesBaseline;

        public SessionStore(string path, string sharedRoot = null, string fallbackSessionPath = null)
        {
            _path = path;
            string root = sharedRoot ?? Path.GetDirectoryName(path);
            _profileRoot = Path.GetFullPath(root);
            _namedRoot = Path.Combine(root, "sessions");
            _browserPath = Path.Combine(root, "browser.json");
            _fallbackSessionPath = fallbackSessionPath;
        }

        public SessionState Load()
        {
            try
            {
                string path = File.Exists(_path) ? _path : _fallbackSessionPath;
                if (path == null || !File.Exists(path))
                {
                    return new SessionState();
                }

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                {
                    var serializer = new DataContractJsonSerializer(typeof(SessionState));
                    object result = serializer.ReadObject(stream);
                    return result as SessionState ?? new SessionState();
                }
            }
            catch
            {
                return new SessionState();
            }
        }

        public void Save(SessionState state)
        {
            try
            {
                WriteObject(_path, state, typeof(SessionState));
            }
            catch
            {
            }
        }

        internal void SaveOrThrow(SessionState state)
        {
            WriteObject(_path, state, typeof(SessionState));
        }

        internal void ReplaceBrowserPreferences(BrowserPreferences preferences)
        {
            WriteObject(_browserPath, preferences, typeof(BrowserPreferences));
            _preferencesBaseline = BrowserPreferencesMerge.Copy(preferences);
        }

        internal void ResetProfile(string recoveryFolder, SessionState defaults)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            paths.Add(Path.Combine(_profileRoot, "session.json")); paths.Add(_path); paths.Add(_browserPath);
            if (Directory.Exists(_namedRoot))
            {
                ProfileMaintenance.CheckPath(_profileRoot, _namedRoot);
                foreach (string path in Directory.EnumerateFiles(_namedRoot, "*.json")) paths.Add(path);
            }
            string instances = Path.Combine(_profileRoot, "instances");
            ProfileMaintenance.CheckPath(_profileRoot, instances);
            for (int i = 1; i <= 256; i++) paths.Add(Path.Combine(instances, "session-" + i + ".json"));
            string raw = Path.Combine(recoveryFolder, "original-settings");
            ProfileMaintenance.CheckPath(_profileRoot, raw);
            Directory.CreateDirectory(raw);
            foreach (string path in paths)
            {
                ProfileMaintenance.CheckPath(_profileRoot, path);
                if (!File.Exists(path)) continue;
                string relative = Path.GetFullPath(path).Substring(_profileRoot.TrimEnd(Path.DirectorySeparatorChar).Length + 1);
                string backup = Path.Combine(raw, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)); File.Copy(path, backup, false);
            }
            // No state is removed until every original JSON file has a recovery copy.
            foreach (string path in paths) if (File.Exists(path)) File.Delete(path);
            ReplaceBrowserPreferences(new BrowserPreferences());
            WriteObject(Path.Combine(_profileRoot, "session.json"), defaults, typeof(SessionState));
            SaveOrThrow(defaults);
        }

        internal IList<NamedSessionDocument> CaptureNamedSessions()
        {
            var result = new List<NamedSessionDocument>();
            foreach (NamedSessionInfo info in ListNamedSessions())
            {
                SessionState state = LoadNamed(info.Name);
                if (state != null) result.Add(new NamedSessionDocument { Name = info.Name, SavedUtc = info.SavedUtc, State = state });
            }
            return result;
        }

        public IList<NamedSessionInfo> ListNamedSessions()
        {
            var sessions = new List<NamedSessionInfo>();
            if (!Directory.Exists(_namedRoot))
            {
                return sessions;
            }

            foreach (string path in Directory.EnumerateFiles(_namedRoot, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    NamedSessionDocument document = ReadObject(path, typeof(NamedSessionDocument)) as NamedSessionDocument;
                    if (document != null && !String.IsNullOrWhiteSpace(document.Name) && document.State != null)
                    {
                        sessions.Add(new NamedSessionInfo
                        {
                            Name = document.Name,
                            SavedUtc = document.SavedUtc
                        });
                    }
                }
                catch
                {
                }
            }

            sessions.Sort(
                delegate(NamedSessionInfo left, NamedSessionInfo right)
                {
                    int dateOrder = right.SavedUtc.CompareTo(left.SavedUtc);
                    return dateOrder != 0
                        ? dateOrder
                        : StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
                });
            return sessions;
        }

        public BrowserPreferences LoadBrowserPreferences()
        {
            BrowserPreferences preferences = ReadBrowserPreferences();
            _preferencesBaseline = BrowserPreferencesMerge.Copy(preferences);
            return preferences;
        }

        private BrowserPreferences ReadBrowserPreferences()
        {
            try
            {
                return ReadObject(_browserPath, typeof(BrowserPreferences)) as BrowserPreferences ?? new BrowserPreferences();
            }
            catch { return new BrowserPreferences(); }
        }

        public void SaveBrowserPreferences(BrowserPreferences preferences)
        {
            try
            {
                using (new SharedFileGate(_browserPath))
                {
                    BrowserPreferences merged = _preferencesBaseline == null ? preferences
                        : BrowserPreferencesMerge.Merge(_preferencesBaseline, preferences, ReadBrowserPreferences());
                    WriteObjectCore(_browserPath, merged, typeof(BrowserPreferences));
                    // Keep the baseline equal to what this window actually displayed.
                    _preferencesBaseline = BrowserPreferencesMerge.Copy(preferences);
                }
            }
            catch { }
        }

        public bool NamedSessionExists(string name)
        {
            return File.Exists(GetNamedPath(name));
        }

        public SessionState LoadNamed(string name)
        {
            string path = GetNamedPath(name);
            if (!File.Exists(path))
            {
                return null;
            }

            NamedSessionDocument document = ReadObject(path, typeof(NamedSessionDocument)) as NamedSessionDocument;
            if (document == null || document.State == null
                || !String.Equals(document.Name, NormalizeName(name), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return document.State;
        }

        public void SaveNamed(string name, SessionState state)
        {
            string normalizedName = NormalizeName(name);
            var document = new NamedSessionDocument
            {
                Name = normalizedName,
                SavedUtc = DateTime.UtcNow,
                State = state
            };
            WriteObject(GetNamedPath(normalizedName), document, typeof(NamedSessionDocument));
        }

        public bool DeleteNamed(string name)
        {
            string path = GetNamedPath(name);
            using (new SharedFileGate(path))
            {
                if (!File.Exists(path)) return false;
                File.Delete(path);
                return true;
            }
        }

        private string GetNamedPath(string name)
        {
            string normalizedName = NormalizeName(name).ToUpperInvariant();
            string hash;
            using (SHA1 sha1 = SHA1.Create())
            {
                byte[] bytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(normalizedName));
                var builder = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }

                hash = builder.ToString();
            }

            return Path.Combine(_namedRoot, hash + ".json");
        }

        private static string NormalizeName(string name)
        {
            string normalized = (name ?? "").Trim();
            if (normalized.Length == 0)
            {
                throw new ArgumentException("Session name cannot be empty.", "name");
            }

            if (normalized.Length > 80)
            {
                throw new ArgumentException("Session name must be 80 characters or fewer.", "name");
            }

            return normalized;
        }

        private static object ReadObject(string path, Type type)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                var serializer = new DataContractJsonSerializer(type);
                return serializer.ReadObject(stream);
            }
        }

        internal static void WriteObject(string path, object value, Type type, long maximumBytes = 0)
        {
            using (new SharedFileGate(path)) WriteObjectCore(path, value, type, maximumBytes);
        }

        private static void WriteObjectCore(string path, object value, Type type, long maximumBytes = 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = File.Create(tempPath))
                {
                    var serializer = new DataContractJsonSerializer(type,
                        new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 2000000 });
                    serializer.WriteObject(stream, value);
                    if (maximumBytes > 0 && stream.Length > maximumBytes)
                        throw new InvalidDataException("The backup exceeds the 64 MB size limit.");
                }

                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        if (File.Exists(path)) File.Replace(tempPath, path, null);
                        else File.Move(tempPath, path);
                        break;
                    }
                    catch (IOException ex)
                    {
                        int code = ex.HResult & 0xffff;
                        // Indexers or other readers may briefly deny atomic replacement.
                        if (attempt >= 4 || (code != 32 && code != 33 && code != 1175)) throw;
                        Thread.Sleep(40 * (attempt + 1));
                    }
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch
                    {
                    }
                }
            }
        }
    }
}

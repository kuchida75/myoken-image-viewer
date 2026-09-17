using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Windows.Media.Imaging;

namespace ZonerInspiredViewer
{
    [DataContract]
    internal sealed class SupirRuntimeConfig
    {
        [DataMember] internal string PythonPath;
        [DataMember] internal string RepositoryPath;
        internal bool IsConfigured
        {
            get
            {
                try
                {
                    return !String.IsNullOrWhiteSpace(PythonPath) && Path.IsPathRooted(PythonPath) && File.Exists(PythonPath)
                        && String.Equals(Path.GetFileName(PythonPath), "python.exe", StringComparison.OrdinalIgnoreCase)
                        && !String.IsNullOrWhiteSpace(RepositoryPath) && Path.IsPathRooted(RepositoryPath)
                        && File.Exists(Path.Combine(RepositoryPath, "SUPIR", "util.py"))
                        && File.Exists(Path.Combine(RepositoryPath, "options", "SUPIR_v0.yaml"));
                }
                catch { return false; }
            }
        }
        internal static SupirRuntimeConfig Load(string profile)
        {
            try
            {
                using (var stream = File.OpenRead(Path.Combine(profile, "supir-runtime.json")))
                    return (SupirRuntimeConfig)new DataContractJsonSerializer(typeof(SupirRuntimeConfig)).ReadObject(stream);
            }
            catch { return new SupirRuntimeConfig(); }
        }
        internal void Save(string profile)
        {
            if (!IsConfigured) throw new InvalidOperationException("Select python.exe and a SUPIR repository containing its model configuration.");
            SessionStore.WriteObject(Path.Combine(profile, "supir-runtime.json"), this, typeof(SupirRuntimeConfig));
        }
    }

    internal static class SupirProcess
    {
        internal static string Quote(string value)
        {
            // Windows CommandLineToArgvW rules; never pass through a shell.
            var result = new StringBuilder("\""); int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { result.Append('\\', slashes * 2 + 1).Append(c); slashes = 0; continue; }
                result.Append('\\', slashes).Append(c); slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }

        internal static void Validate(SuperResolutionRequest request)
        {
            if (request.Supir == null || !request.Supir.IsConfigured) throw new InvalidOperationException("SUPIR needs local setup from the AI upscale menu.");
            if (request.Adapter == null || request.Adapter.Name.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException("SUPIR requires an NVIDIA CUDA GPU. Select the RTX under Configure > Performance > AI enhancement GPU.");
            if (request.SoftLimitMb < 12288) throw new InvalidOperationException("SUPIR needs an AI soft memory limit of at least 12288 MB under Configure > Performance.");
            string reason;
            if (!GpuHardware.CanAllocate(request.Adapter, 12288L * 1024 * 1024, request.SoftLimitMb, out reason))
                throw new InvalidOperationException("SUPIR cannot start under current GPU memory pressure: " + reason);
        }

        internal static BitmapSource Run(SuperResolutionRequest request, int width, int height, CancellationToken token, Action<double, string> progress)
        {
            Validate(request);
            string script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "viewer-supir.py");
            if (!File.Exists(script)) throw new FileNotFoundException("The SUPIR adapter script is missing. Reinstall the complete viewer package.");
            string temp = Path.Combine(Path.GetTempPath(), "ZonerViewer-SUPIR-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            string input = Path.Combine(temp, "input.png"), output = Path.Combine(temp, "output.png");
            try
            {
                token.ThrowIfCancellationRequested();
                using (var stream = File.Create(input))
                {
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(request.Source)); encoder.Save(stream);
                }
                var info = new ProcessStartInfo(request.Supir.PythonPath) { UseShellExecute = false, CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = request.Supir.RepositoryPath,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    Arguments = Quote(script) + " --repository " + Quote(request.Supir.RepositoryPath) + " --input " + Quote(input) + " --output " + Quote(output)
                        + " --width " + width + " --height " + height + " --gpu-name " + Quote(request.Adapter.Name) + " --budget-mb " + request.SoftLimitMb };
                info.EnvironmentVariables["HF_HUB_OFFLINE"] = "1"; info.EnvironmentVariables["TRANSFORMERS_OFFLINE"] = "1";
                info.EnvironmentVariables["HF_HUB_DISABLE_TELEMETRY"] = "1";
                var errors = new StringBuilder();
                using (var process = new Process { StartInfo = info })
                {
                    process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data == null) return;
                        lock (errors) { errors.AppendLine(e.Data); if (errors.Length > 6000) errors.Remove(0, errors.Length - 6000); }
                    };
                    process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        double value;
                        if (e.Data != null && e.Data.StartsWith("VIEWER_PROGRESS ") && Double.TryParse(e.Data.Substring(16), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                        {
                            try { progress(0.2 + Math.Max(0, Math.Min(1, value)) * 0.65, "SUPIR diffusion restoration"); }
                            catch (InvalidOperationException) { } // The owner's dispatcher may be closing.
                        }
                    };
                    process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
                    try
                    {
                        while (!process.WaitForExit(100)) token.ThrowIfCancellationRequested();
                        process.WaitForExit(); token.ThrowIfCancellationRequested();
                        if (process.ExitCode != 0) throw new InvalidOperationException("SUPIR failed; no fallback was used. " + errors.ToString());
                    }
                    finally
                    {
                        if (!process.HasExited) { process.Kill(); process.WaitForExit(); }
                    }
                }
                using (var stream = File.OpenRead(output))
                {
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    var frame = decoder.Frames[0];
                    if (frame.PixelWidth != width || frame.PixelHeight != height) throw new InvalidDataException("SUPIR returned an unexpected image size.");
                    frame.Freeze(); return frame;
                }
            }
            finally
            {
                // Only the two files owned by this job are removed, never repository or photo folders.
                try { File.Delete(input); File.Delete(output); Directory.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}

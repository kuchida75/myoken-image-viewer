using System;
using System.IO;
using System.Linq;

namespace ZonerInspiredViewer
{
    internal sealed class UpscaleModel
    {
        internal readonly string Id, Name, File, Hash, Description;
        internal readonly int Tile, Halo, NativeScale, Channels, AdmissionMb;
        internal UpscaleModel(string id, string name, string file, string hash, string description, int tile, int halo, int scale, int channels, int admission)
        { Id = id; Name = name; File = file; Hash = hash; Description = description; Tile = tile; Halo = halo; NativeScale = scale; Channels = channels; AdmissionMb = admission; }
    }

    internal static class UpscaleModels
    {
        internal const string Basic = "basic", RealEsrgan = "realesrgan", RealHat = "realhat", SwinIr = "swinir", Supir = "supir";
        internal static readonly UpscaleModel[] All = {
            new UpscaleModel(Basic, "Basic", "super-resolution-10.onnx", SuperResolution.ModelHash,
                "Small, fast luminance neural network. Conservative detail; native 3x.", 224, 16, 3, 1, 192),
            new UpscaleModel(RealEsrgan, "Real-ESRGAN", "realesrgan-x4plus-128.onnx", "33B076CEBC9CCBA5D9C7EB0854B1048C5819785803D7D435B490079D7464C15B",
                "RealESRGAN_x4plus for general photographs. Native 4x; may smooth or invent fine texture.", 128, 32, 4, 3, 768),
            new UpscaleModel(RealHat, "HAT / Real-HAT", "real-hat-gan-x4-128.onnx", "1338A08B9F0BD39CBD18DAD519DB67E3174D4CB57644116FA922A69A93EBA1F1",
                "Real_HAT_GAN_SRx4, the fidelity-oriented real-world variant. Heavier attention model; native 4x.", 128, 32, 4, 3, 1536),
            new UpscaleModel(SwinIr, "SwinIR-M Real-World \u00d74", "swinir-m-realworld-x4-128.onnx", "0577D742857E6DE7613EDF28C12BED7F517D5CF6CEEACECED20998FF718FF403",
                "Medium SwinIR real-world GAN model for degraded photographs. Native 4x, filtered to the selected scale; may smooth or invent fine texture.", 128, 32, 4, 3, 1536),
            new UpscaleModel(Supir, "SUPIR", null, null,
                "Experimental diffusion restoration. Requires a separately installed SUPIR/CUDA environment and checkpoints. Can change real details.", 0, 0, 0, 0, 12288)
        };

        internal static string Normalize(string id) { return String.IsNullOrEmpty(id) ? Basic : id; }
        internal static bool IsKnown(string id) { return All.Any(m => m.Id == Normalize(id)); }
        internal static UpscaleModel Get(string id)
        {
            var model = All.FirstOrDefault(m => m.Id == Normalize(id));
            if (model == null) throw new ArgumentException("Unknown AI upscale model.");
            return model;
        }
        internal static string DirectoryPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models"); } }
        internal static bool IsInstalled(string id, string modelDirectory, SupirRuntimeConfig supir)
        {
            var model = Get(id);
            return model.Id == Supir ? supir != null && supir.IsConfigured
                : System.IO.File.Exists(Path.Combine(modelDirectory ?? DirectoryPath, model.File));
        }
    }
}

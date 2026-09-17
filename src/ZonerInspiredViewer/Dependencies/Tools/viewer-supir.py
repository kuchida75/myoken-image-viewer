"""Optional local SUPIR adapter. Requires the user's trusted SUPIR installation.

No downloads, LLaVA, face replacement, or original-file writes. Uses the upstream
SUPIR API (https://github.com/Fanghua-Yu/SUPIR), not a substitute upscaler.
"""
import argparse
import os
from pathlib import Path
import sys


def main():
    parser = argparse.ArgumentParser()
    for name in ("repository", "input", "output", "gpu-name"):
        parser.add_argument("--" + name, required=True)
    for name in ("width", "height", "budget-mb"):
        parser.add_argument("--" + name, required=True, type=int)
    args = parser.parse_args()
    if min(args.width, args.height) < 1 or args.width * args.height > 32000000:
        raise ValueError("Invalid output dimensions")
    if args.budget_mb < 12288:
        raise ValueError("SUPIR requires at least 12288 MB AI memory allowance")
    width = max(64, ((args.width + 63) // 64) * 64)
    height = max(64, ((args.height + 63) // 64) * 64)
    if width * height > 4200000:
        raise ValueError("Optional SUPIR preview is limited to 4.2 MP; choose a smaller factor")
    os.environ["HF_HUB_OFFLINE"] = "1"
    os.environ["TRANSFORMERS_OFFLINE"] = "1"
    repository = Path(args.repository).resolve(strict=True)
    os.chdir(repository)
    sys.path.insert(0, str(repository))
    import torch
    from PIL import Image
    from SUPIR.util import create_SUPIR_model, Tensor2PIL, convert_dtype
    import numpy as np

    matches = [i for i in range(torch.cuda.device_count())
               if torch.cuda.get_device_name(i).casefold() == args.gpu_name.casefold()]
    if len(matches) != 1:
        raise RuntimeError("Cannot uniquely match the selected viewer GPU to CUDA; no other GPU was substituted")
    device = torch.device("cuda", matches[0])
    torch.cuda.set_device(device)
    free, total = torch.cuda.mem_get_info(device)
    allowance = args.budget_mb * 1024 * 1024
    if free < 12 * 1024**3:
        raise RuntimeError("Insufficient free CUDA memory for SUPIR")
    torch.cuda.set_per_process_memory_fraction(min(0.9, allowance / total), device)
    print("VIEWER_PROGRESS 0.05", flush=True)
    model = create_SUPIR_model("options/SUPIR_v0.yaml", SUPIR_sign="F").eval().half()
    model.init_tile_vae(encoder_tile_size=512, decoder_tile_size=64)
    model.ae_dtype = convert_dtype("bf16")
    model.model.dtype = convert_dtype("fp16")
    model = model.to(device)
    print("VIEWER_PROGRESS 0.35", flush=True)
    # Use exact target aspect, padded to SUPIR's 64-pixel grid. No implicit 1024px enlargement.
    with Image.open(args.input) as image:
        image = image.convert("RGBA")
        background = Image.new("RGBA", image.size, (128, 128, 128, 255))
        background.alpha_composite(image)
        rgb = background.convert("RGB").resize((args.width, args.height), Image.Resampling.LANCZOS)
        data = np.asarray(rgb).astype(np.float32) / 127.5 - 1
    data = np.pad(data, ((0, height - args.height), (0, width - args.width), (0, 0)), mode="edge")
    tensor = torch.from_numpy(data).permute(2, 0, 1).unsqueeze(0).to(device)
    with torch.inference_mode():
        samples = model.batchify_sample(tensor, [""], num_steps=20, restoration_scale=-1,
            s_churn=5, s_noise=1.01, cfg_scale=4.0, control_scale=1.0, seed=1234, num_samples=1,
            p_p="natural photograph, faithful detail, original colors", n_p="artificial texture, oversharpening, altered face",
            color_fix_type="Wavelet", use_linear_CFG=True, use_linear_control_scale=False,
            cfg_scale_start=1.0, control_scale_start=0.0)
        Tensor2PIL(samples[0], height, width).crop((0, 0, args.width, args.height)).save(args.output, format="PNG")
    print("VIEWER_PROGRESS 1", flush=True)


if __name__ == "__main__":
    main()

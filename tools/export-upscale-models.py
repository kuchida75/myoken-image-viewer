"""Offline export only. Runtime needs neither Python nor PyTorch for these models.

Python 3.12: torch==2.6.0+cpu torchvision==0.21.0+cpu spandrel==0.4.1
onnx==1.17.0 numpy==1.26.4. Download/provenance: upscale-models.json.
"""
import argparse
import hashlib
import json
from pathlib import Path

import numpy as np
import onnx
import torch
from spandrel import ModelLoader


def main():
    entries = json.loads(Path(__file__).with_name("upscale-models.json").read_text())
    parser = argparse.ArgumentParser()
    parser.add_argument("--downloads", type=Path, default=Path("artifacts/dependency-downloads"))
    parser.add_argument("--output", type=Path, default=Path("src/ZonerInspiredViewer/Dependencies/Models"))
    parser.add_argument("--model", action="append", choices=[item["Id"] for item in entries],
                        help="Export only this model; repeat to select several. Defaults to all.")
    args = parser.parse_args()
    torch.set_num_threads(8)
    torch.manual_seed(0)
    args.output.mkdir(parents=True, exist_ok=True)
    for item in entries:
        if args.model and item["Id"] not in args.model:
            continue
        checkpoint = args.downloads / item["Checkpoint"]
        if hashlib.sha256(checkpoint.read_bytes()).hexdigest().upper() != item["CheckpointSHA256"]:
            raise ValueError("Checkpoint checksum mismatch: " + str(checkpoint))
        weights = torch.load(checkpoint, map_location="cpu", weights_only=True)
        if "CheckpointKey" in item:
            weights = weights[item["CheckpointKey"]]
        descriptor = ModelLoader().load_from_state_dict(weights)
        if "Architecture" in item and descriptor.architecture.id != item["Architecture"]:
            raise ValueError("Unexpected model architecture: " + descriptor.architecture.id)
        if descriptor.scale != 4 or descriptor.input_channels != 3 or descriptor.output_channels != 3:
            raise ValueError("Expected a 4x RGB image model")
        model = descriptor.model.eval()
        sample = torch.rand(1, 3, 128, 128)
        destination = args.output / item["File"]
        with torch.inference_mode():
            expected = model(sample)
            torch.onnx.export(model, sample, destination, opset_version=17, input_names=["image"],
                              output_names=["upscaled"], do_constant_folding=True, dynamo=False)
        onnx.checker.check_model(str(destination))
        sample.numpy().astype("<f4").tofile(args.downloads / (item["Id"] + "-input.f32"))
        expected.numpy().astype("<f4").tofile(args.downloads / (item["Id"] + "-expected.f32"))
        print(item["Id"], descriptor.architecture.id, destination.stat().st_size,
              hashlib.sha256(destination.read_bytes()).hexdigest().upper(), flush=True)


if __name__ == "__main__":
    main()

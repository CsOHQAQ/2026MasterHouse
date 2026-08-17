"""Run the official ZhengPeng7/BiRefNet model and write a transparent PNG.

The preprocessing and output selection follow the model card:
1024x1024 resize, ImageNet normalization, final prediction + sigmoid,
then bilinear resize back to the source image size.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path


MODEL_ID = "ZhengPeng7/BiRefNet"
# Pin remote custom code and weights so the Unity art pipeline is reproducible.
MODEL_REVISION = "b7d7f31fed203ab364ac756d62053ee467502434"
IMAGE_SIZE = (1024, 1024)


def report(event: str, **values: object) -> None:
    print(json.dumps({"event": event, **values}, ensure_ascii=False), flush=True)


def select_device(torch: object, requested: str) -> str:
    if requested == "cuda":
        if not torch.cuda.is_available():
            raise RuntimeError("指定了 CUDA，但当前 PyTorch 无法访问 NVIDIA GPU。请改用 auto/cpu 或重新安装 CUDA 环境。")
        return "cuda"
    if requested == "cpu":
        return "cpu"
    return "cuda" if torch.cuda.is_available() else "cpu"


def run(input_path: Path, output_path: Path, mode: str, requested_device: str) -> None:
    try:
        import torch
        from PIL import Image, ImageChops, ImageOps
        from torchvision import transforms
        from transformers import AutoModelForImageSegmentation
    except ImportError as exc:
        raise RuntimeError("BiRefNet Python 环境不完整，请先在 Unity 工具窗口点击“安装 BiRefNet 环境”。") from exc

    if not input_path.is_file():
        raise FileNotFoundError(f"找不到源图：{input_path}")

    device = select_device(torch, requested_device)
    dtype = torch.float16 if device == "cuda" else torch.float32
    report("load_model", model=MODEL_ID, revision=MODEL_REVISION, device=device)
    torch.set_float32_matmul_precision("high")
    model = AutoModelForImageSegmentation.from_pretrained(
        MODEL_ID,
        revision=MODEL_REVISION,
        trust_remote_code=True,
    )
    model.to(device=device)
    model.eval()
    if dtype == torch.float16:
        model.half()

    original = ImageOps.exif_transpose(Image.open(input_path)).convert("RGBA")
    model_image = original.convert("RGB")
    transform = transforms.Compose(
        [
            transforms.Resize(IMAGE_SIZE),
            transforms.ToTensor(),
            transforms.Normalize([0.485, 0.456, 0.406], [0.229, 0.224, 0.225]),
        ]
    )
    inputs = transform(model_image).unsqueeze(0).to(device=device, dtype=dtype)

    report("inference", width=original.width, height=original.height)
    with torch.inference_mode():
        prediction = model(inputs)[-1].sigmoid().float().cpu()[0].squeeze()

    mask = transforms.ToPILImage()(prediction).resize(original.size, Image.Resampling.BILINEAR)
    mask = ImageChops.multiply(original.getchannel("A"), mask)
    if mode == "mask":
        result = Image.new("RGBA", original.size, (255, 255, 255, 255))
        result.putalpha(mask)
    else:
        result = original.copy()
        result.putalpha(mask)

    output_path.parent.mkdir(parents=True, exist_ok=True)
    result.save(output_path, format="PNG", optimize=True)
    report("complete", output=str(output_path), mode=mode, device=device)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Erase an image background with ZhengPeng7/BiRefNet.")
    parser.add_argument("--input", type=Path, required=True, help="Source image path")
    parser.add_argument("--output", type=Path, required=True, help="Output PNG path")
    parser.add_argument("--mode", choices=("cutout", "mask"), default="cutout")
    parser.add_argument("--device", choices=("auto", "cuda", "cpu"), default="auto")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    try:
        run(args.input, args.output, args.mode, args.device)
        return 0
    except Exception as exc:
        report("error", type=type(exc).__name__, message=str(exc))
        raise


if __name__ == "__main__":
    raise SystemExit(main())

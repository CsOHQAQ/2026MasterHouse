"""Build opaque SkyOnly frames for Unity from fixed-composition SkyCycle frames.

BiRefNet supplies one stable foreground matte. The building region is expanded,
reconstructed horizontally from neighboring background pixels, blurred inside
the reconstruction only, and feathered back into the untouched source frame.
The output is deliberately opaque: Unity uses SkyOnly to cover the old exterior
building before drawing the separate HouseCycle layer on top.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageOps


SUPPORTED_EXTENSIONS = {".jpg", ".jpeg", ".png", ".webp"}


def report(event: str, **values: object) -> None:
    print(json.dumps({"event": event, **values}, ensure_ascii=False), flush=True)


def reconstruct_background(
    source: np.ndarray,
    foreground_alpha: np.ndarray,
    dilation_pixels: int,
    blur_sigma: float,
    feather_sigma: float,
) -> np.ndarray:
    hard_mask = (foreground_alpha > 8).astype(np.uint8) * 255
    radius = max(0, dilation_pixels)
    if radius > 0:
        size = radius * 2 + 1
        kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (size, size))
        hard_mask = cv2.dilate(hard_mask, kernel)

    height, width = hard_mask.shape
    x_coordinates = np.arange(width)
    reconstructed = source.astype(np.float32)
    for y in range(height):
        valid = hard_mask[y] == 0
        if np.count_nonzero(valid) < 2:
            continue
        valid_x = x_coordinates[valid]
        for channel in range(3):
            reconstructed[y, :, channel] = np.interp(
                x_coordinates,
                valid_x,
                source[y, valid, channel],
            )

    if blur_sigma > 0:
        reconstructed = cv2.GaussianBlur(reconstructed, (0, 0), blur_sigma)
    matte = hard_mask.astype(np.float32) / 255.0
    if feather_sigma > 0:
        matte = cv2.GaussianBlur(matte, (0, 0), feather_sigma)
    matte = matte[..., None]
    return np.clip(source * (1.0 - matte) + reconstructed * matte, 0, 255).astype(np.uint8)


def run(
    input_dir: Path,
    foreground_mask_path: Path,
    output_dir: Path,
    output_prefix: str,
    dilation_pixels: int,
    blur_sigma: float,
    feather_sigma: float,
) -> None:
    if not input_dir.is_dir():
        raise NotADirectoryError(input_dir)
    if not foreground_mask_path.is_file():
        raise FileNotFoundError(foreground_mask_path)

    with Image.open(foreground_mask_path) as matte_image:
        foreground_alpha = np.asarray(matte_image.convert("RGBA").getchannel("A"))

    sources = sorted(
        path for path in input_dir.iterdir()
        if path.is_file() and path.suffix.lower() in SUPPORTED_EXTENSIONS
    )
    if not sources:
        raise RuntimeError(f"目录中没有可处理图片：{input_dir}")

    output_dir.mkdir(parents=True, exist_ok=True)
    for index, source_path in enumerate(sources):
        with Image.open(source_path) as source_image:
            source = np.asarray(ImageOps.exif_transpose(source_image).convert("RGB"))
        if source.shape[:2] != foreground_alpha.shape:
            raise ValueError(
                f"尺寸不一致：{source_path.name}={source.shape[1]}x{source.shape[0]}，"
                f"遮罩={foreground_alpha.shape[1]}x{foreground_alpha.shape[0]}"
            )

        result = reconstruct_background(
            source,
            foreground_alpha,
            dilation_pixels,
            blur_sigma,
            feather_sigma,
        )
        output_path = output_dir / f"{output_prefix}{index:03d}.png"
        Image.fromarray(result, "RGB").save(output_path, format="PNG", optimize=True)
        report("frame", index=index + 1, total=len(sources), output=output_path.name)

    report("complete", count=len(sources), output_dir=str(output_dir))


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Build opaque Unity SkyOnly sequence frames.")
    parser.add_argument("--input-dir", type=Path, required=True)
    parser.add_argument("--foreground-mask", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--output-prefix", default="skyonly_")
    parser.add_argument("--dilation", type=int, default=8, help="Mask expansion in pixels")
    parser.add_argument("--blur", type=float, default=5.0, help="Reconstruction blur sigma")
    parser.add_argument("--feather", type=float, default=2.0, help="Boundary feather sigma")
    return parser.parse_args()


if __name__ == "__main__":
    args = parse_args()
    run(
        args.input_dir,
        args.foreground_mask,
        args.output_dir,
        args.output_prefix,
        args.dilation,
        args.blur,
        args.feather,
    )

"""Apply one inverted foreground Alpha to a fixed-composition image sequence.

This avoids frame-to-frame segmentation jitter: BiRefNet is run once on a clean
reference frame, then the same matte is applied to every frame in the sequence.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from PIL import Image, ImageChops, ImageOps


SUPPORTED_EXTENSIONS = {".jpg", ".jpeg", ".png", ".webp"}


def report(event: str, **values: object) -> None:
    print(json.dumps({"event": event, **values}, ensure_ascii=False), flush=True)


def run(
    input_dir: Path,
    foreground_mask_path: Path,
    output_dir: Path,
    output_prefix: str | None = None,
) -> None:
    if not input_dir.is_dir():
        raise NotADirectoryError(input_dir)
    if not foreground_mask_path.is_file():
        raise FileNotFoundError(foreground_mask_path)

    with Image.open(foreground_mask_path) as matte_image:
        foreground_alpha = matte_image.convert("RGBA").getchannel("A")
    background_alpha = ImageOps.invert(foreground_alpha)

    sources = sorted(
        path for path in input_dir.iterdir()
        if path.is_file() and path.suffix.lower() in SUPPORTED_EXTENSIONS
    )
    if not sources:
        raise RuntimeError(f"目录中没有可处理图片：{input_dir}")

    output_dir.mkdir(parents=True, exist_ok=True)
    for index, source_path in enumerate(sources):
        with Image.open(source_path) as source_image:
            source = ImageOps.exif_transpose(source_image).convert("RGBA")
        if source.size != background_alpha.size:
            raise ValueError(
                f"尺寸不一致：{source_path.name}={source.size}，遮罩={background_alpha.size}"
            )

        alpha = ImageChops.multiply(source.getchannel("A"), background_alpha)
        source.putalpha(alpha)
        output_stem = f"{output_prefix}{index:03d}" if output_prefix is not None else source_path.stem
        output_path = output_dir / f"{output_stem}.png"
        source.save(output_path, format="PNG", optimize=True)
        report(
            "frame",
            index=index + 1,
            total=len(sources),
            input=source_path.name,
            output=output_path.name,
        )

    report("complete", count=len(sources), output_dir=str(output_dir))


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Apply an inverted BiRefNet matte to an image sequence.")
    parser.add_argument("--input-dir", type=Path, required=True)
    parser.add_argument("--foreground-mask", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--output-prefix", help="Optional stable frame prefix, e.g. skyonly_")
    return parser.parse_args()


if __name__ == "__main__":
    args = parse_args()
    run(args.input_dir, args.foreground_mask, args.output_dir, args.output_prefix)

#!/usr/bin/env python3
"""Remove white-matte fringes from the currently edited furniture sprites.

The script keeps every 1024x1024 canvas and the visible object scale unchanged.
It only adjusts the RGBA pixels around the existing alpha boundary, then renders a
dark-background contact sheet so halo regressions are easy to spot in one image.
"""

from __future__ import annotations

import argparse
import json
import subprocess
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage


FURNITURE_GLOB = "Assets/PC ui/Scene/furniture/*.png"
CANVAS_SIZE = (1024, 1024)
PREVIEW_BACKGROUND = (43, 37, 72, 255)


def edited_furniture(repo: Path) -> list[Path]:
    output = subprocess.check_output(
        [
            "git",
            "-c",
            "core.quotepath=false",
            "diff",
            "--name-only",
            "--",
            FURNITURE_GLOB,
        ],
        cwd=repo,
        text=True,
        encoding="utf-8",
    )
    return [repo / line.strip() for line in output.splitlines() if line.strip()]


def clean_rgba(image: Image.Image) -> Image.Image:
    rgba = np.asarray(image.convert("RGBA")).copy()
    rgb = rgba[..., :3]
    alpha = rgba[..., 3].astype(np.float32) / 255.0

    # Drop the very faint white-sheet residue, then pull the antialiased edge
    # inward by about one source pixel.  This is small enough to retain leaf tips
    # and thin hanging cords on a 1024 sprite canvas.
    alpha = np.clip((alpha - 0.045) / 0.935, 0.0, 1.0)
    alpha = cv2.erode(alpha, np.ones((3, 3), np.uint8), iterations=1)
    alpha = cv2.GaussianBlur(alpha, (0, 0), 0.42)
    alpha = np.clip((alpha - 0.018) / 0.965, 0.0, 1.0)

    visible = alpha > 0.002
    trusted = alpha >= 0.82
    if trusted.any():
        # Soft edge RGB often still contains the old white sheet.  Borrow color
        # from the nearest confidently opaque furniture pixel, which is the usual
        # matte-decontamination operation and does not alter interior artwork.
        _, nearest = ndimage.distance_transform_edt(~trusted, return_indices=True)
        edge = visible & ~trusted
        nearest_rgb = rgb[nearest[0], nearest[1]]
        rgb[edge] = nearest_rgb[edge]

    alpha_u8 = np.round(alpha * 255.0).astype(np.uint8)
    rgb[alpha_u8 == 0] = 0
    return Image.fromarray(np.dstack((rgb, alpha_u8)), mode="RGBA")


def representative_color(image: Image.Image) -> str:
    rgba = np.asarray(image.convert("RGBA"))
    rgb = rgba[..., :3].astype(np.float32)
    alpha = rgba[..., 3].astype(np.float32) / 255.0
    mask = alpha >= 0.72
    if not mask.any():
        mask = alpha > 0.05
    pixels = rgb[mask]

    # Ignore near-white highlights and near-black line art when enough painted
    # pixels remain.  Median in HSV-like saturation weighting gives a stable UI
    # swatch that matches the actual furniture body rather than outlines.
    maximum = pixels.max(axis=1)
    minimum = pixels.min(axis=1)
    saturation = (maximum - minimum) / np.maximum(maximum, 1.0)
    useful = (maximum < 242.0) & (maximum > 35.0) & (saturation > 0.08)
    if useful.sum() >= 256:
        pixels = pixels[useful]
    color = np.median(pixels, axis=0).round().astype(np.uint8)
    return "#" + "".join(f"{channel:02X}" for channel in color)


def render_contact(
    before: list[tuple[str, Image.Image]],
    after: list[tuple[str, Image.Image]],
    output: Path,
) -> None:
    cell_w, cell_h = 210, 210
    columns = 8
    rows = (len(before) + columns - 1) // columns
    sheet = Image.new("RGB", (columns * cell_w, rows * cell_h * 2), (25, 22, 42))
    draw = ImageDraw.Draw(sheet)
    for section, items in enumerate((before, after)):
        y_section = section * rows * cell_h
        draw.text((8, y_section + 5), "BEFORE" if section == 0 else "AFTER", fill=(255, 222, 128))
        for index, (name, image) in enumerate(items):
            col, row = index % columns, index // columns
            x = col * cell_w
            y = y_section + row * cell_h
            tile = Image.new("RGBA", (cell_w, cell_h), PREVIEW_BACKGROUND)
            preview = image.copy().convert("RGBA")
            preview.thumbnail((188, 168), Image.Resampling.LANCZOS)
            tile.alpha_composite(preview, ((cell_w - preview.width) // 2, 176 - preview.height))
            sheet.paste(tile.convert("RGB"), (x, y))
            label = name if len(name) <= 23 else name[:21] + "..."
            draw.text((x + 5, y + 183), label, fill=(232, 228, 245))
    output.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(output, optimize=True)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", type=Path, required=True)
    parser.add_argument("--preview", type=Path, required=True)
    parser.add_argument("--swatches", type=Path, required=True)
    parser.add_argument("--backup-dir", type=Path)
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    repo = args.repo.resolve()
    paths = edited_furniture(repo)
    if not paths:
        raise RuntimeError("No edited furniture PNG files were found")

    before: list[tuple[str, Image.Image]] = []
    after: list[tuple[str, Image.Image]] = []
    swatches: dict[str, str] = {}
    for path in paths:
        source = Image.open(path).convert("RGBA")
        if source.size != CANVAS_SIZE:
            raise RuntimeError(f"Unexpected canvas size for {path}: {source.size}")
        cleaned = clean_rgba(source)
        before.append((path.name, source.copy()))
        after.append((path.name, cleaned.copy()))
        swatches[path.as_posix().replace(repo.as_posix() + "/", "")] = representative_color(cleaned)

        if args.apply:
            if args.backup_dir is not None:
                relative = path.relative_to(repo)
                backup = args.backup_dir / relative
                backup.parent.mkdir(parents=True, exist_ok=True)
                source.save(backup, optimize=True)
            cleaned.save(path, optimize=True)

    render_contact(before, after, args.preview)
    args.swatches.parent.mkdir(parents=True, exist_ok=True)
    args.swatches.write_text(json.dumps(swatches, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Processed {len(paths)} sprites; apply={args.apply}")
    print(f"Dark preview: {args.preview}")
    print(f"Swatches: {args.swatches}")


if __name__ == "__main__":
    main()

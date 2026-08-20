#!/usr/bin/env python3
"""
从临时截图文件批量处理床图：rembg 去背景 → 清理边缘 → 放 1024x1024 画布 → 输出 bed_01.png …
"""
import os
import sys
import numpy as np
import cv2
from PIL import Image, ImageDraw
from rembg import remove
from scipy import ndimage

CANVAS_SIZE = (1024, 1024)
CONTENT_SIZE = (900, 900)
PREVIEW_BG = (43, 37, 72, 255)


def clean_rgba(image: Image.Image) -> Image.Image:
    rgba = np.asarray(image.convert("RGBA")).copy()
    rgb = rgba[..., :3]
    alpha = rgba[..., 3].astype(np.float32) / 255.0

    alpha = np.clip((alpha - 0.045) / 0.935, 0.0, 1.0)
    alpha = cv2.erode(alpha, np.ones((3, 3), np.uint8), iterations=1)
    alpha = cv2.GaussianBlur(alpha, (0, 0), 0.42)
    alpha = np.clip((alpha - 0.018) / 0.965, 0.0, 1.0)

    visible = alpha > 0.002
    trusted = alpha >= 0.82
    if trusted.any():
        _, nearest = ndimage.distance_transform_edt(~trusted, return_indices=True)
        edge = visible & ~trusted
        nearest_rgb = rgb[nearest[0], nearest[1]]
        rgb[edge] = nearest_rgb[edge]

    alpha_u8 = np.round(alpha * 255.0).astype(np.uint8)
    rgb[alpha_u8 == 0] = 0
    return Image.fromarray(np.dstack((rgb, alpha_u8)), mode="RGBA")


def place_on_canvas(source: Image.Image) -> Image.Image:
    alpha = source.getchannel("A")
    box = alpha.getbbox()
    if box is None:
        raise RuntimeError("Empty matte")
    content = source.crop(box)
    scale = min(CONTENT_SIZE[0] / content.width, CONTENT_SIZE[1] / content.height)
    size = (max(1, round(content.width * scale)), max(1, round(content.height * scale)))
    resized = content.resize(size, Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", CANVAS_SIZE, (0, 0, 0, 0))
    position = ((CANVAS_SIZE[0] - size[0]) // 2, (CANVAS_SIZE[1] - size[1]) // 2)
    canvas.alpha_composite(resized, position)
    return canvas


def representative_color(image: Image.Image) -> str:
    rgba = np.asarray(image.convert("RGBA"))
    rgb = rgba[..., :3].astype(np.float32)
    alpha = rgba[..., 3].astype(np.float32) / 255.0
    mask = alpha >= 0.72
    if not mask.any():
        mask = alpha > 0.05
    pixels = rgb[mask]
    maximum = pixels.max(axis=1)
    minimum = pixels.min(axis=1)
    saturation = (maximum - minimum) / np.maximum(maximum, 1.0)
    useful = (maximum < 242.0) & (maximum > 35.0) & (saturation > 0.08)
    if useful.sum() >= 256:
        pixels = pixels[useful]
    color = np.median(pixels, axis=0).round().astype(np.uint8)
    return "#" + "".join(f"{channel:02X}" for channel in color)


def make_contact(items: list, path: str) -> None:
    cell_w, cell_h = 220, 240
    columns = 8
    rows = (len(items) + columns - 1) // columns
    sheet = Image.new("RGB", (columns * cell_w, rows * cell_h), (25, 22, 42))
    draw = ImageDraw.Draw(sheet)
    for index, (name, image, color) in enumerate(items):
        col, row = index % columns, index // columns
        x, y = col * cell_w, row * cell_h
        tile = Image.new("RGBA", (cell_w, cell_h - 20), PREVIEW_BG)
        preview = image.copy().convert("RGBA")
        preview.thumbnail((200, 178), Image.Resampling.LANCZOS)
        tile.alpha_composite(preview, ((cell_w - preview.width) // 2, cell_h - 20 - preview.height - 5))
        sheet.paste(tile.convert("RGB"), (x, y))
        draw.text((x + 5, y + cell_h - 18), f"{name} {color}", fill=(232, 228, 245))
    sheet.save(path, optimize=True)


def main():
    temp_dir = r"C:\Users\xinxinhe\AppData\Local\Temp"
    out_dir = r"C:\Users\xinxinhe\Documents\2026MasterHouse\Assets\PC ui\Scene\furniture"
    contact_path = r"C:\Users\xinxinhe\Documents\2026MasterHouse\Tools\BiRefNet\beds_preview.png"

    # 取本次会话发的床图（按时间排序，前缀 screenshot-20260820-02）
    candidates = sorted([
        os.path.join(temp_dir, f)
        for f in os.listdir(temp_dir)
        if f.startswith("screenshot-20260820-02") and f.endswith(".png")
    ])
    print(f"找到 {len(candidates)} 个候选文件")

    results = []
    for i, src_path in enumerate(candidates, start=1):
        name = f"bed_{i:02d}.png"
        out_path = os.path.join(out_dir, name)
        print(f"[{i}/{len(candidates)}] 处理 {os.path.basename(src_path)} → {name}", flush=True)

        src = Image.open(src_path).convert("RGB")
        cutout = remove(src)                  # rembg 去背景 → RGBA
        cleaned = clean_rgba(cutout)          # 清理白边
        canvas = place_on_canvas(cleaned)     # 放 1024×1024 画布
        canvas.save(out_path, optimize=True)
        color = representative_color(canvas)
        results.append((name, canvas, color))
        print(f"    色值: {color}")

    make_contact(results, contact_path)
    print(f"\n完成！共处理 {len(results)} 张")
    print(f"预览图: {contact_path}")
    print(f"输出目录: {out_dir}")


if __name__ == "__main__":
    main()

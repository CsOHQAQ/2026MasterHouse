#!/usr/bin/env python3
"""Cut furniture from the supplied sheets and place it on uniform sprite canvases.

The existing Unity PNG names and ``.meta`` files are kept, but their old alpha mattes
are never reused.  ZhengPeng7/BiRefNet produces a fresh matte from the supplied art;
all results share one canvas size while the configured Unity display size continues to
control the relative size of each furniture item in the room.
"""

from __future__ import annotations

import argparse
from dataclasses import dataclass
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw


@dataclass(frozen=True)
class RowSpec:
    y0: int
    y1: int
    count: int
    x0: int
    x1: int
    boundaries: tuple[int, ...] | None = None
    cells: tuple[tuple[int, int], ...] | None = None


@dataclass(frozen=True)
class SheetSpec:
    source: str
    rows: tuple[RowSpec, ...]
    targets: tuple[str | None, ...]


SHEETS = (
    SheetSpec(
        "codex-clipboard-096d22b0-343f-4ccf-95e5-ab59ab5c0d37.png",
        (RowSpec(0, 363, 5, 0, 2164), RowSpec(363, 727, 5, 0, 2164)),
        (
            "round_rug_06 1.png", "round_rug_07 1.png", "round_rug_08 1.png",
            "round_rug_09 1.png", "round_rug_10 1.png", "round_rug_01 1.png",
            "round_rug_02 1.png", "round_rug_03 1.png", "round_rug_04 1.png",
            "round_rug_05 1.png",
        ),
    ),
    SheetSpec(
        "codex-clipboard-1ef8fed5-514a-49cd-9714-2a06387bf094.png",
        (RowSpec(0, 2160, 5, 0, 3840),),
        ("01_月牙鱼缸 1.png", None, "04_月牙鱼缸 1.png", "05_月牙鱼缸 1.png", "07_月牙鱼缸 1.png"),
    ),
    SheetSpec(
        "codex-clipboard-60252fc9-57aa-4ee0-8e49-2dfe1afee0f5.png",
        (RowSpec(0, 1120, 5, 0, 3840), RowSpec(1120, 2160, 3, 0, 2580)),
        (
            "02_圆桌 1.png", "03_圆桌 1.png", "04_圆桌 2.png", "05_圆桌 1.png",
            "06_圆桌 1.png", "07_圆桌 1.png", "08_圆桌 1.png", "01_圆桌 3.png",
        ),
    ),
    SheetSpec(
        "codex-clipboard-0b833793-d8c6-4814-b8e3-438a8be30cc9.png",
        (
            RowSpec(
                0,
                2160,
                9,
                0,
                3840,
                (0, 573, 981, 1410, 1836, 2222, 2624, 3028, 3401, 3840),
            ),
        ),
        tuple(f"orb_decoration_{i:02d} 1.png" for i in range(1, 10)),
    ),
    SheetSpec(
        "codex-clipboard-a0b0bcd8-3154-4a3d-b322-58402ab46f09.png",
        (RowSpec(0, 2160, 5, 0, 3840),),
        (
            "potted_plant_01 1.png", "potted_plant_02 1.png", "potted_plant_03 1.png",
            "potted_plant_04 1.png", "potted_plant_06 1.png",
        ),
    ),
    SheetSpec(
        "codex-clipboard-ade7ceaf-4d05-4f6a-a816-a59905450b61.png",
        (RowSpec(0, 1260, 5, 0, 3400), RowSpec(1040, 2160, 5, 320, 3650)),
        (
            "hanging_plant_30 1.png", "hanging_plant_21 1.png", "hanging_plant_22 1.png",
            "hanging_plant_23 1.png", "hanging_plant_24 1.png", "hanging_plant_17 1.png",
            "hanging_plant_18 1.png", "hanging_plant_30 2.png", "hanging_plant_31 1.png",
            "hanging_plant_32 1.png",
        ),
    ),
    SheetSpec(
        "codex-clipboard-d4d36ac0-e917-4a1c-b24f-ebecf908b005.png",
        (RowSpec(0, 1080, 6, 0, 3840), RowSpec(1080, 2160, 7, 0, 3840)),
        (
            "hanging_plant_01 1.png", "hanging_plant_03 1.png", "hanging_plant_06 1.png",
            "hanging_plant_07 1.png", "hanging_plant_08 1.png", "hanging_plant_09 1.png",
            "hanging_plant_36 1.png", "hanging_plant_11 1.png", "hanging_plant_12 1.png",
            "hanging_plant_13 1.png", "hanging_plant_14 1.png", "hanging_plant_15 1.png",
            "hanging_plant_16 1.png",
        ),
    ),
    SheetSpec(
        "codex-clipboard-8daf54fc-54ee-4c1a-9c99-0d97c289ec05.png",
        (
            RowSpec(
                0,
                2160,
                7,
                0,
                3840,
                cells=((0, 622), (622, 1213), (1213, 1810), (1810, 2324), (2324, 2872), (2940, 3420), (3420, 3840)),
            ),
        ),
        (
            "monstera_01 1.png", "monstera_02 1.png", "monstera_03 1.png",
            "monstera_04 1.png", "monstera_05 1.png", "monstera_06 1.png",
            "monstera_08 1.png",
        ),
    ),
    SheetSpec(
        "codex-clipboard-f249a72e-adf9-48fa-abc7-61733d657940.png",
        (RowSpec(0, 1080, 3, 0, 2700), RowSpec(1080, 2160, 5, 0, 3840)),
        (
            "06_猫耳懒人沙发 1.png", "07_猫耳懒人沙发 1.png", "08_猫耳懒人沙发 1.png",
            "01_猫耳懒人沙发 2.png", "02_猫耳懒人沙发 1.png", "03_猫耳懒人沙发 1.png",
            "04_猫耳懒人沙发 1.png", "05_猫耳懒人沙发 2.png",
        ),
    ),
)

MODEL_ID = "ZhengPeng7/BiRefNet"
MODEL_REVISION = "b7d7f31fed203ab364ac756d62053ee467502434"
MODEL_SIZE = (1024, 1024)
CANVAS_SIZE = (1024, 1024)
CONTENT_SIZE = (900, 900)


def background_color(image: Image.Image) -> np.ndarray:
    rgb = np.asarray(image.convert("RGB"))
    samples = np.concatenate((rgb[:32].reshape(-1, 3), rgb[:, :16].reshape(-1, 3)), axis=0)
    return np.median(samples, axis=0).astype(np.int16)


def foreground_difference(rgb: np.ndarray, bg: np.ndarray) -> np.ndarray:
    return np.max(np.abs(rgb.astype(np.int16) - bg), axis=2)


def split_row(image: Image.Image, bg: np.ndarray, spec: RowSpec) -> list[tuple[int, int, int, int]]:
    """Split at low-ink valleys near the expected inter-item boundaries."""
    if spec.cells is not None:
        if len(spec.cells) != spec.count:
            raise ValueError("Manual cell count must equal item count")
        return [(left, spec.y0, right, spec.y1) for left, right in spec.cells]
    if spec.boundaries is not None:
        if len(spec.boundaries) != spec.count + 1:
            raise ValueError("Manual boundary count must equal item count + 1")
        return [
            (spec.boundaries[index], spec.y0, spec.boundaries[index + 1], spec.y1)
            for index in range(spec.count)
        ]
    row = np.asarray(image.crop((spec.x0, spec.y0, spec.x1, spec.y1)).convert("RGB"))
    mask = foreground_difference(row, bg) > 9
    projection = mask.sum(axis=0).astype(np.float64)
    window = max(15, int((spec.x1 - spec.x0) / spec.count * 0.035))
    if window % 2 == 0:
        window += 1
    smooth = np.convolve(projection, np.ones(window), mode="same")
    width = spec.x1 - spec.x0
    cell = width / spec.count
    boundaries = [spec.x0]
    for index in range(1, spec.count):
        expected = index * cell
        radius = int(cell * 0.24)
        left = max(boundaries[-1] - spec.x0 + 32, int(expected - radius))
        right = min(width - 32, int(expected + radius))
        boundary = int(np.argmin(smooth[left:right]) + left) + spec.x0
        boundaries.append(boundary)
    boundaries.append(spec.x1)
    return [
        (boundaries[index], spec.y0, boundaries[index + 1], spec.y1)
        for index in range(spec.count)
    ]


def source_crop(image: Image.Image, bg: np.ndarray, box: tuple[int, int, int, int]) -> Image.Image:
    crop = image.crop(box).convert("RGB")
    rgb = np.asarray(crop)
    diff = foreground_difference(rgb, bg)
    ys, xs = np.where(diff > 9)
    if len(xs) < 100:
        raise RuntimeError(f"No furniture pixels detected in source cell {box}")
    margin = max(4, round(min(crop.size) * 0.012))
    left = max(0, int(xs.min()) - margin)
    top = max(0, int(ys.min()) - margin)
    right = min(crop.width, int(xs.max()) + 1 + margin)
    bottom = min(crop.height, int(ys.max()) + 1 + margin)
    return crop.crop((left, top, right, bottom))


def select_device(torch: object, requested: str) -> str:
    if requested == "cuda":
        if not torch.cuda.is_available():
            raise RuntimeError("CUDA was requested, but PyTorch cannot access an NVIDIA GPU")
        return "cuda"
    if requested == "cpu":
        return "cpu"
    return "cuda" if torch.cuda.is_available() else "cpu"


def meaningful_support(alpha: np.ndarray) -> np.ndarray:
    import cv2

    seed = (alpha >= 0.10).astype(np.uint8)
    count, labels, stats, _ = cv2.connectedComponentsWithStats(seed, connectivity=8)
    if count <= 1:
        return np.ones_like(seed)
    areas = stats[1:, cv2.CC_STAT_AREA]
    minimum_area = max(alpha.size * 0.00003, 8)
    kept = 1 + np.flatnonzero(areas >= minimum_area)
    components = np.isin(labels, kept).astype(np.uint8)
    return cv2.dilate(components, np.ones((5, 5), np.uint8), iterations=2)


def unmatte_white(rgb: np.ndarray, alpha: np.ndarray) -> np.ndarray:
    """Remove the white-sheet contribution from soft edge pixels."""
    a = np.clip(alpha.astype(np.float32) / 255.0, 1.0 / 255.0, 1.0)[..., None]
    foreground = (rgb.astype(np.float32) - 255.0 * (1.0 - a)) / a
    foreground = np.clip(foreground, 0.0, 255.0).astype(np.uint8)
    foreground[alpha == 0] = 0
    return foreground


def extract_white_cutout(image: Image.Image) -> Image.Image:
    """Build a soft alpha directly from distance to the source sheet's white."""
    import cv2

    rgb = np.asarray(image.convert("RGB"))
    border = np.concatenate(
        (rgb[:12].reshape(-1, 3), rgb[-12:].reshape(-1, 3), rgb[:, :12].reshape(-1, 3), rgb[:, -12:].reshape(-1, 3)),
        axis=0,
    )
    bg = np.median(border, axis=0).astype(np.int16)
    diff = foreground_difference(rgb, bg).astype(np.float32)

    alpha = np.clip((diff - 1.5) / 7.0, 0.0, 1.0) * 255.0
    alpha_u8 = np.round(alpha).astype(np.uint8)
    seed = (alpha_u8 >= 48).astype(np.uint8)
    component_count, component_labels, stats, _ = cv2.connectedComponentsWithStats(seed, connectivity=8)
    if component_count > 1:
        areas = stats[1:, cv2.CC_STAT_AREA]
        keep = 1 + int(np.argmax(areas))
        support = (component_labels == keep).astype(np.uint8)
        support = cv2.dilate(support, np.ones((5, 5), np.uint8), iterations=1)
        alpha_u8 *= support
    clean_rgb = unmatte_white(rgb, alpha_u8)
    return Image.fromarray(np.dstack((clean_rgb, alpha_u8)), mode="RGBA")


def infer_cutouts(images: list[Image.Image], requested_device: str, batch_size: int) -> list[Image.Image]:
    import cv2
    import torch
    from torchvision import transforms
    from transformers import AutoModelForImageSegmentation

    device = select_device(torch, requested_device)
    dtype = torch.float16 if device == "cuda" else torch.float32
    print(f"Loading {MODEL_ID}@{MODEL_REVISION} on {device}", flush=True)
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

    transform = transforms.Compose(
        [
            transforms.Resize(MODEL_SIZE),
            transforms.ToTensor(),
            transforms.Normalize([0.485, 0.456, 0.406], [0.229, 0.224, 0.225]),
        ]
    )
    results: list[Image.Image] = []
    for start in range(0, len(images), max(1, batch_size)):
        batch = images[start : start + max(1, batch_size)]
        inputs = torch.stack([transform(image.convert("RGB")) for image in batch])
        inputs = inputs.to(device=device, dtype=dtype)
        with torch.inference_mode():
            predictions = model(inputs)[-1].sigmoid().float().cpu()
        for image, prediction in zip(batch, predictions):
            rgb = np.asarray(image.convert("RGB"))
            alpha = prediction.squeeze().numpy()
            alpha = cv2.resize(alpha, image.size, interpolation=cv2.INTER_LINEAR)
            alpha = np.clip((alpha - 0.035) / 0.93, 0.0, 1.0)
            alpha *= meaningful_support(alpha)
            alpha_u8 = np.round(alpha * 255.0).astype(np.uint8)
            clean_rgb = unmatte_white(rgb, alpha_u8)
            results.append(Image.fromarray(np.dstack((clean_rgb, alpha_u8)), mode="RGBA"))
        print(f"BiRefNet: {min(start + len(batch), len(images))}/{len(images)}", flush=True)
    return results


def place_on_uniform_canvas(source: Image.Image) -> Image.Image:
    alpha = source.getchannel("A")
    box = alpha.getbbox()
    if box is None:
        raise RuntimeError("BiRefNet returned an empty furniture matte")
    content = source.crop(box)
    scale = min(CONTENT_SIZE[0] / content.width, CONTENT_SIZE[1] / content.height)
    size = (max(1, round(content.width * scale)), max(1, round(content.height * scale)))
    resized = content.resize(size, Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", CANVAS_SIZE, (0, 0, 0, 0))
    position = ((CANVAS_SIZE[0] - size[0]) // 2, (CANVAS_SIZE[1] - size[1]) // 2)
    canvas.alpha_composite(resized, position)
    return canvas


def make_contact(before: list[tuple[str, Image.Image]], after: list[tuple[str, Image.Image]], path: Path) -> None:
    cell_w, cell_h = 220, 220
    count = len(after)
    columns = 8
    rows = (count + columns - 1) // columns
    sheet = Image.new("RGB", (columns * cell_w, rows * cell_h * 2), "#d9dde5")
    draw = ImageDraw.Draw(sheet)
    for section, items in enumerate((before, after)):
        y_offset = section * rows * cell_h
        for index, (name, image) in enumerate(items):
            col, row = index % columns, index // columns
            x, y = col * cell_w, y_offset + row * cell_h
            tile = Image.new("RGBA", (cell_w, cell_h), (224, 228, 236, 255))
            preview = image.copy().convert("RGBA")
            preview.thumbnail((200, 178), Image.Resampling.LANCZOS)
            tile.alpha_composite(preview, ((cell_w - preview.width) // 2, 182 - preview.height))
            sheet.paste(tile.convert("RGB"), (x, y))
            draw.text((x + 5, y + 190), name, fill="#111827")
    path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(path, optimize=True)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-dir", type=Path, required=True)
    parser.add_argument("--furniture-dir", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--contact-sheet", type=Path, required=True)
    parser.add_argument("--device", choices=("auto", "cuda", "cpu"), default="auto")
    parser.add_argument("--batch-size", type=int, default=4)
    parser.add_argument("--matte", choices=("white", "birefnet"), default="white")
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)

    before: list[tuple[str, Image.Image]] = []
    jobs: list[tuple[str, Image.Image]] = []
    written: set[str] = set()
    for sheet_spec in SHEETS:
        source_path = args.source_dir / sheet_spec.source
        image = Image.open(source_path).convert("RGB")
        bg = background_color(image)
        cells: list[tuple[int, int, int, int]] = []
        for row in sheet_spec.rows:
            cells.extend(split_row(image, bg, row))
        if len(cells) != len(sheet_spec.targets):
            raise RuntimeError(f"Cell/target mismatch for {source_path.name}")
        for box, target_name in zip(cells, sheet_spec.targets):
            if target_name is None:
                continue
            target_path = args.furniture_dir / target_name
            if not target_path.exists():
                raise FileNotFoundError(target_path)
            target = Image.open(target_path).convert("RGBA")
            try:
                extracted = source_crop(image, bg, box)
            except RuntimeError as exc:
                raise RuntimeError(f"{source_path.name}: {exc}") from exc
            before.append((target_name, target))
            jobs.append((target_name, extracted))
            written.add(target_name)

    if len(written) != 74:
        raise RuntimeError(f"Expected 74 replacement sprites, wrote {len(written)}")
    if args.matte == "birefnet":
        cutouts = infer_cutouts(
            [image for _, image in jobs],
            requested_device=args.device,
            batch_size=args.batch_size,
        )
    else:
        cutouts = [extract_white_cutout(image) for _, image in jobs]
    after: list[tuple[str, Image.Image]] = []
    for (target_name, _), cutout in zip(jobs, cutouts):
        result = place_on_uniform_canvas(cutout)
        result.save(args.output_dir / target_name, optimize=True)
        after.append((target_name, result))
    make_contact(before, after, args.contact_sheet)
    print(f"Wrote {len(written)} sprites to {args.output_dir}")
    print(f"Contact sheet: {args.contact_sheet}")


if __name__ == "__main__":
    main()

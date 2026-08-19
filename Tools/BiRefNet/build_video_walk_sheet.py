"""Extract a seamless walk cycle from video and build a transparent sprite sheet.

The foreground matte uses the same pinned ZhengPeng7/BiRefNet revision as
``background_erase.py``. Every selected frame shares one crop and one transform,
so the animation keeps the source video's registration instead of recentering
each pose and introducing artificial jitter.
"""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

import cv2
import numpy as np


MODEL_ID = "ZhengPeng7/BiRefNet"
MODEL_REVISION = "b7d7f31fed203ab364ac756d62053ee467502434"
MODEL_SIZE = (1024, 1024)


def report(event: str, **values: object) -> None:
    print(json.dumps({"event": event, **values}, ensure_ascii=False), flush=True)


def select_device(torch: object, requested: str) -> str:
    if requested == "cuda":
        if not torch.cuda.is_available():
            raise RuntimeError("CUDA was requested, but PyTorch cannot access an NVIDIA GPU")
        return "cuda"
    if requested == "cpu":
        return "cpu"
    return "cuda" if torch.cuda.is_available() else "cpu"


def read_selected_frames(video: Path, indices: list[int]) -> tuple[list[np.ndarray], float]:
    capture = cv2.VideoCapture(str(video))
    if not capture.isOpened():
        raise RuntimeError(f"cannot open video: {video}")
    fps = float(capture.get(cv2.CAP_PROP_FPS))
    total = int(capture.get(cv2.CAP_PROP_FRAME_COUNT))
    last_requested = max(indices)
    if last_requested >= total:
        raise ValueError(f"requested frame {last_requested}, but video only contains {total} frames")

    wanted = set(indices)
    selected: dict[int, np.ndarray] = {}
    frame_index = 0
    while frame_index <= last_requested:
        ok, frame = capture.read()
        if not ok:
            break
        if frame_index in wanted:
            selected[frame_index] = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        frame_index += 1
    capture.release()
    missing = [index for index in indices if index not in selected]
    if missing:
        raise RuntimeError(f"failed to decode selected frames: {missing}")
    return [selected[index] for index in indices], fps


def meaningful_foreground_support(alpha: np.ndarray) -> np.ndarray:
    seed = (alpha >= 0.12).astype(np.uint8)
    count, labels, stats, _ = cv2.connectedComponentsWithStats(seed, connectivity=8)
    if count <= 1:
        return np.ones_like(seed)
    areas = stats[1:, cv2.CC_STAT_AREA]
    minimum_area = max(alpha.size * 0.0002, int(areas.max()) * 0.02)
    kept_labels = 1 + np.flatnonzero(areas >= minimum_area)
    components = np.isin(labels, kept_labels).astype(np.uint8)
    # Keep soft BiRefNet transitions around every meaningful subject. This is
    # important for multi-character sources such as the cat visitor and pet.
    kernel = np.ones((9, 9), np.uint8)
    return cv2.dilate(components, kernel, iterations=2)


def infer_cutouts(frames: list[np.ndarray], requested_device: str, batch_size: int) -> list[object]:
    import torch
    from PIL import Image
    from torchvision import transforms
    from transformers import AutoModelForImageSegmentation

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

    transform = transforms.Compose(
        [
            transforms.Resize(MODEL_SIZE),
            transforms.ToTensor(),
            transforms.Normalize([0.485, 0.456, 0.406], [0.229, 0.224, 0.225]),
        ]
    )
    cutouts: list[Image.Image] = []
    for batch_start in range(0, len(frames), max(1, batch_size)):
        batch_frames = frames[batch_start : batch_start + max(1, batch_size)]
        inputs = torch.stack([transform(Image.fromarray(frame)) for frame in batch_frames])
        inputs = inputs.to(device=device, dtype=dtype)
        with torch.inference_mode():
            predictions = model(inputs)[-1].sigmoid().float().cpu()

        for rgb, prediction in zip(batch_frames, predictions):
            alpha = prediction.squeeze().numpy()
            alpha = cv2.resize(alpha, (rgb.shape[1], rgb.shape[0]), interpolation=cv2.INTER_LINEAR)
            support = meaningful_foreground_support(alpha)
            alpha = np.clip((alpha - 0.035) / 0.93, 0.0, 1.0) * support
            rgba = np.dstack((rgb, np.round(alpha * 255.0).astype(np.uint8)))
            rgba[rgba[:, :, 3] == 0, :3] = 0
            cutouts.append(Image.fromarray(rgba, mode="RGBA"))
        report("inference", completed=min(batch_start + len(batch_frames), len(frames)), total=len(frames))
    return cutouts


def union_alpha_bbox(images: list[object]) -> tuple[int, int, int, int]:
    boxes = [image.getchannel("A").getbbox() for image in images]
    boxes = [box for box in boxes if box is not None]
    if not boxes:
        raise RuntimeError("BiRefNet returned an empty foreground for every selected frame")
    return (
        min(box[0] for box in boxes),
        min(box[1] for box in boxes),
        max(box[2] for box in boxes),
        max(box[3] for box in boxes),
    )


def stabilize_root(images: list[object], frame_start: int, frame_end: int) -> list[object]:
    """Remove source-video root drift by aligning the weighted foot-center of a frame range."""
    import numpy as np
    from PIL import Image

    if not 0 <= frame_start <= frame_end < len(images):
        raise ValueError("stabilize-range must be inside the selected frame range")
    anchors: list[float] = []
    for index in range(frame_start, frame_end + 1):
        alpha = np.asarray(images[index].getchannel("A"), dtype=np.float32)
        box = images[index].getchannel("A").getbbox()
        if box is None:
            raise RuntimeError(f"frame {index} has no foreground for root stabilization")
        foot_top = round(box[3] - (box[3] - box[1]) * 0.18)
        yy, xx = np.where((alpha >= 16) & (np.indices(alpha.shape)[0] >= foot_top))
        weights = alpha[yy, xx]
        anchors.append(float(np.average(xx, weights=weights)))
    target = float(np.median(anchors))
    stabilized = list(images)
    for offset, index in enumerate(range(frame_start, frame_end + 1)):
        shift_x = round(target - anchors[offset])
        if shift_x == 0:
            continue
        translated = Image.new("RGBA", images[index].size, (0, 0, 0, 0))
        translated.alpha_composite(images[index], (shift_x, 0))
        stabilized[index] = translated
    return stabilized


def build_sheet(
    cutouts: list[object],
    frame_width: int,
    frame_height: int,
    columns: int,
    head_padding: float,
    foot_padding: float,
    horizontal_padding: float,
    scale_reference_frame: int | None,
) -> object:
    from PIL import Image

    rows = math.ceil(len(cutouts) / columns)
    sheet = Image.new("RGBA", (frame_width * columns, frame_height * rows), (0, 0, 0, 0))
    source_box = union_alpha_bbox(cutouts)
    source_width = source_box[2] - source_box[0]
    source_height = source_box[3] - source_box[1]
    horizontal_margin = max(0, round(frame_width * horizontal_padding))
    top = round(frame_height * head_padding)
    bottom = round(frame_height * foot_padding)
    available_width = frame_width - horizontal_margin * 2
    available_height = frame_height - top - bottom
    local_y = top
    if scale_reference_frame is None:
        scale = min(available_width / source_width, available_height / source_height)
    else:
        if not 0 <= scale_reference_frame < len(cutouts):
            raise ValueError("scale-reference-frame is outside the selected frame range")
        reference_box = cutouts[scale_reference_frame].getchannel("A").getbbox()
        if reference_box is None:
            raise RuntimeError("scale reference frame has no foreground")
        reference_height = reference_box[3] - reference_box[1]
        scale = min(available_width / source_width, available_height / reference_height)
        above_reference = reference_box[1] - source_box[1]
        below_reference = source_box[3] - reference_box[3]
        if above_reference > 0:
            scale = min(scale, top / above_reference)
        if below_reference > 0:
            scale = min(scale, bottom / below_reference)
        local_y = round(top - above_reference * scale)
    scaled_size = (max(1, round(source_width * scale)), max(1, round(source_height * scale)))
    local_x = (frame_width - scaled_size[0]) // 2

    for frame_index, cutout in enumerate(cutouts):
        fixed_crop = cutout.crop(source_box)
        resized = fixed_crop.convert("RGBa").resize(scaled_size, Image.Resampling.LANCZOS).convert("RGBA")
        cell = Image.new("RGBA", (frame_width, frame_height), (0, 0, 0, 0))
        cell.alpha_composite(resized, (local_x, local_y))
        col = frame_index % columns
        row = frame_index // columns
        sheet.alpha_composite(cell, (col * frame_width, row * frame_height))
    return sheet


def save_preview(sheet: object, frame_width: int, frame_height: int, output: Path) -> None:
    from PIL import Image, ImageDraw

    checker = Image.new("RGB", sheet.size, "#eeeeee")
    draw = ImageDraw.Draw(checker)
    step = 32
    for y in range(0, checker.height, step):
        for x in range(0, checker.width, step):
            if (x // step + y // step) % 2:
                draw.rectangle((x, y, x + step - 1, y + step - 1), fill="#c8c8c8")
    checker.paste(sheet, (0, 0), sheet)
    draw = ImageDraw.Draw(checker)
    for x in range(frame_width, checker.width, frame_width):
        draw.line((x, 0, x, checker.height), fill="#e83e5b", width=3)
    for y in range(frame_height, checker.height, frame_height):
        draw.line((0, y, checker.width, y), fill="#e83e5b", width=3)
    preview = checker.copy()
    preview.thumbnail((1600, 1600), Image.Resampling.LANCZOS)
    preview.save(output, quality=92)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Build a BiRefNet walk sprite sheet from an MP4")
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--json-output", type=Path, required=True)
    parser.add_argument("--preview", type=Path)
    # The supplied cheetah source settles into its cleanest 30-frame loop at
    # source frame 23. Keeping these as defaults makes the checked-in sheet
    # reproducible while callers can still override them for another video.
    parser.add_argument("--start-frame", type=int, default=23)
    parser.add_argument("--cycle-frames", type=int, default=30)
    parser.add_argument("--stride", type=int, default=2)
    parser.add_argument(
        "--frame-indices",
        help="Comma-separated source frame indices; overrides start/cycle/stride",
    )
    parser.add_argument("--columns", type=int, default=5)
    parser.add_argument("--frame-width", type=int, default=400)
    parser.add_argument("--frame-height", type=int, default=520)
    parser.add_argument("--head-padding", type=float, default=0.18)
    parser.add_argument("--foot-padding", type=float, default=0.1215)
    parser.add_argument("--horizontal-padding", type=float, default=0.05)
    parser.add_argument(
        "--scale-reference-frame",
        type=int,
        help="Selected-frame index whose visible height should match the await-sheet padding",
    )
    parser.add_argument("--frames-per-second", type=float, default=12.0)
    parser.add_argument("--movement-window", help="Inclusive selected-frame range, for example 7:15")
    parser.add_argument("--stabilize-range", help="Inclusive selected-frame range whose foot root is aligned")
    parser.add_argument("--invert-facing", action="store_true")
    parser.add_argument("--batch-size", type=int, default=2)
    parser.add_argument("--device", choices=("auto", "cuda", "cpu"), default="auto")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if not args.input.is_file():
        raise FileNotFoundError(args.input)
    if args.columns <= 0:
        raise ValueError("columns must be positive")
    if not 0 <= args.horizontal_padding < 0.5:
        raise ValueError("horizontal-padding must be in [0, 0.5)")
    if args.frame_indices:
        indices = [int(value.strip()) for value in args.frame_indices.split(",") if value.strip()]
        if not indices or min(indices) < 0:
            raise ValueError("frame-indices must contain non-negative integers")
    else:
        if args.cycle_frames <= 0 or args.stride <= 0:
            raise ValueError("cycle-frames and stride must be positive")
        indices = list(range(args.start_frame, args.start_frame + args.cycle_frames, args.stride))
    frames, source_fps = read_selected_frames(args.input, indices)
    report("decode", source_fps=source_fps, selected_frames=indices)
    cutouts = infer_cutouts(frames, args.device, args.batch_size)
    if args.stabilize_range:
        start_text, end_text = args.stabilize_range.split(":", 1)
        cutouts = stabilize_root(cutouts, int(start_text), int(end_text))
    sheet = build_sheet(
        cutouts,
        args.frame_width,
        args.frame_height,
        args.columns,
        args.head_padding,
        args.foot_padding,
        args.horizontal_padding,
        args.scale_reference_frame,
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.json_output.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(args.output, format="PNG", optimize=True)
    rows = math.ceil(len(frames) / args.columns)
    metadata = {
        "frameWidth": args.frame_width,
        "frameHeight": args.frame_height,
        "columns": args.columns,
        "rows": rows,
        "frameCount": len(frames),
        "footPadding": args.foot_padding,
        "headPadding": args.head_padding,
        "framesPerSecond": args.frames_per_second,
    }
    if args.movement_window:
        start_text, end_text = args.movement_window.split(":", 1)
        move_start = int(start_text)
        move_end = int(end_text)
        if not 0 <= move_start <= move_end < len(frames):
            raise ValueError("movement-window must be inside the selected frame range")
        metadata.update(
            {
                "hasMovementWindow": True,
                "moveStartFrame": move_start,
                "moveEndFrame": move_end,
            }
        )
    if args.invert_facing:
        metadata["invertFacing"] = True
    args.json_output.write_text(json.dumps(metadata, ensure_ascii=False), encoding="utf-8")
    if args.preview:
        save_preview(sheet, args.frame_width, args.frame_height, args.preview)
    report(
        "complete",
        output=str(args.output),
        json_output=str(args.json_output),
        sheet_size=list(sheet.size),
        frames=len(frames),
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3
"""Synchronize 家具表.csv swatch colors into FurnitureTable.asset.

This is a narrow fallback for cases where Unity is already in Play Mode and the
editor CSV postprocessor cannot run immediately.  It only rewrites the existing
``swatchColor`` field for existing furniture IDs; it never adds or removes rows.
"""

from __future__ import annotations

import csv
import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
CSV_PATH = ROOT / "Assets" / "Configs" / "家具表.csv"
ASSET_PATH = ROOT / "Assets" / "Resources" / "OutGameUI" / "FurnitureTable.asset"

ID_RE = re.compile(r"^(\s*- id: )(.+)$")
COLOR_RE = re.compile(
    r"^(\s*swatchColor: )\{r: [^,]+, g: [^,]+, b: [^,]+, a: [^}]+\}$"
)


def load_colors() -> dict[str, tuple[int, int, int]]:
    with CSV_PATH.open("r", encoding="utf-8-sig", newline="") as stream:
        rows = list(csv.DictReader(stream))

    colors: dict[str, tuple[int, int, int]] = {}
    for row in rows:
        item_id = row["id"].strip()
        value = row["色值"].strip().lstrip("#")
        if not re.fullmatch(r"[0-9A-Fa-f]{6}", value):
            raise ValueError(f"Invalid 色值 for {item_id}: {row['色值']!r}")
        colors[item_id] = tuple(int(value[index:index + 2], 16) for index in (0, 2, 4))
    return colors


def unity_float(channel: int) -> str:
    return format(channel / 255.0, ".9g")


def main() -> None:
    colors = load_colors()
    lines = ASSET_PATH.read_text(encoding="utf-8").splitlines(keepends=True)
    current_id: str | None = None
    seen: set[str] = set()
    changed = 0

    for index, line in enumerate(lines):
        body = line.rstrip("\r\n")
        ending = line[len(body):]
        id_match = ID_RE.match(body)
        if id_match:
            current_id = id_match.group(2).strip()
            continue

        color_match = COLOR_RE.match(body)
        if not color_match or current_id not in colors:
            continue

        red, green, blue = colors[current_id]
        replacement = (
            f"{color_match.group(1)}{{r: {unity_float(red)}, g: {unity_float(green)}, "
            f"b: {unity_float(blue)}, a: 1}}{ending}"
        )
        if replacement != line:
            lines[index] = replacement
            changed += 1
        seen.add(current_id)

    missing = sorted(set(colors) - seen)
    if missing:
        raise RuntimeError(f"FurnitureTable.asset is missing IDs: {missing}")

    ASSET_PATH.write_text("".join(lines), encoding="utf-8", newline="")
    print(f"Synced {len(seen)} swatches; changed {changed} asset entries.")


if __name__ == "__main__":
    main()

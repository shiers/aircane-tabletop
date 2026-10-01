#!/usr/bin/env python3
"""Generate placeholder Aircane Tabletop desktop icons.

Produces a simple, brand-neutral placeholder: a white letter "A" on a dark
rounded-square background with an indigo->sky gradient (matching the wrapper's
loading screen). NO D&D / Pathfinder / Paizo imagery — this is intentionally a
generic placeholder until final branding is ready.

Outputs (into this directory):
    icon.png          1024x1024 source
    icon.ico          multi-size Windows ICO (16/32/48/256)
    icon.icns         macOS iconset
    128x128.png
    128x128@2x.png    (256x256)
    32x32.png
    tray.png          32x32 monochrome (white A, transparent bg) for the system tray

Regenerate with:  python desktop/src-tauri/icons/generate_placeholder_icons.py
Or, once the Rust toolchain is installed, replace with the real pipeline:
    cargo tauri icon desktop/src-tauri/icons/icon.png
"""

from __future__ import annotations

import os
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))

# Brand-ish placeholder colours (indigo -> sky), matching loading.html.
TOP = (79, 70, 229)      # indigo-600
BOTTOM = (14, 165, 233)  # sky-500
FG = (255, 255, 255, 255)


def _load_font(size: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    """Best-effort bold font; falls back to Pillow's default if none found."""
    candidates = [
        "arialbd.ttf",
        "Arial Bold.ttf",
        "DejaVuSans-Bold.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "C:/Windows/Fonts/arialbd.ttf",
        "/System/Library/Fonts/SFNSRounded.ttf",
    ]
    for c in candidates:
        try:
            return ImageFont.truetype(c, size)
        except Exception:
            continue
    return ImageFont.load_default()


def _rounded_gradient(size: int) -> Image.Image:
    """A rounded square filled with a vertical indigo->sky gradient."""
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))

    # Build the gradient on a full square first.
    grad = Image.new("RGBA", (size, size))
    for y in range(size):
        t = y / max(size - 1, 1)
        r = int(TOP[0] + (BOTTOM[0] - TOP[0]) * t)
        g = int(TOP[1] + (BOTTOM[1] - TOP[1]) * t)
        b = int(TOP[2] + (BOTTOM[2] - TOP[2]) * t)
        for x in range(size):
            grad.putpixel((x, y), (r, g, b, 255))

    # Rounded-rectangle mask.
    radius = int(size * 0.22)
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [(0, 0), (size - 1, size - 1)], radius=radius, fill=255
    )
    img.paste(grad, (0, 0), mask)
    return img


def _draw_letter(img: Image.Image, color=FG) -> None:
    size = img.size[0]
    draw = ImageDraw.Draw(img)
    font = _load_font(int(size * 0.62))
    text = "A"
    # Center using the text bounding box.
    bbox = draw.textbbox((0, 0), text, font=font)
    tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
    x = (size - tw) / 2 - bbox[0]
    y = (size - th) / 2 - bbox[1]
    draw.text((x, y), text, font=font, fill=color)


def make_app_icon(size: int) -> Image.Image:
    img = _rounded_gradient(size)
    _draw_letter(img)
    return img


def make_tray_icon(size: int = 32) -> Image.Image:
    """Monochrome white 'A' on a transparent background for the tray."""
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    _draw_letter(img, color=(255, 255, 255, 255))
    return img


def main() -> None:
    source = make_app_icon(1024)
    source.save(os.path.join(HERE, "icon.png"))

    # Standard PNG sizes referenced by tauri.conf.json.
    make_app_icon(32).save(os.path.join(HERE, "32x32.png"))
    make_app_icon(128).save(os.path.join(HERE, "128x128.png"))
    make_app_icon(256).save(os.path.join(HERE, "128x128@2x.png"))

    # Windows ICO (multi-size).
    source.save(
        os.path.join(HERE, "icon.ico"),
        sizes=[(16, 16), (32, 32), (48, 48), (256, 256)],
    )

    # macOS ICNS (Pillow writes the required member sizes from the source).
    try:
        source.save(os.path.join(HERE, "icon.icns"))
    except Exception as exc:  # pragma: no cover - platform/codec dependent
        print(f"WARNING: could not write icon.icns ({exc}). "
              f"Generate it on macOS with `iconutil` or `cargo tauri icon`.")

    # Monochrome tray icon.
    make_tray_icon(32).save(os.path.join(HERE, "tray.png"))

    print("Generated placeholder icons in", HERE)


if __name__ == "__main__":
    main()

"""
Medallion icons from game-icons.net silhouettes (CC BY 3.0, authors: Lorc, Delapouite and others; https://game-icons.net).

Usage: python3 build_gameicons.py <game-icons repo> <out dir>

Each silhouette is rendered as a gold relief (vertical gradient, dark outline, inner highlight) on a lacquer-red disc with a
bronze ring, then pixelated (48 px → 72 px, nearest) so it sits with the painted pixel-art menu icons (64×72, round medallions).
"""
import io
import re
import sys
from pathlib import Path

import cairosvg
from PIL import Image, ImageChops, ImageDraw, ImageFilter

ICONS = {
    # out name: (author, icon)
    "gi_friends": ("delapouite", "shaking-hands"),
    "gi_chronicle": ("delapouite", "scroll-quill"),
    "gi_bounty": ("delapouite", "wanted-reward"),
    "gi_codex": ("lorc", "open-book"),
    "gi_orders": ("lorc", "scroll-unfurled"),
    "gi_mail": ("lorc", "envelope"),
    "gi_settings": ("lorc", "cog"),
    "gi_pagoda": ("delapouite", "pagoda"),
    "gi_waterfall": ("delapouite", "waterfall"),
    "gi_peak": ("lorc", "mountains"),
    "gi_talisman": ("lorc", "fire-ring"),
    "gi_pass": ("lorc", "laurel-crown"),
    "gi_zodiac": ("delapouite", "yin-yang"),
    "gi_mentor": ("delapouite", "crane"),
    "gi_evolve": ("lorc", "dragon-head"),
    "gi_treasure": ("delapouite", "two-coins"),
    "gi_wanted": ("lorc", "crowned-skull"),
}

SIZE = 48  # working size before the pixel upscale


def silhouette(svg_path: Path) -> Image.Image:
    svg = svg_path.read_text()
    svg = re.sub(r'<path d="M0 0h512v512H0z"/>', "", svg)  # drop the black background square
    png = cairosvg.svg2png(bytestring=svg.encode(), output_width=SIZE - 14, output_height=SIZE - 14)
    img = Image.open(io.BytesIO(png)).convert("RGBA")
    return img.split()[3]  # alpha = the shape


def medallion(mask: Image.Image) -> Image.Image:
    s = SIZE
    out = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(out)
    # bronze ring and lacquer disc (radial-ish: darker edge)
    d.ellipse((1, 1, s - 2, s - 2), fill=(70, 38, 20, 255))
    d.ellipse((2, 2, s - 3, s - 3), fill=(176, 116, 58, 255))
    d.ellipse((4, 4, s - 5, s - 5), fill=(96, 52, 26, 255))
    for i, c in enumerate([(66, 14, 12), (82, 18, 14), (98, 24, 18), (112, 30, 22)]):
        d.ellipse((5 + i * 2, 5 + i * 2, s - 6 - i * 2, s - 6 - i * 2), fill=c + (255,))
    # gold relief: vertical gradient clipped by the silhouette, with a dark outline and a light top edge
    off = (s - mask.width) // 2
    full = Image.new("L", (s, s), 0)
    full.paste(mask, (off, off))
    grad = Image.new("RGBA", (s, s))
    gd = ImageDraw.Draw(grad)
    for y in range(s):
        t = y / (s - 1)
        r = int(255 - 70 * t)
        g = int(226 - 92 * t)
        b = int(150 - 96 * t)
        gd.line((0, y, s, y), fill=(r, g, b, 255))
    outline = full.filter(ImageFilter.MaxFilter(3))
    dark = Image.new("RGBA", (s, s), (40, 16, 8, 255))
    out.paste(dark, (0, 1), outline)  # outline + drop shadow
    out.paste(grad, (0, 0), full)
    highlight = ImageChops.subtract(full, ImageChops.offset(full, 0, 1))
    out.paste(Image.new("RGBA", (s, s), (255, 246, 210, 255)), (0, 0), highlight)
    return out


def main(repo: str, out_dir: str) -> None:
    repo_path, out = Path(repo), Path(out_dir)
    out.mkdir(parents=True, exist_ok=True)
    for name, (author, icon) in ICONS.items():
        img = medallion(silhouette(repo_path / author / f"{icon}.svg"))
        big = img.resize((64, 64), Image.NEAREST)
        canvas = Image.new("RGBA", (64, 72), (0, 0, 0, 0))  # same 64×72 frame as the painted menu icons
        canvas.paste(big, (0, 4), big)
        canvas.save(out / f"{name}.png", optimize=True)
        print(name)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])

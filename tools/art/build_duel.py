"""
Ink-duel assets (2.2): poses, ink effects, light effects (turned to grey ink), backgrounds, avatars, cut-ins.

Usage: python3 build_duel.py <src dir> <drawable-nodpi dir>
  <src>/set2/<path>_<pose>.png   cut-out poses (qi, body, flame, poison, rival)
  <src>/set2/bg_<name>.jpg       monochrome duel backgrounds
  <src>/ink/ink_*.jpg, st_*.jpg, screen_*.jpg   black ink on white
  <src>/ink/glow_*.jpg, combo_*.jpg            coloured light on black (→ grey ink wash with alpha)
  <src>/ink/avatar_*.jpg, <src>/cutin/cutin_*.jpg
Everything is black / grey / white: colour is reserved for the characters themselves.
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageOps

POSE_H = 360      # pose bitmaps: the tallest pose of a fighter is 360 px
FX_MAX = 512


def ink_rgba(src: Image.Image, thr=235) -> Image.Image:
    g = np.asarray(src.convert("L")).astype(float)
    a = np.clip((thr - g) / 180, 0, 1) * 255
    c = np.full_like(g, 16)
    im = Image.fromarray(np.dstack([c, c, c + 4, a]).astype(np.uint8), "RGBA")
    return im.crop(im.getbbox()) if im.getbbox() else im


def glow_rgba(src: Image.Image) -> Image.Image:
    im = src.convert("RGB")
    im = im.crop(im.convert("L").point(lambda v: 255 if v > 22 else 0).getbbox())
    a = np.asarray(im).astype(float)
    m = a.max(axis=2)
    al = np.clip((m - 28) / 200, 0, 1) ** 1.3 * 235
    hh, ww = m.shape
    fx = np.clip(np.minimum(np.arange(ww), ww - 1 - np.arange(ww)) / (ww * 0.12 + 1), 0, 1)
    fy = np.clip(np.minimum(np.arange(hh), hh - 1 - np.arange(hh)) / (hh * 0.12 + 1), 0, 1)
    al = al * fy[:, None] * fx[None, :]
    g = np.clip(55 + (255 - m) * 0.5, 45, 175)
    return Image.fromarray(np.dstack([g, g, g, al]).astype(np.uint8), "RGBA")


def fit(im: Image.Image, mx: int) -> Image.Image:
    if max(im.size) <= mx:
        return im
    k = mx / max(im.size)
    return im.resize((max(1, int(im.width * k)), max(1, int(im.height * k))), Image.LANCZOS)


def main() -> None:
    src, out = Path(sys.argv[1]), Path(sys.argv[2])
    # poses: scaled together per fighter so relative heights stay as painted (heights are normalised again in code)
    for path in ["qi", "body", "flame", "poison", "rival"]:
        for f in sorted((src / "set2").glob(f"{path}_*.png")):
            im = Image.open(f).convert("RGBA")
            im = im.resize((max(1, int(im.width * POSE_H / im.height)), POSE_H), Image.LANCZOS)
            im.save(out / f"duel_pose_{f.stem}.webp", quality=86, method=6)
    for f in sorted((src / "set2").glob("bg_*.jpg")):
        Image.open(f).convert("RGB").resize((720, 1280), Image.LANCZOS).save(out / f"duel_{f.stem}.jpg", quality=84)
    for f in sorted((src / "ink").glob("*.jpg")):
        n = f.stem
        if n.startswith("avatar_"):
            a = Image.open(f).convert("L")
            w, h = a.size
            a = a.crop((int(w * 0.12), int(h * 0.04), int(w * 0.88), int(h * 0.04 + w * 0.76))).resize((160, 160), Image.LANCZOS)
            a = ImageOps.autocontrast(a).convert("RGBA")
            m = Image.new("L", (160, 160), 0)
            ImageDraw.Draw(m).ellipse((0, 0, 159, 159), fill=255)
            a.putalpha(m)
            a.save(out / f"duel_{n}.png", optimize=True)
        elif n.startswith(("glow_", "combo_")):
            fit(glow_rgba(Image.open(f)), FX_MAX).save(out / f"duel_{n}.webp", quality=78, method=6)
        elif n == "screen_red_vignette":
            fit(ink_rgba(Image.open(f), thr=250), 720).save(out / f"duel_{n}.webp", quality=78, method=6)
        else:
            fit(ink_rgba(Image.open(f)), 720 if n.startswith("screen_") else FX_MAX).save(out / f"duel_{n}.webp", quality=80, method=6)
    for f in sorted((src / "cutin").glob("cutin_*.jpg")):
        im = ImageOps.autocontrast(ImageOps.grayscale(Image.open(f).convert("RGB")))
        im = im.crop(im.point(lambda p: 255 if p > 25 else 0).getbbox())
        fit(im, 720).convert("RGB").save(out / f"duel_{f.stem}.jpg", quality=84)


if __name__ == "__main__":
    main()

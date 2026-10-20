"""Hero full figures (chroma key by sampled bg colour) and paper UI pieces (flood-fill the white outside)."""
import sys, numpy as np
from PIL import Image, ImageDraw
U, OUT = sys.argv[1], sys.argv[2]
def key(path, out, h=900, tol=(38, 80)):
    a = np.asarray(Image.open(path).convert('RGB')).astype(np.float32)
    bg = np.median(np.concatenate([a[:20, :20].reshape(-1, 3), a[:20, -20:].reshape(-1, 3), a[-20:, :20].reshape(-1, 3), a[-20:, -20:].reshape(-1, 3)]), 0)
    d = np.sqrt(((a - bg) ** 2).sum(2))
    al = np.clip((d - tol[0]) / (tol[1] - tol[0]), 0, 1)
    # despill: pull the dominant bg channel down to the max of the other two near edges
    ch = int(np.argmax(bg)); others = [c for c in range(3) if c != ch]
    lim = a[..., others].max(2)
    sp = np.minimum(a[..., ch], lim + 10)
    a[..., ch] = np.where(al < 1, sp, np.minimum(a[..., ch], lim + 40))
    im = Image.fromarray(np.dstack([a, al * 255]).astype(np.uint8), 'RGBA')
    im = im.crop(im.split()[3].point(lambda v: 255 if v > 30 else 0).getbbox())
    im = im.resize((int(im.width * h / im.height), h), Image.LANCZOS); im.save(out, quality=88, method=4)
def paper(path, out, size, solid=True):
    im = Image.open(path).convert('RGB'); a = np.asarray(im).astype(np.float32)
    m = a.min(2); ink = np.clip((242 - m) / 190, 0, 1)
    if solid:
        mask = Image.fromarray(((m > 232) * 255).astype(np.uint8)).copy(); ImageDraw.floodfill(mask, (0, 0), 128)
        outside = np.asarray(mask) == 128
        al = np.where(outside, ink, 1.0)
        rgb = np.where(outside[..., None], np.clip((a - 255 * (1 - al[..., None])) / np.maximum(al[..., None], 1e-3), 0, 255), a)
    else:
        al = ink; rgb = np.clip((a - 255 * (1 - al[..., None])) / np.maximum(al[..., None], 1e-3), 0, 255)
    r = Image.fromarray(np.dstack([rgb, al * 255]).astype(np.uint8), 'RGBA')
    r = r.crop(r.split()[3].point(lambda v: 255 if v > 25 else 0).getbbox()); r.thumbnail(size, Image.LANCZOS); r.save(out, quality=90, method=4)
for k, p in {'28f149c9': 'poison', '1c8aeba4': 'flame', '852d6146': 'body', '155b7218': 'qi'}.items():
    key(f'{U}/{k}-image.jpg', f'{OUT}/ink_hero_full_{p}.webp', tol=(22, 55) if p == 'qi' else (38, 80))
paper(f'{U}/1d953226-image.jpg', f'{OUT}/ink_tech_palm.webp', (192, 192), solid=False)
paper(f'{U}/f6900bbf-image.jpg', f'{OUT}/ink_ui_hp_bar.webp', (720, 200), solid=False)
paper(f'{U}/b42bc4f8-image.jpg', f'{OUT}/ink_ui_tech_plate_ready.webp', (256, 256), solid=False)
paper(f'{U}/1143877b-image.jpg', f'{OUT}/ink_ui_tech_plate.webp', (256, 256))
paper(f'{U}/02997189-image.jpg', f'{OUT}/ink_ui_card_scroll.webp', (420, 820))
paper(f'{U}/13b3d0e0-image.jpg', f'{OUT}/ink_ui_slot_empty.webp', (192, 192))
print('ok')
TECH = {'2152d443': 'claw', 'a5196ff0': 'siphon', '8de3a989': 'step', '53f811f4': 'shield', '77b29bb2': 'needle', '0a1daff1': 'flame',
        'a003b8f2': 'fist', 'ace1a07d': 'daggers', '2cd8118c': 'qi_blast', '475df106': 'sword', 'e46a2051': 'mirror', '7f2d3fd8': 'seal',
        '6a72950c': 'gu', '8d5657a0': 'scorpion', 'c9d5a475': 'poison_cloud', '8a113274': 'lotus', '91578e6e': 'phoenix',
        'd074c786': 'sword_rain', 'b7c91c39': 'mountain', 'd0b35064': 'thunder', '2529c5f1': 'reincarnation', '1747c596': 'smoke', 'd0a90272': 'star', '1c625da7': 'talisman'}
import os
for k, n in TECH.items():
    if os.path.exists(f'{U}/{k}-image.jpg'): paper(f'{U}/{k}-image.jpg', f'{OUT}/ink_tech_{n}.webp', (192, 192), solid=False)

"""Pose sheets (one creature, poses in a grid on a flat green/blue background) -> one transparent WebP per pose.
Rows are split on empty horizontal bands, then each row on empty vertical bands; poses are read left to right."""
import sys, numpy as np
from PIL import Image
U, OUT = sys.argv[1], sys.argv[2]
POSES6 = ['stance', 'windup', 'attack', 'skill', 'hurt', 'fall']
SHEETS = {  # upload -> (npc id, which cells in reading order to keep)
    'f7ad759c': ('thunder_eagle', None), '65ea10bc': ('frost_spider', None), '9eef78ad': ('stone_golem', None),
    'ceb69504': ('fire_fox', [0, 1, 2, 3, 4, 7]), '2b4cf787': ('bamboo_viper', None), '9e14f2ce': ('wild_boar', [0, 1, 2, 4, 5, 6]),
    'e52f2f47': ('grey_wolf', [0, 1, 2, 3, 4, 7]), 'aeeae185': ('storm_qilin', None), '5ee5d348': ('jiao_dragon', None),
    '9554ac52': ('ancient_tortoise', [0, 1, 2, 3, 6, 7]), 'fdbcff98': ('training_dummy', 'dummy'),
    '61cfec72': ('flame_tribulation', None), 'e3c8f63c': ('heavenly_tribulation', None),
    '3a6bffb6': ('flame_spirit_primal', None), 'fc9c8116': ('flame_spirit_greater', None), '16cb02a7': ('flame_spirit_lesser', None)}

def key(a):
    bg = np.median(np.concatenate([a[:12, :12].reshape(-1, 3), a[:12, -12:].reshape(-1, 3), a[-12:, :12].reshape(-1, 3), a[-12:, -12:].reshape(-1, 3)]), 0)
    d = np.sqrt(((a - bg) ** 2).sum(2))
    al = np.clip((d - 40) / 45, 0, 1)
    ch = int(np.argmax(bg)); others = [c for c in range(3) if c != ch]
    lim = a[..., others].max(2)
    a = a.copy(); a[..., ch] = np.minimum(a[..., ch], lim + np.where(al < 1, 8, 30))
    return a, al

def bands(profile, min_gap):
    on = profile > 0
    segs, start, gap = [], None, 0
    for i, v in enumerate(on):
        if v:
            if start is None: start = i
            gap = 0; end = i
        elif start is not None:
            gap += 1
            if gap >= min_gap: segs.append((start, end)); start = None
    if start is not None: segs.append((start, end))
    return [s for s in segs if s[1] - s[0] > 25]

from scipy import ndimage

DILATE = {"frost_spider": 16, "storm_qilin": 9}

def poses(al, expect, dilate=6):
    """Pose boxes in reading order: blobs of the (dilated) mask; the biggest are poses, the rest join the nearest one."""
    mask = ndimage.binary_dilation(al > 0.35, iterations=dilate)
    lab, n = ndimage.label(mask)
    objs = ndimage.find_objects(lab)
    sizes = ndimage.sum(mask, lab, range(1, n + 1))
    order = sorted(range(n), key=lambda i: -sizes[i])
    big = sorted(order[:expect])
    boxes = {i: [objs[i][1].start, objs[i][0].start, objs[i][1].stop, objs[i][0].stop] for i in big}
    for i in order[expect:]:
        if sizes[i] < 40: continue
        sl = objs[i]; cx = (sl[1].start + sl[1].stop) / 2; cy = (sl[0].start + sl[0].stop) / 2
        j = min(big, key=lambda b: (max(boxes[b][0] - cx, 0, cx - boxes[b][2]) ** 2 + max(boxes[b][1] - cy, 0, cy - boxes[b][3]) ** 2))
        b = boxes[j]; boxes[j] = [min(b[0], sl[1].start), min(b[1], sl[0].start), max(b[2], sl[1].stop), max(b[3], sl[0].stop)]
    bl = list(boxes.values())
    # reading order: group into rows by vertical centre, then left to right
    bl.sort(key=lambda b: (b[1] + b[3]) / 2)
    rows, cur = [], []
    for b in bl:
        if cur and (b[1] + b[3]) / 2 - (cur[-1][1] + cur[-1][3]) / 2 > (b[3] - b[1]) * 0.45: rows.append(cur); cur = []
        cur.append(b)
    rows.append(cur)
    return [b for r in rows for b in sorted(r, key=lambda b: b[0])]

for k, (npc, keep) in SHEETS.items():
    a = np.asarray(Image.open(f'{U}/{k}-image.jpg').convert('RGB')).astype(np.float32)
    a, al = key(a)
    names = ['stance', 'hurt', 'fall'] if keep == 'dummy' else POSES6
    expect = 3 if keep == 'dummy' else (max(keep) + 1 if isinstance(keep, list) else 6)
    cells = poses(al, expect, DILATE.get(npc, 6))
    if isinstance(keep, list): cells = [cells[i] for i in keep]
    print(npc, len(cells))
    rgba = np.dstack([a, al * 255]).astype(np.uint8)
    for name, (x0, y0, x1, y1) in zip(names, cells):
        im = Image.fromarray(rgba[y0:y1, x0:x1], 'RGBA')
        im.thumbnail((420, 420), Image.LANCZOS)
        im.save(f'{OUT}/duel_pose_{npc}_{name}.webp', quality=88, method=4)

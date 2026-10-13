"""1.4 sheets: per-path hero sheets (7 rows x 6 frames), storm qilin, VFX, unified technique/menu icons, tower art.
Grid sheets are cut by row bands (empty horizontal gaps) and columns; every connected piece goes to the column of
its centre, so trails and afterimages stay with their frame."""
import sys, numpy as np
from PIL import Image
from scipy import ndimage
from extract import key
from slice import clean_mask, strip
U = '/root/.claude/uploads/987109f3-131e-5dee-b953-b1b8eb54c846/'
OUT = sys.argv[1] if len(sys.argv) > 1 else 'out'

def grid(name, nrows, ncols, min_area=30):
    rgba, mask = key(U + name + '-image.jpg')
    m = clean_mask(mask, rgba[..., :3].astype(int).sum(axis=2) / 3)
    H, W = m.shape
    prof = m.sum(axis=1)
    # row bands: split at the nrows-1 emptiest horizontal gaps between content
    occ = prof > 2
    runs, y = [], 0
    while y < H:
        if occ[y]:
            s = y
            while y < H and occ[y]: y += 1
            runs.append([s, y])
        else: y += 1
    while len(runs) > nrows:  # merge the pair separated by the smallest gap
        gaps = [runs[i + 1][0] - runs[i][1] for i in range(len(runs) - 1)]
        i = int(np.argmin(gaps)); runs[i] = [runs[i][0], runs[i + 1][1]]; del runs[i + 1]
    assert len(runs) == nrows, (name, len(runs))
    lab, n = ndimage.label(m)
    objs = ndimage.find_objects(lab)
    cells = [[[] for _ in range(ncols)] for _ in range(nrows)]
    for i, sl in enumerate(objs):
        area = int((lab[sl] == i + 1).sum())
        if area < min_area: continue
        cy = (sl[0].start + sl[0].stop) / 2; cx = (sl[1].start + sl[1].stop) / 2
        r = next(k for k, (a, b) in enumerate(runs) if a - 4 <= cy <= b + 4) if any(a - 4 <= cy <= b + 4 for a, b in runs) else None
        if r is None: continue
        c = min(ncols - 1, int(cx / (W / ncols)))
        cells[r][c].append(i + 1)
    out = []
    for r in range(nrows):
        for c in range(ncols):
            ids = cells[r][c]
            if not ids: out.append(None); continue
            sel = np.isin(lab, ids)
            yy, xx = np.where(sel)
            y0, y1, x0, x1 = yy.min(), yy.max() + 1, xx.min(), xx.max() + 1
            crop = rgba[y0:y1, x0:x1].copy()
            crop[..., 3] = np.where(sel[y0:y1, x0:x1], 255, 0)
            body = ndimage.binary_erosion(sel[y0:y1, x0:x1], iterations=3)
            # horizontal anchor: the largest piece's centre (the character), not the trails
            sizes = [(int((lab == i).sum()), i) for i in ids]
            main = max(sizes)[1]
            mx = np.where(lab[y0:y1, x0:x1] == main)[1]
            out.append((Image.fromarray(crop, 'RGBA'), float(mx.mean()) if len(mx) else (x1 - x0) / 2))
    return out

def fill(frs, i, j):
    """missing frame i -> copy of j"""
    if frs[i] is None: frs[i] = frs[j]

HEROES = {'qi': 'dd9ad060', 'flame': '49303d01', 'body': '27ec067e', 'poison': '3e5e0ca7'}
PARTS = {  # 7x6: rows idle, strike, technique, special, guard+hit, defeat+victory, meditate+dash
    'idle': range(0, 6), 'attack': range(6, 12), 'palm': range(12, 18), 'special': range(18, 24),
    'defend': range(24, 27), 'hit': range(27, 30), 'defeat': range(30, 34), 'victory': range(34, 36),
    'meditate': range(36, 38), 'dash': range(39, 40),  # dash afterimages keep green fringes: one clean frame, the stage moves it
}

def heroes(only=None):
    for path, f in HEROES.items():
        if only and path not in only: continue
        frs = grid(f, 7, 6)
        for i, fr in enumerate(frs):
            if fr is None: frs[i] = frs[i - 1]
        # a frame that is only particles (no body): keep the particles over the previous pose
        ref = np.median([frs[i][0].size[1] for i in PARTS['idle']])
        scale = .72 * 256 / ref
        for part, idx in PARTS.items():
            strip(frs, list(idx), scale, False).save(f'{OUT}/anim_hero_{path}_{part}.png', optimize=True)
        print(path, round(scale, 2), [fr[0].size for fr in frs[:2]])

def qilin():
    frs = grid('87c771b2', 6, 3)
    ref = np.median([frs[i][0].size[1] for i in range(6)])
    scale = .62 * 256 / ref
    for part, idx in (('idle', range(0, 6)), ('attack', range(6, 12)), ('defeat', range(12, 18))):
        strip(frs, list(idx), scale, True).save(f'{OUT}/anim_storm_qilin_{part}.png', optimize=True)
    print('qilin', round(scale, 2))

def vfx():
    frs = grid('fb2c38f9', 6, 6, min_area=4)
    names = ['slash', 'qi_burst', 'fire_burst', 'poison_burst', 'shield_break', 'heal']
    for r, n in enumerate(names):
        row = frs[r * 6:(r + 1) * 6]
        big = max(max(f[0].size) for f in row if f)
        sc = min(1.6, 230 / big)
        im = Image.new('RGBA', (256 * 6, 256), (0, 0, 0, 0))
        for k, fr in enumerate(row):
            if fr is None: continue
            g = fr[0].resize((max(1, round(fr[0].size[0] * sc)), max(1, round(fr[0].size[1] * sc))), Image.LANCZOS)
            im.paste(g, (k * 256 + (256 - g.size[0]) // 2, (256 - g.size[1]) // 2), g)
        im.save(f'{OUT}/fx2_{n}.png', optimize=True)

TECH = {
    'palm_strike': (0, 0), 'sword_thrust': (0, 1), 'qi_blast': (0, 2), 'flying_daggers': (0, 3), 'iron_fist': (0, 4), 'flame_palm': (0, 5),
    'venom_needle': (1, 0), 'sword_qi_wave': (1, 1), 'flowing_water': (1, 2), 'iron_shirt': (1, 4),
    'cloud_step': (2, 2), 'mirror_palm': (2, 3), 'qi_siphon': (2, 5),
    'crane_beak_strike': (3, 0), 'shadow_dart': (3, 1), 'thunder_palm': (3, 5),
    'tiger_claw': (4, 0), 'mountain_splitter': (4, 2), 'heavenly_sword_rain': (4, 3), 'spirit_lance': (4, 4),
    'phoenix_wing': (5, 0), 'inferno_burst': (5, 2), 'scorpion_tail': (5, 3), 'miasma_cloud': (5, 4),
    'golden_bell': (6, 0), 'lotus_shield': (6, 1), 'ember_aura': (6, 2), 'smoke_veil': (6, 4), 'swallow_glide': (6, 5),
    'ghost_step': (7, 1), 'turtle_reversal': (7, 2), 'cloud_mirror': (7, 5),
    'heavenly_seal': (8, 0), 'vajra_body': (8, 1), 'phoenix_rebirth': (8, 2), 'thousand_poisons': (8, 3),
    'custom_attack': (9, 2), 'custom_defense': (9, 3), 'custom_movement': (9, 4),
}
MENU = {  # 4x6 sheet (rows 3 and 5 repeat 2 and 4)
    'menu_meditate': (0, 0), 'menu_explore': (0, 1), 'menu_battle': (0, 2), 'menu_hero': (0, 3),
    'menu_sect': (1, 0), 'icon_stones': (1, 1), 'icon_tokens': (1, 2), 'menu_notifications': (1, 3),
    'menu_quests': (2, 0), 'menu_settings': (2, 1), 'menu_ranking': (2, 2), 'menu_tower': (2, 3),
    'menu_worldboss': (4, 0), 'menu_chat': (4, 1), 'menu_pet': (4, 2), 'menu_forge': (4, 3),
}

def round_icon(fr, size):
    im = fr[0]
    s = max(im.size)
    sq = Image.new('RGBA', (s, s), (0, 0, 0, 0)); sq.paste(im, ((s - im.size[0]) // 2, (s - im.size[1]) // 2), im)
    return sq.resize((size, size), Image.LANCZOS)

def icons():
    t = grid('433b1304', 10, 6)
    for name, (r, c) in TECH.items():
        round_icon(t[r * 6 + c], 96).save(f'{OUT}/tech_{name}.png', optimize=True)
    m = grid('387b2b30', 6, 4)
    for name, (r, c) in MENU.items():
        size = 64 if name.startswith('icon_') else 72
        round_icon(m[r * 4 + c], size).save(f'{OUT}/{name}.png', optimize=True)

def pictures():
    a = Image.open(U + '6816f6aa-image.jpg').convert('RGB')
    a.crop((0, 0, 768, 452)).save(f'{OUT}/arena_tower.jpg', quality=88)
    for name, f, top in (('tile_menu_tower', 'd9930ffa', 400), ('tile_menu_worldboss', 'a2a70a82', 250)):
        im = Image.open(U + f + '-image.jpg').convert('RGB')
        im.crop((0, top, 768, top + 262)).save(f'{OUT}/{name}.jpg', quality=88)
        im.resize((384, 688)).save(f'{OUT}/{name}_full.jpg', quality=85)

if __name__ == '__main__':
    what = sys.argv[2:] or ['heroes', 'qilin', 'vfx', 'icons', 'pictures']
    for w in what: globals()[w]()

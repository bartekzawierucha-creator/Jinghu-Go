import numpy as np
from PIL import Image
from scipy import ndimage
from extract import key
U='/root/.claude/uploads/987109f3-131e-5dee-b953-b1b8eb54c846/'
NIGHT = (11, 14, 17)

def grid_cells(name, cols, rows, inset=0, fill=()):
    rgba, mask = key(U + name + '-image.jpg')
    orig = np.asarray(Image.open(U + name + '-image.jpg').convert('RGB'))
    m = ndimage.binary_opening(mask, iterations=1)
    H, W = m.shape
    cells = []
    for r in range(rows):
        for c in range(cols):
            y0, y1 = int(H * r / rows), int(H * (r + 1) / rows)
            x0, x1 = int(W * c / cols), int(W * (c + 1) / cols)
            sub = m[y0:y1, x0:x1]
            lab, n = ndimage.label(ndimage.binary_dilation(sub, iterations=3))
            sizes = ndimage.sum(sub, lab, range(1, n + 1))
            big = int(np.argmax(sizes)) + 1
            near = ndimage.binary_dilation(lab == big, iterations=14)
            ids = [i + 1 for i, s in enumerate(sizes) if s >= sizes.max() * 0.03 and (near & (lab == i + 1)).any()]
            keep = np.isin(lab, ids) & sub
            filled = ndimage.binary_fill_holes(keep) if len(cells) in fill else keep
            holes = filled & ~keep
            keep = filled
            yy, xx = np.where(keep)
            a, b, cc, d = yy.min(), yy.max() + 1, xx.min(), xx.max() + 1
            crop = rgba[y0 + a:y0 + b, x0 + cc:x0 + d].copy()
            hz = holes[a:b, cc:d]
            crop[..., :3][hz] = orig[y0 + a:y0 + b, x0 + cc:x0 + d][hz]
            crop[..., 3] = np.where(keep[a:b, cc:d], 255, 0)
            cells.append(Image.fromarray(crop, 'RGBA'))
    return cells

def fit(im, h):
    w = max(1, round(im.size[0] * h / im.size[1]))
    if w > h * 1.25:  # very wide: fit the width instead
        w = round(h * 1.25); h2 = max(1, round(im.size[1] * w / im.size[0]))
        return im.resize((w, h2), Image.LANCZOS)
    return im.resize((w, h), Image.LANCZOS)

def harden(im):
    a = np.asarray(im)[..., 3]
    im.putalpha(Image.fromarray(np.where(a > 110, 255, 0).astype(np.uint8)))
    return im

OUT = 'out/'
# ---- items sheet 1 (5x7 with repeats) and sheet 2 (5x5)
s1 = grid_cells('26c28363', 5, 7, fill=(1, 16))
s2 = grid_cells('7eedb556', 5, 5)
items1 = ['boar_tusk','viper_venom','bamboo_essence','fox_tail','fire_crystal','golem_core','spirit_silk','frost_silk','demon_blood','thunder_feather',
          'soul_jade','tortoise_shell','dragon_scale','blood_crystal','star_iron','lotus_seed','heaven_dew','foundation_pill','demon_core_pill','core_pill']
for i, n in enumerate(items1): harden(fit(s1[i], 96)).save(OUT + f'item_{n}.png', optimize=True)
for n, i in (('nascent_pill', 20), ('bamboo_staff', 21), ('boar_tusk_spear', 22), ('venom_hook', 33), ('fox_fire_fan', 34)):
    harden(fit(s1[i], 96)).save(OUT + f'item_{n}.png', optimize=True)
items2 = ['golem_gauntlets','frost_sword','thunder_blade','dragon_bone_sword','blood_demon_saber','boar_hide_vest','viper_scale_robe','fire_fox_cloak','golem_plate','frost_silk_robe',
          'dragon_scale_armor','bamboo_charm','fox_eye_amulet','golem_heart','soul_jade_pendant','dragon_pearl','iron_ring','fire_ring','frost_ring','thunder_ring',
          'void_ring','ancient_compass','tortoise_mirror','demon_bell','celestial_lotus']
for i, n in enumerate(items2): harden(fit(s2[i], 96)).save(OUT + f'item_{n}.png', optimize=True)

# ---- techniques (3x6): square 96x96 RGB on the night colour, like the existing tech icons
t = grid_cells('c2190b02', 3, 6)
tech = {'crane_beak_strike': 0, 'shadow_dart': 1, 'thunder_palm': 2, 'tiger_claw': 2, 'heavenly_sword_rain': 3, 'spirit_lance': 4, 'mountain_splitter': 5,
        'phoenix_wing': 6, 'inferno_burst': 7, 'scorpion_tail': 8, 'miasma_cloud': 9, 'golden_bell': 10, 'lotus_shield': 11, 'ember_aura': 12,
        'swallow_glide': 13, 'ghost_step': 14, 'turtle_reversal': 15, 'cloud_mirror': 16, 'smoke_veil': 17}
for n, i in tech.items():
    im = t[i]; side = max(im.size)
    sq = Image.new('RGBA', (side, side), NIGHT + (255,))
    sq.alpha_composite(im, ((side - im.size[0]) // 2, (side - im.size[1]) // 2))
    sq.convert('RGB').resize((96, 96), Image.LANCZOS).save(OUT + f'tech_{n}.png', optimize=True)

# ---- emblems / map markers / menu icons (4x6 with repeats)
e = grid_cells('25b74c86', 4, 6)
em = {'emblem_nascent_soul': (0, 128), 'emblem_soul_transformation': (1, 128), 'emblem_void_refinement': (2, 128), 'map_boss': (3, 160),
      'map_ore': (4, 160), 'map_ruins': (5, 160), 'map_shrine': (6, 160), 'map_lotus': (7, 160), 'map_bamboo': (8, 160),
      'menu_quests': (9, 72), 'menu_meridians': (10, 72), 'menu_ranking': (11, 72), 'menu_pet': (12, 72), 'menu_hero': (14, 72),
      'menu_checkin': (17, 72), 'map_cultivator': (19, 160)}
for n, (i, h) in em.items(): harden(fit(e[i], h)).save(OUT + n + '.png', optimize=True)
print('icons ok')

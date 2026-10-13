import numpy as np
from PIL import Image
from scipy import ndimage
from extract import key
from slice import strip
from slice2 import frames
U = '/root/.claude/uploads/987109f3-131e-5dee-b953-b1b8eb54c846/'
D = '/home/claude/jianghu-go/client/JianghuGo.Android/Resources/drawable-nodpi/'
NIGHT = (11, 14, 17)
def q(im, path):
    im.convert('RGBA').quantize(colors=256, method=Image.Quantize.FASTOCTREE, dither=Image.Dither.NONE).save(path, optimize=True)

# enemies v2
for npc, f, idle, atk, dfe, flip, frac in (('corpse_puppet', '379866dc', range(0, 6), range(6, 12), range(12, 18), False, .72),
                                          ('fire_fox', '17057d6b', range(0, 6), [6, 7, 8, 10, 11], range(12, 18), True, .44)):
    frs, _ = frames(f, [3] * 6)
    ref = np.median([frs[i][0].size[1] for i in idle])
    for part, idx in (('idle', idle), ('attack', atk), ('defeat', dfe)):
        im = strip(frs, list(idx), frac * 256 / ref, flip)
        im.save(f'out/anim_{npc}_{part}.png'); q(im, D + f'anim_{npc}_{part}.png')

# menu tile panels (5 stacked, black separators)
im = Image.open(U + '9861a764-image.jpg').convert('RGB')
a = np.asarray(im).astype(int).sum(axis=2).mean(axis=1); H = im.size[1]
cuts = [0] + [int(H * k / 5) - 40 + int(np.argmin(a[int(H * k / 5) - 40:int(H * k / 5) + 40])) for k in range(1, 5)] + [H]
for n, (y0, y1) in zip(['quests', 'meridians', 'ranking', 'pet', 'hero'], zip(cuts, cuts[1:])):
    im.crop((0, y0 + 6, 768, y1 - 6)).save(D + f'tile_menu_{n}.jpg', quality=86)
print('cuts', cuts)

# events + two techniques (3x4, labels under the last row are ignored: largest component only)
rgba, mask = key(U + '3dddb58b-image.jpg')
m = ndimage.binary_opening(mask, iterations=2)
Hh, W = m.shape
names = ['tech_tiger_claw', 'tech_smoke_veil', 'event_qi_storm', 'event_spirit_blessing', 'event_full_moon', 'event_spring_festival',
         'event_lantern_festival', 'event_heavenly_rain', 'event_meteor_shower', 'event_demon_invasion', 'event_ancestor_blessing', 'event_qi_tide']
for i, n in enumerate(names):
    r, c = divmod(i, 3)
    y0, y1, x0, x1 = int(Hh * r / 4), int(Hh * (r + 1) / 4), int(W * c / 3), int(W * (c + 1) / 3)
    sub = m[y0:y1, x0:x1]
    lab, k = ndimage.label(sub)
    big = lab == (int(np.argmax(ndimage.sum(sub, lab, range(1, k + 1)))) + 1)
    big = ndimage.binary_fill_holes(big)
    yy, xx = np.where(big)
    crop = rgba[y0 + yy.min():y0 + yy.max() + 1, x0 + xx.min():x0 + xx.max() + 1].copy()
    crop[..., 3] = np.where(big[yy.min():yy.max() + 1, xx.min():xx.max() + 1], 255, 0)
    ic = Image.fromarray(crop, 'RGBA')
    if n.startswith('tech_'):
        side = max(ic.size); sq = Image.new('RGBA', (side, side), NIGHT + (255,))
        sq.alpha_composite(ic, ((side - ic.size[0]) // 2, (side - ic.size[1]) // 2))
        sq.convert('RGB').resize((96, 96), Image.LANCZOS).save(D + n + '.png', optimize=True)
    else:
        ic = ic.resize((128, round(ic.size[1] * 128 / ic.size[0])), Image.LANCZOS)
        al = np.asarray(ic)[..., 3]; ic.putalpha(Image.fromarray(np.where(al > 110, 255, 0).astype(np.uint8)))
        q(ic, D + n + '.png'); ic.save('out/' + n + '.png')
print('ok')

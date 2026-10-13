import numpy as np
from PIL import Image
from scipy import ndimage
from extract import key
U = '/root/.claude/uploads/987109f3-131e-5dee-b953-b1b8eb54c846/'
rgba, mask = key(U + 'dac9a7ef-image.jpg')
m = ndimage.binary_opening(mask, structure=np.ones((5, 5)))
B = {'wild_boar': (0, 228, 0, 192), 'bamboo_viper': (0, 228, 192, 384), 'fire_fox': (0, 228, 384, 576), 'stone_golem': (0, 228, 576, 768),
     'demonic_cultivator': (228, 455, 0, 192), 'frost_spider': (228, 455, 192, 384), 'sect_traitor': (228, 455, 384, 576), 'thunder_eagle': (228, 455, 576, 768),
     'corpse_puppet': (455, 688, 0, 192), 'bandit_chief': (455, 688, 192, 480), 'ancient_tortoise': (688, 1050, 192, 576), 'jiao_dragon': (688, 1050, 576, 768),
     'blood_demon_lord': (1050, 1376, 0, 192)}
for nm, (y0, y1, x0, x1) in B.items():
    sub = m[y0 + 4:y1 - 4, x0 + 4:x1 - 4]
    lab, n = ndimage.label(ndimage.binary_dilation(sub, iterations=3))
    sizes = ndimage.sum(sub, lab, range(1, n + 1))
    big = int(np.argmax(sizes)) + 1
    near = ndimage.binary_dilation(lab == big, iterations=10)
    ids = [i + 1 for i, s in enumerate(sizes) if s > sizes.max() * 0.02 and (near & (lab == i + 1)).any()]
    keep = np.isin(lab, ids) & sub
    yy, xx = np.where(keep)
    a, b, c, d = yy.min(), yy.max() + 1, xx.min(), xx.max() + 1
    crop = rgba[y0 + 4 + a:y0 + 4 + b, x0 + 4 + c:x0 + 4 + d].copy()
    crop[..., 3] = np.where(keep[a:b, c:d], 255, 0)
    im = Image.fromarray(crop, 'RGBA')
    h = 200; w = round(im.size[0] * h / im.size[1])
    if w > 260: w = 260; h = round(im.size[1] * w / im.size[0])
    im = im.resize((w, h), Image.LANCZOS)
    al = np.asarray(im)[..., 3]; im.putalpha(Image.fromarray(np.where(al > 110, 255, 0).astype(np.uint8)))
    im.save(f'out/tile_{nm}.png', optimize=True)

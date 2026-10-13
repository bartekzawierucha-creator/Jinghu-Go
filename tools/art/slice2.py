import numpy as np
from PIL import Image
from scipy import ndimage
from extract import key
from slice import clean_mask, strip
U='/root/.claude/uploads/987109f3-131e-5dee-b953-b1b8eb54c846/'

def boxes(m, merge=3):
    lab, n = ndimage.label(ndimage.binary_dilation(m, iterations=merge))
    res = []
    for i, sl in enumerate(ndimage.find_objects(lab)):
        area = int((m[sl] & (lab[sl] == i + 1)).sum())
        res.append([sl[0].start, sl[0].stop, sl[1].start, sl[1].stop, area])
    return res

def dist(a, b):
    dy = max(0, max(a[0], b[0]) - min(a[1], b[1])); dx = max(0, max(a[2], b[2]) - min(a[3], b[3]))
    return (dx * dx + dy * dy) ** .5

def frames(name, rows, merge=3):
    rgba, mask = key(U + name + '-image.jpg')
    m = clean_mask(mask, rgba[..., :3].astype(int).sum(axis=2) / 3)
    rgba[..., 3] = np.where(m, 255, 0)
    bs = [b for b in boxes(m, merge) if b[4] > 40]
    med = np.median(sorted(b[4] for b in bs)[-sum(rows):])
    N = sum(rows)
    # attach small pieces (effects, debris, sparks) to the nearest big sprite
    big = [b for b in bs if b[4] >= med * 0.22]
    small = [b for b in bs if b[4] < med * 0.22]
    while len(big) > N:  # too many: the smallest "big" one is an effect too
        big.sort(key=lambda b: b[4]); small.append(big.pop(0))
    for s in small:
        t = min(big, key=lambda b: dist(b, s))
        if dist(t, s) > 120: continue
        t[0], t[1], t[2], t[3] = min(t[0], s[0]), max(t[1], s[1]), min(t[2], s[2]), max(t[3], s[3]); t[4] += s[4]
    # too few: split the widest (merged neighbours) at the emptiest column
    while len(big) < N:
        big.sort(key=lambda b: (b[3] - b[2]) / max(1, np.median([x[3] - x[2] for x in big])), reverse=True)
        b = big.pop(0)
        prof = m[b[0]:b[1], b[2]:b[3]].sum(axis=0)
        w = b[3] - b[2]; a, z = int(w * .25), int(w * .75)
        c = b[2] + a + int(np.argmin(prof[a:z]))
        big += [[b[0], b[1], b[2], c, b[4] // 2], [b[0], b[1], c, b[3], b[4] // 2]]
    # rows: by vertical centre, then left to right
    big.sort(key=lambda b: (b[0] + b[1]) / 2)
    order = []
    k = 0
    for n in rows:
        order += sorted(big[k:k + n], key=lambda b: b[2]); k += n
    out = []
    for b in order:
        sub = m[b[0]:b[1], b[2]:b[3]]
        yy, xx = np.where(sub)
        y0, y1, x0, x1 = b[0] + yy.min(), b[0] + yy.max() + 1, b[2] + xx.min(), b[2] + xx.max() + 1
        crop = rgba[y0:y1, x0:x1].copy()
        body = ndimage.binary_erosion(m[y0:y1, x0:x1], iterations=5)
        cx = np.where(body)[1].mean() if body.any() else (x1 - x0) / 2
        out.append((Image.fromarray(crop, 'RGBA'), cx))
    return out, order

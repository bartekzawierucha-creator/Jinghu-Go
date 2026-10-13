import numpy as np
from PIL import Image
from scipy import ndimage
from extract import key
U='/root/.claude/uploads/987109f3-131e-5dee-b953-b1b8eb54c846/'

def clean_mask(mask, img=None):
    m = ndimage.binary_opening(mask, iterations=1)
    # drop drawn ground lines: thin rows mostly covered
    H, W = m.shape
    lab, n = ndimage.label(m)
    for i, sl in enumerate(ndimage.find_objects(lab)):
        h = sl[0].stop - sl[0].start; w = sl[1].stop - sl[1].start
        if h <= 10 and w > W * 0.3: m[sl][lab[sl] == i + 1] = False
    rowfrac = m.mean(axis=1)
    for y in np.where(rowfrac > 0.55)[0]:
        r = np.concatenate([[0], m[y].astype(np.int8), [0]]); d = np.diff(r)
        longest = (np.where(d == -1)[0] - np.where(d == 1)[0]).max()
        dark = longest > W * 0.5 and (img is None or img[y][m[y]].mean() < 70)
        if dark and (rowfrac[max(0, y - 8)] < 0.45 or rowfrac[min(H - 1, y + 8)] < 0.45):  # a thin drawn line, not sprites
            m[y] = False
    return m

def cuts(profile, n, lo, hi):
    """n-1 cut positions near uniform splits of [lo,hi), each at the emptiest line nearby."""
    out = [lo]
    L = hi - lo
    for k in range(1, n):
        c = lo + L * k / n
        a, b = int(c - L / n * 0.35), int(c + L / n * 0.35)
        seg = profile[a:b]
        out.append(a + int(np.argmin(seg + np.linspace(0, 1e-3, len(seg)))) if len(seg) else int(c))
    out.append(hi)
    return out

def segment(profile, n, thr=2):
    occ = profile > thr
    segs = []
    y = 0
    L = len(profile)
    while y < L:
        if occ[y]:
            s = y
            while y < L and occ[y]: y += 1
            segs.append([s, y])
        else:
            y += 1
    mass = lambda sg: profile[sg[0]:sg[1]].sum()
    total = profile.sum()
    segs = [sg for sg in segs if mass(sg) > total * 0.002]
    while len(segs) > n:
        i = min(range(len(segs)), key=lambda k: mass(segs[k]))
        if i == 0: j = 1
        elif i == len(segs) - 1: j = i - 1
        else: j = i - 1 if segs[i][0] - segs[i-1][1] <= segs[i+1][0] - segs[i][1] else i + 1
        a, b = sorted((i, j))
        segs[a] = [segs[a][0], segs[b][1]]; del segs[b]
    while len(segs) < n:
        i = max(range(len(segs)), key=lambda k: segs[k][1] - segs[k][0])
        s, e = segs[i]; w = e - s
        a, b = s + int(w * 0.2), s + int(w * 0.8)
        c = a + int(np.argmin(profile[a:b]))
        segs[i:i+1] = [[s, c], [c, e]]
    return segs

def frames(name, rows):
    rgba, mask = key(U + name + '-image.jpg')
    m = clean_mask(mask)
    rgba[..., 3] = np.where(m, 255, 0)
    H, W = m.shape
    bands = segment(m.sum(axis=1).astype(float), len(rows))
    out = []
    for r, ncol in enumerate(rows):
        y0b, y1b = bands[r]
        band = m[y0b:y1b]
        cols = segment(band.sum(axis=0).astype(float), ncol, thr=0)
        ys = [y0b] * 2
        for c in range(ncol):
            cs = [cols[c][0], None]; cs.append(cols[c][1]); cs = [cols[c][0], cols[c][1]]
            sub = band[:, cs[0]:cs[1]]
            lab, n = ndimage.label(ndimage.binary_dilation(sub, iterations=2))
            if n == 0: out.append(None); continue
            sizes = ndimage.sum(sub, lab, range(1, n + 1))
            keep = np.isin(lab, [i + 1 for i, s in enumerate(sizes) if s >= max(60, sizes.max() * 0.02)]) & sub
            yy, xx = np.where(keep)
            y0, y1, x0, x1 = yy.min(), yy.max() + 1, xx.min(), xx.max() + 1
            crop = rgba[y0b + y0:y0b + y1, cs[0] + x0:cs[0] + x1].copy()
            crop[..., 3] = np.where(keep[y0:y1, x0:x1], crop[..., 3], 0)
            # anchor: x centre of mass of the densest (body) part
            body = ndimage.binary_erosion(keep[y0:y1, x0:x1], iterations=4)
            cx = (np.where(body)[1].mean() if body.any() else (x1 - x0) / 2)
            out.append((Image.fromarray(crop, 'RGBA'), cx))
    return out

def strip(frs, idx, scale, flip, cellw=320, cellh=256, foot=246):
    im = Image.new('RGBA', (cellw * len(idx), cellh), (0, 0, 0, 0))
    for k, i in enumerate(idx):
        f, cx = frs[i]
        w, h = f.size
        nw, nh = max(1, round(w * scale)), max(1, round(h * scale))
        g = f.resize((nw, nh), Image.LANCZOS)
        a = np.asarray(g)[..., 3]
        g.putalpha(Image.fromarray(np.where(a > 110, 255, 0).astype(np.uint8)))
        cxs = cx * scale
        if flip:
            g = g.transpose(Image.FLIP_LEFT_RIGHT); cxs = nw - cxs
        x = round(k * cellw + cellw / 2 - cxs)
        y = foot - nh
        tile = Image.new('RGBA', (cellw, cellh), (0, 0, 0, 0))
        tile.paste(g, (round(cellw / 2 - cxs), y), g)
        im.paste(tile, (k * cellw, 0), tile)
    return im

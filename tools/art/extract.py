import sys, numpy as np
from PIL import Image
from scipy import ndimage

def key(path):
    im = np.asarray(Image.open(path).convert('RGB')).astype(int)
    r,g,b = im[...,0],im[...,1],im[...,2]
    bg = (g > 120) & (g > r + 50) & (g > b + 50)
    alpha = (~bg).astype(np.uint8)*255
    # despill
    out = im.copy()
    spill = out[...,1] > np.maximum(out[...,0], out[...,2])
    out[...,1] = np.where(spill, np.maximum(out[...,0], out[...,2]), out[...,1])
    rgba = np.dstack([out, alpha]).astype(np.uint8)
    return rgba, ~bg

def components(mask, merge=6, min_area=400):
    m = ndimage.binary_opening(mask, iterations=1)
    lab, n = ndimage.label(ndimage.binary_dilation(m, iterations=merge))
    objs = ndimage.find_objects(lab)
    comps = []
    for i, sl in enumerate(objs):
        h = sl[0].stop - sl[0].start; w = sl[1].stop - sl[1].start
        area = (m[sl] & (lab[sl] == i+1)).sum()
        if area < min_area: continue
        if h < 12 and w > 200: continue  # drawn ground lines
        comps.append((sl[0].start, sl[0].stop, sl[1].start, sl[1].stop, area))
    return comps

def rows(comps, tol=60):
    comps = sorted(comps, key=lambda c: (c[0]+c[1])/2)
    out = []
    for c in comps:
        cy = (c[0]+c[1])/2
        if out and abs(cy - out[-1][0]) < tol:
            out[-1][1].append(c); out[-1][0] = np.mean([(x[0]+x[1])/2 for x in out[-1][1]])
        else:
            out.append([cy, [c]])
    return [sorted(r[1], key=lambda c: c[2]) for r in out]

if __name__ == '__main__':
    for p in sys.argv[1:]:
        rgba, mask = key(p)
        rs = rows(components(mask))
        print(p.split('/')[-1], [len(r) for r in rs])

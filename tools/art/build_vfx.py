"""1.4 light overlays from the uploaded effect videos (720x1280, black background = transparent light).
Every frame gets alpha = brightness and its colour un-premultiplied, so the strips draw with plain alpha blending.
Usage: build_vfx.py <dir with f_<video>/NNN.png frames> <out dir>"""
import sys, numpy as np
from PIL import Image
SRC, OUT = sys.argv[1], sys.argv[2]

def light(rgb, floor=20):
    peak = rgb.max(axis=2).astype(np.float32)
    a = np.clip((peak - floor) * 255 / (255 - floor), 0, 255)
    col = np.clip(rgb.astype(np.float32) * 255 / np.maximum(peak[..., None], 1), 0, 255)
    return np.dstack([np.where(a[..., None] > 0, col, 0), a]).astype(np.uint8)

def grab(video, idx, size, box=None, floor=20):
    out = []
    for i in idx:
        im = Image.open(f'{SRC}/f_{video}/{i:03d}.png').convert('RGB')
        if box: im = im.crop(box)
        out.append(light(np.asarray(im.resize(size, Image.LANCZOS)), floor))
    return out

def spread(a, b, n):
    return [round(a + (b - a) * k / (n - 1)) for k in range(n)]

def save(name, frames, q=60):
    h, w = frames[0].shape[:2]
    strip = Image.new('RGBA', (w * len(frames), h))
    for k, f in enumerate(frames): strip.paste(Image.fromarray(f, 'RGBA'), (k * w, 0))
    strip.save(f'{OUT}/{name}.webp', quality=q, alpha_quality=45, method=4)
    print(f'{name}: {len(frames)} x {w}x{h}')

EVERY4 = range(1, 97, 4)  # 24 frames of a 4 s clip
EVERY6 = range(1, 97, 6)  # 16 frames (loops and big one-shots: APK size)
# auras (loops) around the fighter
save('ovl_aura_qi', grab('e5ccbd37', EVERY6, (200, 356)))
save('ovl_aura_flame', grab('08a91d57', EVERY6, (200, 356)))
save('ovl_aura_body', grab('ed16f268', EVERY6, (200, 356)))
save('ovl_aura_poison', grab('b784fb6f', EVERY6, (200, 356)))
save('ovl_meditate', grab('2f492016', EVERY6, (200, 356)))
# one-shots
save('ovl_heal', grab('72fee473', EVERY4, (200, 356)))
save('ovl_breakthrough', grab('4df1c2d2', EVERY6, (270, 480)))
save('ovl_lightning', grab('89714a70', EVERY4, (200, 356), floor=24))
save('ovl_victory', grab('ce78b914', EVERY6, (270, 480)))
for name, (a, b) in {'white': (1, 19), 'crit': (21, 45), 'qi': (47, 67), 'heavy': (70, 96)}.items():
    save(f'ovl_hit_{name}', grab('da81caf3', spread(a, b, 9), (192, 192), box=(0, 280, 720, 1000)))
# status loops: stun, burn, weaken side by side in one clip
for name, (x0, x1) in {'stun': (0, 250), 'bleed': (250, 480), 'weaken': (480, 720)}.items():
    save(f'ovl_status_{name}', grab('d4ba7e57', range(1, 97, 6), (96, 192), box=(x0, 380, x1, 880), floor=20))
# screen overlays (loops)
save('ovl_petals', grab('cecbda7c', EVERY6, (270, 480)))
save('ovl_mist', grab('c9614c76', EVERY6, (270, 240), box=(0, 640, 720, 1280)))
save('ovl_rain', grab('650c0d4f', range(49, 97, 2), (270, 480), floor=20))
save('ovl_low_hp', grab('b9d5f97e', EVERY6, (180, 320), floor=6))
save('ovl_portal', grab('0cfbd40a', range(1, 193, 8), (220, 220), box=(0, 280, 720, 1000)))
save('ovl_shine', grab('8adeb136', EVERY4, (144, 256)))

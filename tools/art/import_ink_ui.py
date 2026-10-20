"""Ink UI icons/popups: ink-on-white -> alpha (unpremultiplied colour kept), cropped, WebP."""
import sys, numpy as np
from PIL import Image
U, OUT = sys.argv[1], sys.argv[2]
ICONS = {  # upload id -> name
 'f13d7911':'nav_home','364a3007':'nav_hero','9ad1b4fd':'nav_combat','6ab5ba13':'nav_world','2b254d36':'nav_sect',
 '3b4eef58':'nav_bag','e8828b66':'nav_social','b10563d4':'ico_quest','29107333':'ico_shop','d95a4f22':'ico_achievement',
 '25f0f0a5':'ico_event','b3985cdc':'ico_tower','d69756d3':'ico_beast','15273559':'ico_meridian','1a82445f':'ico_market',
 'a52443b8':'ico_alchemy','a31c92a5':'ico_forge','233c476d':'ico_settings','3fedce8b':'ico_ranking','9d75c4e7':'ico_weather',
 '86878104':'ico_journey','706b7442':'ico_dao','dc2d0f0f':'ico_place','5c53c6f2':'ico_arena','5133ca5f':'ico_hunt','bedc85a7':'ico_demon'}
POPUPS = {'4c6bea7e':'popup_technique','b3fa0883':'popup_chest','19f88e12':'popup_breakthrough'}
def alpha(img):
    a = np.asarray(img.convert('RGB')).astype(np.float32)
    m = a.min(axis=2)
    al = np.clip((242 - m) / 190, 0, 1)
    rgb = np.clip((a - 255 * (1 - al[..., None])) / np.maximum(al[..., None], 1e-3), 0, 255)
    return np.dstack([rgb, al * 255]).astype(np.uint8)
def crop(arr, pad):
    ys, xs = np.where(arr[..., 3] > 40)
    y0, y1, x0, x1 = ys.min(), ys.max(), xs.min(), xs.max()
    s = max(y1 - y0, x1 - x0) // 2 + pad; cy, cx = (y0 + y1) // 2, (x0 + x1) // 2
    im = Image.fromarray(arr, 'RGBA'); return im.crop((cx - s, cy - s, cx + s, cy + s))
for k, n in ICONS.items():
    crop(alpha(Image.open(f'{U}/{k}-image.jpg')), 6).resize((192, 192), Image.LANCZOS).save(f'{OUT}/ink_{n}.webp', quality=88, method=4)
for k, n in POPUPS.items():
    im = Image.fromarray(alpha(Image.open(f'{U}/{k}-image.jpg')), 'RGBA'); im = im.crop(im.getbbox())
    im.thumbnail((640, 900)); im.save(f'{OUT}/ink_{n}.webp', quality=85, method=4)
# logo: keep the paper scroll opaque, crop to scroll on transparent
L = Image.open(f'{U}/8d3229c4-image.jpg').convert('RGB'); a = np.asarray(L).astype(int)
mask = (np.abs(a - 255).sum(2) > 18); ys, xs = np.where(mask)
L.crop((xs.min(), ys.min(), xs.max(), ys.max())).resize((560, int(560 * (ys.max() - ys.min()) / (xs.max() - xs.min()))), Image.LANCZOS).save(f'{OUT}/ink_logo.webp', quality=88)
print('ok')

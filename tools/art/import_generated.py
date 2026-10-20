"""
Cuts the generated art sheets (painterly xianxia set, 768x1376 each) into the game's drawables.

Usage: python3 import_generated.py <out drawable dir> items=<sheet> techs=<sheet> npcs=<sheet> arena_forest=<img> hero_qi=<img>
       map=<img> key_art=<img>
Each argument is optional; only the given sheets are imported. Grid positions were measured on the delivered sheets.
"""
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageEnhance, ImageFilter

# item sheet: 4 columns × 7 rows of dark discs (diameter ~172)
ITEM_COLS = [93, 286, 480, 672]
ITEM_ROWS = [105, 299, 494, 690, 884, 1079, 1274]
ITEMS = {
    (0, 0): "spider_venom", (0, 1): "scorpion_venom", (0, 2): "toad_venom", (0, 3): "nightshade",
    (1, 0): "ghost_orchid", (1, 1): "frost_silk_venom", (1, 2): "blood_lotus_sap", (1, 3): "thunder_eel_venom",
    (2, 0): "jiao_bile", (2, 1): "seven_step_snake", (2, 2): "heart_eater_root", (2, 3): "phoenix_ash_venom",
    (3, 0): "sword_echo", (3, 1): "breakthrough_pill_mortal", (3, 2): "breakthrough_pill_immortal", (3, 3): "breakthrough_pill_saint",
    (4, 2): "breakthrough_pill_primal", (4, 3): "breakthrough_pill_divine",
    (5, 1): "manual_storm_heaven", (5, 2): "manual_thousand_harvest",
    (6, 0): "herb_seed", (6, 1): "lotus_seedling", (6, 2): "phoenix_ash_ring", (6, 3): "formation_five_elements",
}

# technique sheet: 5 columns × 7 rows of framed cards
TECH_COLS = [(5, 147), (159, 302), (313, 456), (467, 610), (622, 764)]
TECH_ROWS = [(12, 164), (209, 374), (414, 589), (601, 776), (787, 955), (965, 1137), (1147, 1320)]
TECHS = [
    ["star_piercing_finger", "dragon_elephant_fist", "nine_suns_palm", "corpse_flower_sting", "void_step_guard"],
    ["heaven_sword_array", "immovable_mountain", "phoenix_nirvana", "gu_swarm", "star_mirror"],
    ["sword_intent_wave", "titan_crush", "sky_burning_lotus", "heart_rot_needle", "primordial_shell"],
    [None] * 5,  # alternates of row 3
    ["thousand_world_seal", "vajra_reincarnation", "world_ending_flame", "ten_thousand_gu", "karma_reversal"],
    [None] * 5,  # alternates of row 5
    ["dao_severing_sword", "chaos_body_strike", "first_dawn_ray", "myriad_venom_rain", "dao_barrier"],
]

# npc sheet: 2×2 portraits with captions under each; (box, tile name) — tiles are landscape 1.64:1, centre-cropped in lists
NPCS = {
    "tile_dummy": (0, 60, 384, 294),
    "tile_wolf": (384, 290, 768, 524),
    "tile_bandit": (0, 800, 384, 1034),
    "tile_wild_boar": (384, 920, 768, 1154),
}


def soft_circle(size: int, inset: int) -> Image.Image:
    big = size * 4
    m = Image.new("L", (big, big), 0)
    ImageDraw.Draw(m).ellipse((inset * 4, inset * 4, big - inset * 4, big - inset * 4), fill=255)
    return m.resize((size, size), Image.LANCZOS).filter(ImageFilter.GaussianBlur(1))


def rounded(size: int, radius: int) -> Image.Image:
    big = size * 4
    m = Image.new("L", (big, big), 0)
    ImageDraw.Draw(m).rounded_rectangle((0, 0, big - 1, big - 1), radius * 4, fill=255)
    return m.resize((size, size), Image.LANCZOS)


def items(sheet: Image.Image, out: Path) -> None:
    for (r, c), name in ITEMS.items():
        cx, cy, half = ITEM_COLS[c], ITEM_ROWS[r], 86
        icon = sheet.crop((cx - half, cy - half, cx + half, cy + half)).resize((128, 128), Image.LANCZOS).convert("RGBA")
        icon.putalpha(soft_circle(128, 2))
        icon.save(out / f"item_{name}.png", optimize=True)


def techs(sheet: Image.Image, out: Path) -> None:
    for r, row in enumerate(TECHS):
        for c, name in enumerate(row):
            if name is None:
                continue
            (x0, x1), (y0, y1) = TECH_COLS[c], TECH_ROWS[r]
            inset = 9  # inside the gold frame
            w = x1 - x0 - 2 * inset
            cy = (y0 + y1) // 2
            card = sheet.crop((x0 + inset, cy - w // 2, x0 + inset + w, cy - w // 2 + w)).resize((128, 128), Image.LANCZOS).convert("RGBA")
            card.putalpha(rounded(128, 16))
            card.save(out / f"tech_{name}.png", optimize=True)


def npcs(sheet: Image.Image, out: Path) -> None:
    for name, box in NPCS.items():
        sheet.crop(box).convert("RGB").resize((396, 242), Image.LANCZOS).save(out / f"{name}.png", optimize=True)


def main() -> None:
    out = Path(sys.argv[1])
    args = dict(a.split("=", 1) for a in sys.argv[2:])
    if "items" in args:
        items(Image.open(args["items"]).convert("RGB"), out)
    if "techs" in args:
        techs(Image.open(args["techs"]).convert("RGB"), out)
    if "npcs" in args:
        npcs(Image.open(args["npcs"]).convert("RGB"), out)
    if "arena_forest" in args:  # portrait source → the battle stage's landscape band around the path
        Image.open(args["arena_forest"]).convert("RGB").crop((0, 560, 768, 1012)).save(out / "arena_bamboo.jpg", quality=88)
    if "hero_qi" in args:
        Image.open(args["hero_qi"]).convert("RGB").crop((205, 45, 535, 375)).resize((384, 384), Image.LANCZOS).save(out / "portrait_qi.jpg", quality=90)
    if "map" in args:  # parchment toned down a little so glowing markers keep their contrast
        m = Image.open(args["map"]).convert("RGB")
        m = ImageEnhance.Brightness(m).enhance(0.82)
        m.save(out / "map_texture.jpg", quality=86)
    for bg in ("bg_login", "bg_sect", "bg_forge", "bg_estate", "bg_meditation", "bg_market", "bg_hero"):
        if bg in args:  # full-screen backdrops (9:16)
            Image.open(args[bg]).convert("RGB").save(out / f"{bg}.jpg", quality=86)
    if "map_night" in args:
        Image.open(args["map_night"]).convert("RGB").save(out / "map_texture_night.jpg", quality=86)
    if "arena_forest_v2" in args:  # portrait source with open ground at the bottom
        Image.open(args["arena_forest_v2"]).convert("RGB").crop((0, 860, 768, 1312)).save(out / "arena_bamboo.jpg", quality=88)
    # portrait (9:16) sources → the battle stage's landscape band (768x452) where the fighters stand
    bands = {"arena_city": (0, 760, 768, 1212), "arena_swamp": (0, 720, 768, 1172), "arena_ruins": (0, 2, 768, 454),
             "arena_plains": (0, 520, 768, 972), "arena_tortoise_lake": (0, 560, 768, 1012)}
    for name, box in bands.items():
        if name in args:
            Image.open(args[name]).convert("RGB").crop(box).save(out / f"{name}.jpg", quality=88)
    # enemy tiles (landscape 1.64:1) and hero avatars (square, around the face)
    crops = {"tile_bamboo_viper": ((0, 280, 768, 748), (396, 242)), "tile_fire_fox": ((0, 560, 768, 1028), (396, 242)),
             "tile_blood_demon_lord": ((0, 190, 768, 658), (396, 242)), "tile_jiao_dragon": ((0, 240, 768, 708), (396, 242)),
             "tile_ancient_tortoise": ((0, 420, 768, 888), (396, 242)), "tile_bandit_chief": ((0, 200, 768, 668), (396, 242)),
             "tile_corpse_puppet": ((0, 180, 768, 648), (396, 242)), "tile_thunder_eagle": ((0, 400, 768, 868), (396, 242)),
             "tile_frost_spider": ((0, 320, 768, 788), (396, 242)), "tile_sect_traitor": ((0, 130, 768, 598), (396, 242)),
             "tile_demonic_cultivator": ((0, 150, 768, 618), (396, 242)), "tile_stone_golem": ((0, 250, 768, 718), (396, 242)),
             "portrait_poison": ((220, 170, 520, 470), (384, 384)), "portrait_flame": ((240, 150, 540, 450), (384, 384)),
             "portrait_body": ((240, 100, 540, 400), (384, 384))}
    for name, (box, size) in crops.items():
        if name in args:
            img = Image.open(args[name]).convert("RGB").crop(box).resize(size, Image.LANCZOS)
            img.save(out / (f"{name}.png" if name.startswith("tile_") else f"{name}.jpg"), **({"optimize": True} if name.startswith("tile_") else {"quality": 90}))
    if "key_art" in args:
        Image.open(args["key_art"]).convert("RGB").save(out / "key_art.jpg", quality=88)


if __name__ == "__main__":
    main()

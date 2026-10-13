#!/usr/bin/env python3
"""Content pack 2 (MVP 1.1): adds traits, techniques, NPCs and bosses, items, recipes, loot, realms, world nodes and
events to content/game-config. Idempotent: entries whose id already exists are left as they are.

Run: python3 tools/content/expand_content.py   (then the server's config validators check everything at startup)
"""
import json
import os

ROOT = os.path.join(os.path.dirname(__file__), "..", "..", "content", "game-config")


def load(name):
    with open(os.path.join(ROOT, name), encoding="utf-8") as f:
        return json.load(f)


def dump(name, data, list_keys):
    """Pretty JSON with one list entry per line for the long content lists (diff-friendly)."""
    def enc(v):
        return json.dumps(v, ensure_ascii=False)

    lines = ["{"]
    keys = list(data.keys())
    for i, k in enumerate(keys):
        v = data[k]
        comma = "," if i < len(keys) - 1 else ""
        if k in list_keys and isinstance(v, list):
            lines.append(f'  "{k}": [')
            for j, item in enumerate(v):
                lines.append("    " + enc(item) + ("," if j < len(v) - 1 else ""))
            lines.append("  ]" + comma)
        elif k in list_keys and isinstance(v, dict):
            lines.append(f'  "{k}": {{')
            items = list(v.items())
            for j, (kk, vv) in enumerate(items):
                lines.append(f'    "{kk}": ' + enc(vv) + ("," if j < len(items) - 1 else ""))
            lines.append("  }" + comma)
        else:
            lines.append(f'  "{k}": ' + enc(v) + comma)
    lines.append("}")
    with open(os.path.join(ROOT, name), "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")


def add(lst, entries, key="id"):
    have = {e[key] for e in lst}
    for e in entries:
        if e[key] not in have:
            lst.append(e)


# ---- character creation: 8 more traits per category (all rarities stay covered) ------------------------------
cc = load("character-creation.json")
T = lambda i, cat, r, pl, en, mods: {"id": i, "category": cat, "rarity": r, "namePl": pl, "nameEn": en, "modifiers": mods}
add(cc["traits"], [
    T("bg_fisher_village", "background", "common", "Wioska rybacka", "Fishing Village", {"constitution": 1, "willpower": 1}),
    T("bg_wandering_merchant", "background", "common", "Syn wędrownego kupca", "Wandering Merchant's Son", {"insight": 1, "fate": 1}),
    T("bg_temple_orphan", "background", "rare", "Sierota ze świątyni", "Temple Orphan", {"willpower": 2, "spirit": 2}),
    T("bg_imperial_guard", "background", "rare", "Dziecko gwardii cesarskiej", "Imperial Guard's Child", {"strength": 2, "constitution": 2}),
    T("bg_alchemist_apprentice", "background", "epic", "Uczeń alchemika", "Alchemist's Apprentice", {"insight": 3, "elementalAffinity": 2, "focus": 1}),
    T("bg_assassin_guild", "background", "epic", "Wychowanek gildii skrytobójców", "Assassin Guild Ward", {"agility": 4, "focus": 2}),
    T("bg_demon_sealer", "background", "legendary", "Ród pieczętujących demony", "Demon Sealer Lineage", {"spirit": 4, "willpower": 3, "insight": 1}),
    T("bg_fallen_immortal", "background", "mythic", "Upadły nieśmiertelny", "Fallen Immortal", {"spirit": 4, "insight": 3, "fate": 3}),
    T("bl_river_folk", "bloodline", "common", "Krew ludu rzeki", "River Folk Blood", {"agility": 1, "constitution": 1}),
    T("bl_steppe_rider", "bloodline", "common", "Krew jeźdźców stepu", "Steppe Rider Blood", {"agility": 2}),
    T("bl_iron_ox", "bloodline", "rare", "Krew Żelaznego Wołu", "Iron Ox Blood", {"strength": 2, "constitution": 2}),
    T("bl_moon_rabbit", "bloodline", "rare", "Krew Księżycowego Zająca", "Moon Rabbit Blood", {"agility": 2, "fate": 2}),
    T("bl_black_tortoise", "bloodline", "epic", "Krew Czarnego Żółwia", "Black Tortoise Blood", {"constitution": 4, "willpower": 2}),
    T("bl_vermilion_bird", "bloodline", "epic", "Krew Cynobrowego Ptaka", "Vermilion Bird Blood", {"spirit": 3, "elementalAffinity": 3}),
    T("bl_white_tiger", "bloodline", "legendary", "Krew Białego Tygrysa", "White Tiger Blood", {"strength": 4, "agility": 3, "willpower": 1}),
    T("bl_qilin", "bloodline", "mythic", "Krew Qilina", "Qilin Blood", {"fate": 4, "spirit": 3, "insight": 3}),
    T("co_frail_body", "constitution", "common", "Wątłe ciało, bystry umysł", "Frail Body, Sharp Mind", {"insight": 2, "constitution": -1}),
    T("co_warm_blood", "constitution", "common", "Gorąca krew", "Warm Blood", {"strength": 1, "elementalAffinity": 1}),
    T("co_jade_bones", "constitution", "rare", "Jadeitowe kości", "Jade Bones", {"constitution": 3, "spirit": 1}),
    T("co_wind_meridians", "constitution", "rare", "Meridiany wiatru", "Wind Meridians", {"agility": 3, "focus": 1}),
    T("co_poison_immune", "constitution", "epic", "Ciało odporne na trucizny", "Poison-Immune Body", {"constitution": 3, "willpower": 2, "elementalAffinity": 1}),
    T("co_thunder_veins", "constitution", "epic", "Żyły gromu", "Thunder Veins", {"strength": 2, "spirit": 2, "agility": 2}),
    T("co_dao_heart", "constitution", "legendary", "Serce Dao", "Dao Heart", {"insight": 4, "willpower": 4}),
    T("co_chaos_body", "constitution", "mythic", "Ciało Pierwotnego Chaosu", "Primordial Chaos Body", {"strength": 3, "spirit": 3, "constitution": 3, "agility": 2}),
])
dump("character-creation.json", cc, {"traits", "stats"})

# ---- combat: techniques and NPCs ---------------------------------------------------------------------------------
cb = load("combat.json")


def atk(i, pl, en, power, scaling, rmin, rmax, qi, cd, speed=0, acc=0, path=None, fx=None):
    a = {"power": power, "scaling": scaling, "rangeMin": rmin, "rangeMax": rmax, "accuracyPermille": acc}
    if fx:
        a["effect"] = {"type": fx[0], "chancePermille": fx[1], "magnitude": fx[2], "duration": fx[3]}
    t = {"id": i, "namePl": pl, "nameEn": en, "slot": "attack", "qiCost": qi, "cooldown": cd, "speed": speed}
    if path:
        t["path"] = path
    t["attack"] = a
    return t


def other(i, pl, en, slot, spec, qi, cd, speed=0, path=None):
    t = {"id": i, "namePl": pl, "nameEn": en, "slot": slot, "qiCost": qi, "cooldown": cd, "speed": speed}
    if path:
        t["path"] = path
    t[slot] = spec
    return t


add(cb["techniques"], [
    atk("crane_beak_strike", "Dziób Żurawia", "Crane Beak Strike", 14, "agility", 1, 1, 2, 0, 1, 30),
    atk("shadow_dart", "Cienista Strzałka", "Shadow Dart", 9, "agility", 2, 3, 3, 0, 2, 60),
    atk("thunder_palm", "Gromowa Dłoń", "Thunder Palm", 18, "strength", 1, 1, 10, 2, 0, 0, None, ("stun", 200, 0, 1)),
    atk("tiger_claw", "Pazur Tygrysa", "Tiger Claw", 20, "strength", 1, 1, 6, 1, 0, 20, "body", ("bleed", 400, 3, 2)),
    atk("mountain_splitter", "Rozłupanie Góry", "Mountain Splitter", 30, "strength", 1, 1, 14, 3, -1, 0, "body", ("stun", 250, 0, 1)),
    atk("heavenly_sword_rain", "Deszcz Niebiańskich Mieczy", "Heavenly Sword Rain", 18, "spirit", 2, 3, 14, 2, 0, 0, "qi", ("weaken", 250, 150, 2)),
    atk("spirit_lance", "Duchowa Włócznia", "Spirit Lance", 24, "spirit", 1, 3, 16, 2, -1, 10, "qi"),
    atk("phoenix_wing", "Skrzydło Feniksa", "Phoenix Wing", 20, "spirit", 1, 2, 12, 2, 0, 0, "flame", ("bleed", 500, 5, 2)),
    atk("inferno_burst", "Wybuch Piekielnego Ognia", "Inferno Burst", 32, "spirit", 1, 1, 20, 3, -2, 0, "flame", ("bleed", 300, 6, 3)),
    atk("scorpion_tail", "Ogon Skorpiona", "Scorpion Tail", 12, "agility", 1, 2, 8, 1, 1, 20, "poison", ("bleed", 800, 6, 3)),
    atk("miasma_cloud", "Chmura Miazmatu", "Miasma Cloud", 8, "spirit", 1, 3, 10, 2, 0, 50, "poison", ("weaken", 600, 250, 3)),
    other("golden_bell", "Złoty Dzwon", "Golden Bell", "defense", {"blockPermille": 700}, 8, 2, 0, "body"),
    other("lotus_shield", "Tarcza Lotosu", "Lotus Shield", "defense", {"blockPermille": 450}, 3, 0, 0, "qi"),
    other("ember_aura", "Aura Żaru", "Ember Aura", "defense", {"blockPermille": 500}, 5, 1, 0, "flame"),
    other("smoke_veil", "Zasłona Dymu", "Smoke Veil", "defense", {"blockPermille": 400}, 2, 0, 1, "poison"),
    other("swallow_glide", "Lot Jaskółki", "Swallow Glide", "movement", {"evasionPermille": 300}, 5, 1, 3),
    other("ghost_step", "Krok Ducha", "Ghost Step", "movement", {"evasionPermille": 400}, 8, 2, 3, "poison"),
    other("turtle_reversal", "Odwrócenie Żółwia", "Turtle Reversal", "counter", {"reflectPermille": 600, "qiSteal": 0}, 6, 2, 0, "body"),
    other("cloud_mirror", "Lustro Chmur", "Cloud Mirror", "counter", {"reflectPermille": 350, "qiSteal": 8}, 5, 2, 0, "qi"),
])


def S(t, s, a, c, i=None, w=None, f=None, e=None, fate=None):
    return {"strength": t, "spirit": s, "agility": a, "constitution": c, "insight": i or (s + a) // 2,
            "willpower": w or (t + c) // 2, "focus": f or a // 2 + 4, "elementalAffinity": e or s // 3, "fate": fate or 3}


def L(*ids):
    """8 slots: (id, level) or None, in the fixed layout attack×4, defense×2, movement, counter."""
    return [None if x is None else {"id": x[0], "level": x[1]} for x in ids]


def npc(i, pl, en, qi, stats, loadout, tier, hp=None, boss=False, world_only=False, tameable=False):
    n = {"id": i, "namePl": pl, "nameEn": en}
    if hp:
        n["hpOverride"] = hp
    n.update({"rewardQi": qi, "stats": stats, "loadout": loadout, "tier": tier})
    if boss:
        n["boss"] = True
    if world_only:
        n["worldOnly"] = True
    if tameable:
        n["tameable"] = True
    return n


for n in cb["npcs"]:
    n.setdefault("tier", 1)
    if n["id"] == "grey_wolf":
        n["tameable"] = True

add(cb["npcs"], [
    npc("wild_boar", "Duchowy Dzik", "Spirit Boar", 50, S(16, 5, 9, 16), L(("palm_strike", 2), ("crane_beak_strike", 2), None, None, ("flowing_water", 2), None, None, ("turtle_reversal", 1)), 1, tameable=True),
    npc("bamboo_viper", "Bambusowa Żmija", "Bamboo Viper", 55, S(9, 10, 17, 9), L(("crane_beak_strike", 2), ("scorpion_tail", 2), ("shadow_dart", 1), None, ("smoke_veil", 1), None, ("ghost_step", 1), None), 1, tameable=True),
    npc("fire_fox", "Ognisty Lis", "Fire Fox", 90, S(12, 18, 18, 12, fate=6), L(("phoenix_wing", 3), ("flame_palm", 3), ("shadow_dart", 2), None, ("ember_aura", 2), None, ("swallow_glide", 2), ("mirror_palm", 2)), 2, tameable=True),
    npc("stone_golem", "Kamienny Golem", "Stone Golem", 110, S(22, 8, 6, 24), L(("mountain_splitter", 3), ("thunder_palm", 2), ("palm_strike", 3), None, ("golden_bell", 3), ("iron_shirt", 3), None, ("turtle_reversal", 2)), 2, hp=320),
    npc("demonic_cultivator", "Demoniczny Kultywator", "Demonic Cultivator", 160, S(20, 24, 18, 18, fate=5), L(("spirit_lance", 4), ("miasma_cloud", 4), ("heavenly_sword_rain", 3), ("qi_blast", 4), ("lotus_shield", 3), ("smoke_veil", 3), ("ghost_step", 3), ("qi_siphon", 4)), 3),
    npc("frost_spider", "Lodowa Pajęczyca", "Frost Spider", 150, S(18, 20, 24, 16), L(("scorpion_tail", 4), ("venom_needle", 4), ("shadow_dart", 4), None, ("smoke_veil", 3), None, ("ghost_step", 4), ("cloud_mirror", 3)), 3, tameable=True),
    npc("sect_traitor", "Zdrajca Sekty", "Sect Traitor", 170, S(24, 20, 22, 20), L(("sword_qi_wave", 5), ("sword_thrust", 5), ("thunder_palm", 4), ("flying_daggers", 4), ("iron_shirt", 4), ("flowing_water", 4), ("cloud_step", 4), ("mirror_palm", 4)), 3),
    npc("thunder_eagle", "Gromowy Orzeł", "Thunder Eagle", 230, S(26, 28, 32, 22, fate=6), L(("thunder_palm", 6), ("heavenly_sword_rain", 5), ("shadow_dart", 6), ("crane_beak_strike", 6), ("lotus_shield", 5), None, ("swallow_glide", 6), ("cloud_mirror", 5)), 4, tameable=True),
    npc("corpse_puppet", "Trupia Marionetka", "Corpse Puppet", 210, S(32, 18, 16, 34), L(("mountain_splitter", 6), ("tiger_claw", 6), ("palm_strike", 6), None, ("golden_bell", 6), ("iron_shirt", 6), None, ("turtle_reversal", 5)), 4, hp=520),
    npc("bandit_chief", "Herszt Bandytów", "Bandit Chief", 260, S(24, 16, 20, 24, fate=6), L(("sword_thrust", 5), ("thunder_palm", 4), ("flying_daggers", 5), ("tiger_claw", 4), ("iron_shirt", 5), ("flowing_water", 5), ("cloud_step", 4), ("mirror_palm", 4)), 2, hp=360, boss=True, world_only=True),
    npc("ancient_tortoise", "Prastary Żółw", "Ancient Tortoise", 420, S(28, 30, 10, 40, w=30), L(("mountain_splitter", 6), ("spirit_lance", 6), ("palm_strike", 6), None, ("golden_bell", 7), ("lotus_shield", 7), None, ("turtle_reversal", 7)), 3, hp=700, boss=True, world_only=True, tameable=True),
    npc("jiao_dragon", "Smok Jiao", "Jiao Dragon", 700, S(40, 42, 34, 40, fate=8), L(("spirit_lance", 8), ("inferno_burst", 7), ("heavenly_sword_rain", 8), ("thunder_palm", 7), ("golden_bell", 7), ("ember_aura", 7), ("swallow_glide", 7), ("cloud_mirror", 7)), 4, hp=900, boss=True, world_only=True),
    npc("blood_demon_lord", "Władca Krwawych Demonów", "Blood Demon Lord", 1200, S(48, 50, 40, 48, fate=10), L(("inferno_burst", 10), ("miasma_cloud", 9), ("spirit_lance", 10), ("tiger_claw", 9), ("golden_bell", 9), ("smoke_veil", 9), ("ghost_step", 9), ("qi_siphon", 10)), 5, hp=1300, boss=True, world_only=True),
])
dump("combat.json", cb, {"techniques", "npcs"})

# ---- items ---------------------------------------------------------------------------------------------------------
it = load("items.json")
M = lambda i, pl, en, r: {"id": i, "namePl": pl, "nameEn": en, "type": "material", "rarity": r, "tradable": True}
C = lambda i, pl, en, r, qi: {"id": i, "namePl": pl, "nameEn": en, "type": "consumable", "rarity": r, "tradable": True, "consumable": {"qi": qi}}


def E(i, pl, en, r, slot, stats, bag=0):
    eq = {"slot": slot, "stats": stats}
    if bag:
        eq["bagBonus"] = bag
    return {"id": i, "namePl": pl, "nameEn": en, "type": "equipment", "rarity": r, "tradable": True, "equipment": eq}


add(it["items"], [
    # materials (tier 1 → 5)
    M("boar_tusk", "Kieł Dzika", "Boar Tusk", "mortal"),
    M("viper_venom", "Jad Żmii", "Viper Venom", "spirit"),
    M("bamboo_essence", "Esencja Bambusa", "Bamboo Essence", "mortal"),
    M("fox_tail", "Ogon Ognistego Lisa", "Fire Fox Tail", "spirit"),
    M("fire_crystal", "Kryształ Ognia", "Fire Crystal", "earth"),
    M("golem_core", "Rdzeń Golema", "Golem Core", "earth"),
    M("spirit_silk", "Duchowy Jedwab", "Spirit Silk", "spirit"),
    M("frost_silk", "Lodowy Jedwab", "Frost Silk", "earth"),
    M("demon_blood", "Krew Demona", "Demon Blood", "earth"),
    M("thunder_feather", "Gromowe Pióro", "Thunder Feather", "heavenly"),
    M("soul_jade", "Jadeit Duszy", "Soul Jade", "heavenly"),
    M("tortoise_shell", "Skorupa Prastarego Żółwia", "Ancient Tortoise Shell", "heavenly"),
    M("dragon_scale", "Łuska Smoka", "Dragon Scale", "divine"),
    M("blood_crystal", "Krwawy Kryształ", "Blood Crystal", "divine"),
    M("star_iron", "Gwiezdne Żelazo", "Star Iron", "heavenly"),
    M("lotus_seed", "Nasienie Złotego Lotosu", "Golden Lotus Seed", "earth"),
    # consumables
    C("heaven_dew", "Niebiańska Rosa", "Heaven Dew", "spirit", 150),
    C("foundation_pill", "Pigułka Fundamentu", "Foundation Pill", "earth", 600),
    C("demon_core_pill", "Pigułka z Rdzenia Demona", "Demon Core Pill", "earth", 900),
    C("core_pill", "Pigułka Złotego Rdzenia", "Golden Core Pill", "heavenly", 1500),
    C("nascent_pill", "Pigułka Narodzin Duszy", "Nascent Soul Pill", "divine", 4000),
    # weapons
    E("bamboo_staff", "Bambusowy Kij", "Bamboo Staff", "mortal", "weapon", {"agility": 2, "focus": 1}),
    E("boar_tusk_spear", "Włócznia z Kła Dzika", "Boar Tusk Spear", "spirit", "weapon", {"strength": 4, "constitution": 1}),
    E("venom_hook", "Zatruty Hak", "Venom Hook", "spirit", "weapon", {"agility": 3, "elementalAffinity": 2}),
    E("fox_fire_fan", "Wachlarz Lisiego Ognia", "Fox-Fire Fan", "earth", "weapon", {"spirit": 5, "elementalAffinity": 3}),
    E("golem_gauntlets", "Rękawice Golema", "Golem Gauntlets", "earth", "weapon", {"strength": 6, "constitution": 3}),
    E("frost_sword", "Lodowy Miecz", "Frost Sword", "earth", "weapon", {"agility": 5, "spirit": 3}),
    E("thunder_blade", "Ostrze Gromu", "Thunder Blade", "heavenly", "weapon", {"strength": 7, "agility": 5, "spirit": 3}),
    E("dragon_bone_sword", "Miecz ze Smoczej Kości", "Dragon Bone Sword", "divine", "weapon", {"strength": 10, "spirit": 8, "agility": 6}),
    E("blood_demon_saber", "Szabla Krwawego Demona", "Blood Demon Saber", "divine", "weapon", {"strength": 12, "agility": 8, "fate": 2}),
    # robes
    E("boar_hide_vest", "Kamizelka ze Skóry Dzika", "Boar Hide Vest", "mortal", "robe", {"constitution": 3}),
    E("viper_scale_robe", "Szata z Łusek Żmii", "Viper Scale Robe", "spirit", "robe", {"agility": 2, "constitution": 2}),
    E("fire_fox_cloak", "Płaszcz Ognistego Lisa", "Fire Fox Cloak", "earth", "robe", {"spirit": 3, "constitution": 3}),
    E("golem_plate", "Pancerz Golema", "Golem Plate", "earth", "robe", {"constitution": 6, "willpower": 2}),
    E("frost_silk_robe", "Szata z Lodowego Jedwabiu", "Frost Silk Robe", "heavenly", "robe", {"constitution": 5, "spirit": 4, "willpower": 3}),
    E("dragon_scale_armor", "Zbroja ze Smoczych Łusek", "Dragon Scale Armor", "divine", "robe", {"constitution": 10, "willpower": 6, "strength": 4}),
    # talismans
    E("bamboo_charm", "Bambusowy Amulet", "Bamboo Charm", "mortal", "talisman", {"focus": 2}),
    E("fox_eye_amulet", "Amulet Lisiego Oka", "Fox Eye Amulet", "spirit", "talisman", {"insight": 3, "fate": 1}),
    E("golem_heart", "Serce Golema", "Golem Heart", "earth", "talisman", {"willpower": 4, "constitution": 2}),
    E("soul_jade_pendant", "Wisior z Jadeitu Duszy", "Soul Jade Pendant", "heavenly", "talisman", {"spirit": 6, "insight": 4}),
    E("dragon_pearl", "Smocza Perła", "Dragon Pearl", "divine", "talisman", {"spirit": 10, "insight": 6, "fate": 3}),
    # rings
    E("iron_ring", "Żelazny Pierścień", "Iron Ring", "mortal", "ring", {"strength": 1, "constitution": 1}),
    E("fire_ring", "Pierścień Ognia", "Fire Ring", "earth", "ring", {"spirit": 3, "elementalAffinity": 3}),
    E("frost_ring", "Pierścień Mrozu", "Frost Ring", "earth", "ring", {"agility": 3, "focus": 3}),
    E("thunder_ring", "Pierścień Gromu", "Thunder Ring", "heavenly", "ring", {"strength": 3, "agility": 3, "spirit": 3}),
    E("void_ring", "Pierścień Pustki", "Void Ring", "heavenly", "ring", {"focus": 4}, 40),
    # treasures
    E("ancient_compass", "Starożytny Kompas", "Ancient Compass", "spirit", "treasure", {"fate": 2, "insight": 2}),
    E("tortoise_mirror", "Lustro ze Skorupy Żółwia", "Tortoise Shell Mirror", "heavenly", "treasure", {"constitution": 5, "willpower": 5}),
    E("demon_bell", "Dzwon Demona", "Demon Bell", "heavenly", "treasure", {"spirit": 5, "willpower": 4}),
    E("celestial_lotus", "Niebiański Lotos", "Celestial Lotus", "divine", "treasure", {"spirit": 8, "insight": 8, "fate": 4}),
])

D = lambda item, ch, mn=1, mx=1: {"item": item, "chancePermille": ch, "min": mn, "max": mx}
loot = it["lootTables"]
for k, v in {
    "wild_boar": {"stones": 20, "drops": [D("boar_tusk", 700, 1, 2), D("spirit_herb", 300, 1, 2)]},
    "bamboo_viper": {"stones": 22, "drops": [D("viper_venom", 650, 1, 2), D("bamboo_essence", 500, 1, 3)]},
    "fire_fox": {"stones": 45, "drops": [D("fox_tail", 600, 1, 1), D("fire_crystal", 250, 1, 1), D("heaven_dew", 100)]},
    "stone_golem": {"stones": 55, "drops": [D("golem_core", 450), D("iron_ore", 800, 2, 5), D("refining_stone", 400, 1, 2)]},
    "demonic_cultivator": {"stones": 90, "drops": [D("demon_blood", 600, 1, 2), D("foundation_pill", 120), D("technique_scroll", 30)]},
    "frost_spider": {"stones": 80, "drops": [D("frost_silk", 600, 1, 2), D("spirit_silk", 500, 1, 2)]},
    "sect_traitor": {"stones": 100, "drops": [D("jade", 500, 1, 2), D("lotus_seed", 250), D("foundation_pill", 150)]},
    "thunder_eagle": {"stones": 140, "drops": [D("thunder_feather", 500), D("star_iron", 200)]},
    "corpse_puppet": {"stones": 130, "drops": [D("soul_jade", 300), D("demon_blood", 600, 1, 3)]},
    "bandit_chief": {"stones": 250, "drops": [D("jade", 900, 2, 4), D("ancient_compass", 200), D("foundation_pill", 400), D("phoenix_feather", 20)]},
    "ancient_tortoise": {"stones": 400, "drops": [D("tortoise_shell", 800), D("lotus_seed", 600, 1, 3), D("core_pill", 200)]},
    "jiao_dragon": {"stones": 800, "drops": [D("dragon_scale", 700, 1, 2), D("star_iron", 500, 1, 2), D("core_pill", 350), D("dragon_pearl", 30)]},
    "blood_demon_lord": {"stones": 1500, "drops": [D("blood_crystal", 800, 1, 2), D("nascent_pill", 250), D("blood_demon_saber", 40), D("celestial_lotus", 15)]},
}.items():
    loot.setdefault(k, v)
dump("items.json", it, {"items", "lootTables"})

# ---- crafting ------------------------------------------------------------------------------------------------------
cr = load("crafting.json")
I = lambda item, q: {"item": item, "quantity": q}


def R(i, kind, inputs, stones, out, ok=1000, q=1):
    return {"id": i, "kind": kind, "inputs": inputs, "stones": stones, "output": {"item": out, "quantity": q}, "successPermille": ok}


add(cr["recipes"], [
    R("carve_bamboo_staff", "forge", [I("bamboo_essence", 3)], 15, "bamboo_staff"),
    R("carve_bamboo_charm", "forge", [I("bamboo_essence", 2), I("spirit_herb", 2)], 15, "bamboo_charm"),
    R("forge_iron_ring", "forge", [I("iron_ore", 4)], 20, "iron_ring"),
    R("forge_boar_tusk_spear", "forge", [I("boar_tusk", 3), I("iron_ore", 3)], 40, "boar_tusk_spear"),
    R("tan_boar_hide_vest", "forge", [I("boar_tusk", 2), I("spirit_herb", 3)], 30, "boar_hide_vest"),
    R("forge_venom_hook", "forge", [I("viper_venom", 3), I("iron_ore", 3)], 45, "venom_hook"),
    R("weave_viper_scale_robe", "forge", [I("viper_venom", 2), I("spirit_silk", 2)], 60, "viper_scale_robe"),
    R("craft_fox_eye_amulet", "forge", [I("fox_tail", 1), I("jade", 1)], 90, "fox_eye_amulet"),
    R("craft_fox_fire_fan", "forge", [I("fox_tail", 2), I("fire_crystal", 1), I("bamboo_essence", 3)], 150, "fox_fire_fan"),
    R("weave_fire_fox_cloak", "forge", [I("fox_tail", 2), I("spirit_silk", 3)], 140, "fire_fox_cloak"),
    R("forge_fire_ring", "forge", [I("fire_crystal", 2), I("iron_ore", 4)], 120, "fire_ring"),
    R("forge_golem_gauntlets", "forge", [I("golem_core", 1), I("iron_ore", 8)], 180, "golem_gauntlets"),
    R("forge_golem_plate", "forge", [I("golem_core", 2), I("iron_ore", 10)], 220, "golem_plate"),
    R("carve_golem_heart", "forge", [I("golem_core", 1), I("jade", 2)], 160, "golem_heart"),
    R("forge_frost_sword", "forge", [I("frost_silk", 2), I("star_iron", 1), I("iron_ore", 5)], 260, "frost_sword"),
    R("forge_frost_ring", "forge", [I("frost_silk", 2), I("iron_ore", 4)], 160, "frost_ring"),
    R("weave_frost_silk_robe", "forge", [I("frost_silk", 4), I("spirit_silk", 4), I("soul_jade", 1)], 420, "frost_silk_robe"),
    R("forge_thunder_blade", "forge", [I("thunder_feather", 2), I("star_iron", 2)], 600, "thunder_blade"),
    R("forge_thunder_ring", "forge", [I("thunder_feather", 1), I("star_iron", 1)], 380, "thunder_ring"),
    R("forge_void_ring", "forge", [I("star_iron", 3), I("soul_jade", 2)], 700, "void_ring"),
    R("carve_soul_jade_pendant", "forge", [I("soul_jade", 2), I("jade", 3)], 520, "soul_jade_pendant"),
    R("craft_tortoise_mirror", "forge", [I("tortoise_shell", 1), I("soul_jade", 1)], 800, "tortoise_mirror"),
    R("forge_demon_bell", "forge", [I("demon_blood", 4), I("star_iron", 1)], 650, "demon_bell"),
    R("forge_dragon_bone_sword", "forge", [I("dragon_scale", 3), I("star_iron", 3)], 2000, "dragon_bone_sword"),
    R("forge_dragon_scale_armor", "forge", [I("dragon_scale", 4), I("frost_silk", 4)], 2200, "dragon_scale_armor"),
    R("brew_heaven_dew", "alchemy", [I("spirit_herb", 4), I("bamboo_essence", 2)], 15, "heaven_dew", 800),
    R("brew_foundation_pill", "alchemy", [I("lotus_seed", 1), I("spirit_herb", 6), I("viper_venom", 1)], 80, "foundation_pill", 650),
    R("brew_demon_core_pill", "alchemy", [I("demon_blood", 3), I("lotus_seed", 1)], 150, "demon_core_pill", 550),
    R("brew_core_pill", "alchemy", [I("lotus_seed", 3), I("soul_jade", 1), I("fire_crystal", 1)], 400, "core_pill", 450),
    R("brew_nascent_pill", "alchemy", [I("blood_crystal", 1), I("lotus_seed", 5), I("dragon_scale", 1)], 1500, "nascent_pill", 350),
])
dump("crafting.json", cr, {"recipes"})

# ---- cultivation: six realms with a gentler curve ------------------------------------------------------------------
cu = load("cultivation.json")
add(cu["realms"], [
    {"id": "nascent_soul", "namePl": "Narodziny Duszy", "nameEn": "Nascent Soul", "stages": 9},
    {"id": "soul_transformation", "namePl": "Przemiana Duszy", "nameEn": "Soul Transformation", "stages": 9},
    {"id": "void_refinement", "namePl": "Rafinacja Pustki", "nameEn": "Void Refinement", "stages": 9},
])
cu["qiRequired"]["growth"] = 1.1
cu["rulesVersion"] = "cultivation-2"
dump("cultivation.json", cu, {"realms"})

# ---- world: more node kinds and events -----------------------------------------------------------------------------
w = load("world.json")
Y = lambda item, ch, mn=1, mx=1: {"item": item, "chancePermille": ch, "min": mn, "max": mx}
NK = lambda i, pl, en, weight, qi, npc=None, yields=None: {"id": i, "namePl": pl, "nameEn": en, "weight": weight, "rewardQi": qi, "npcId": npc, **({"yields": yields} if yields else {})}
add(w["nodeKinds"], [
    NK("ore_vein", "Żyła Rudy", "Ore Vein", 18, 8, None, [Y("iron_ore", 900, 2, 4), Y("refining_stone", 250), Y("star_iron", 15)]),
    NK("jade_outcrop", "Jadeitowa Skała", "Jade Outcrop", 8, 10, None, [Y("jade", 800, 1, 2), Y("soul_jade", 20)]),
    NK("bamboo_grove", "Bambusowy Gaj", "Bamboo Grove", 14, 8, None, [Y("bamboo_essence", 900, 1, 3), Y("spirit_silk", 200)]),
    NK("lotus_pond", "Staw Lotosów", "Lotus Pond", 6, 20, None, [Y("lotus_seed", 500), Y("heaven_dew", 150)]),
    NK("ancient_ruins", "Starożytne Ruiny", "Ancient Ruins", 4, 30, None, [Y("soul_jade", 60), Y("refining_stone", 300), Y("jade", 400, 1, 2), Y("fire_crystal", 200)]),
    NK("shrine", "Przydrożna Kapliczka", "Wayside Shrine", 6, 60, None, [Y("heaven_dew", 300)]),
    NK("boar_den", "Legowisko Dzika", "Boar Den", 10, 0, "wild_boar"),
    NK("viper_nest", "Gniazdo Żmij", "Viper Nest", 8, 0, "bamboo_viper"),
    NK("fox_den", "Nora Ognistego Lisa", "Fire Fox Den", 5, 0, "fire_fox"),
    NK("golem_ruin", "Ruina Golema", "Golem Ruin", 4, 0, "stone_golem"),
    NK("spider_cave", "Jaskinia Pajęczycy", "Spider Cave", 3, 0, "frost_spider"),
    NK("demon_altar", "Ołtarz Demonów", "Demon Altar", 3, 0, "demonic_cultivator"),
    NK("traitor_camp", "Obóz Zdrajcy", "Traitor's Camp", 3, 0, "sect_traitor"),
    NK("eagle_peak", "Szczyt Orła", "Eagle Peak", 2, 0, "thunder_eagle"),
    NK("cursed_grave", "Przeklęty Grobowiec", "Cursed Tomb", 2, 0, "corpse_puppet"),
    NK("bandit_fortress", "Twierdza Bandytów (boss)", "Bandit Fortress (boss)", 2, 0, "bandit_chief"),
    NK("tortoise_lake", "Jezioro Prastarego Żółwia (boss)", "Ancient Tortoise Lake (boss)", 1, 0, "ancient_tortoise"),
    NK("dragon_abyss", "Otchłań Smoka Jiao (boss)", "Jiao Dragon Abyss (boss)", 1, 0, "jiao_dragon"),
    NK("blood_gate", "Wrota Krwawego Demona (boss)", "Blood Demon Gate (boss)", 1, 0, "blood_demon_lord"),
])
ek = w["events"]["kinds"]
EV = lambda i, pl, en, weight, mult: {"id": i, "namePl": pl, "nameEn": en, "weight": weight, "rewardMultiplierPermille": mult}
add(ek, [
    EV("full_moon", "Pełnia Księżyca", "Full Moon", 2, 1500),
    EV("spring_festival", "Święto Wiosny", "Spring Festival", 2, 1300),
    EV("lantern_festival", "Święto Lampionów", "Lantern Festival", 2, 1400),
    EV("heavenly_rain", "Niebiański Deszcz", "Heavenly Rain", 2, 1600),
    EV("meteor_shower", "Deszcz Meteorów", "Meteor Shower", 1, 2200),
    EV("demon_invasion", "Inwazja Demonów", "Demon Invasion", 1, 2500),
    EV("ancestor_blessing", "Błogosławieństwo Przodków", "Ancestors' Blessing", 1, 1800),
    EV("qi_tide", "Przypływ Qi", "Qi Tide", 2, 1700),
])
w["rulesVersion"] = "world-3"
dump("world.json", w, {"nodeKinds", "events"})
print("traits", len(cc["traits"]), "techniques", len(cb["techniques"]), "npcs", len(cb["npcs"]), "items", len(it["items"]),
      "recipes", len(cr["recipes"]), "realms", len(cu["realms"]), "nodeKinds", len(w["nodeKinds"]), "events", len(ek))

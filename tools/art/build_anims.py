import numpy as np
from slice import strip
from slice2 import frames
R6 = [3]*6
SPECS = {
 # name: (file, rows, idle, attack, defeat, flip, body height fraction of the 256 cell)
 'sect_traitor': ('fe9d2c98', [6,3,3,3,3,3], range(0,6), range(9,15), range(15,21), True, .70),
 'frost_spider': ('caea176f', R6, range(0,6), range(6,12), range(12,18), True, .46),
 'jiao_dragon': ('0bf9329f', R6, range(0,6), range(6,12), range(12,18), True, .78),
 'ancient_tortoise': ('b0c15cac', R6, range(0,6), range(6,12), range(12,18), True, .60),
 'blood_demon_lord': ('29d7b7d5', [3,3,3,3,4,4], range(0,6), range(6,12), [12,13,14,15,17,18,19], False, .84),
 'corpse_puppet': ('45e0e8c6', [6,6,4,3], range(0,6), range(6,12), range(12,19), True, .72),
 'bamboo_viper': ('fe3abba6', R6, range(0,6), range(6,12), range(12,18), True, .42),
 'wild_boar': ('6fa47244', R6, range(0,6), range(6,12), range(12,18), True, .50),
 'demonic_cultivator': ('087f40ae', [5,3,3,4,3], range(0,5), range(5,11), [11,12,13,14,16,17], True, .72),
 'stone_golem': ('8180b096', [4,4,4,3,3,3], range(0,8), range(8,15), range(15,21), False, .78),
 'bandit_chief': ('738bc04d', R6, range(0,6), range(6,12), range(12,18), False, .80),
 'fire_fox': ('1ede6bea', R6, range(0,6), range(6,12), range(12,18), True, .44),
 'thunder_eagle': ('29cdc731', R6, range(0,6), range(6,12), range(12,18), True, .62),
}
import sys
only = sys.argv[1:]
for npc, (f, rows, idle, atk, dfe, flip, frac) in SPECS.items():
    if only and npc not in only: continue
    frs, _ = frames(f, rows)
    assert len(frs) == sum(rows) and all(x is not None for x in frs), npc
    ref = np.median([frs[i][0].size[1] for i in idle])
    scale = frac * 256 / ref
    for part, idx in (('idle', idle), ('attack', atk), ('defeat', dfe)):
        strip(frs, list(idx), scale, flip).save(f'out/anim_{npc}_{part}.png', optimize=True)
    print(npc, len(frs), round(scale, 2))

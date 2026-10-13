# Art slicer

Turns generated sheets (flat #00FF00 background) into game assets: chroma key + despill, per-sprite segmentation
(merges effects into their sprite, splits touching sprites), baseline-aligned 320×256 animation cells, icon crops,
256-colour PNGs. Sheet ids and frame ranges are in `build_anims.py`, `build_icons.py`, `build_tiles.py`
(`U` = folder with the uploaded sheets). `preview.py out.png <npc>...` renders a check sheet.

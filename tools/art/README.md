# Art slicer

Turns generated sheets (flat #00FF00 background) into game assets: chroma key + despill, per-sprite segmentation
(merges effects into their sprite, splits touching sprites), baseline-aligned 320×256 animation cells, icon crops,
256-colour PNGs. Sheet ids and frame ranges are in `build_anims.py`, `build_icons.py`, `build_tiles.py`
(`U` = folder with the uploaded sheets). `preview.py out.png <npc>...` renders a check sheet.

1.4: `build_v4.py <out>` cuts the per-path hero sheets (7×6), the storm qilin, effects, the unified technique/menu icons and the tower art;
`build_vfx.py <frames> <out>` turns the effect videos (frames extracted with `ffmpeg -i clip.mp4 f_<id>/%03d.png`) into `ovl_*.webp`
light overlays (brightness becomes alpha).

# -*- coding: utf-8 -*-
"""Build labelled contact sheets from the exported scenery sprites, grouped by chapter."""
import io, os, sys
from PIL import Image, ImageDraw

ROOT = r'C:\Users\User\AppData\Local\Temp\claude\e--modgames-bdc\a40f5b2b-ab9a-433e-afc5-b3e21f6ad02c\scratchpad\scn'
OUT = os.path.join(ROOT, 'sheets')
os.makedirs(OUT, exist_ok=True)

rows = []
for line in io.open(os.path.join(ROOT, '_index.txt'), encoding='utf-8'):
    line = line.rstrip('\n')
    if not line:
        continue
    obj, room, spr = line.split('\t')
    rows.append((obj, room, spr))

# Group by the room prefix so a sheet is one place, which makes the drawings legible in
# context: a sheet of graveyard props reads very differently from a mixed one.
def area(room):
    r = room[4:] if room.startswith('Lvl_') else room
    return r.split('_')[0]

rows.sort(key=lambda r: (area(r[1]), r[1], r[0]))

COLS, ROWS = 5, 4
CW, CH = 300, 210
PAD = 26
sheet_no = 0
manifest = []
i = 0
while i < len(rows):
    batch = rows[i:i + COLS * ROWS]
    i += COLS * ROWS
    sheet_no += 1
    W = COLS * CW
    H = ROWS * (CH + PAD)
    im = Image.new('RGB', (W, H), (255, 255, 255))
    dr = ImageDraw.Draw(im)
    names = []
    for k, (obj, room, spr) in enumerate(batch):
        cx, cy = (k % COLS) * CW, (k // COLS) * (CH + PAD)
        dr.rectangle([cx, cy, cx + CW - 1, cy + CH + PAD - 1], outline=(190, 190, 190))
        dr.text((cx + 6, cy + 6), str(k + 1), fill=(200, 0, 0))
        try:
            s = Image.open(os.path.join(ROOT, obj + '.png')).convert('RGBA')
        except Exception:
            names.append('%d\t%s\t%s\t(missing)' % (k + 1, obj, room))
            continue
        bg = Image.new('RGBA', s.size, (255, 255, 255, 255))
        bg.alpha_composite(s)
        s = bg.convert('RGB')
        s.thumbnail((CW - 20, CH - 10), Image.LANCZOS)
        im.paste(s, (cx + (CW - s.width) // 2, cy + PAD + (CH - s.height) // 2))
        names.append('%d\t%s\t%s' % (k + 1, obj, room))
    p = os.path.join(OUT, 'sheet_%02d.png' % sheet_no)
    im.save(p)
    manifest.append('=== sheet_%02d.png\n' % sheet_no + '\n'.join(names))

io.open(os.path.join(OUT, 'manifest.txt'), 'w', encoding='utf-8', newline='').write(
    '\n\n'.join(manifest))
print('sheets:', sheet_no, 'items:', len(rows))

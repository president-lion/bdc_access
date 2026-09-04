# -*- coding: utf-8 -*-
"""Contact sheets for the invisible look-at hotspots.

Each cell shows TWO pictures of the same hotspot, split by a grey line:

  left   the object's own sprite - meaningful when the object is real art that the story
         reveals later, and a featureless blob when it is only a collision shape
  right  the room's own artwork cropped around the hotspot, with the hotspot's box drawn
         on it in red - meaningful for the collision shapes, and an empty patch of floor
         for the ones that are revealed later

Which half carries the information cannot be decided mechanically, so both are shown and
the transcriber picks."""
import io, os
from PIL import Image, ImageDraw

ROOT = r'C:\Users\User\AppData\Local\Temp\claude\e--modgames-bdc\a40f5b2b-ab9a-433e-afc5-b3e21f6ad02c\scratchpad\masks'
OUT = os.path.join(ROOT, 'sheets')
os.makedirs(OUT, exist_ok=True)

rows = []
for line in io.open(os.path.join(ROOT, '_crops.txt'), encoding='utf-8'):
    line = line.rstrip('\n')
    if not line:
        continue
    name, room, bg, x, y, w, h = line.split('\t')
    rows.append((name, room, bg, int(x), int(y), int(w), int(h)))
rows.sort(key=lambda r: (r[1], r[0]))

bgs = {}
def on_white(im):
    im = im.convert('RGBA')
    page = Image.new('RGBA', im.size, (255, 255, 255, 255))
    page.alpha_composite(im)
    return page.convert('RGB')

def bg_image(n):
    # Composited onto WHITE. These are black line drawings on a transparent ground, and
    # converting straight to RGB mattes the ink onto black - which is what made the first
    # run of these sheets come out as 698 black rectangles.
    if n not in bgs:
        bgs[n] = on_white(Image.open(os.path.join(ROOT, 'bg', n + '.png')))
    return bgs[n]

PAD, MIN = 45, 150
COLS, ROWS_ = 5, 4
CW, CH, LAB = 320, 220, 24
HALF = (CW - 12) // 2

cells = []
for name, room, bg, x, y, w, h in rows:
    panel = Image.new('RGB', (CW, CH), (255, 255, 255))

    sp_path = os.path.join(ROOT, 'spr', name + '.png')
    if os.path.exists(sp_path):
        try:
            sp = on_white(Image.open(sp_path))
            sp.thumbnail((HALF - 6, CH - 8), Image.LANCZOS)
            panel.paste(sp, (4 + (HALF - sp.width) // 2, (CH - sp.height) // 2))
        except Exception:
            pass

    try:
        im = bg_image(bg)
        cx, cy = x + w / 2.0, y + h / 2.0
        cw, ch = max(w + PAD * 2, MIN), max(h + PAD * 2, MIN)
        l, t = max(0, int(cx - cw / 2)), max(0, int(cy - ch / 2))
        r, b = min(im.width, l + int(cw)), min(im.height, t + int(ch))
        if r - l >= 8 and b - t >= 8:
            c = im.crop((l, t, r, b)).copy()
            d = ImageDraw.Draw(c)
            bx0, bx1 = sorted((x - l, x - l + w))
            by0, by1 = sorted((y - t, y - t + h))
            d.rectangle([bx0, by0, max(bx1, bx0 + 1), max(by1, by0 + 1)],
                        outline=(220, 0, 0), width=2)
            c.thumbnail((HALF - 6, CH - 8), Image.LANCZOS)
            panel.paste(c, (HALF + 8 + (HALF - c.width) // 2, (CH - c.height) // 2))
    except Exception:
        pass

    d = ImageDraw.Draw(panel)
    d.line([HALF + 4, 4, HALF + 4, CH - 4], fill=(180, 180, 180))
    cells.append((name, room, panel))

sheet = 0
manifest = []
i = 0
while i < len(cells):
    batch = cells[i:i + COLS * ROWS_]
    i += COLS * ROWS_
    sheet += 1
    im = Image.new('RGB', (COLS * CW, ROWS_ * (CH + LAB)), (255, 255, 255))
    dr = ImageDraw.Draw(im)
    names = []
    for k, (name, room, panel) in enumerate(batch):
        px, py = (k % COLS) * CW, (k // COLS) * (CH + LAB)
        dr.rectangle([px, py, px + CW - 1, py + CH + LAB - 1], outline=(150, 150, 150))
        dr.text((px + 6, py + 6), str(k + 1), fill=(200, 0, 0))
        im.paste(panel, (px, py + LAB))
        names.append('%d\t%s\t%s' % (k + 1, name, room))
    im.save(os.path.join(OUT, 'mask_%02d.png' % sheet))
    manifest.append('=== mask_%02d.png\n' % sheet + '\n'.join(names))

io.open(os.path.join(OUT, 'manifest.txt'), 'w', encoding='utf-8', newline='').write(
    '\n\n'.join(manifest))
print('sheets:', sheet, 'cells:', len(cells))

# sheet.py - blow the low-res renders up with NEAREST (the pixel look), caption each with its node
# name and lay them out as one contact sheet. Also writes the 512 px stills and the 1024 hero.
#   python sheet.py IN_DIR OUT_DIR
import os, sys, glob
from PIL import Image, ImageDraw, ImageFont

src, dst = sys.argv[1], sys.argv[2]
os.makedirs(dst, exist_ok=True)
CELL, CAP, COLS, PAD = 512, 44, 5, 12
BG, INK, TXT = (14, 12, 30), (27, 26, 51), (246, 231, 200)
font = ImageFont.load_default(size=26)

names = sorted(os.path.basename(p)[:-4] for p in glob.glob(os.path.join(src, '*.png')) if not os.path.basename(p).startswith('hero_'))
order = ['kart_cup', 'kart_saucer', 'item_cube', 'item_shards', 'boost_pad', 'ramp_lip', 'air_marker', 'gantry', 'podium', 'floor_tile']
names = [n for n in order if n in names] + [n for n in names if n not in order]
cells = []
for n in names:
    im = Image.open(os.path.join(src, n + '.png')).convert('RGB')
    still = im.resize((CELL, CELL), Image.NEAREST)
    still.save(os.path.join(dst, n + '.png'))
    cells.append((n, still))

rows = (len(cells) + COLS - 1) // COLS
W = COLS * (CELL + PAD) + PAD
H = rows * (CELL + CAP + PAD) + PAD
sheet = Image.new('RGB', (W, H), BG)
d = ImageDraw.Draw(sheet)
for i, (n, im) in enumerate(cells):
    x = PAD + (i % COLS) * (CELL + PAD)
    y = PAD + (i // COLS) * (CELL + CAP + PAD)
    sheet.paste(im, (x, y))
    d.rectangle((x, y + CELL, x + CELL, y + CELL + CAP), fill=INK)
    d.text((x + CELL / 2, y + CELL + CAP / 2), n, fill=TXT, font=font, anchor='mm')
sheet.save(os.path.join(dst, 'contact.png'))
print('SHEET', os.path.join(dst, 'contact.png'), sheet.size)

hero = os.path.join(src, 'hero_kart.png')
if os.path.exists(hero):
    Image.open(hero).convert('RGB').resize((1024, 1024), Image.NEAREST).save(os.path.join(dst, 'hero_kart.png'))
    print('HERO written')
print('SHEET_DONE')

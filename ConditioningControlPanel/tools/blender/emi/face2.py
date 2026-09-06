# Render EMI kaomoji faces with her real font (Noto Sans Mono, decompressed from the
# bundled woff2), falling back to Segoe UI Symbol for glyphs the latin subset lacks.
# Output: faces/<name>.png, 152x137 RGBA, pink (255,105,180) with a 5px stroke like face.js.
# Game atlas:  python face2.py --game OUTDIR  ->  OUTDIR/emi-faces.png plus one png per frame.
#   760x137 RGB, five 152x137 frames left to right in the owner-locked order ^_^ :3 >_< o_o $_$,
#   one shared glyph size so a frame swap never rescales the face, pink over the dim screen
#   purple (the glass material emits this image, so the background is the idle screen glow).
import os, sys
from PIL import Image, ImageDraw, ImageFont
from fontTools.ttLib import TTFont

HERE = os.path.dirname(os.path.abspath(__file__))
WOFF2 = r'C:\Projects\Conditioning-Control-Panel---CSharp-WPF\ConditioningControlPanel\Resources\web\arcademy\emi\fonts\NotoSansMono-latin.woff2'
if not os.path.exists(WOFF2):   # mirrored copy inside the repo: tools/blender/emi -> Resources/web
    WOFF2 = os.path.normpath(os.path.join(HERE, '..', '..', '..', 'Resources', 'web', 'arcademy', 'emi', 'fonts', 'NotoSansMono-latin.woff2'))
NOTO = os.path.join(HERE, 'NotoSansMono-latin.ttf')
FALLBACK = r'C:\Windows\Fonts\seguisym.ttf'
if not os.path.exists(FALLBACK):
    FALLBACK = r'C:\Windows\Fonts\consola.ttf'

if not os.path.exists(NOTO):
    f = TTFont(WOFF2)
    f.flavor = None
    f.save(NOTO)
    print('converted', NOTO)

cmap = TTFont(NOTO).getBestCmap()

FACES = {
    'idle': '^_^',
    'glee': '(≧◡≦)',
    'sad': 'T_T',
    'shock': '(◉_◉)',
    'smug': '(¬‿¬)',
}
# The game's five faces, owner-locked: this exact set, this exact order, no other face ever.
GAME_FACES = [('joy', '^_^'), ('cat', ':3'), ('strain', '>_<'), ('stare', 'o_o'), ('cash', '$_$')]
W, H = 152, 137
PINK = (255, 105, 180, 255)
SCREEN = (62, 46, 92, 255)      # the idle screen glow behind the glyph (build_hs.py's faint purple)
ST = 5

def font_for(ch, px):
    return ImageFont.truetype(NOTO if ord(ch) in cmap else FALLBACK, px)

def measure(text, px):
    # per-glyph font choice (her font where it has the glyph, symbol fallback elsewhere)
    w = 0; top = 10**6; bot = -10**6
    for ch in text:
        f = font_for(ch, px)
        l, tt, r, b = f.getbbox(ch, anchor='ls')
        top = min(top, tt); bot = max(bot, b)
        w += f.getlength(ch)
    return w, top, bot

def fit_px(text, st):
    # largest glyph size that keeps the face inside the 152x137 cell with its stroke
    kao = len(text) > 3
    fit_w = W * (1.0 if kao else 0.95) - 2 * st
    fit_h = H * 0.95 - 2 * st
    w100, t100, b100 = measure(text, 100)
    return int(min(fit_w / max(w100, 1), fit_h / max(b100 - t100, 1)) * 100)

def render_face(text, fs, st, bg=(0, 0, 0, 0)):
    w, top, bot = measure(text, fs)
    im = Image.new('RGBA', (W, H), bg)
    d = ImageDraw.Draw(im)
    x = (W - w) / 2
    base = (H - (bot - top)) / 2 - top - H * 0.02
    for ch in text:
        f = font_for(ch, fs)
        d.text((x, base), ch, font=f, fill=PINK, stroke_width=st, stroke_fill=PINK, anchor='ls')
        x += f.getlength(ch)
    return im

if '--game' in sys.argv:
    out_dir = sys.argv[sys.argv.index('--game') + 1]
    os.makedirs(out_dir, exist_ok=True)
    fs = min(fit_px(text, ST) for _, text in GAME_FACES)
    atlas = Image.new('RGB', (W * len(GAME_FACES), H), SCREEN[:3])
    for i, (name, text) in enumerate(GAME_FACES):
        im = render_face(text, fs, ST, SCREEN).convert('RGB')
        im.save(os.path.join(out_dir, 'face%d_%s.png' % (i, name)))
        atlas.paste(im, (i * W, 0))
        print('frame', i, name, text)
    atlas.save(os.path.join(out_dir, 'emi-faces.png'), optimize=True)
    print('ATLAS', atlas.size, 'px', fs, 'stroke', ST)
    print('FACES_DONE')
    sys.exit(0)

os.makedirs(os.path.join(HERE, 'faces'), exist_ok=True)
for name, text in FACES.items():
    kao = len(text) > 3
    st = 2 if kao else ST
    fs = fit_px(text, st)
    im = render_face(text, fs, st)
    out = os.path.join(HERE, 'faces', name + '.png')
    im.save(out)
    print(name, 'px', fs, 'stroke', st, im.getbbox())
print('FACES_DONE')

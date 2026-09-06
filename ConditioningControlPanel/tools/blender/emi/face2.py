# Render EMI kaomoji faces with her real font (Noto Sans Mono, decompressed from the
# bundled woff2), falling back to Segoe UI Symbol for glyphs the latin subset lacks.
# Output: faces/<name>.png, 152x137 RGBA, pink (255,105,180) with a 5px stroke like face.js.
# Game atlas:  python face2.py --game OUTDIR  ->  OUTDIR/emi-faces.png plus one png per frame.
#   1064x137 RGB, seven 152x137 frames left to right in the owner-locked order
#   ^_^ :3 >_< o_o $_$ ★_★ @_@, one shared glyph size (measured on the first five only, so
#   appending a face never rescales the ones already on the glass) and pink over the dim
#   screen purple (the glass material emits this image, so the background is the idle glow).
#   Two frames are not a plain text render:
#     1 :3    drawn in a cell turned on its side and rotated upright, so the colon reads as
#             two eyes side by side and the 3 as the cat mouth under them (a 90 degree turn
#             is lossless, so the stroke weight is the same as every other frame's).
#     6 @_@   drawn, not typed: the @ glyph's counters close up solid under the 5 px stroke,
#             so the spiral eyes are stroked here at the same pink over a small open mouth.
import math, os, sys
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
# The game's faces, owner-locked: this exact set, this exact order. Index is the atlas frame and
# the runtime (race/gltf.js FACES) quotes it, so a face is only ever APPENDED, never reordered.
# mode: '' plain text render, 'turn' render sideways and rotate upright, 'spiral' drawn below.
GAME_FACES = [('joy', '^_^', ''), ('cat', ':3', 'turn'), ('strain', '>_<', ''),
              ('stare', 'o_o', ''), ('cash', '$_$', ''),
              ('starry', '★_★', ''), ('spiral', '@_@', 'spiral')]
SIZED_ON = 5            # the shared glyph size is measured on the first five faces only
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

def render_face(text, fs, st, bg=(0, 0, 0, 0), cell_w=W, cell_h=H):
    w, top, bot = measure(text, fs)
    im = Image.new('RGBA', (cell_w, cell_h), bg)
    d = ImageDraw.Draw(im)
    x = (cell_w - w) / 2
    base = (cell_h - (bot - top)) / 2 - top - cell_h * 0.02
    for ch in text:
        f = font_for(ch, fs)
        d.text((x, base), ch, font=f, fill=PINK, stroke_width=st, stroke_fill=PINK, anchor='ls')
        x += f.getlength(ch)
    return im

# Spiral eyes: an Archimedean r = a * theta stroked from 0.35 to 1.45 turns (the inner 0.35 is
# skipped so the middle is a clean cap instead of a smear), mirrored left to right, over a small
# open mouth. Drawn at 4x and resampled down so the curve is as smooth as the font glyphs beside
# it. The eyes sit on the monospace advance grid, so they land where o_o's eyes land.
SP_R, SP_W, SP_T0, SP_T1 = 34.0, 11.0, 0.35, 1.45     # radius, stroke, turns in, turns out
SP_EYE_Y, SP_MOUTH = 54.0, (26.0, 20.0, 100.0)        # eye centre y, mouth w / h / centre y
SS = 4                                                 # supersample factor

def render_spiral(bg=(0, 0, 0, 0)):
    adv = ImageFont.truetype(NOTO, 74).getlength('o')  # the monospace advance, one cell per glyph
    im = Image.new('RGBA', (W * SS, H * SS), bg)
    d = ImageDraw.Draw(im)
    x0 = (W - 3 * adv) / 2
    a = (SP_R - SP_W / 2) / (SP_T1 * 2 * math.pi)
    for i, cx in enumerate((x0 + adv * 0.5, x0 + adv * 2.5)):
        s = 1 if i == 0 else -1                        # the right eye winds the other way
        pts = []
        for k in range(401):
            th = (SP_T0 + (SP_T1 - SP_T0) * k / 400.0) * 2 * math.pi
            r = a * th
            pts.append(((cx + s * r * math.cos(th + math.pi / 2)) * SS,
                        (SP_EYE_Y + r * math.sin(th + math.pi / 2)) * SS))
        d.line(pts, fill=PINK, width=int(SP_W * SS), joint='curve')
        for px, py in (pts[0], pts[-1]):               # round caps at both ends of the stroke
            d.ellipse([px - SP_W * SS / 2, py - SP_W * SS / 2, px + SP_W * SS / 2, py + SP_W * SS / 2], fill=PINK)
    mw, mh, my = SP_MOUTH
    d.ellipse([(W / 2 - mw / 2) * SS, (my - mh / 2) * SS, (W / 2 + mw / 2) * SS, (my + mh / 2) * SS], fill=PINK)
    return im.resize((W, H), Image.LANCZOS)

def render_frame(text, fs, st, mode, bg):
    if mode == 'turn':      # a sideways cell turned 90 degrees clockwise: pixel exact, no rescale
        return render_face(text, fs, st, bg, H, W).rotate(-90, expand=True)
    if mode == 'spiral':
        return render_spiral(bg)
    return render_face(text, fs, st, bg)

if '--game' in sys.argv:
    out_dir = sys.argv[sys.argv.index('--game') + 1]
    os.makedirs(out_dir, exist_ok=True)
    fs = min(fit_px(text, ST) for _, text, _m in GAME_FACES[:SIZED_ON])
    atlas = Image.new('RGB', (W * len(GAME_FACES), H), SCREEN[:3])
    for i, (name, text, mode) in enumerate(GAME_FACES):
        im = render_frame(text, fs, ST, mode, SCREEN).convert('RGB')
        im.save(os.path.join(out_dir, 'face%d_%s.png' % (i, name)))
        atlas.paste(im, (i * W, 0))
        print('frame', i, name, text.encode('ascii', 'backslashreplace').decode(), mode or 'text')
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

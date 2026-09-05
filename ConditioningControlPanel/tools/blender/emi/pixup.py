# Nearest-neighbour upscale of low-res renders to a target size (pixel-art look).
import sys, glob, os
from PIL import Image
d, target, pattern = sys.argv[1], int(sys.argv[2]), sys.argv[3]
for p in glob.glob(os.path.join(d, pattern)):
    im = Image.open(p)
    if im.size[0] >= target:
        continue
    im.resize((target, target), Image.NEAREST).save(p)
    print('pixup', os.path.basename(p), im.size[0], '->', target)
print('PIXUP_DONE')

# 把 ps_props.py 渲出的分块拼成检查图：每行两件（每件 正面材质 / 背面材质 / 正面白模 / 背面白模）
import sys, os, glob
from PIL import Image, ImageDraw
d = sys.argv[1]; out = sys.argv[2]
names = sorted({os.path.basename(p).rsplit('_', 1)[0] for p in glob.glob(os.path.join(d, '*_0.png'))})
T = 320; cols = 8; rows = (len(names) + 1) // 2
sheet = Image.new('RGB', (cols * T, rows * (T + 22)), (40, 40, 40))
dr = ImageDraw.Draw(sheet)
for i, n in enumerate(names):
    r, c0 = i // 2, (i % 2) * 4
    for v in range(4):
        p = os.path.join(d, f'{n}_{v}.png')
        if os.path.exists(p):
            sheet.paste(Image.open(p).convert('RGB'), ((c0 + v) * T, r * (T + 22) + 22))
    dr.text((c0 * T + 6, r * (T + 22) + 4), n, fill=(255, 255, 255))
sheet.save(out); print(out, len(names))

# 我方帅旗旗面：干净的奶白布 + 中间原样 TRIPO 图标（不带字、不垫底、不改色）+ 四边毛边。燕尾缺口由网格做（贴图不带）。
# 运行：python3 Tools/cover/flag_mine.py → Assets/_PaperSiege/Resources/Cover/flag_mine.png
import os, sys
import numpy as np
from PIL import Image, ImageFilter
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import banner as B
W, H = 512, 313
rng = np.random.default_rng(7)
yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
base = np.zeros((H, W, 3), np.float32) + np.array([232, 220, 192], np.float32)
base += ((np.sin(xx * 2.1) * 0.5 + 0.5) * (np.sin(yy * 2.3) * 0.5 + 0.5) - 0.25)[..., None] * 8
base += rng.normal(0, 3.5, (H, W, 1))
n = np.asarray(Image.fromarray((rng.random((H // 32 + 1, W // 32 + 1)) * 255).astype(np.uint8)).resize((W, H), Image.BICUBIC).filter(ImageFilter.GaussianBlur(12)), np.float32) / 255
base *= (1 - 0.05 * (n - 0.5))[..., None]
mark = B.tripo_mark(int(H * 0.62 * 147 / 134))        # 图标高约旗高的 62%
x0, y0 = (W - mark.width) // 2, (H - mark.height) // 2
src = np.asarray(mark).astype(np.float32)
al = src[..., 3:4] / 255
sub = base[y0:y0 + mark.height, x0:x0 + mark.width]
sub[:] = sub * (1 - al) + src[..., :3] * al
a = np.ones((H, W), np.float32)
fray = rng.random((H, W))
edge = np.minimum(np.minimum(xx, W - 1 - xx), np.minimum(yy, H - 1 - yy))
a[(edge < 3) & (fray > 0.45)] = 0
a[edge < 1] = 0
out = np.dstack([np.clip(base, 0, 255), a * 255]).astype(np.uint8)
Image.fromarray(out).save(os.path.join(B.ROOT, 'Assets/_PaperSiege/Resources/Cover/flag_mine.png'))
print('ok', mark.size)

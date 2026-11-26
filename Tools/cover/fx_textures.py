# 特效贴图（程序画，手绘感，和积木“本色压暗细描边”同一路）：
#   fx_flame.png  火苗序列帧 4×2：水滴形，外红 → 中橙 → 芯淡黄三层套叠，边缘一道深暖色细描边；
#                 每帧火尖甩动、边缘波浪不同，连播是在跳。底部圆、钉在格子下沿（粒子轴心放底部）。
#   fx_puff.png   卡通烟团 2×2 四种形状：几个圆叠成一团，干净边 + 浅灰细描边 + 左上亮右下暗（运行时染色：蒸汽白 / 烟棕灰）。
# 运行：python3 Tools/cover/fx_textures.py → Assets/_PaperSiege/Resources/Fx/
import os
import numpy as np
from PIL import Image
from scipy import ndimage
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '../..'))
OUT = os.path.join(ROOT, 'Assets/_PaperSiege/Resources/Fx'); os.makedirs(OUT, exist_ok=True)
S = 256          # 每帧输出尺寸
SS = 2           # 超采样倍数（画大再缩，边缘干净不锯齿）
N = S * SS


def smooth_noise(n, cells, seed):
    r = np.random.default_rng(seed)
    g = r.random((cells + 3, cells + 3)).astype(np.float32)
    return np.asarray(Image.fromarray((g * 255).astype(np.uint8)).resize((n, n), Image.BICUBIC), np.float32) / 255


def flame_mask(xx, v, t, seed, scale=1.0, base=0.06):
    """水滴形火苗：v 0=底 1=顶。scale<1 是套在里面的中层 / 芯（同一底点往上缩）"""
    vs = (v - base) / scale                                  # 缩放后的纵坐标（底部对齐）
    sway = 0.09 * np.sin(t * 2 * np.pi) + 0.04 * np.sin(t * 4 * np.pi + seed)
    cx = 0.5 + sway * np.clip(vs, 0, 1) ** 2.2               # 越往上甩得越多
    # 半宽：底部是半圆（v<0.24），往上平滑收成尖
    body = np.where(vs < 0.24, np.sqrt(np.clip(1 - ((0.24 - vs) / 0.24) ** 2, 0, 1)), 1.0)
    taper = np.clip((1.0 - vs) / 0.76, 0, 1) ** 1.35
    w = 0.30 * scale * body * np.where(vs < 0.24, 1.0, taper)
    # 边缘波浪（越往上越大），每帧不同 → 跳动
    wav = 1 + 0.16 * np.sin(vs * 17 + t * 2 * np.pi * 2 + seed) * np.clip(vs - 0.25, 0, 1) * 2.2
    m = (np.abs(xx - cx) < w * wav) & (vs > 0) & (vs < 1)
    return m, cx


def flame(i):
    t = i / 8
    seed = 3 + i * 1.7
    yy, xx = np.mgrid[0:N, 0:N].astype(np.float32) / N
    v = 1 - yy
    outer, cx = flame_mask(xx, v, t, seed, 0.92, 0.05)
    # 一侧小火舌：和主体连着（从主体中部长出来，往外上方翘），左右交替
    side = 1 if i % 2 else -1
    tv = (v - 0.30) / 0.42
    tcx = 0.5 + side * (0.10 + 0.13 * np.clip(tv, 0, 1) ** 1.3) + 0.03 * np.sin(t * 6.28 + 1)
    tw = 0.075 * np.clip(1 - tv, 0, 1) ** 1.1 * np.clip(tv * 4, 0, 1)
    tongue = (np.abs(xx - tcx) < tw) & (tv > 0) & (tv < 1)
    if i % 4 != 3:
        outer |= tongue
    outer = ndimage.binary_closing(outer, iterations=3) & (v > 0.04)
    mid, _ = flame_mask(xx, v, t + 0.05, seed + 1, 0.70, 0.06)
    core, _ = flame_mask(xx, v, t + 0.1, seed + 2, 0.44, 0.07)
    mid &= ndimage.binary_erosion(outer, iterations=int(0.035 * N))
    core &= ndimage.binary_erosion(mid, iterations=int(0.03 * N))
    rgb = np.zeros((N, N, 3), np.float32)
    rgb[outer] = (222, 78, 38)
    rgb[mid] = (246, 152, 48)
    rgb[core] = (255, 228, 138)
    # 内层的小高光块（左上偏）
    hl = core & ~ndimage.shift(core, (int(0.02 * N), -int(0.015 * N)), order=0)
    rgb[hl] = (255, 245, 205)
    rim = outer & ~ndimage.binary_erosion(outer, iterations=int(0.012 * N))
    rgb[rim] = (148, 46, 26)
    a = outer.astype(np.float32) * 255
    img = Image.fromarray(np.dstack([rgb, a]).astype(np.uint8))
    return np.asarray(img.resize((S, S), Image.LANCZOS))


def puff(k):
    rng = np.random.default_rng(40 + k)
    yy, xx = np.mgrid[0:N, 0:N].astype(np.float32) / N
    m = np.zeros((N, N), bool)
    circles = [(0.5, 0.56, 0.24)]
    for _ in range(4 + k):
        ang = rng.uniform(0, 2 * np.pi); d = rng.uniform(0.12, 0.2)
        circles.append((0.5 + np.cos(ang) * d, 0.52 + np.sin(ang) * d * 0.8, rng.uniform(0.12, 0.18)))
    for cx, cy, r in circles:
        m |= np.hypot(xx - cx, yy - cy) < r
    m = ndimage.binary_closing(m, iterations=4)
    # 明暗：左上亮、右下一道暗影（离右下边缘近的地方）
    shifted = ndimage.shift(m, (-int(0.05 * N), -int(0.05 * N)), order=0)
    shade = np.where(m & ~shifted, 0.74, 1.0)
    rim = m & ~ndimage.binary_erosion(m, iterations=int(0.012 * N))
    val = np.where(rim, 0.55, shade)
    g = (val * 255).astype(np.float32)
    rgb = np.dstack([g, g, g])
    a = m.astype(np.float32) * 255
    img = Image.fromarray(np.dstack([rgb, a]).astype(np.uint8))
    return np.asarray(img.resize((S, S), Image.LANCZOS))


sheet = np.zeros((S * 2, S * 4, 4), np.uint8)
for i in range(8):
    r, c = i // 4, i % 4
    sheet[r * S:(r + 1) * S, c * S:(c + 1) * S] = flame(i)
Image.fromarray(sheet).save(os.path.join(OUT, 'fx_flame.png'))
ps = np.zeros((S * 2, S * 2, 4), np.uint8)
for k in range(4):
    r, c = k // 2, k % 2
    ps[r * S:(r + 1) * S, c * S:(c + 1) * S] = puff(k)
Image.fromarray(ps).save(os.path.join(OUT, 'fx_puff.png'))
old = os.path.join(OUT, 'fx_smoke.png')
if os.path.exists(old): os.remove(old)
print('ok')

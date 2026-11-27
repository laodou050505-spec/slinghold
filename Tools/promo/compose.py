#!/usr/bin/env python3
"""《木垒》宣传图合成（照《将军沙盘》Tools/promo_compose.py）：游戏层（Unity 黑底 / 白底两张）叠到写实房间照片上，线性光空间。
用法：compose.py <游戏层前缀（不含 _k/_w.png）> <房间照片> <输出.jpg> [--shadow] [--grade warm|night|none]
  --shadow：积木压在桌面 / 地上的软接触阴影
  --grade ：房间照片的调色（warm = 傍晚暖光；night = 入夜偏蓝压暗，和游戏夜关一致）
游戏层原像素不调，只调房间。"""
import sys, numpy as np
from PIL import Image
from scipy import ndimage as ndi

args = [a for a in sys.argv[1:] if not a.startswith('--')]
flags = [a for a in sys.argv[1:] if a.startswith('--')]
pre, bgp, out = args[:3]
grade = 'none'
for i, a in enumerate(sys.argv):
    if a == '--grade': grade = sys.argv[i + 1]
args = [a for a in args if a not in ('warm', 'night', 'none', 'dusk')]
W, H = 2560, 1440

def lin(a):
    a = a.astype(np.float64) / 255
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)

def srgb(a):
    a = np.clip(a, 0, 1)
    return np.where(a <= 0.0031308, a * 12.92, 1.055 * a ** (1 / 2.4) - 0.055)

k = lin(np.asarray(Image.open(pre + '_k.png').convert('RGB')))
w = lin(np.asarray(Image.open(pre + '_w.png').convert('RGB')))
alpha = np.clip(1 - (w - k).mean(-1), 0, 1)[..., None]
bg_img = Image.open(bgp).convert('RGB')
s = max(W / bg_img.width, H / bg_img.height)
bg_img = bg_img.resize((round(bg_img.width * s), round(bg_img.height * s)), Image.LANCZOS)
l, t = (bg_img.width - W) // 2, (bg_img.height - H) // 2
bg = lin(np.asarray(bg_img.crop((l, t, l + W, t + H))))
if grade == 'warm':
    bg = bg * np.array([1.0, 0.92, 0.80]) * 0.85
elif grade == 'dusk':
    bg = bg * np.array([1.0, 0.86, 0.70]) * 0.58
    yy, xx = np.mgrid[0:H, 0:W]
    v = 1 - 0.45 * np.clip(((xx - W / 2) / (W * 0.62)) ** 2 + ((yy - H * 0.45) / (H * 0.7)) ** 2, 0, 1)   # 房间四角压暗（只压房间，不压游戏层）
    bg = bg * v[..., None]
elif grade == 'night':
    lum = bg.mean(-1, keepdims=True)
    bg = (bg * 0.5 + lum * 0.5) * np.array([0.55, 0.62, 0.9]) * 0.42
if '--shadow' in flags:
    a = alpha[..., 0]
    sh = ndi.gaussian_filter(ndi.shift(a, (16, 0), order=1), 20)
    bg = bg * (1 - 0.4 * np.clip(sh, 0, 1))[..., None]
res = k + bg * (1 - alpha)
img = Image.fromarray((srgb(res) * 255 + 0.5).astype(np.uint8))
img.save(out, quality=93)
print(out, img.size)

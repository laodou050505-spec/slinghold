# 封面漂浮纸页（程序画，不用生图）：旧纸 + 撕边 + 褐色墨线 + 一点砖红
#   1 弹道演算纸  2 积木构件图（拱门积木轴测 + 尺寸 + 排线）  3 打字机野战笔记（涂黑 + 红铅笔）
# 运行：python3 Tools/cover/scraps.py → Assets/_PaperSiege/Art/Cover/scrap_{1,2,3}.png
import os, math
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
from scipy import ndimage
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '../..'))
OUT = os.path.join(ROOT, 'Assets/_PaperSiege/Resources/Cover'); os.makedirs(OUT, exist_ok=True)
COURIER = os.path.join(ROOT, 'ArtSource/Cover/fonts/CourierPrime-Bold.ttf')
INK = (74, 56, 44); RED = (168, 74, 58)
S = 4   # 超采样

def paper(w, h, rng, tone=(222, 208, 176)):
    a = np.zeros((h, w, 3), np.float32) + np.array(tone, np.float32)
    n = np.asarray(Image.fromarray((rng.random((h // 40 + 1, w // 40 + 1)) * 255).astype('uint8')).resize((w, h), Image.BICUBIC).filter(ImageFilter.GaussianBlur(20)), np.float32) / 255
    a *= (1 - 0.1 * (n - 0.5))[..., None]
    a += rng.normal(0, 4, (h, w, 1))
    fib = np.asarray(Image.fromarray((rng.random((h, w)) > 0.997).astype('uint8') * 255).filter(ImageFilter.GaussianBlur(0.8)), np.float32) / 255
    a -= fib[..., None] * 25
    return a

def torn_alpha(w, h, rng):
    """四边撕开：边缘向内吃进去 0～3% 的不规则锯齿 + 毛边"""
    yy, xx = np.mgrid[0:h, 0:w]
    a = np.ones((h, w), np.float32)
    def ragged(n, amp):
        t = np.arange(n)
        return amp * (0.5 + 0.35 * np.sin(t * rng.uniform(0.01, 0.03) + rng.uniform(0, 6)) + 0.15 * rng.random(n))
    top, bot = ragged(w, h * 0.03), ragged(w, h * 0.03)
    lef, rig = ragged(h, w * 0.03), ragged(h, w * 0.03)
    a[yy < top[None, :]] = 0; a[yy > h - 1 - bot[None, :]] = 0
    a[xx < lef[:, None]] = 0; a[xx > w - 1 - rig[:, None]] = 0
    hard = a < 0.5
    near = ndimage.binary_dilation(hard, iterations=3) & ~hard
    a[near & (rng.random((h, w)) > 0.55)] = 0
    # 边缘一圈发黄发暗（纸边氧化）
    d = ndimage.distance_transform_edt(a > 0.5)
    return a, np.clip(1 - d / 18, 0, 1)

def finish(name, img_draw_fn, w, h, seed):
    rng = np.random.default_rng(seed)
    base = paper(w, h, rng)
    lay = Image.new('RGBA', (w * S, h * S), (0, 0, 0, 0))
    img_draw_fn(ImageDraw.Draw(lay), rng, w * S, h * S)
    lay = lay.resize((w, h), Image.LANCZOS)
    la = np.asarray(lay, np.float32)
    al = la[..., 3:4] / 255 * 0.92
    base = base * (1 - al) + la[..., :3] * al
    a, rim = torn_alpha(w, h, rng)
    base *= (1 - 0.25 * rim[..., None] * np.array([0.6, 0.85, 1.2]))
    out = np.dstack([np.clip(base, 0, 255), a * 255]).astype(np.uint8)
    Image.fromarray(out).save(os.path.join(OUT, name))

def dotted(d, pts, col, r, step):
    acc = 0.0
    for (x0, y0), (x1, y1) in zip(pts[:-1], pts[1:]):
        L = math.hypot(x1 - x0, y1 - y0); t = acc
        while t < L:
            x, y = x0 + (x1 - x0) * t / L, y0 + (y1 - y0) * t / L
            d.ellipse((x - r, y - r, x + r, y + r), fill=col); t += step
        acc = t - L

def dashed_circle(d, cx, cy, R, col, wdt, n=48):
    for i in range(n):
        if i % 2: continue
        a0, a1 = i / n * 360, (i + 1) / n * 360
        d.arc((cx - R, cy - R, cx + R, cy + R), a0, a1, fill=col, width=wdt)

def cross(d, x, y, s, col, wdt):
    d.line((x - s, y - s, x + s, y + s), fill=col, width=wdt); d.line((x - s, y + s, x + s, y - s), fill=col, width=wdt)

def s1(d, rng, W, H):
    """弹道演算：几道虚线抛物线 + 一道红点线 + 角度弧 + 叉 + 虚线圆 + 小字"""
    f = ImageFont.truetype(COURIER, int(H * 0.05))
    ox, oy = W * 0.1, H * 0.82
    d.line((ox, oy, W * 0.92, oy), fill=INK + (255,), width=S * 2)
    for k, (vx, vy) in enumerate(((1.0, 1.25), (1.0, 1.0), (1.0, 0.72))):
        pts = []
        for i in range(60):
            t = i / 59 * 1.0
            x = ox + vx * t * W * 0.78; y = oy - (vy * t - 0.5 * 1.25 * t * t * vy / 0.62) * H * 0.95
            if y > oy: break
            pts.append((x, y))
        if k == 1: dotted(d, pts, RED + (255,), S * 3.2, S * 14)
        else:
            for a, b in zip(pts[::2][:-1], pts[::2][1:]): d.line((a, b), fill=INK + (200,), width=S * 2)
        if pts: cross(d, pts[-1][0], pts[-1][1], S * 6, INK + (255,), S * 2)
    d.arc((ox - W * 0.12, oy - W * 0.12, ox + W * 0.12, oy + W * 0.12), 300, 360, fill=INK + (255,), width=S * 2)
    d.text((ox + W * 0.13, oy - H * 0.13), '38°', font=f, fill=INK + (255,))
    dashed_circle(d, W * 0.62, H * 0.3, W * 0.13, INK + (180,), S * 2)
    cross(d, W * 0.62, H * 0.3, S * 5, RED + (255,), S * 2)
    d.text((W * 0.1, H * 0.08), 'TRAJ. No.7   d = 3.2 m', font=f, fill=INK + (255,))
    d.text((W * 0.55, H * 0.86), 't = 1.8 s', font=f, fill=INK + (230,))

def s2(d, rng, W, H):
    """积木构件图：拱门积木轴测线稿 + 排线 + 尺寸线"""
    f = ImageFont.truetype(COURIER, int(H * 0.05))
    def iso(x, y, z):  # 轴测投影
        return (W * 0.48 + (x - y) * W * 0.17, H * 0.62 + (x + y) * H * 0.085 - z * H * 0.3)
    def poly(pts, wdt=S * 2, col=INK + (255,)):
        d.line([iso(*p) for p in pts] + [iso(*pts[0])], fill=col, width=wdt, joint='curve')
    poly([(-1, 0, 0), (1, 0, 0), (1, 0, 1), (-1, 0, 1)])
    poly([(-1, 0, 1), (1, 0, 1), (1, 0.8, 1), (-1, 0.8, 1)])
    poly([(1, 0, 0), (1, 0.8, 0), (1, 0.8, 1), (1, 0, 1)])
    # 拱洞（正面半圆 + 两条竖边）
    arc = [(-0.45 + 0.9 * i / 20, 0, 0.55 + 0.45 * math.sin(math.pi * i / 20) * 0.6) for i in range(21)]
    d.line([iso(-0.45, 0, 0)] + [iso(*p) for p in arc] + [iso(0.45, 0, 0)], fill=INK + (255,), width=S * 2)
    # 排线阴影（侧面）
    for i in range(14):
        t = i / 13
        d.line((iso(1, 0.06 + 0.7 * t, 0.05), iso(1, 0.06 + 0.7 * t, 0.95)), fill=INK + (150,), width=S)
    # 尺寸线
    a, b = iso(-1, -0.35, 0), iso(1, -0.35, 0)
    d.line((a, b), fill=RED + (255,), width=S * 2)
    for p in (a, b): d.line((p[0], p[1] - S * 8, p[0], p[1] + S * 8), fill=RED + (255,), width=S * 2)
    d.text(((a[0] + b[0]) / 2 - W * 0.12, (a[1] + b[1]) / 2 + H * 0.05), '2 G', font=f, fill=RED + (255,))
    d.text((W * 0.08, H * 0.06), 'W_ARCH  /  1:4', font=f, fill=INK + (255,))
    d.text((W * 0.6, H * 0.9), 'OAK · PAINTED', font=ImageFont.truetype(COURIER, int(H * 0.04)), fill=INK + (200,))

def s3(d, rng, W, H):
    """打字机野战笔记：几行字 + 涂黑 + 红铅笔下划线和圈"""
    f = ImageFont.truetype(COURIER, int(H * 0.058))
    lines = [('FIELD NOTE  11.25', 0), ('wall west  -  2 blocks lost', 0), ('catch rate', 5), ('banner holds. next arc 41°', 0), ('resupply: 3 / 5', 0), ('aim for the lantern', 4)]
    y = H * 0.1
    for i, (ln, bars) in enumerate(lines):
        x = W * 0.08 + rng.uniform(-3, 3) * S
        d.text((x, y), ln, font=f, fill=INK + (int(rng.uniform(200, 255)),))
        if bars:                                   # 涂黑：真的画几块墨，不用字形
            bx = x + d.textlength(ln + '  ', font=f)
            d.rectangle((bx, y + H * 0.012, bx + bars * W * 0.045, y + H * 0.062), fill=(40, 34, 30, 235))
        if i == 3:
            d.line((W * 0.07, y + H * 0.075, W * 0.8, y + H * 0.068), fill=RED + (220,), width=S * 3)
        y += H * 0.13
    d.ellipse((W * 0.55, H * 0.6, W * 0.92, H * 0.78), outline=RED + (210,), width=S * 3)
    d.rectangle((W * 0.08, H * 0.88, W * 0.5, H * 0.92), fill=(40, 34, 30, 235))

if __name__ == '__main__':
    finish('scrap_1.png', s1, 768, 560, 1)
    finish('scrap_2.png', s2, 640, 560, 2)
    finish('scrap_3.png', s3, 720, 640, 3)
    print('ok')

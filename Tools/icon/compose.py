#!/usr/bin/env python3
"""应用图标合成（11-27）：Unity 实拍的透明层（ArtSource/Icon_1127/层/*.png）→ 裁切居中 + 背景 + 微阴影 → 1024² 图标。
另出一张“对比页”：每个方案的大图 + 头显应用列表尺寸（128 / 64 px，圆角）并排，方便挑。
用法：python3 Tools/icon/compose.py"""
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
LAY = os.path.join(ROOT, 'ArtSource/Icon_1127/层')
OUT = os.path.join(ROOT, 'ArtSource/Icon_1127')
S = 1024


def grad(top, bot, s=S, curve=1.0):
    t = np.linspace(0, 1, s)[:, None] ** curve
    a = np.array(top, float)[None, None, :] / 255
    b = np.array(bot, float)[None, None, :] / 255
    g = a * (1 - t[..., None]) + b * t[..., None]
    g = np.repeat(g, s, axis=1)
    return Image.fromarray((g * 255).astype(np.uint8)).convert('RGBA')


def paper(base, seed=3, strength=4):   # 11-27 第一版 10 太脏，像发霉的斑
    """淡淡的纸纹（低频斑驳 + 细颗粒），让背景不是死平的渐变"""
    rng = np.random.default_rng(seed)
    lo = Image.fromarray((rng.random((32, 32)) * 255).astype(np.uint8)).resize((S, S), Image.BICUBIC)
    hi = Image.fromarray((rng.random((S, S)) * 255).astype(np.uint8))
    n = (np.asarray(lo, float) - 128) * 0.6 + (np.asarray(hi, float) - 128) * 0.25
    a = np.asarray(base.convert('RGB'), float) + n[..., None] * strength / 128
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8)).convert('RGBA')


def vignette(img, k=0.18):
    y, x = np.mgrid[0:S, 0:S] / (S - 1) - 0.5
    v = 1 - k * np.clip((x * x + y * y) * 2.2, 0, 1)
    a = np.asarray(img.convert('RGB'), float) * v[..., None]
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8)).convert('RGBA')


def fit(layer, box=0.8, cx=0.5, cy=0.5, crop=None):
    """把透明层里的主体按包围盒缩放到图标的 box 比例、放到 (cx, cy)；crop = 先按相对坐标裁一块"""
    im = layer
    if crop:
        w, h = im.size
        im = im.crop((int(crop[0] * w), int(crop[1] * h), int(crop[2] * w), int(crop[3] * h)))
    bb = im.getbbox()
    im = im.crop(bb)
    sc = box * S / max(im.size)
    im = im.resize((max(1, int(im.width * sc)), max(1, int(im.height * sc))), Image.LANCZOS)
    canvas = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    canvas.alpha_composite(im, (int(cx * S - im.width / 2), int(cy * S - im.height / 2)))
    return canvas


def shadow(layer, off=(0, 14), blur=18, alpha=0.35, color=(40, 25, 15)):
    a = np.asarray(layer.split()[3], float) / 255 * alpha
    sh = Image.new('RGBA', layer.size, color + (0,))
    sh.putalpha(Image.fromarray((a * 255).astype(np.uint8)))
    sh = sh.filter(ImageFilter.GaussianBlur(blur))
    c = Image.new('RGBA', layer.size, (0, 0, 0, 0))
    c.alpha_composite(sh, off)
    return c


def compose(bg, fg, drop=True):
    out = bg.copy()
    if drop:
        out.alpha_composite(shadow(fg))
    out.alpha_composite(fg)
    return out


def L(name):
    return Image.open(os.path.join(LAY, name + '.png')).convert('RGBA')


def sun_disc(img, cx, cy, r, col=(255, 236, 190), glow=(255, 214, 150)):
    """柔和的日轮 + 光晕（画在背景上）"""
    g = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(g)
    for k in range(18, 0, -1):
        rr = r * (1 + k * 0.22)
        d.ellipse((cx - rr, cy - rr, cx + rr, cy + rr), fill=glow + (int(10 * (19 - k) / 18),))
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=col + (255,))
    g = g.filter(ImageFilter.GaussianBlur(3))
    out = img.copy(); out.alpha_composite(g)
    return out


def build():
    vers = {}
    # A 帅旗灯笼（封面同款）：暖晨色背景
    bgA = vignette(paper(grad((246, 226, 192), (214, 176, 128), curve=1.1)), 0.22)
    vers['A_帅旗灯笼'] = compose(bgA, fit(L('V1_帅旗灯笼'), 0.84, 0.5, 0.53))
    # A2 帅旗灯笼 + 飘散积木：深一点的黄昏蓝，积木更跳
    bgA2 = vignette(paper(grad((118, 150, 186), (238, 196, 142), curve=0.9), 5), 0.2)
    vers['A2_帅旗灯笼_积木'] = compose(bgA2, fit(L('V1c_帅旗灯笼_积木'), 0.9, 0.5, 0.52, crop=(0, 0, 0.82, 1)))   # 右边那片侧着的积木只剩一条白线，裁掉
    # B 我方城堡（修正版）：晨天渐变 + 日轮
    bgB = sun_disc(vignette(paper(grad((150, 186, 214), (244, 216, 170), curve=1.0), 7), 0.16), 760, 250, 58)
    vers['B_城堡'] = compose(bgB, fit(L('V2_我方城堡'), 0.78, 0.5, 0.56))
    # C 主楼帅旗特写：大块造型，小尺寸更好认
    bgC = vignette(paper(grad((126, 168, 204), (236, 210, 168), curve=1.2), 9), 0.18)
    vers['C_主楼帅旗'] = compose(bgC, fit(L('V4_主楼帅旗'), 1.0, 0.5, 0.6))   # 主体下沿压出画框（出血），不悬空
    # D 拉满的弹弓 + 远处敌城：黄昏暖色，动作感
    bgD = sun_disc(vignette(paper(grad((98, 118, 168), (246, 178, 120), curve=0.85), 11), 0.2), 690, 610, 70, (255, 226, 170), (255, 170, 110))
    vers['D_拉满弹弓'] = compose(bgD, fit(L('V3_拉满弹弓'), 0.98, 0.5, 0.5))
    # D2 弹弓 + 城更大
    vers['D2_弹弓远城'] = compose(bgD, fit(L('V3b_拉满弹弓_远城'), 0.98, 0.5, 0.5))
    return vers


def round_mask(s, r):
    m = Image.new('L', (s, s), 0)
    ImageDraw.Draw(m).rounded_rectangle((0, 0, s - 1, s - 1), r, fill=255)
    return m


def sheet(vers):
    names = list(vers)
    W = 420
    cell_h = W + 190
    cols = 3
    rows = (len(names) + cols - 1) // cols
    page = Image.new('RGB', (cols * W + (cols + 1) * 30, rows * cell_h + 40), (36, 34, 40))
    font = None
    for fp in ['/System/Library/Fonts/PingFang.ttc', '/System/Library/Fonts/STHeiti Medium.ttc', '/System/Library/Fonts/Hiragino Sans GB.ttc']:
        if os.path.exists(fp):
            font = ImageFont.truetype(fp, 30); small = ImageFont.truetype(fp, 20); break
    d = ImageDraw.Draw(page)
    for i, n in enumerate(names):
        x = 30 + (i % cols) * (W + 30)
        y = 30 + (i // cols) * cell_h
        big = vers[n].convert('RGB').resize((W, W), Image.LANCZOS)
        page.paste(big, (x, y), round_mask(W, int(W * 0.22)))
        d.text((x, y + W + 12), n.replace('_', ' '), fill=(240, 236, 228), font=font)
        # 头显应用列表里的大小（约 128 / 64 px，圆角）
        for k, s in enumerate((128, 64)):
            sm = vers[n].convert('RGB').resize((s, s), Image.LANCZOS)
            page.paste(sm, (x + k * 150, y + W + 56), round_mask(s, int(s * 0.22)))
        d.text((x + 230, y + W + 100), '← 列表里的实际大小', fill=(170, 166, 160), font=small)
    return page


if __name__ == '__main__':
    vers = build()
    for n, im in vers.items():
        im.convert('RGB').save(os.path.join(OUT, f'icon_{n}.png'))
    sheet(vers).save(os.path.join(OUT, '图标方案_对比.png'))
    print('ok', list(vers))

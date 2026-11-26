# 封面标题“积木拼字”：字形切成一块块旧漆积木（相邻不重色、浅色倒角 + 深色缝 + 掉漆露木），透明底，印到布旗上用
# 运行：python3 Tools/cover/title_blocks.py  → ArtSource/Cover/title/title_{zh,en}.png（+ 预览）
import os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
from scipy import ndimage
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '../..'))
OUT = os.path.join(ROOT, 'ArtSource/Cover/title'); os.makedirs(OUT, exist_ok=True)
FONTS = os.path.join(ROOT, 'ArtSource/Cover/fonts')
PAINT = np.array([[96,140,134],[192,150,76],[160,86,66]], np.float32)   # 灰青 赭黄 砖红（和积木漆色同源；奶白会和布底混掉，不用）
BARE = np.array([178,140,96], np.float32)

def glyph_mask(text, font, size, vertical):
    f = ImageFont.truetype(font, size)
    if vertical:
        imgs = [glyph_mask(ch, font, size, False) for ch in text]
        w = max(i.shape[1] for i in imgs); gap = int(size * 0.06)
        h = sum(i.shape[0] for i in imgs) + gap * (len(imgs) - 1)
        m = np.zeros((h, w), bool); y = 0
        for i in imgs: x = (w - i.shape[1]) // 2; m[y:y+i.shape[0], x:x+i.shape[1]] = i; y += i.shape[0] + gap
        return m
    bb = f.getbbox(text)
    im = Image.new('L', (bb[2] - bb[0] + 20, bb[3] - bb[1] + 20), 0)
    ImageDraw.Draw(im).text((10 - bb[0], 10 - bb[1]), text, font=f, fill=255)
    return np.asarray(im) > 127

def blocks(mask, cell, seed, pad=40):
    rng = np.random.default_rng(seed)
    m = np.pad(mask, pad); H, W = m.shape
    gap = max(4, cell // 24)
    # 顺着笔画切：横笔画（横向连续长度 > 纵向）只竖着切，竖笔画只横着切 —— 每一笔像几块积木首尾相接
    def runs(a, axis):
        a = a if axis == 1 else a.T
        out = np.zeros(a.shape, np.int32)
        for i, row in enumerate(a):
            d = np.diff(np.concatenate(([0], row.astype(np.int8), [0])))
            st, en = np.where(d == 1)[0], np.where(d == -1)[0]
            for s0, e0 in zip(st, en): out[i, s0:e0] = e0 - s0
        return out if axis == 1 else out.T
    rh, rv = runs(m, 1), runs(m, 0)
    horiz = ndimage.uniform_filter((rh > rv * 1.15).astype(np.float32), 9) > 0.5
    vcut = np.zeros((H, W), bool); hcut = np.zeros((H, W), bool)
    x = pad + rng.integers(cell // 3, cell)
    while x < W: vcut[:, x:x+gap] = True; x += int(cell * rng.uniform(0.9, 1.3))
    y = pad + rng.integers(cell // 3, cell)
    while y < H: hcut[y:y+gap, :] = True; y += int(cell * rng.uniform(0.9, 1.3))
    cut = (vcut & horiz) | (hcut & ~horiz)
    b = m & ~cut
    b = ndimage.binary_opening(b, iterations=2)                 # 去掉切出来的碎渣
    lab, n = ndimage.label(b)
    sizes = ndimage.sum(b, lab, range(1, n + 1))
    for k, s in enumerate(sizes, 1):                            # 太小的碎块并回去（改成缝的颜色）
        if s < cell * cell * 0.08: b[lab == k] = False
    lab, n = ndimage.label(b)
    # 没切断的半截缝（两边是同一块）补回去，免得像一道划痕
    slit, ns = ndimage.label(m & ~b)
    for k in range(1, ns + 1):
        sk = slit == k
        ring = ndimage.binary_dilation(sk, iterations=2) & ~sk
        ids = np.unique(lab[ring]); ids = ids[ids > 0]
        if len(ids) == 1: b[sk] = True
    lab, n = ndimage.label(b)
    # 太薄的碎条（某个方向不到 0.32 格）并回相邻那块：把它和邻块之间的缝填上
    for k, sl in enumerate(ndimage.find_objects(lab), 1):
        if sl is None: continue
        hh, ww = sl[0].stop - sl[0].start, sl[1].stop - sl[1].start
        if min(hh, ww) < cell * 0.32:
            b |= ndimage.binary_dilation(lab == k, iterations=gap + 2) & m
    lab, n = ndimage.label(b)
    # 相邻不重色：按邻接图贪心上色
    near = {k: set() for k in range(1, n + 1)}
    dl = ndimage.grey_dilation(lab, size=(gap * 3 + 3, gap * 3 + 3))
    for k in range(1, n + 1):
        ring = (ndimage.binary_dilation(lab == k, iterations=gap + 2)) & (lab > 0) & (lab != k)
        near[k] = set(np.unique(lab[ring]))
    colour = {}
    for k in range(1, n + 1):
        used = {colour[j] for j in near[k] if j in colour}
        opts = [c for c in range(len(PAINT)) if c not in used] or list(range(len(PAINT)))
        colour[k] = int(rng.choice(opts))
    rgb = np.zeros((H, W, 3), np.float32); a = np.zeros((H, W), np.float32)
    tex = np.asarray(Image.fromarray((rng.random((H // 4 + 1, W // 4 + 1)) * 255).astype('uint8')).resize((W, H), Image.BICUBIC), np.float32) / 255
    fine = rng.random((H, W))
    yy, xx = np.mgrid[0:H, 0:W]
    for k in range(1, n + 1):
        sel = lab == k
        c = PAINT[colour[k]]
        grain = np.sin(yy[sel] * 0.25 + np.sin(xx[sel] * 0.015) * 5) * 0.04
        rgb[sel] = c * (0.93 + 0.1 * tex[sel, None] + grain[:, None])
    # 掉漆：只在每块的边角磕掉（离缝 2～8 像素内、随噪声成片）
    dist = ndimage.distance_transform_edt(b)
    chip = b & (dist < 2 + 7 * tex) & (tex > 0.62) & (fine > 0.25)
    rgb[chip] = BARE * (0.85 + 0.2 * fine[chip, None])
    # 倒角：左上亮、右下暗（像积木的棱）
    inner = ndimage.binary_erosion(b, iterations=max(2, cell // 30))
    rim = b & ~inner
    sh = np.roll(b, (-3, -3), (0, 1))
    lit = rim & ~sh                                             # 右下边
    rgb[rim] *= 1.1; rgb[lit] *= 0.7
    edge = b & ~ndimage.binary_erosion(b, iterations=1)
    rgb[edge] *= 0.5
    # 整个字形外轮廓一圈深色细线（印在布上更好认）
    sil = ndimage.binary_closing(b, iterations=gap) & m
    outline = ndimage.binary_dilation(sil, iterations=3) & ~b
    outline &= ~ndimage.binary_dilation(cut & m, iterations=0) | ~m
    rgb[outline] = np.array([62, 48, 40], np.float32)
    a[b] = 1.0; a[outline] = 0.9
    out = np.dstack([np.clip(rgb, 0, 255), a * 255]).astype('uint8')
    return Image.fromarray(out)

def preview(img, name):
    bg = Image.new('RGBA', img.size, (226, 214, 190, 255)); bg.alpha_composite(img)
    bg.convert('RGB').save(os.path.join(OUT, name))

def stack(masks, gap):
    w = max(x.shape[1] for x in masks); h = sum(x.shape[0] for x in masks) + gap * (len(masks) - 1)
    out = np.zeros((h, w), bool); y = 0
    for x in masks: c = (w - x.shape[1]) // 2; out[y:y+x.shape[0], c:c+x.shape[1]] = x; y += x.shape[0] + gap
    return out

if __name__ == '__main__':
    zh = blocks(glyph_mask('木垒', os.path.join(FONTS, 'ZCOOLQingKeHuangYou-Regular.ttf'), 900, True), 150, 11)
    zh.save(os.path.join(OUT, 'title_zh.png')); preview(zh, 'title_zh_preview.png')
    en = blocks(glyph_mask('SLINGHOLD', os.path.join(FONTS, 'Bungee-Regular.ttf'), 520, False), 120, 5)
    en.save(os.path.join(OUT, 'title_en.png')); preview(en, 'title_en_preview.png')
    print(zh.size, en.size)
    en2 = blocks(stack([glyph_mask('SLING', os.path.join(FONTS, 'Bungee-Regular.ttf'), 520, False), glyph_mask('HOLD', os.path.join(FONTS, 'Bungee-Regular.ttf'), 520, False)], 40), 120, 9)
    en2.save(os.path.join(OUT, 'title_en2.png')); preview(en2, 'title_en2_preview.png')

# 封面布旗印花：奶白旧布（织纹、水渍、发黄）+ 上半部原样 TRIPO 图标（黑版，不带字、不垫底）+ 下半部积木拼字标题 + 一行小字
#              + 底边撕成长短不一的布条、四周毛边（透明度）
# 运行：python3 Tools/cover/banner.py → Assets/_PaperSiege/Art/Cover/banner_{zh,en}.png（1024×2304，RGBA）
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
from scipy import ndimage
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '../..'))
TITLE = os.path.join(ROOT, 'ArtSource/Cover/title')
FONTS = os.path.join(ROOT, 'ArtSource/Cover/fonts')
OUT = os.path.join(ROOT, 'Assets/_PaperSiege/Resources/Cover'); os.makedirs(OUT, exist_ok=True)
W, H = 1024, 2304
FRINGE = 330          # 底部布条区高度（像素）

def tripo_mark(width):
    """从横版黑标志里裁出图标（字标 TRIPO 不要），放大后把边缘收锐；颜色原样"""
    src = Image.open(os.path.join(ROOT, 'ArtSource/Brand/raw/tripo_logo_h_800_black.png')).convert('RGBA')
    a = np.asarray(src)[..., 3]
    cols = np.where(a.max(0) > 10)[0]
    gaps = np.where(np.diff(cols) > 8)[0]
    x1 = cols[gaps[0]] + 1                      # 图标和字母 T 之间的第一道空隙
    rows = np.where(a[:, cols[0]:x1].max(1) > 10)[0]
    icon = src.crop((cols[0] - 2, rows[0] - 2, x1 + 2, rows[-1] + 3))
    k = width / icon.width
    big = icon.resize((width, int(icon.height * k)), Image.LANCZOS)
    arr = np.asarray(big).astype(np.float32)
    al = arr[..., 3] / 255
    al = np.clip((al - 0.5) * 2.6 + 0.5, 0, 1)   # 边缘收锐（放大后的软边变回干净的边）
    # 颜色：半透明边缘处的颜色按预乘还原，避免发灰
    arr[..., 3] = al * 255
    return Image.fromarray(arr.astype(np.uint8))

def cloth(rng):
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    base = np.zeros((H, W, 3), np.float32) + np.array([226, 214, 188], np.float32)
    weave = (np.sin(xx * 2.1) * 0.5 + 0.5) * (np.sin(yy * 2.3) * 0.5 + 0.5)
    base += (weave[..., None] - 0.25) * 9
    base += rng.normal(0, 4, (H, W, 1))
    def lowfreq(sc, blur):
        n = rng.random((H // sc + 1, W // sc + 1)).astype(np.float32)
        return np.asarray(Image.fromarray((n * 255).astype(np.uint8)).resize((W, H), Image.BICUBIC).filter(ImageFilter.GaussianBlur(blur)), np.float32) / 255
    stain = lowfreq(64, 30)
    base *= (1 - 0.06 * np.clip(stain - 0.45, 0, 1)[..., None] * np.array([0.6, 0.9, 1.4]))   # 发黄水渍
    yellow = np.clip(1 - yy / H, 0, 1) * 0.0 + np.clip((yy / H - 0.6) * 2, 0, 1)
    base *= (1 - 0.06 * yellow[..., None] * np.array([0.5, 0.8, 1.6]))                     # 下摆更旧
    # 竖向的布纹褶皱阴影（低频，配合网格的真实摆动）
    base *= (0.96 + 0.05 * np.sin(xx / W * 9.0 + lowfreq(128, 40) * 3))[..., None]
    return base

def edges(rng):
    """透明度：四周毛边 + 底部撕开的布条（斜着撕、末端参差、楔形裂口、散线头）"""
    a = np.ones((H, W), np.float32)
    yy, xx = np.mgrid[0:H, 0:W]
    fray = rng.random((H, W))
    for side in (xx, W - 1 - xx):
        a[(side < 4) & (fray > 0.35)] = 0
        a[side < 1] = 0
    top = H - FRINGE
    xs = [0]
    while xs[-1] < W: xs.append(xs[-1] + int(rng.uniform(110, 210)))
    xs[-1] = W
    for x0, x1 in zip(xs[:-1], xs[1:]):
        L = rng.uniform(0.3, 1.0) * FRINGE                     # 这一条有多长
        slant = rng.uniform(-0.35, 0.35)                        # 末端斜着撕
        ph = rng.uniform(0, 9)
        for xi in range(x0, x1):
            t = (xi - x0) / max(1, x1 - x0)
            end = top + L + slant * (xi - (x0 + x1) / 2) + np.sin(xi * 0.09 + ph) * 10 + np.sin(xi * 0.31) * 5 + rng.normal(0, 3.5)
            # 末端两角收窄一点（撕开的布条是尖的）
            end -= (abs(t - 0.5) * 2) ** 3 * L * 0.35
            a[int(max(top, end)):, xi] = 0
        # 和右边那条之间的楔形裂口：从 top 往上裂一小段，越往下越宽
        crack = int(rng.uniform(30, 110))
        for yi in range(top - crack, H):
            w = int(1 + max(0, yi - (top - crack)) * 0.06 + rng.normal(0, 0.8))
            a[yi, max(0, x1 - w // 2):min(W, x1 + w // 2 + 1)] = 0
    # 毛边：透明区旁 1～4 像素随机吃掉
    hard = a < 0.5
    near = ndimage.binary_dilation(hard, iterations=4) & ~hard
    a[near & (fray > 0.6)] = 0
    a = np.maximum(a, 0)
    # 散线头：从布条末端垂下的几根细线
    edge_pts = np.argwhere(ndimage.binary_dilation(hard, iterations=1) & ~hard & (yy > top + 40))
    for k in rng.choice(len(edge_pts), size=min(28, len(edge_pts)), replace=False):
        y, x = edge_pts[k]
        ln = int(rng.uniform(12, 45)); dx = rng.uniform(-0.25, 0.25)
        for t in range(ln):
            yi, xi = y + t, int(x + dx * t)
            if 0 <= yi < H and 0 <= xi < W: a[yi, xi] = 0.85
    return a

def paste(dst, img, cx, cy, ink_mul=1.0):
    """把 RGBA 印到布上：按 alpha 覆盖，颜色乘一点布纹（印在布上而不是贴纸）"""
    x0, y0 = int(cx - img.width / 2), int(cy - img.height / 2)
    src = np.asarray(img).astype(np.float32)
    sub = dst[y0:y0 + img.height, x0:x0 + img.width]
    al = src[..., 3:4] / 255 * ink_mul
    weave = sub.mean(-1, keepdims=True) / 214.0
    sub[:] = sub * (1 - al) + src[..., :3] * np.clip(weave, 0.9, 1.06) * al

def text(dst, s, font, size, cx, cy, col):
    im = Image.new('RGBA', (W, size * 2), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    f = ImageFont.truetype(font, size)
    if 'Outfit' in font:
        try: f.set_variation_by_axes([500])          # 可变字重：取 Medium，和中文那行一样粗
        except Exception: pass
    bb = d.textbbox((0, 0), s, font=f)
    d.text(((W - (bb[2] - bb[0])) / 2 - bb[0], size // 2), s, font=f, fill=col)
    im = im.crop(im.getbbox()); paste(dst, im, cx, cy, 0.92)

def text_at(dst, s, font, size, cx, cy, col, weight=500):
    im = Image.new('RGBA', (W, size * 2), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
    f = ImageFont.truetype(font, size)
    if 'Outfit' in font:
        try: f.set_variation_by_axes([weight])
        except Exception: pass
    d.text((10, size // 2), s, font=f, fill=col)
    im = im.crop(im.getbbox()); paste(dst, im, cx, cy, 0.92)

def build(lang, seed):
    rng = np.random.default_rng(seed)
    img = cloth(rng)
    # 标题是主角：放在旗面中上部
    t = Image.open(os.path.join(TITLE, 'title_zh.png' if lang == 'zh' else 'title_en2.png'))
    area_top, area_bot = 150, H - FRINGE - 330
    box_w, box_h = W * (0.80 if lang == 'zh' else 0.88), (area_bot - area_top) * (0.86 if lang == 'zh' else 0.46)
    k = min(box_w / t.width, box_h / t.height)
    t = t.resize((int(t.width * k), int(t.height * k)), Image.LANCZOS)
    ty = area_top + t.height / 2 if lang == 'zh' else area_top + (area_bot - area_top) * 0.47   # 英文两行偏矮：整组放在可印区域中部，和底部小标拉开
    paste(img, t, W / 2, ty)
    line_y = ty + t.height / 2 + 68
    if lang == 'zh':
        text(img, 'SLINGHOLD · 弹弓攻城', os.path.join(ROOT, 'Assets/_PaperSiege/Resources/Fonts/NotoSansSC-Medium.otf'), 50, W / 2, line_y, (88, 62, 50, 255))
    else:
        text(img, 'A TABLETOP SIEGE', os.path.join(FONTS, 'CourierPrime-Bold.ttf'), 58, W / 2, line_y, (88, 62, 50, 255))
    # 公司标志：右下角布条上方一枚小标（像织在布上的商标），原样颜色、不拉伸；署名文字另起一行在它下面（品牌规则）
    brand = os.path.join(ROOT, 'Assets/_PaperSiege/Resources/Fonts/Outfit.ttf')
    sans = os.path.join(ROOT, 'Assets/_PaperSiege/Resources/Fonts/NotoSansSC-Medium.otf')
    # 11-27 用户：署名字太小看不清（24 px ≈ 布上 6 mm，头显里只有几个像素）→ 字 44 px（≈ 1.1 cm）、加粗，图标也放大；
    # 整组挪到布旗下沿居中（英文一行变长，放右下角会被裁）
    mx, my = W / 2, H - FRINGE - 250
    paste(img, tripo_mark(int(W * 0.15)), mx, my)
    if lang == 'zh': text_at(img, 'Tripo XR 出品', os.path.join(ROOT, 'Assets/_PaperSiege/Resources/Fonts/NotoSansSC-Medium.otf'), 46, mx, my + 112, (52, 42, 36, 255), weight=700)
    else: text_at(img, 'Tripo XR Developers', brand, 46, mx, my + 112, (52, 42, 36, 255), weight=650)
    a = edges(rng)
    out = np.dstack([np.clip(img, 0, 255), a * 255]).astype(np.uint8)
    Image.fromarray(out).save(os.path.join(OUT, f'banner_{lang}.png'))
    prev = Image.new('RGB', (W, H), (50, 48, 46)); prev.paste(Image.fromarray(out), (0, 0), Image.fromarray(out))
    prev.save(os.path.join(ROOT, f'ArtSource/Cover/banner_{lang}_preview.png'))

if __name__ == '__main__':
    build('zh', 3); build('en', 4); print('ok')

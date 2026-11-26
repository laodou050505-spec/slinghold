# 结算小布旗：积木拼字的结果大字（胜利 / 失守 / 时间到，英文 VICTORY / FALLEN / TIME UP）印在小布旗上；
# 输的两面旗边缘被火燎焦（只是焦边，不做破洞）。外加一张空白旧纸（战报纸条底）。
# 运行：python3 Tools/cover/result_flags.py → Assets/_PaperSiege/Resources/Cover/result_{win,lost,time}_{zh,en}.png、note_paper.png
import os, sys
import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import title_blocks as T
import banner as B
import scraps as S
OUT = os.path.join(B.ROOT, 'Assets/_PaperSiege/Resources/Cover')
W, H, FR = 512, 640, 110
ZH = os.path.join(T.FONTS, 'ZCOOLQingKeHuangYou-Regular.ttf'); EN = os.path.join(T.FONTS, 'Bungee-Regular.ttf')
WORDS = {'win': ('胜利', ['VICTORY']), 'lost': ('失守', ['FALLEN']), 'time': ('时间到', ['TIME', 'UP']), 'end': ('全胜', ['THE', 'END'])}
# 只重画指定的几面：python3 result_flags.py end（不带参数 = 全部；注意 rng 种子用了 hash()，每次运行都会变，已定稿的别重画）
ONLY = set(sys.argv[1:])

def cloth(rng):
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    a = np.zeros((H, W, 3), np.float32) + np.array([228, 216, 190], np.float32)
    a += ((np.sin(xx * 2.1) * 0.5 + 0.5) * (np.sin(yy * 2.3) * 0.5 + 0.5) - 0.25)[..., None] * 8
    a += rng.normal(0, 3.5, (H, W, 1))
    return a

def edges(rng):
    a = np.ones((H, W), np.float32)
    yy, xx = np.mgrid[0:H, 0:W]
    top = H - FR
    xs = [0]
    while xs[-1] < W: xs.append(xs[-1] + int(rng.uniform(70, 130)))
    xs[-1] = W
    for x0, x1 in zip(xs[:-1], xs[1:]):
        L = rng.uniform(0.3, 1.0) * FR; ph = rng.uniform(0, 9)
        for xi in range(x0, x1):
            t = (xi - x0) / max(1, x1 - x0)
            end = top + L + np.sin(xi * 0.12 + ph) * 6 + rng.normal(0, 2.5) - (abs(t - 0.5) * 2) ** 3 * L * 0.35
            a[int(max(top, end)):, xi] = 0
    fray = rng.random((H, W))
    edge = np.minimum(xx, W - 1 - xx)
    a[(edge < 3) & (fray > 0.4)] = 0
    hard = a < 0.5
    a[ndimage.binary_dilation(hard, iterations=3) & ~hard & (fray > 0.6)] = 0
    return a

def scorch(img, a, rng):
    """火燎焦边：离透明边越近越焦（深褐 → 炭黑），带不规则的噪声边界"""
    d = ndimage.distance_transform_edt(a > 0.5)
    n = np.asarray(Image.fromarray((rng.random((H // 16 + 1, W // 16 + 1)) * 255).astype(np.uint8)).resize((W, H), Image.BICUBIC), np.float32) / 255
    k = np.clip(1 - (d - 2) / (10 + 22 * n), 0, 1) ** 1.3   # 窄而参差的一圈焦边
    burnt = np.array([58, 40, 30], np.float32); brown = np.array([150, 104, 66], np.float32)
    img[:] = img * (1 - k[..., None]) + (brown * (1 - k[..., None]) + burnt * k[..., None]) * k[..., None]
    a[(k > 0.9) & (n > 0.5)] = 0      # 最焦的地方烧掉一点点边
    rim = (a > 0.5) & (ndimage.distance_transform_edt(a > 0.5) < 2.5)
    img[rim] = burnt * 0.8            # 最外沿一道炭黑

for key, (zh, en) in WORDS.items():
    if ONLY and key not in ONLY: continue
    for lang in ('zh', 'en'):
        rng = np.random.default_rng(sum(map(ord, key + lang)))
        img = cloth(rng)
        if lang == 'zh':
            m = T.glyph_mask(zh, ZH, 520, True)
            t = T.blocks(m, 110, 3)
        else:
            m = T.stack([T.glyph_mask(w, EN, 300, False) for w in en], 30)
            t = T.blocks(m, 80, 3)
        box_w, box_h = W * (0.78 if lang == 'zh' else 0.9), (H - FR) * 0.84
        k = min(box_w / t.width, box_h / t.height)
        t = t.resize((int(t.width * k), int(t.height * k)), Image.LANCZOS)
        B.paste(img, t, W / 2, 40 + (H - FR - 40) / 2)
        a = edges(rng)
        if key in ('lost', 'time'): scorch(img, a, rng)
        out = np.dstack([np.clip(img, 0, 255), a * 255]).astype(np.uint8)
        Image.fromarray(out).save(os.path.join(OUT, f'result_{key}_{lang}.png'))
if not ONLY: S.finish('note_paper.png', lambda d, rng, w, h: None, 560, 640, 9)
if not ONLY:
    import shutil; shutil.move(os.path.join(S.OUT, 'note_paper.png'), os.path.join(OUT, 'note_paper.png')) if S.OUT != OUT else None
print('ok')

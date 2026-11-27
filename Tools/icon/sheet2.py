#!/usr/bin/env python3
"""第二轮图标方案对比页：每个方案一张大图（圆角）+ 头显应用列表里的实际大小（128 / 64 px）"""
import os, sys
from PIL import Image, ImageDraw, ImageFont
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
D = os.path.join(ROOT, 'ArtSource/Icon_1127')
items = [
    ('1 弹弓', 'gen/1_弹弓/icon.png'),
    ('2 被击中的塔', 'gen/2_被击中的塔/icon.png'),
    ('3 海报 · 弹道', 'gen/3_海报弹道/icon.png'),
    ('4 帅旗灯笼 · 夜', 'gen/4_帅旗灯笼夜/icon.png'),
    ('5 积木“垒”字 · 深青', 'icon_E1_垒字_深青.png'),
    ('6 积木“垒”字 · 砖红', 'icon_E2_垒字_砖红.png'),
]

def rmask(s, r):
    m = Image.new('L', (s, s), 0); ImageDraw.Draw(m).rounded_rectangle((0, 0, s - 1, s - 1), r, fill=255); return m

fp = '/System/Library/Fonts/Hiragino Sans GB.ttc'
font = ImageFont.truetype(fp, 32); small = ImageFont.truetype(fp, 20)
W = 440; cols = 3; ch = W + 200
rows = (len(items) + cols - 1) // cols
page = Image.new('RGB', (cols * W + (cols + 1) * 36, rows * ch + 40), (30, 29, 34))
d = ImageDraw.Draw(page)
for i, (name, f) in enumerate(items):
    p = os.path.join(D, f)
    if not os.path.exists(p): continue
    im = Image.open(p).convert('RGB')
    x = 36 + (i % cols) * (W + 36); y = 30 + (i // cols) * ch
    page.paste(im.resize((W, W), Image.LANCZOS), (x, y), rmask(W, int(W * 0.22)))
    d.text((x, y + W + 12), name, fill=(240, 236, 228), font=font)
    for k, s in enumerate((128, 64)):
        page.paste(im.resize((s, s), Image.LANCZOS), (x + k * 150, y + W + 60), rmask(s, int(s * 0.22)))
    d.text((x + 236, y + W + 104), '← 列表里的实际大小', fill=(160, 156, 150), font=small)
page.save(os.path.join(D, '图标方案_对比2.png'))
print('ok')

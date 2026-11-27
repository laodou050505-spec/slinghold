#!/usr/bin/env python3
"""字标图标（11-27）：用封面同一套“积木拼字”（Tools/cover/title_blocks.py）把一个“垒”字 / “木垒”两字拼成图标主体。
E：砖红底上一个大“垒”字（积木拼成），底下一小块帅旗灯笼剪影；F：奶白旧布底，“木垒”竖排两字 + 角上一面小旗。"""
import os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
sys.path.insert(0, os.path.join(ROOT, 'Tools/cover'))
import title_blocks as tb
sys.path.insert(0, os.path.join(ROOT, 'Tools/icon'))
from compose import grad, paper, vignette, shadow, fit, S

FONT = os.path.join(ROOT, 'ArtSource/Cover/fonts/ZCOOLQingKeHuangYou-Regular.ttf')
OUT = os.path.join(ROOT, 'ArtSource/Icon_1127')


def blocks_on(bgcol_top, bgcol_bot, text, vertical, cell, seed, box, cy=0.5, paint=None, noise=3):
    if paint is not None:
        tb.PAINT = np.array(paint, np.float32)
    g = tb.blocks(tb.glyph_mask(text, FONT, 900, vertical), cell, seed)
    bg = vignette(paper(grad(bgcol_top, bgcol_bot, curve=1.1), seed, noise), 0.2)
    fg = fit(g, box, 0.5, cy)
    out = bg.copy(); out.alpha_composite(shadow(fg, (0, 16), 16, 0.4)); out.alpha_composite(fg)
    return out


if __name__ == '__main__':
    base = [[96, 140, 134], [192, 150, 76], [160, 86, 66]]
    # E1：深青底 + 大“垒”字（奶白 / 赭黄 / 砖红积木）
    e1 = blocks_on((58, 92, 96), (34, 60, 66), '垒', False, 150, 21, 0.78, 0.52, [[220, 209, 183], [192, 150, 76], [176, 92, 70]])
    e1.convert('RGB').save(os.path.join(OUT, 'icon_E1_垒字_深青.png'))
    # E2：砖红底 + 大“垒”字（奶白 / 赭黄 / 灰青）
    e2 = blocks_on((178, 96, 72), (130, 64, 50), '垒', False, 150, 33, 0.78, 0.52, [[226, 214, 190], [204, 162, 84], [96, 140, 134]])
    e2.convert('RGB').save(os.path.join(OUT, 'icon_E2_垒字_砖红.png'))
    # F：奶白旧布底 + “木垒”竖排（原封面配色）
    f = blocks_on((236, 226, 204), (214, 198, 166), '木垒', True, 150, 11, 0.86, 0.5, base, noise=5)
    f.convert('RGB').save(os.path.join(OUT, 'icon_F_木垒竖排.png'))
    print('ok')

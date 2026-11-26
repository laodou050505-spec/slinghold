#!/usr/bin/env python3
"""把 PSTests.RealRoom 截的透明游戏层叠到真实房间照片上（模拟头显里透视画面 + 游戏的样子）。
用法：python3 Tools/realroom_composite.py <层目录> <房间照片> <输出目录>
合成用“预乘”公式：结果 = 游戏层颜色 + 房间 ×（1 − 游戏层透明度）——和头显合成器一样；
光柱 / 烟花这类叠加发光的东西只有这样才看得见（11-26 第一次按普通透明度合成，光柱被抹掉了）。"""
import glob, os, sys
import numpy as np
from PIL import Image

lay_dir, photo, out_dir = sys.argv[1:4]
os.makedirs(out_dir, exist_ok=True)
outs = []
for f in sorted(glob.glob(os.path.join(lay_dir, 'R*.png'))):
    L = np.asarray(Image.open(f).convert('RGBA')).astype(np.float32) / 255
    room = np.asarray(Image.open(photo).convert('RGB').resize((L.shape[1], L.shape[0]), Image.LANCZOS)).astype(np.float32) / 255
    o = np.clip(L[..., :3] + room * (1 - L[..., 3:4]), 0, 1)
    im = Image.fromarray((o * 255).astype(np.uint8))
    name = os.path.splitext(os.path.basename(f))[0]
    im.save(os.path.join(out_dir, f'合成_{name}.jpg'), quality=90)
    outs.append(im)
if outs:
    w, h = 640, 360
    sheet = Image.new('RGB', (w * 2, h * ((len(outs) + 1) // 2)))
    for k, c in enumerate(outs):
        sheet.paste(c.resize((w, h)), ((k % 2) * w, (k // 2) * h))
    sheet.save(os.path.join(out_dir, '合成_一览.jpg'), quality=88)
print(len(outs), '张')

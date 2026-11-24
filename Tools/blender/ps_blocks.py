# 弹弓攻城：积木模块件（木块 11 种 + 石块 7 种）程序建模 + 手绘感贴图烘焙 + 导出 FBX
# 运行：/Applications/Blender.app/Contents/MacOS/Blender -b -P ps_blocks.py -- <输出目录> [贴图边长]
# 产物：<out>/blocks_wood.fbx、blocks_stone.fbx；<out>/tex/{wood,stone}_{albedo,mask,normal}.png；预览图 <out>/preview_*.png
#
# 贴图约定（Unity 着色器 PSBlock 按这个合成）：
#   albedo（sRGB）：裸木 / 裸石本色（含木纹、边缘干扫提亮）
#   mask（线性）  ：R = 漆面覆盖（木块：1 = 有漆，0 = 漆磨掉露木；石块：青苔），G = 缝隙 AO，B = 笔触明暗（0.5 为中性）
#   normal（线性）：切线空间法线（木纹、笔触、凹痕）
# 漆色不烘进贴图，在 Unity 里按块指定，所以同一套贴图能拼出多种漆色的城。
import sys, os, math, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy, bmesh
from mathutils import Vector, Matrix
from ps_lib import *

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = os.path.abspath(argv[0] if argv else os.path.join(os.path.dirname(__file__), '../../ArtSource/Models/blocks'))
RES = int(argv[1]) if len(argv) > 1 else 2048
os.makedirs(os.path.join(OUT, 'tex'), exist_ok=True)

g = G


def arch_profile(w, h_rect, segs=12, z0=0.0):
    """拱形轮廓（XZ 平面，逆时针）：宽 w，直段高 h_rect，上面半圆"""
    r = w / 2
    pts = [(-r, z0 - 0.001), (r, z0 - 0.001), (r, z0 + h_rect)]
    for i in range(1, segs):
        a = math.pi * i / segs
        pts.append((r * math.cos(a), z0 + h_rect + r * math.sin(a)))
    pts.append((-r, z0 + h_rect))
    return pts


# ───────────────────────── 木块 ─────────────────────────
# 通用手感：倒角圆润（0.08～0.1 格）、角上磕掉几块、整体轻微歪斜、表面低频起伏 —— 像用了很多年的手削木块。
WB, WJ, WW = 0.09, 0.011, 0.022     # 倒角、表面起伏、歪斜（单位：格）


def wood_finish(o, rng, chips=4, chip=0.13, bevel=WB):
    boolean(o, chip_cutters(o, rng, chips, chip * g))
    return finish(o, rng, bevel * g, seg=3, jitter=WJ * g, warp=WW * g)


def plank_grooves(sx, sy, sz, ys, along='X'):
    """木板拼缝：在顶面、底面和两端刻浅槽，看起来是几块木板拼起来的"""
    d, wdt = 0.045 * g, 0.03 * g
    cuts = []
    for y in ys:
        cuts.append(cutter_box(sx + 0.2 * g, wdt, d * 2, (0, y, sz)))          # 顶面
        cuts.append(cutter_box(sx + 0.2 * g, wdt, d * 2, (0, y, 0)))           # 底面
        for e in (-1, 1):
            cuts.append(cutter_box(d * 2, wdt, sz + 0.2 * g, (e * sx / 2, y, sz / 2)))   # 两端
    return cuts


def nails(pts, z):
    return [ico('nail', 0.035 * g, (x, y, z + 0.004 * g), scale=(1, 1, 0.55), subdiv=1) for x, y in pts]


def w_cube(rng):
    return wood_finish(box('W_Cube', g, g, g), rng, chips=4)


def w_slab(rng):
    o = box('W_Slab', 2 * g, g, 0.5 * g)
    boolean(o, plank_grooves(2 * g, g, 0.5 * g, [0.0]))
    o = wood_finish(o, rng, chips=4, chip=0.11)
    ns = nails([(x * g, y * g) for x in (-0.82, 0.82) for y in (-0.25, 0.25)], 0.5 * g)
    return join([o] + ns, 'W_Slab')


def w_beam(rng):
    o = box('W_Beam', 3 * g, 0.8 * g, 0.4 * g)
    boolean(o, plank_grooves(3 * g, 0.8 * g, 0.4 * g, [-0.13 * g, 0.14 * g]))
    o = wood_finish(o, rng, chips=5, chip=0.1, bevel=0.075)
    ns = nails([(x * g, y * g) for x in (-1.3, 0, 1.3) for y in (-0.27, 0.0, 0.27)], 0.4 * g)
    return join([o] + ns, 'W_Beam')


def ring_grooves(sx, sy, zs, d=0.05 * g, wdt=0.035 * g):
    """方柱一圈箍线（四面各一条浅槽）"""
    cuts = []
    for z in zs:
        cuts.append(cutter_box(sx + 0.2 * g, d * 2, wdt, (0, -sy / 2, z)))
        cuts.append(cutter_box(sx + 0.2 * g, d * 2, wdt, (0, sy / 2, z)))
        cuts.append(cutter_box(d * 2, sy + 0.2 * g, wdt, (-sx / 2, 0, z)))
        cuts.append(cutter_box(d * 2, sy + 0.2 * g, wdt, (sx / 2, 0, z)))
    return cuts


def w_pillar(rng):
    o = box('W_Pillar', g, g, 2 * g)
    boolean(o, ring_grooves(g, g, [0.22 * g, 1.78 * g]))
    return wood_finish(o, rng, chips=5)


def w_column(rng):
    o = cylinder('W_Column', 0.45 * g, 2 * g, verts=32)
    cut = []
    for z in (0.22 * g, 1.78 * g):
        ring = cylinder('ring', 0.52 * g, 0.04 * g, verts=32, c=(0, 0, z - 0.02 * g))
        core = cylinder('core', 0.415 * g, 0.06 * g, verts=32, c=(0, 0, z - 0.03 * g))
        boolean(ring, [core])
        cut.append(ring)
    boolean(o, cut)
    boolean(o, chip_cutters(o, rng, 4, 0.11 * g, min_planes=1))
    return finish(o, rng, 0.07 * g, seg=3, jitter=WJ * g, warp=WW * g)


def arch_ring(w_open, h_rect, sy, band=0.16 * g, wdt=0.035 * g, d=0.045 * g, faces=(-1, 1)):
    """拱圈槽：门洞 / 拱洞外面一圈和洞口同形的浅槽（前后两面），像一圈拱石的外沿"""
    cuts = []
    for f in faces:
        outer = prism('ro', arch_profile(w_open + 2 * (band + wdt), h_rect, 24), d * 2, c=(0, f * sy / 2, 0))
        inner = prism('ri', arch_profile(w_open + 2 * band, h_rect, 24), d * 3, c=(0, f * sy / 2, 0))
        boolean(outer, [inner])
        cuts.append(outer)
    return cuts


def w_arch(rng):
    o = box('W_Arch', 2 * g, g, g)
    boolean(o, [prism('cut', arch_profile(0.9 * g, 0.2 * g, 20), 1.4 * g)])
    boolean(o, arch_ring(0.9 * g, 0.2 * g, g))
    return wood_finish(o, rng, chips=4, chip=0.11)


def w_window(rng):
    o = box('W_Window', g, g, g)
    boolean(o, [prism('cut', arch_profile(0.24 * g, 0.28 * g, 10, z0=0.32 * g), 0.6 * g, c=(0, 0.5 * g, 0))])
    o = wood_finish(o, rng, chips=3, chip=0.12)
    sill = box('sill', 0.42 * g, 0.12 * g, 0.07 * g, cy=0.52 * g, z0=0.25 * g)
    sill = finish(sill, rng, 0.02 * g, seg=2, jitter=0.004 * g)
    return join([o, sill], 'W_Window')


def w_battlement(rng):
    o = box('W_Battlement', 2 * g, g, 0.6 * g)
    boolean(o, [box('n', 0.36 * g, 1.4 * g, 0.4 * g, cx=x * g, z0=0.3 * g) for x in (-0.5, 0.5)])
    return wood_finish(o, rng, chips=5, chip=0.1, bevel=0.07)


def w_gate(rng):
    """城门：拱形门洞内凹 + 门板竖缝 + 门钉 + 门环。门在 +Y 面（导出时转到 Unity 的 -Z，朝玩家）"""
    o = box('W_Gate', 2 * g, g, 1.5 * g)
    boolean(o, [prism('rec', arch_profile(1.0 * g, 0.75 * g, 20), 0.24 * g, c=(0, 0.5 * g, 0))])
    boolean(o, [box('s', 0.025 * g, 0.08 * g, 1.1 * g, cx=x * g, cy=0.38 * g) for x in (-0.25, 0.0, 0.25)])
    boolean(o, arch_ring(1.0 * g, 0.75 * g, g, band=0.1 * g, faces=(1,)))
    o = wood_finish(o, rng, chips=4, chip=0.11)
    studs = [ico('stud', 0.035 * g, (x * g, 0.38 * g, z * g), subdiv=1) for x in (-0.38, -0.12, 0.12, 0.38) for z in (0.2, 0.55, 0.9)]
    knob = cylinder('knob', 0.05 * g, 0.06 * g, verts=12, axis='Y', c=(0.3 * g, 0.4 * g, 0.62 * g))
    return join([o] + studs + [knob], 'W_Gate')


def w_roof(rng):
    """三角屋顶：山墙芯 + 两块斜屋面板（刻瓦垄）+ 屋脊圆木，屋脊沿 X"""
    L, W, H, T = 2 * g, g, 0.85 * g, 0.09 * g
    core = prism('core', [(-W / 2, 0), (W / 2, 0), (0, H)], 1.9 * g)
    core.rotation_euler = (0, 0, math.pi / 2)
    bpy.context.view_layer.update()
    core.data.transform(core.matrix_world)
    core.matrix_world = Matrix.Identity(4)
    ang = math.atan2(H, W / 2)
    slope = math.hypot(W / 2, H) + 0.14 * g
    parts = [finish(core, rng, 0.04 * g, seg=2, jitter=0.004 * g)]
    for s in (-1, 1):
        b = box('board', L + 0.16 * g, slope, T, z0=-T / 2)
        # 瓦垄：板面上沿斜向的几道浅槽
        boolean(b, [cutter_box(0.03 * g, slope + 0.2 * g, 0.06 * g, (x * g, 0, T / 2)) for x in (-0.75, -0.45, -0.15, 0.15, 0.45, 0.75)])
        bpy.context.view_layer.update()
        mid = Vector((0, s * W / 4, H / 2))
        nrm = Vector((0, s * math.sin(ang), math.cos(ang)))
        b.matrix_world = Matrix.Translation(mid + nrm * (T / 2) + Vector((0, s * 0.02 * g, -0.03 * g))) @ Matrix.Rotation(-s * ang, 4, 'X')
        bpy.context.view_layer.update()
        b.data.transform(b.matrix_world)
        b.matrix_world = Matrix.Identity(4)
        boolean(b, chip_cutters(b, rng, 2, 0.07 * g))
        parts.append(finish(b, rng, 0.03 * g, seg=2, jitter=0.006 * g, warp=0.01 * g))
    ridge = cylinder('ridge', 0.075 * g, L + 0.22 * g, verts=14, axis='X', c=(0, 0, H + 0.05 * g))
    parts.append(finish(ridge, rng, 0.02 * g, seg=2, jitter=0.003 * g))
    return join(parts, 'W_Roof')


def w_cone(rng):
    """尖塔顶：整体是一个略微内凹的圆锥，分五层瓦檐，每层檐口一圈瓦片（水平方向内外交错）+ 尖顶 + 顶珠 + 底座"""
    R, tiers, H = 0.64 * g, 5, 1.55 * g
    z0 = 0.08 * g
    parts = [finish(cylinder('seat', 0.47 * g, 0.12 * g, verts=28), rng, 0.02 * g, seg=2)]

    def rad(t):
        # 圆锥母线略微内凹（顶部收得更快），t = 0 底 → 1 顶
        return R * (1 - t) ** 1.15

    for k in range(tiers):
        t0, t1 = k / tiers, (k + 1) / tiers
        lip = 0.05 * g
        t = cylinder('tier', rad(t0) + lip, H / tiers + 0.06 * g, verts=32, c=(0, 0, z0 + t0 * H - 0.02 * g), r_top=rad(t1) * 0.98)
        zb = z0 + t0 * H - 0.02 * g
        for v in t.data.vertices:
            if v.co.z < zb + 0.005 * g:
                a = math.atan2(v.co.y, v.co.x)
                i = int(round(a / (2 * math.pi / 32)))
                f = 1.0 + (0.045 if i % 2 else -0.02) + rng.uniform(-0.01, 0.01)
                v.co.x *= f
                v.co.y *= f
        t.rotation_euler = (0, 0, rng.uniform(0, 1))
        bpy.context.view_layer.update()
        t.data.transform(t.matrix_world)
        t.matrix_world = Matrix.Identity(4)
        parts.append(finish(t, rng, 0.018 * g, seg=2, jitter=0.004 * g, angle=40))
    top = z0 + H
    parts.append(cylinder('spire', 0.06 * g, 0.3 * g, verts=12, c=(0, 0, top - 0.06 * g), r_top=0.03 * g))
    parts.append(ico('ball', 0.07 * g, (0, 0, top + 0.27 * g), subdiv=2))
    return join(parts, 'W_Cone')


# ───────────────────────── 石块 ─────────────────────────
def stone_finish(o, rng, n_chip=5):
    boolean(o, chip_cutters(o, rng, n_chip, 0.14 * g, stone=True, corner_share=0.7))
    return finish(o, rng, 0.06 * g, seg=2, jitter=0.022 * g, warp=0.018 * g, angle=35)


def brick_cuts(sx, sz, rows, brick, faces=(-1, 1), depth=0.05 * g, gap=0.035 * g, sy=g):
    """在 ±Y 两面刻砖缝：水平缝 + 错缝的竖缝"""
    cuts = []
    rh = sz / rows
    for f in faces:
        y = f * sy / 2
        for r in range(1, rows):
            cuts.append(box('h', sx + 0.2 * g, depth * 2, gap, cy=y, z0=r * rh - gap / 2))
        for r in range(rows):
            off = (brick / 2) if r % 2 else 0
            x = -sx / 2 + brick - off
            while x < sx / 2 - 0.1 * g:
                cuts.append(box('v', gap, depth * 2, rh - gap, cx=x, cy=y, z0=r * rh + gap / 2))
                x += brick
    return cuts


def s_cube(rng):
    return stone_finish(box('S_Cube', g, g, g), rng, 3)


def s_slab(rng):
    return stone_finish(box('S_Slab', 2 * g, g, 0.5 * g), rng, 4)


def s_pillar(rng):
    return stone_finish(box('S_Pillar', g, g, 2 * g), rng, 5)


def s_arch(rng):
    o = box('S_Arch', 2 * g, g, g)
    boolean(o, [prism('cut', arch_profile(0.9 * g, 0.2 * g, 16), 1.4 * g)])
    return stone_finish(o, rng, 4)


def s_battlement(rng):
    o = box('S_Battlement', 2 * g, g, 0.6 * g)
    boolean(o, [box('n', 0.36 * g, 1.4 * g, 0.4 * g, cx=x * g, z0=0.3 * g) for x in (-0.5, 0.5)])
    return stone_finish(o, rng, 3)


def s_wall(rng):
    o = box('S_Wall', 2 * g, g, g)
    boolean(o, brick_cuts(2 * g, g, 3, 0.66 * g))
    return stone_finish(o, rng, 4)


def s_shield(rng):
    """盾墙：厚石板 + 正面浮雕盾徽 + 四角铆钉"""
    o = box('S_Shield', 3 * g, 0.5 * g, 2 * g)
    boolean(o, brick_cuts(3 * g, 2 * g, 4, 0.75 * g, faces=(-1, 1), sy=0.5 * g))
    o = stone_finish(o, rng, 5)
    pts = [(-0.6, 1.75), (0.6, 1.75), (0.6, 1.0)]
    for i in range(1, 10):
        a = i / 10
        pts.append((0.6 * math.cos(a * math.pi / 2), 1.0 - 0.75 * math.sin(a * math.pi / 2)))
    pts += [(0, 0.25)]
    for i in range(1, 10):
        a = 1 - i / 10
        pts.append((-0.6 * math.cos(a * math.pi / 2), 1.0 - 0.75 * math.sin(a * math.pi / 2)))
    pts.append((-0.6, 1.0))
    crest = prism('crest', [(x * g, z * g) for x, z in pts], 0.1 * g, c=(0, 0.27 * g, 0))
    crest = finish(crest, rng, 0.025 * g, seg=2, jitter=0.003 * g)
    rivets = [ico('rv', 0.05 * g, (x * g, 0.26 * g, z * g), subdiv=1) for x in (-1.3, 1.3) for z in (0.25, 1.75)]
    return join([o, crest] + rivets, 'S_Shield')


WOOD = [w_cube, w_slab, w_beam, w_pillar, w_column, w_arch, w_window, w_battlement, w_gate, w_roof, w_cone]
STONE = [s_cube, s_slab, s_pillar, s_arch, s_battlement, s_wall, s_shield]
if len(argv) > 2 and argv[2] == 'quick':      # 冒烟测试：只做几件
    WOOD, STONE = [w_cube, w_gate], [s_cube]
# 木纹方向：竖着的件沿 Z，其余沿 X
GRAIN_Z = {'W_Pillar', 'W_Column', 'W_Window', 'W_Cube', 'W_Cone'}


# ───────────────────────── 烘焙材质 ─────────────────────────
def srgb(h):
    h = h.lstrip('#')
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple((x / 12.92) if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c) + (1,)


class NB:
    """节点小助手"""
    def __init__(self, mat):
        self.t = mat.node_tree
        self.n = self.t.nodes
        self.l = self.t.links

    def node(self, kind, **kw):
        nd = self.n.new(kind)
        for k, v in kw.items():
            if k in nd.inputs:
                nd.inputs[k].default_value = v
            else:
                setattr(nd, k, v)
        return nd

    def link(self, a, b):
        self.l.new(a, b)

    def math(self, op, a, b=None, clamp=False):
        m = self.node('ShaderNodeMath', operation=op, use_clamp=clamp)
        for i, x in enumerate((a, b)):
            if x is None:
                continue
            if isinstance(x, (int, float)):
                m.inputs[i].default_value = x
            else:
                self.link(x, m.inputs[i])
        return m.outputs[0]

    def maprange(self, x, a, b, c=0.0, d=1.0, smooth=True):
        m = self.node('ShaderNodeMapRange', interpolation_type='SMOOTHSTEP' if smooth else 'LINEAR')
        self.link(x, m.inputs['Value'])
        m.inputs['From Min'].default_value = a
        m.inputs['From Max'].default_value = b
        m.inputs['To Min'].default_value = c
        m.inputs['To Max'].default_value = d
        return m.outputs['Result']

    def noise(self, vec, scale, detail=4.0, rough=0.55, sx=1, sy=1, sz=1):
        mp = self.node('ShaderNodeMapping')
        mp.inputs['Scale'].default_value = (sx, sy, sz)
        self.link(vec, mp.inputs['Vector'])
        nz = self.node('ShaderNodeTexNoise')
        nz.inputs['Scale'].default_value = scale
        nz.inputs['Detail'].default_value = detail
        nz.inputs['Roughness'].default_value = rough
        self.link(mp.outputs['Vector'], nz.inputs['Vector'])
        return nz.outputs['Fac']


def bake_material(name, stone, grain_z):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = NB(m)
    b.n.clear()
    out = b.node('ShaderNodeOutputMaterial')
    tc = b.node('ShaderNodeTexCoord')
    obj = tc.outputs['Object']
    inv = 1.0 / g
    # 木纹沿长轴拉长：竖件沿 Z，横件沿 X
    if grain_z:
        ga = dict(sx=1.4 * inv, sy=22 * inv, sz=1.4 * inv)
        st = dict(sx=26 * inv, sy=26 * inv, sz=2.5 * inv)
    else:
        ga = dict(sx=1.4 * inv, sy=22 * inv, sz=22 * inv)
        st = dict(sx=2.5 * inv, sy=26 * inv, sz=26 * inv)
    if grain_z:
        ga = dict(sx=22 * inv, sy=22 * inv, sz=1.4 * inv)
    # 边缘因子：倒角法线和真实法线的差（只在 Cycles 里有效，烘焙用 Cycles）
    bev = b.node('ShaderNodeBevel', samples=8)
    bev.inputs['Radius'].default_value = 0.07 * g
    geo = b.node('ShaderNodeNewGeometry')
    dot = b.node('ShaderNodeVectorMath', operation='DOT_PRODUCT')
    b.link(bev.outputs['Normal'], dot.inputs[0])
    b.link(geo.outputs['True Normal'], dot.inputs[1])
    edge = b.math('MULTIPLY', b.math('SUBTRACT', 1.0, dot.outputs['Value']), 9.0, clamp=True)
    ao = b.node('ShaderNodeAmbientOcclusion', samples=24, only_local=True)   # 只算自身遮挡（逐对烘焙时低模也在场景里，不能让它挡住高模）
    ao.inputs['Distance'].default_value = 0.45 * g
    aoF = ao.outputs['AO']
    grain = b.noise(obj, 1.0, detail=6.0, rough=0.6, **ga)
    stroke = b.noise(obj, 1.0, detail=2.0, rough=0.45, **st)
    # 棱线：小半径倒角法线和真实法线的差 → 只在棱上一窄条
    bevL = b.node('ShaderNodeBevel', samples=8)
    bevL.inputs['Radius'].default_value = 0.035 * g
    dotL = b.node('ShaderNodeVectorMath', operation='DOT_PRODUCT')
    b.link(bevL.outputs['Normal'], dotL.inputs[0])
    b.link(geo.outputs['True Normal'], dotL.inputs[1])
    lineF = b.maprange(b.math('SUBTRACT', 1.0, dotL.outputs['Value']), 0.035, 0.11)
    patch = b.noise(obj, 1.0, detail=3.0, sx=4.5 * inv, sy=4.5 * inv, sz=4.5 * inv)
    big = b.noise(obj, 7.0 * inv / 7.0 * 1.0, detail=4.0, sx=7 * inv, sy=7 * inv, sz=7 * inv)
    scuffN = b.noise(obj, 1.0, detail=8.0, rough=0.62, sx=3 * inv, sy=3 * inv, sz=3 * inv)
    dentN = b.node('ShaderNodeTexVoronoi', feature='SMOOTH_F1')
    dentN.inputs['Scale'].default_value = 9 * inv
    b.link(obj, dentN.inputs['Vector'])
    dents = dentN.outputs['Distance']

    if not stone:
        # 漆面覆盖：边缘磨掉 + 大块随机磨损 + 零星刮痕
        # 掉漆成块：棱上只有“磨损斑”覆盖到的地方才掉，不是整圈都掉
        edgeWear = b.math('MULTIPLY', edge, b.maprange(patch, 0.42, 0.62))
        wear = b.math('ADD', edgeWear, b.math('MULTIPLY', b.math('SUBTRACT', big, 0.5), 0.7))
        paint = b.math('SUBTRACT', 1.0, b.maprange(wear, 0.5, 0.62))
        scuff = b.maprange(scuffN, 0.66, 0.71)
        paint = b.math('MULTIPLY', paint, b.math('SUBTRACT', 1.0, b.math('MULTIPLY', scuff, 0.9)), clamp=True)
        # 漆面笔触（0.5 中性）+ 边缘干扫提亮
        brush = b.maprange(stroke, 0.3, 0.7, 0.4, 0.6, smooth=False)
        # 裸木颜色
        ramp = b.node('ShaderNodeValToRGB')
        ramp.color_ramp.elements[0].position = 0.35
        ramp.color_ramp.elements[0].color = srgb('8a6440')
        ramp.color_ramp.elements[1].position = 0.68
        ramp.color_ramp.elements[1].color = srgb('c9a273')
        b.link(grain, ramp.inputs['Fac'])
        alb = b.node('ShaderNodeMix', data_type='RGBA', blend_type='SCREEN')
        alb.inputs['Factor'].default_value = 0.0
        b.link(b.math('MULTIPLY', edgeWear, 0.5), alb.inputs['Factor'])
        b.link(ramp.outputs['Color'], alb.inputs[6])
        alb.inputs[7].default_value = srgb('e8d2a8')
        albedo = alb.outputs[2]
        # 漆把木纹填平大半；漆层比裸木略高一点（掉漆处有台阶感）
        grainH = b.math('MULTIPLY', grain, b.math('SUBTRACT', 1.0, b.math('MULTIPLY', paint, 0.75)))
        height = b.math('ADD', b.math('MULTIPLY', grainH, 0.7), b.math('ADD', b.math('MULTIPLY', stroke, 0.2), b.math('MULTIPLY', dents, 0.3)))
        height = b.math('ADD', height, b.math('MULTIPLY', paint, 0.35))
    else:
        # 石块：R = 青苔（缝隙里、零星）；本色灰赭带斑
        cav = b.math('SUBTRACT', 1.0, aoF)
        moss = b.math('MULTIPLY', b.maprange(b.math('ADD', cav, b.math('MULTIPLY', b.math('SUBTRACT', big, 0.5), 0.6)), 0.5, 0.7), b.maprange(scuffN, 0.55, 0.68))
        paint = moss
        brush = b.math('ADD', b.maprange(stroke, 0.3, 0.7, 0.38, 0.62, smooth=False), b.math('MULTIPLY', edge, 0.22), clamp=True)
        vor = b.node('ShaderNodeTexVoronoi', feature='F1')
        vor.inputs['Scale'].default_value = 4 * inv
        b.link(obj, vor.inputs['Vector'])
        ramp = b.node('ShaderNodeValToRGB')
        ramp.color_ramp.elements[0].position = 0.0
        ramp.color_ramp.elements[0].color = srgb('7c7d7c')
        ramp.color_ramp.elements[1].position = 1.0
        ramp.color_ramp.elements[1].color = srgb('aeaba2')
        mixf = b.math('ADD', b.math('MULTIPLY', vor.outputs['Distance'], 0.6), b.math('MULTIPLY', big, 0.5))
        b.link(mixf, ramp.inputs['Fac'])
        alb = b.node('ShaderNodeMix', data_type='RGBA', blend_type='SCREEN')
        b.link(b.math('MULTIPLY', edge, 0.45), alb.inputs['Factor'])
        b.link(ramp.outputs['Color'], alb.inputs[6])
        alb.inputs[7].default_value = srgb('d6d3ca')
        albedo = alb.outputs[2]
        pits = b.noise(obj, 1.0, detail=8.0, rough=0.7, sx=14 * inv, sy=14 * inv, sz=14 * inv)
        height = b.math('ADD', b.math('MULTIPLY', pits, 0.35), b.math('ADD', b.math('MULTIPLY', dents, 0.6), b.math('MULTIPLY', stroke, 0.2)))

    # 合成遮罩
    comb = b.node('ShaderNodeCombineColor')
    b.link(paint, comb.inputs[0])
    b.link(aoF, comb.inputs[1])
    b.link(brush, comb.inputs[2])
    emMask = b.node('ShaderNodeEmission')
    b.link(comb.outputs['Color'], emMask.inputs['Color'])
    emWhite = b.node('ShaderNodeEmission')
    emWhite.inputs['Color'].default_value = (1, 1, 1, 1)
    m['_white'] = emWhite.name
    emLine = b.node('ShaderNodeEmission')
    b.link(lineF, emLine.inputs['Color'])
    m['_line'] = emLine.name
    emAlb = b.node('ShaderNodeEmission')
    b.link(albedo, emAlb.inputs['Color'])
    bump = b.node('ShaderNodeBump')
    bump.inputs['Strength'].default_value = 1.0
    bump.inputs['Distance'].default_value = 0.0016 if stone else 0.0025
    b.link(height, bump.inputs['Height'])
    bsdf = b.node('ShaderNodeBsdfPrincipled')
    b.link(bump.outputs['Normal'], bsdf.inputs['Normal'])
    img = b.node('ShaderNodeTexImage')
    img.name = 'BAKE'
    m['_mask'] = emMask.name
    m['_alb'] = emAlb.name
    m['_bsdf'] = bsdf.name
    m['_out'] = out.name
    return m


def route(m, which):
    t = m.node_tree
    out = t.nodes[m['_out']]
    src = {'mask': m['_mask'], 'albedo': m['_alb'], 'normal': m['_bsdf'], 'line': m['_line'], 'cover': m['_white']}[which]
    for l in list(out.inputs['Surface'].links):
        t.links.remove(l)
    t.links.new(t.nodes[src].outputs[0], out.inputs['Surface'])


# ───────────────────────── 主流程 ─────────────────────────
def setup_cycles():
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    try:
        prefs = bpy.context.preferences.addons['cycles'].preferences
        prefs.compute_device_type = 'METAL'
        prefs.get_devices()
        for d in prefs.devices:
            d.use = True
        sc.cycles.device = 'GPU'
    except Exception as e:
        print('GPU 不可用，用 CPU：', e)
    sc.render.bake.margin = 12
    sc.render.bake.use_clear = True


def triangulate(objs):
    """烘焙前统一三角化：保证 Blender 烘法线用的三角形和 Unity 导入后的一致（MikkTSpace 切线才对得上）"""
    for o in objs:
        md = o.modifiers.new('tri', 'TRIANGULATE')
        md.quad_method = 'BEAUTY'
        md.ngon_method = 'BEAUTY'
        md.keep_custom_normals = True
        apply_mods(o)
        o.data.name = o.name


def unwrap_and_pack(objs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(72), island_margin=0.002, scale_to_bounds=False)
    bpy.ops.uv.select_all(action='SELECT')
    bpy.ops.uv.average_islands_scale()
    bpy.ops.uv.pack_islands(margin=0.0025, rotate=True, shape_method='CONCAVE', margin_method='FRACTION')
    bpy.ops.object.mode_set(mode='OBJECT')


def bake_set(name, pairs, stone):
    """pairs = [(低模, 高模)]：低模展 UV 打包成一张图集，逐对把高模烘到低模（selected to active）"""
    import numpy as np
    from ps_wear import dilate
    sc = bpy.context.scene
    lows = [l for l, h in pairs]
    for i, (l, h) in enumerate(pairs):
        loc = ((i % 6) * 5 * g, (i // 6) * 5 * g, 0)
        l.location = loc
        h.location = loc
    unwrap_and_pack(lows)
    mats = {}
    for l, h in pairs:
        gz = (l.name in GRAIN_Z) and not stone
        key = ('S' if stone else 'W') + ('Z' if gz else 'X')
        if key not in mats:
            mats[key] = bake_material(name + '_' + key, stone, gz)
        h.data.materials.clear()
        h.data.materials.append(mats[key])
    tgt = bpy.data.materials.new('target_' + name)
    tgt.use_nodes = True
    tn = tgt.node_tree.nodes.new('ShaderNodeTexImage')
    tgt.node_tree.nodes.active = tn
    for l in lows:
        l.data.materials.clear()
        l.data.materials.append(tgt)
    passes = (('cover', 'Non-Color'), ('albedo', 'sRGB'), ('mask', 'Non-Color'), ('normal', 'Non-Color'), ('line', 'Non-Color'))
    imgs = {}
    for which, cs in passes:
        im = bpy.data.images.new(f'{name}_{which}', RES, RES, alpha=True, float_buffer=False)
        im.generated_color = (0, 0, 0, 0)
        im.colorspace_settings.name = cs
        imgs[which] = im
    sc.render.bake.margin = 0
    sc.render.bake.use_clear = False
    for which, _ in passes:
        for m in mats.values():
            route(m, which)
        tn.image = imgs[which]
        sc.cycles.samples = {'mask': 40, 'line': 12, 'normal': 6}.get(which, 2)
        for l, h in pairs:
            for o in bpy.data.objects:
                o.hide_render = o not in (l, h)
            bpy.ops.object.select_all(action='DESELECT')
            h.select_set(True)
            l.select_set(True)
            bpy.context.view_layer.objects.active = l
            kw = dict(use_selected_to_active=True, cage_extrusion=0.015 * g, max_ray_distance=0.06 * g, margin=0, use_clear=False)
            if which == 'normal':
                bpy.ops.object.bake(type='NORMAL', normal_space='TANGENT', **kw)
            else:
                bpy.ops.object.bake(type='EMIT', **kw)
        print('烘焙完成', name, which)
    # 向 UV 岛外扩边，然后存盘；棱线写进 mask 的 A 通道
    px = {w: np.array(imgs[w].pixels[:], dtype=np.float32).reshape(RES, RES, 4) for w, _ in passes}
    cover = px['cover'][..., 0] > 0.5
    print('图集覆盖率', name, round(float(cover.mean()), 3))
    for w in ('albedo', 'mask', 'normal', 'line'):
        px[w] = dilate(px[w], cover)
    px['mask'][..., 3] = px['line'][..., 0]
    for w in ('albedo', 'normal'):
        px[w][..., 3] = 1.0
    for w in ('albedo', 'mask', 'normal'):
        out = bpy.data.images.new(f'{name}_{w}_out', RES, RES, alpha=True, float_buffer=False)
        out.colorspace_settings.name = 'sRGB' if w == 'albedo' else 'Non-Color'
        out.alpha_mode = 'CHANNEL_PACKED'
        out.pixels[:] = px[w].ravel()
        pth = os.path.join(OUT, 'tex', f'{name}_{w}.png')
        out.filepath_raw = pth
        out.file_format = 'PNG'
        out.save()
        print('存盘', pth)
    for o in bpy.data.objects:
        o.hide_render = False


def export_set(name, objs):
    for o in objs:
        o.location = (0, 0, 0)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    p = os.path.join(OUT, f'blocks_{name}.fbx')
    bpy.ops.export_scene.fbx(filepath=p, use_selection=True, apply_scale_options='FBX_SCALE_UNITS',
                             axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                             mesh_smooth_type='OFF', use_tspace=True, add_leaf_bones=False,
                             path_mode='STRIP', embed_textures=False, use_custom_props=False)
    print('导出', p)


def main():
    from ps_wear import damage, make_low, make_lod, TARGET
    reset()
    setup_cycles()
    rng = random.Random(1124)
    report = []
    sets = {}
    for name, fns, stone in (('wood', WOOD, False), ('stone', STONE, True)):
        pairs = []
        for fn in fns:
            base = fn(rng)
            nm = base.name
            base.name = nm + '_HI'
            hi = damage(base, rng, stone, 2 if nm in GRAIN_Z else 0)
            lo = make_low(hi, nm, TARGET[nm])
            tris, nonman = mesh_report(lo)
            htris = len(hi.data.polygons)
            report.append(f'{nm:14s} 低模三角面 {tris:5d}  非流形边 {nonman}   高模面 {htris}')
            print(report[-1])
            pairs.append((lo, hi))
        sets[name] = pairs
    for name, pairs in sets.items():
        bake_set(name, pairs, name == 'stone')
    out = {}
    for name, pairs in sets.items():
        lows = [l for l, h in pairs]
        lods = [make_lod(l) for l in lows]
        for l, lod in zip(lows, lods):
            t1, n1 = mesh_report(lod)
            report.append(f'{lod.name:18s} 三角面 {t1:5d}  非流形边 {n1}')
        for l, h in pairs:
            bpy.data.objects.remove(h, do_unlink=True)
        out[name] = lows + lods
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'blocks.blend'))
    for name, objs in out.items():
        export_set(name, objs)
    open(os.path.join(OUT, 'report.txt'), 'w').write('\n'.join(report) + '\n')


main()

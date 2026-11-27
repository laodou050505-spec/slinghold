# 木垒：道具（帅旗灯笼、投石机、弹弓、5 种弹、补给盘、弹盒、钉子、说明木牌）程序建模 + 几何磨损 + 高模烘低模
# 运行：/Applications/Blender.app/Contents/MacOS/Blender -b -P ps_props.py -- <输出目录> [贴图边长] [quick]
# 产物：<out>/props.fbx（低模 + _LOD1）、<out>/tex/props_{albedo,mask,normal}.png、<out>/props.blend、<out>/report.txt、
#       Docs/进度截图/1124_道具/tiles/*.png（检查图分块，外部再拼）
#
# 贴图约定（和积木不同，道具是多色的）：
#   albedo（sRGB）：最终底色（漆色 + 磨损露木 + 图案都烘进去）；albedo.A = 自发光遮罩
#   mask（线性）：R = 0（Unity 材质 _PaintCover = 0）、G = AO（只算自身）、B = 笔触（0.5 中性）、A = 棱线
#   normal：切线空间
# 颜色怎么烘进去：每个部件建模时标上颜色 / 材质属性；体素重建成高模后，按“离哪个部件最近”把属性写到高模顶点上
# （PCol = 颜色 + 发光，PMat = 可磨损 / 木纹 / 金属 / 裸木），再按位置画图案（旗上的太阳纹、火丸火焰）。
import sys, os, math, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy, bmesh
import numpy as np
from mathutils import Vector, Matrix, noise
from mathutils.bvhtree import BVHTree
from ps_lib import G, reset, link, obj_from_bm, box, cylinder, ico, prism, apply_mods, boolean, finish, mesh_report, chip_cutters, cutter_box
from ps_wear import arrays, write, tangent, press, groove, make_low, make_lod, dilate

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
HERE = os.path.dirname(os.path.abspath(__file__))
MECH = len(argv) > 2 and argv[2] == 'mech'
TABLE = len(argv) > 2 and argv[2] == 'table'
OUT = os.path.abspath(argv[0] if argv else (
    os.path.join(HERE, '../../ArtSource/Models/mech') if MECH else
    os.path.join(HERE, '../../ArtSource/Models/table') if TABLE else
    os.path.join(HERE, '../../ArtSource/Models/props')
))
RES = int(argv[1]) if len(argv) > 1 else 2048
QUICK = len(argv) > 2 and argv[2] == 'quick'
COVER = len(argv) > 2 and argv[2] == 'cover'
PREFIX = 'cover' if COVER else ('mech' if MECH else ('table' if TABLE else 'props'))
SHOTS = os.path.abspath(os.path.join(HERE, '../../Docs/进度截图/' + (
    '1125_封面道具' if COVER else
    ('1127_机关模型' if MECH else
     ('1127_虚拟桌' if TABLE else '1124_道具'))
)))
os.makedirs(os.path.join(OUT, 'tex'), exist_ok=True)
g = G


# ───────────────────────── 部件属性 ─────────────────────────
def lin(h):
    h = h.lstrip('#')
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return [(x / 12.92) if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c]


def T(ob, col, wear=1.0, grain=0.35, metal=0.0, bare=0.0, emit=0.0):
    """给部件标属性：col 漆色（裸木时是染色）、wear 允许掉漆露木、grain 木纹可见度、metal 金属边缘提亮、bare 裸木、emit 发光"""
    ob['pc'] = col
    ob['pm'] = (wear, grain, metal, bare)
    ob['pe'] = emit
    return ob


PAINT = dict(teal='6d958e', ochre='c09a55', brick='a05a46', cream='dcd1b7', blue='6b8198', sage='8d9773')
WOOD = dict(col='d2b48c', wear=0.0, grain=1.0, bare=1.0)            # 裸木（原色）
STAIN = dict(col='a07a55', wear=0.0, grain=1.0, bare=1.0)           # 深一点的旧木（染过）
IRON = dict(col='3b3a3c', wear=0.0, grain=0.0, metal=1.0)
ROPE = dict(col='b59a6a', wear=0.0, grain=0.25)
LEATHER = dict(col='7a4b2e', wear=0.0, grain=0.15)


def P(name, **kw):
    """部件属性快捷：P('teal') 漆木；P(WOOD) 裸木 ..."""
    return kw


def tag(ob, spec):
    if isinstance(spec, str):
        return T(ob, PAINT.get(spec, spec))
    s = dict(spec)
    return T(ob, s.pop('col'), **s)


def tube(name, pts, res=24, bevel_res=6):
    """沿折线点 [(x, y, z, 半径)] 生成圆管（树枝、绳子）。单位米。"""
    cu = bpy.data.curves.new(name, 'CURVE')
    cu.dimensions = '3D'
    cu.bevel_depth = 1.0
    cu.bevel_resolution = bevel_res
    cu.resolution_u = res
    cu.use_fill_caps = True
    sp = cu.splines.new('BEZIER')
    sp.bezier_points.add(len(pts) - 1)
    for bp, (x, y, z, r) in zip(sp.bezier_points, pts):
        bp.co = (x, y, z)
        bp.radius = r
        bp.handle_left_type = bp.handle_right_type = 'AUTO'
    ob = bpy.data.objects.new(name + '_c', cu)
    link(ob)
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    bpy.data.objects.remove(ob, do_unlink=True)
    bpy.data.curves.remove(cu)
    o = bpy.data.objects.new(name, me)
    link(o)
    return o


def torus(name, R, r, c, axis=(0, 0, 1), maj=24, mn=8):
    bm = bmesh.new()
    for i in range(maj):
        a = 2 * math.pi * i / maj
        for j in range(mn):
            b = 2 * math.pi * j / mn
            bm.verts.new(((R + r * math.cos(b)) * math.cos(a), (R + r * math.cos(b)) * math.sin(a), r * math.sin(b)))
    bm.verts.ensure_lookup_table()
    for i in range(maj):
        for j in range(mn):
            a = i * mn + j
            b = ((i + 1) % maj) * mn + j
            c2 = ((i + 1) % maj) * mn + (j + 1) % mn
            d = i * mn + (j + 1) % mn
            bm.faces.new((bm.verts[a], bm.verts[b], bm.verts[c2], bm.verts[d]))
    z = Vector(axis).normalized()
    rot = Vector((0, 0, 1)).rotation_difference(z).to_matrix()
    bmesh.ops.rotate(bm, verts=bm.verts, cent=Vector(), matrix=rot)
    bmesh.ops.translate(bm, verts=bm.verts, vec=Vector(c))
    return obj_from_bm(name, bm)


def beam(name, p0, p1, w, h=None):
    """从 p0 到 p1 的方木（截面 w × h）"""
    h = h or w
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    o = box(name, w, h, d.length, z0=-d.length / 2)
    rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix().to_4x4()
    o.data.transform(Matrix.Translation((p0 + p1) / 2) @ rot)
    return o


def ring_on(name, p, axis, R, r=0.018 * g):
    return torus(name, R, r, p, axis)


# ───────────────────────── 1. 帅旗灯笼 ─────────────────────────
def k_pole(rng):
    parts = []
    b = box('base', 0.8 * g, 0.8 * g, 0.35 * g)
    boolean(b, chip_cutters(b, rng, 3, 0.1 * g))
    parts.append(tag(finish(b, rng, 0.07 * g, jitter=0.01 * g, warp=0.015 * g), 'brick'))
    pole = cylinder('pole', 0.05 * g, 2.45 * g, verts=16, c=(0, 0, 0.33 * g))
    parts.append(tag(finish(pole, rng, 0.012 * g, seg=2, jitter=0.003 * g), WOOD))
    parts.append(tag(ico('finial', 0.075 * g, (0, 0, 2.82 * g), subdiv=2), 'ochre'))
    arm = box('arm', 0.5 * g, 0.07 * g, 0.07 * g, cx=-0.27 * g, z0=1.97 * g)
    parts.append(tag(finish(arm, rng, 0.012 * g, seg=2, jitter=0.003 * g), STAIN))
    brace = beam('brace', (-0.02 * g, 0, 1.75 * g), (-0.3 * g, 0, 2.0 * g), 0.045 * g)
    parts.append(tag(finish(brace, rng, 0.01 * g, seg=2), STAIN))
    parts.append(tag(torus('hook', 0.035 * g, 0.012 * g, (-0.46 * g, 0, 1.93 * g), axis=(0, 1, 0), maj=16, mn=6), IRON))
    for z in (2.18, 2.67):            # 旗子绑在杆上的两道绳
        parts.append(tag(torus('lash', 0.062 * g, 0.016 * g, (0, 0, z * g), maj=16, mn=6), ROPE))
    parts.append(tag(torus('band', 0.065 * g, 0.014 * g, (0, 0, 0.42 * g), maj=16, mn=6), IRON))
    return parts


def k_flag(rng):
    """燕尾旗：原点在挂旗边中点，旗面在 XZ 平面、朝 +Y；网格布面 + 加厚（保证封闭），布面起伏，远端毛边；挂旗边一道布套"""
    W, H, NOTCH = 0.9, 0.55, 0.16
    nx, nz = 30, 18
    bm = bmesh.new()
    vs = [[bm.verts.new((W * i / nx * g, 0, (-H / 2 + H * j / nz) * g)) for j in range(nz + 1)] for i in range(nx + 1)]
    for i in range(nx):
        for j in range(nz):
            cx = (i + 0.5) / nx * W
            cz = -H / 2 + (j + 0.5) / nz * H
            if cx > W - NOTCH * (1 - abs(cz) / (H / 2)):      # 燕尾缺口
                continue
            bm.faces.new((vs[i][j], vs[i + 1][j], vs[i + 1][j + 1], vs[i][j + 1]))
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context='VERTS')
    bnd = {v for e in bm.edges if e.is_boundary for v in e.verts}
    for v in bm.verts:
        x, z = v.co.x / g, v.co.z / g
        v.co.y += (math.sin(x * 7.0 + z * 2.0) * 0.035 + math.sin(x * 3.1) * 0.02) * g * min(1.0, x / 0.25)
        if v in bnd and x > 0.05:                # 毛边：边缘点在布面内随机缩进一点
            v.co.x -= rng.uniform(0, 0.025) * g
            v.co.z -= math.copysign(rng.uniform(0, 0.018), z) * g
    o = obj_from_bm('cloth', bm)
    md = o.modifiers.new('sol', 'SOLIDIFY')
    md.thickness = 0.045 * g
    md.offset = 0.0
    md.use_even_offset = True
    apply_mods(o)
    parts = [tag(o, dict(col='e8dcc0', wear=0.0, grain=0.0))]
    sleeve = cylinder('sleeve', 0.068 * g, 0.6 * g, verts=14, c=(0, 0, -0.3 * g))   # 布套包在旗杆（半径 0.05）外面
    parts.append(tag(sleeve, dict(col='d9cba9', wear=0.0, grain=0.0)))
    return parts


def k_lantern(rng):
    """灯笼：原点在挂钩；木框 + 四面纸（发光）"""
    parts = [tag(torus('ring', 0.035 * g, 0.012 * g, (0, 0, -0.035 * g), axis=(1, 0, 0), maj=16, mn=6), IRON)]
    top = box('top', 0.38 * g, 0.38 * g, 0.06 * g, z0=-0.13 * g)
    parts.append(tag(finish(top, rng, 0.015 * g, seg=2, jitter=0.003 * g), 'brick'))
    parts.append(tag(cylinder('knob', 0.03 * g, 0.07 * g, verts=10, c=(0, 0, -0.08 * g)), 'brick'))
    bot = box('bot', 0.36 * g, 0.36 * g, 0.06 * g, z0=-0.6 * g)
    parts.append(tag(finish(bot, rng, 0.015 * g, seg=2, jitter=0.003 * g), 'brick'))
    for sx in (-1, 1):
        for sy in (-1, 1):
            p = box('post', 0.045 * g, 0.045 * g, 0.42 * g, cx=sx * 0.155 * g, cy=sy * 0.155 * g, z0=-0.555 * g)
            parts.append(tag(finish(p, rng, 0.01 * g, seg=2), STAIN))
    paper = box('paper', 0.29 * g, 0.29 * g, 0.4 * g, z0=-0.545 * g)
    parts.append(tag(paper, dict(col='f1d9a0', wear=0.0, grain=0.0, emit=1.0)))
    for z in (-0.35,):
        for sx, sy, w, d in ((0, -1, 0.3, 0.02), (0, 1, 0.3, 0.02), (-1, 0, 0.02, 0.3), (1, 0, 0.02, 0.3)):
            parts.append(tag(box('bar', w * g, d * g, 0.025 * g, cx=sx * 0.15 * g, cy=sy * 0.15 * g, z0=z * g), STAIN))
    parts.append(tag(cylinder('tassel', 0.025 * g, 0.14 * g, verts=10, c=(0, 0, -0.74 * g), r_top=0.012 * g), 'brick'))
    return parts


# ───────────────────────── 2. 投石机 ─────────────────────────
PIVOT = (0.0, 0.0, 1.55)          # 转轴（格）
WHEELS = [(sx * 0.84, sy * 1.0, 0.35) for sx in (-1, 1) for sy in (-1, 1)]


def c_frame(rng):
    parts = []
    def wood(o, spec=STAIN, chips=2):
        boolean(o, chip_cutters(o, rng, chips, 0.08 * g))
        return tag(finish(o, rng, 0.04 * g, seg=2, jitter=0.008 * g, warp=0.01 * g), spec)
    for sx in (-1, 1):
        parts.append(wood(box('rail', 0.2 * g, 3.0 * g, 0.2 * g, cx=sx * 0.62 * g, z0=0.42 * g), STAIN, 3))
    for y in (-1.3, 0.0, 1.3):
        parts.append(wood(box('cross', 1.46 * g, 0.18 * g, 0.18 * g, cy=y * g, z0=0.46 * g), WOOD))
    for y in (-1.0, 1.0):
        parts.append(tag(cylinder('axle', 0.06 * g, 1.82 * g, verts=12, axis='X', c=(0, y * g, 0.35 * g)), IRON))
        for sx in (-1, 1):
            parts.append(tag(box('axlebox', 0.22 * g, 0.24 * g, 0.16 * g, cx=sx * 0.62 * g, cy=y * g, z0=0.3 * g), STAIN))
    px, py, pz = PIVOT
    for sx in (-1, 1):
        x = sx * 0.6
        parts.append(wood(beam('post', (x * g, -0.75 * g, 0.6 * g), (x * g, 0, (pz + 0.12) * g), 0.17 * g), STAIN))
        parts.append(wood(beam('post', (x * g, 0.75 * g, 0.6 * g), (x * g, 0, (pz + 0.12) * g), 0.17 * g), STAIN))
        mount = box('mount', 0.26 * g, 0.3 * g, 0.26 * g, cx=x * g, z0=(pz - 0.13) * g)
        parts.append(wood(mount, 'brick'))
        parts.append(tag(ring_on('hoop', (x * g, 0, (pz - 0.02) * g), (1, 0, 0), 0.16 * g, 0.016 * g), IRON))
        # 前方止挡立柱
        parts.append(wood(box('stopPost', 0.15 * g, 0.15 * g, 1.45 * g, cx=x * g, cy=1.05 * g, z0=0.6 * g), STAIN))
    parts.append(tag(cylinder('pivotAxle', 0.075 * g, 1.45 * g, verts=16, axis='X', c=(0, 0, pz * g)), IRON))
    stop = box('stop', 1.36 * g, 0.18 * g, 0.18 * g, cy=1.05 * g, z0=1.95 * g)
    parts.append(wood(stop, 'teal'))
    for x in np.linspace(-0.35, 0.35, 6):      # 止挡上的缓冲绳
        parts.append(tag(torus('pad', 0.12 * g, 0.03 * g, (x * g, 1.05 * g, 2.04 * g), axis=(1, 0, 0), maj=14, mn=6), ROPE))
    for sx in (-1, 1):                          # 斜撑
        parts.append(wood(beam('brace', (sx * 0.62 * g, -1.3 * g, 0.62 * g), (sx * 0.6 * g, -0.45 * g, 1.2 * g), 0.11 * g), WOOD))
    for sx in (-1, 1):                          # 钉子
        for y in (-1.3, 0.0, 1.3):
            parts.append(tag(ico('nail', 0.035 * g, (sx * 0.62 * g, y * g, 0.64 * g), scale=(1, 1, 0.5), subdiv=1), IRON))
    return parts


def c_arm(rng):
    """投臂：原点在转轴。静止时向后（-Y）下倾 30°，末端勺斗开口朝上；前端短柄绑绳"""
    a = math.radians(30)
    d = Vector((0, -math.cos(a), -math.sin(a)))
    L = 1.45 * g
    parts = []
    arm = beam('arm', d * (-0.28 * g), d * L, 0.14 * g, 0.12 * g)
    boolean(arm, chip_cutters(arm, rng, 3, 0.07 * g))
    parts.append(tag(finish(arm, rng, 0.035 * g, seg=2, jitter=0.007 * g), 'ochre'))
    hub = cylinder('hub', 0.14 * g, 0.36 * g, verts=20, axis='X', c=(0, 0, 0))
    parts.append(tag(finish(hub, rng, 0.02 * g, seg=2), STAIN))
    for x in (-0.13, -0.07, 0.07, 0.13):       # 扭力绳束
        parts.append(tag(torus('rope', 0.16 * g, 0.03 * g, (x * g, 0, 0), axis=(1, 0, 0), maj=18, mn=6), ROPE))
    for t in (0.35, 0.75):                      # 铁箍
        c = d * (L * t)
        parts.append(tag(torus('band', 0.1 * g, 0.016 * g, tuple(c), axis=tuple(d), maj=16, mn=6), IRON))
    cc = d * (L + 0.12 * g) + Vector((0, 0, 0.1 * g))
    cup = ico('cup', 0.3 * g, tuple(cc), subdiv=3)
    hollow = ico('in', 0.24 * g, tuple(cc + Vector((0, 0, 0.07 * g))), subdiv=3)
    top = box('cutTop', 1.0 * g, 1.0 * g, 1.0 * g, cx=cc.x, cy=cc.y, z0=cc.z + 0.06 * g)
    boolean(cup, [hollow, top])
    parts.append(tag(finish(cup, rng, 0.02 * g, seg=2, jitter=0.005 * g), STAIN))
    parts.append(tag(torus('cupRim', 0.29 * g, 0.022 * g, tuple(cc + Vector((0, 0, 0.055 * g))), maj=24, mn=6), IRON))
    return parts, cc + Vector((0, 0, 0.12 * g))


def c_wheel(rng):
    """轮子：原点在轮心，轴向 X；木轮辋 + 6 根辐条 + 轮毂 + 铁轮箍"""
    parts = []
    rim = cylinder('rim', 0.33 * g, 0.15 * g, verts=32, axis='X')
    inner = cylinder('rin', 0.25 * g, 0.3 * g, verts=32, axis='X')
    boolean(rim, [inner])
    boolean(rim, chip_cutters(rim, rng, 3, 0.06 * g, min_planes=1))
    parts.append(tag(finish(rim, rng, 0.025 * g, seg=2, jitter=0.006 * g), STAIN))
    tire = cylinder('tire', 0.355 * g, 0.12 * g, verts=32, axis='X')
    tin = cylinder('tin', 0.325 * g, 0.3 * g, verts=32, axis='X')
    boolean(tire, [tin])
    parts.append(tag(tire, IRON))
    for i in range(6):
        a = math.pi * i / 3
        p1 = Vector((0, math.cos(a), math.sin(a))) * 0.27 * g
        parts.append(tag(finish(beam('spoke', (0, 0, 0), tuple(p1), 0.06 * g), rng, 0.01 * g, seg=2), WOOD))
    parts.append(tag(finish(cylinder('hub', 0.09 * g, 0.2 * g, verts=16, axis='X'), rng, 0.015 * g, seg=2), 'brick'))
    for i in range(8):                           # 轮箍钉
        a = 2 * math.pi * i / 8
        parts.append(tag(ico('rv', 0.022 * g, (0.0, math.cos(a) * 0.355 * g, math.sin(a) * 0.355 * g), subdiv=1), IRON))
    return parts


# ───────────────────────── 3. 弹弓 ─────────────────────────
FORK_TIPS = [(-0.9, 0.0, 2.2), (0.88, 0.02, 2.18)]


def s_fork(rng):
    """Y 形树杈弹弓：原点在握把中部（手捏的地方），叉尖朝 +Z，叉在 XZ 平面"""
    parts = []
    grip = tube('grip', [(0, 0, -1.0 * g, 0.16 * g), (0.02 * g, 0, -0.3 * g, 0.17 * g), (0, 0, 0.3 * g, 0.16 * g), (0, 0, 0.62 * g, 0.17 * g)])
    parts.append(tag(grip, dict(col='9c7350', wear=0.0, grain=1.0, bare=1.0)))
    for (tx, ty, tz), bend in zip(FORK_TIPS, (1, -1)):
        arm = tube('fork', [(0, 0, 0.5 * g, 0.16 * g), (tx * 0.45 * g, 0, 1.15 * g, 0.135 * g),
                            ((tx * 0.85 + bend * 0.05) * g, ty * g, 1.75 * g, 0.12 * g), (tx * g, ty * g, tz * g, 0.105 * g)])
        parts.append(tag(arm, dict(col='9c7350', wear=0.0, grain=1.0, bare=1.0)))
        parts.append(tag(ico('tipcap', 0.105 * g, (tx * g, ty * g, tz * g), subdiv=2), dict(col='b48a62', wear=0.0, grain=1.0, bare=1.0)))
        # 叉尖绕绳（皮筋绑在这里）
        dirv = Vector((tx - (tx * 0.85 + bend * 0.05), 0, tz - 1.75)).normalized()
        for k in range(4):
            c = Vector((tx, ty, tz)) * g - dirv * (0.16 + k * 0.045) * g
            parts.append(tag(torus('wrap', 0.118 * g, 0.022 * g, tuple(c), axis=tuple(dirv), maj=16, mn=6), ROPE))
    # 树节疤
    for p in ((0.12, 0.08, -0.55), (-0.1, -0.1, 0.1)):
        parts.append(tag(ico('knot', 0.06 * g, (p[0] * g, p[1] * g, p[2] * g), scale=(1, 1, 0.7), subdiv=1), dict(col='7a5638', wear=0.0, grain=1.0, bare=1.0)))
    # 握把缠皮条（螺旋）
    hel = []
    for i in range(60):
        t = i / 59
        a = t * 2 * math.pi * 5.0
        z = (-0.85 + 1.15 * t) * g
        hel.append((math.cos(a) * 0.175 * g, math.sin(a) * 0.175 * g, z, 0.028 * g))
    parts.append(tag(tube('wrapL', hel, res=4, bevel_res=2), LEATHER))
    return parts


def s_pouch(rng):
    """皮兜：一小块弯起来的厚皮，两端各一个穿皮筋的孔；原点在中心"""
    o = box('pouch', 0.75 * g, 0.06 * g, 0.34 * g, z0=-0.17 * g)
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=8, use_grid_fill=True)
    for v in bm.verts:
        x = v.co.x / (0.375 * g)
        v.co.y += (x * x) * 0.16 * g          # 向 +Y 弯（兜住弹丸的一面朝 -Y）
        v.co.z *= 1.0 - 0.25 * x * x          # 两端收窄
    bm.to_mesh(o.data)
    bm.free()
    holes = [cylinder('h', 0.04 * g, 0.5 * g, verts=12, axis='Y', c=(s * 0.3 * g, 0.15 * g, 0)) for s in (-1, 1)]
    boolean(o, holes)
    return [tag(o, LEATHER)]


# ───────────────────────── 4. 弹 ─────────────────────────
BR = 0.25


def b_stone(rng):
    return [tag(ico('ball', BR * g, (0, 0, 0), subdiv=4), dict(col='c8a274', wear=0.0, grain=1.0, bare=1.0))]


def b_fire(rng):
    return [tag(ico('ball', BR * g, (0, 0, 0), subdiv=4), dict(col='a9392a', wear=0.0, grain=0.15))]


def b_ice(rng):
    o = ico('ice', 0.25 * g, (0, 0, 0), subdiv=2)
    for v in o.data.vertices:                  # 切面略不规则的冰球
        v.co *= rng.uniform(0.95, 1.05)
    for p in o.data.polygons:
        p.use_smooth = False
    spikes = []
    for _ in range(3):                          # 三颗小冰棱，沿球面法线方向
        d = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))).normalized()
        sp = ico('sp', 0.045 * g, (0, 0, 0), scale=(1, 1, 1.7), subdiv=0)
        sp.data.transform(Matrix.Translation(d * 0.24 * g) @ Vector((0, 0, 1)).rotation_difference(d).to_matrix().to_4x4())
        spikes.append(sp)
    parts = [tag(o, dict(col='cfe6f0', wear=0.0, grain=0.0))]
    parts += [tag(s, dict(col='e8f4f8', wear=0.0, grain=0.0)) for s in spikes]
    return parts


def b_bomb(rng):
    parts = [tag(ico('ball', 0.235 * g, (0, 0, 0), subdiv=4), IRON)]
    parts.append(tag(cylinder('cap', 0.075 * g, 0.08 * g, verts=14, c=(0, 0, 0.2 * g)), dict(col='6b6460', wear=0.0, grain=0.0, metal=1.0)))
    parts.append(tag(tube('fuse', [(0, 0, 0.26 * g, 0.022 * g), (0.03 * g, 0, 0.34 * g, 0.02 * g), (0.08 * g, 0.02 * g, 0.4 * g, 0.018 * g)], res=8, bevel_res=2), dict(col='c2a878', wear=0.0, grain=0.0)))
    parts.append(tag(ico('spark', 0.04 * g, (0.085 * g, 0.02 * g, 0.42 * g), subdiv=2), dict(col='ffb347', wear=0.0, grain=0.0, emit=1.0)))
    for i in range(10):                          # 腰箍铆钉
        a = 2 * math.pi * i / 10
        parts.append(tag(ico('rv', 0.025 * g, (math.cos(a) * 0.235 * g, math.sin(a) * 0.235 * g, 0), subdiv=1), dict(col='55524f', wear=0.0, grain=0.0, metal=1.0)))
    return parts


def b_split(rng):
    o = ico('ball', BR * g, (0, 0, 0), subdiv=4)
    cuts = []
    for k in range(3):
        sl = box('slab', 0.025 * g, 0.7 * g, 0.7 * g, z0=-0.35 * g)
        sl.data.transform(Matrix.Rotation(2 * math.pi * k / 3, 4, 'Z') @ Matrix.Translation((0, 0.35 * g, 0)))
        core = ico('core', 0.2 * g, (0, 0, 0), subdiv=3)
        boolean(sl, [core])
        cuts.append(sl)
    boolean(o, cuts)
    parts = [tag(o, dict(col='c8a274', wear=0.0, grain=1.0, bare=1.0))]
    for z in (-1, 1):
        parts.append(tag(cylinder('pin', 0.05 * g, 0.05 * g, verts=12, c=(0, 0, z * 0.235 * g - (0.025 * g if z > 0 else 0.025 * g))), IRON))
    return parts


# ───────────────────────── 5. 补给盘 / 弹盒 / 钉子 / 木牌 ─────────────────────────
def t_tray(rng):
    parts = []
    base = box('base', 3.0 * g, 2.0 * g, 0.1 * g)
    boolean(base, [cutter_box(3.2 * g, 0.03 * g, 0.06 * g, (0, y * g, 0.1 * g)) for y in (-0.5, 0.0, 0.5)])
    parts.append(tag(finish(base, rng, 0.02 * g, seg=2, jitter=0.005 * g), WOOD))
    for sy in (-1, 1):
        w = box('wall', 3.0 * g, 0.12 * g, 0.35 * g, cy=sy * 0.94 * g)
        boolean(w, chip_cutters(w, rng, 2, 0.06 * g))
        parts.append(tag(finish(w, rng, 0.03 * g, seg=2, jitter=0.006 * g), 'teal'))
    for sx in (-1, 1):
        w = box('wall', 0.12 * g, 1.76 * g, 0.35 * g, cx=sx * 1.44 * g)
        parts.append(tag(finish(w, rng, 0.03 * g, seg=2, jitter=0.006 * g), 'teal'))
    for sx in (-1, 1):
        for sy in (-1, 1):
            parts.append(tag(ico('nail', 0.03 * g, (sx * 1.44 * g, sy * 0.94 * g, 0.35 * g), scale=(1, 1, 0.5), subdiv=1), IRON))
    return parts


BOX_X = [(-2 + i) * 0.62 for i in range(5)]       # 五格中心（格）
PEG_DX = [(-2 + k) * 0.1 for k in range(5)]


def a_box(rng):
    o = box('box', 3.2 * g, 0.9 * g, 0.7 * g)
    cells = [box('cell', 0.54 * g, 0.7 * g, 0.8 * g, cx=x * g, z0=0.12 * g) for x in BOX_X]
    boolean(o, cells)
    holes = [cylinder('h', 0.026 * g, 0.16 * g, verts=10, axis='Y', c=((x + dx) * g, 0.45 * g, 0.26 * g)) for x in BOX_X for dx in PEG_DX]
    boolean(o, holes)
    boolean(o, chip_cutters(o, rng, 4, 0.07 * g))
    parts = [tag(finish(o, rng, 0.035 * g, seg=2, jitter=0.007 * g, warp=0.01 * g), 'ochre')]
    band = box('band', 3.22 * g, 0.92 * g, 0.09 * g, z0=0.58 * g)
    inner = box('bi', 3.0 * g, 0.72 * g, 0.3 * g, z0=0.5 * g)
    boolean(band, [inner] + [box('cell', 0.54 * g, 0.7 * g, 0.8 * g, cx=x * g, z0=0.12 * g) for x in BOX_X])
    parts.append(tag(band, 'brick'))
    return parts


def a_peg(rng):
    return [tag(cylinder('shaft', 0.02 * g, 0.11 * g, verts=12), dict(col='d2b48c', wear=0.0, grain=0.8, bare=1.0)),
            tag(ico('head', 0.036 * g, (0, 0, 0.11 * g), scale=(1, 1, 0.75), subdiv=2), 'brick')]


def h_tag(rng):
    """手刻小木牌：原点在穿绳孔中心，正面朝 +Y（空白，Unity 贴图示）"""
    pts = [(-0.6, -0.68), (0.6, -0.68), (0.6, 0.0), (0.42, 0.12), (-0.42, 0.12), (-0.6, 0.0)]
    o = prism('tag', [(x * g, z * g) for x, z in pts], 0.08 * g)
    boolean(o, [cylinder('hole', 0.055 * g, 0.3 * g, verts=14, axis='Y', c=(0, 0, 0))])
    boolean(o, chip_cutters(o, rng, 3, 0.05 * g))
    return [tag(finish(o, rng, 0.02 * g, seg=2, jitter=0.006 * g, warp=0.01 * g), 'cream')]


# ───────────────────────── 封面（帅旗灯笼小场景）：旗杆、木签横臂、大灯笼、木签 ─────────────────────────
def cv_pole(rng):
    """封面旗杆：原点在杆底中心，杆沿 +Z；横杆沿 X（布旗挂在 -X 一侧）；灯笼横臂沿 +X；杆中段一道铁箍（木签横臂从这里伸出）"""
    parts = []
    pole = cylinder('pole', 0.3 * g, 18.0 * g, verts=20, c=(0, 0, 0), r_top=0.24 * g)
    parts.append(tag(finish(pole, rng, 0.05 * g, seg=2, jitter=0.012 * g, warp=0.03 * g), STAIN))
    parts.append(tag(ico('finial', 0.42 * g, (0, 0, 18.35 * g), subdiv=2), 'ochre'))
    parts.append(tag(cylinder('neck', 0.2 * g, 0.4 * g, verts=14, c=(0, 0, 18.0 * g)), 'ochre'))
    bar = cylinder('bar', 0.17 * g, 7.0 * g, verts=14, axis='X', c=(-2.85 * g, 0, 16.9 * g))
    parts.append(tag(finish(bar, rng, 0.03 * g, seg=2, jitter=0.008 * g, warp=0.02 * g), STAIN))
    for x in (-6.2, 0.55):
        parts.append(tag(ico('cap', 0.22 * g, (x * g, 0, 16.9 * g), subdiv=2), 'brick'))
    for z in (16.75, 17.05):                      # 横杆和旗杆交接处的十字绑绳
        parts.append(tag(torus('lash', 0.34 * g, 0.07 * g, (0, 0, z * g), maj=20, mn=8), ROPE))
    parts.append(tag(torus('lashx', 0.22 * g, 0.06 * g, (0.0, 0, 16.9 * g), axis=(1, 0, 0), maj=16, mn=6), ROPE))
    arm = cylinder('larm', 0.13 * g, 3.6 * g, verts=12, axis='X', c=(1.9 * g, 0, 14.6 * g))
    parts.append(tag(finish(arm, rng, 0.025 * g, seg=2, jitter=0.006 * g), STAIN))
    brace = beam('lbrace', (0.15 * g, 0, 12.9 * g), (2.0 * g, 0, 14.55 * g), 0.16 * g)
    parts.append(tag(finish(brace, rng, 0.02 * g, seg=2), STAIN))
    parts.append(tag(torus('lband', 0.32 * g, 0.06 * g, (0, 0, 14.6 * g), maj=18, mn=6), IRON))
    parts.append(tag(torus('hook', 0.2 * g, 0.05 * g, (3.55 * g, 0, 14.42 * g), axis=(0, 1, 0), maj=16, mn=6), IRON))
    parts.append(tag(torus('collar', 0.33 * g, 0.07 * g, (0, 0, 8.0 * g), maj=18, mn=6), IRON))
    for z in (2.0, 5.5, 11.5):                    # 几道旧绳圈（杆身不至于光秃）
        parts.append(tag(torus('wrap', 0.31 * g, 0.05 * g, (0, 0, z * g), maj=18, mn=6), ROPE))
    tip = cylinder('tip', 0.27 * g, 0.5 * g, verts=14, c=(0, 0, -0.12 * g), r_top=0.3 * g)
    parts.append(tag(tip, IRON))
    return parts, (3.55 * g, 0.0, 14.22 * g)   # 灯笼挂点（米）


def cv_tagarm(rng):
    """挂木签的横臂：原点在旗杆轴心，沿 +X；根部绑在铁箍上；四个挂绳缺口"""
    parts = []
    arm = cylinder('arm', 0.12 * g, 5.4 * g, verts=12, axis='X', c=(2.9 * g, 0, 0))
    parts.append(tag(finish(arm, rng, 0.02 * g, seg=2, jitter=0.006 * g, warp=0.015 * g), STAIN))
    parts.append(tag(torus('root', 0.36 * g, 0.075 * g, (0.32 * g, 0, 0), axis=(1, 0, 0), maj=16, mn=6), ROPE))
    parts.append(tag(ico('end', 0.18 * g, (5.65 * g, 0, 0), subdiv=2), 'teal'))
    for x in (1.4, 2.6, 3.8, 5.0):
        parts.append(tag(torus('knot', 0.14 * g, 0.045 * g, (x * g, 0, 0), axis=(1, 0, 0), maj=12, mn=6), ROPE))
    return parts


def cv_lantern(rng):
    """大号纸灯笼：游戏里帅旗灯笼的结构放大 4 倍（原点在挂钩）"""
    parts = k_lantern(rng)
    S = Matrix.Scale(4.0, 4)
    for p in parts:
        p.matrix_world = S @ p.matrix_world
    return parts


def cv_tag(rng):
    """菜单木签：原点在穿绳孔中心，正面朝 +Y（字在 Unity 里写）；约 13 × 4.5 cm × 7 mm"""
    pts = [(-1.3, -0.95), (1.3, -0.95), (1.3, -0.12), (1.08, 0.08), (-1.08, 0.08), (-1.3, -0.12)]
    o = prism('tag', [(x * g, z * g) for x, z in pts], 0.14 * g)
    boolean(o, [cylinder('hole', 0.07 * g, 0.4 * g, verts=14, axis='Y', c=(0, 0, -0.08 * g)),
                cylinder('hole2', 0.06 * g, 0.4 * g, verts=14, axis='Y', c=(0, 0, -0.8 * g))])   # 底孔：下一块木签挂在这里（一串木签首尾相连）
    boolean(o, chip_cutters(o, rng, 4, 0.08 * g))
    parts = [tag(finish(o, rng, 0.03 * g, seg=2, jitter=0.008 * g, warp=0.015 * g), WOOD)]
    for sx in (-1.05, 1.05):                      # 两颗小钉（真实木牌的钉补）
        parts.append(tag(cylinder('nail', 0.05 * g, 0.03 * g, verts=10, axis='Y', c=(sx * g, -0.085 * g, -0.75 * g)), IRON))
    return parts


def cv_stick(rng):
    """结算横木：原点在中点，沿 X 长 6.8 格（34 cm）；两端各一道绑绳（吊绳绑在这里）+ 漆头；中间两道绳圈（挂布旗 / 木签的地方）"""
    parts = []
    st = cylinder('stick', 0.13 * g, 6.8 * g, verts=12, axis='X', c=(0, 0, 0))
    parts.append(tag(finish(st, rng, 0.025 * g, seg=2, jitter=0.006 * g, warp=0.02 * g), STAIN))
    for sx in (-1, 1):
        parts.append(tag(ico('cap', 0.19 * g, (sx * 3.45 * g, 0, 0), subdiv=2), 'teal'))
        for dx in (-0.08, 0.08):
            parts.append(tag(torus('lash', 0.16 * g, 0.045 * g, ((sx * 3.0 + dx) * g, 0, 0), axis=(1, 0, 0), maj=12, mn=6), ROPE))
    for x in (-1.5, 1.4):
        parts.append(tag(torus('knot', 0.155 * g, 0.042 * g, (x * g, 0, 0), axis=(1, 0, 0), maj=12, mn=6), ROPE))
    return parts


ITEMS = [
    # 名字, 建模函数, 体素（格）, 磨损强度, 低模面数
    ('K_Pole', k_pole, 0.0045, 0.8, 1600),
    ('K_Flag', k_flag, 0.004, 0.2, 1000),
    ('K_Lantern', k_lantern, 0.004, 0.6, 1600),
    ('C_Frame', c_frame, 0.011, 1.0, 4000),
    ('C_Arm', c_arm, 0.008, 1.0, 2400),
    ('C_Wheel', c_wheel, 0.006, 1.0, 1600),
    ('S_Fork', s_fork, 0.005, 0.7, 3000),
    ('S_Pouch', s_pouch, 0.004, 0.4, 800),
    ('B_Stone', b_stone, 0.0035, 0.8, 900),
    ('B_Fire', b_fire, 0.0035, 0.5, 900),
    ('B_Ice', b_ice, 0.0035, 0.2, 700),
    ('B_Bomb', b_bomb, 0.0035, 0.6, 1200),
    ('B_Split', b_split, 0.0035, 0.6, 1000),
    ('T_Tray', t_tray, 0.007, 0.9, 2400),
    ('A_Box', a_box, 0.005, 0.8, 4000),
    ('A_Peg', a_peg, 0.0025, 0.0, 300),
    ('H_Tag', h_tag, 0.0045, 0.8, 800),
]
if QUICK:
    ITEMS = [i for i in ITEMS if i[0] in ('K_Flag', 'B_Fire', 'S_Fork')]
if COVER:
    ITEMS = [
        ('CV_Pole', cv_pole, 0.02, 0.8, 4000),
        ('CV_TagArm', cv_tagarm, 0.012, 0.8, 1200),
        ('CV_Lantern', cv_lantern, 0.014, 0.6, 2400),
        ('CV_Tag', cv_tag, 0.01, 0.9, 900),
        ('CV_Stick', cv_stick, 0.012, 0.8, 1200),
    ]


# ───────────────────────── 关卡机关建模函数（mech 模式专用，不影响 props/cover） ─────────────────────────

def mech_hay(rng):
    """M_Hay 草垛：1.1×0.9×0.8格，底面中心为原点。
    实心圆角草捆核心 + 密排垂直草茎（细圆柱，轻微随机倾斜，有的嵌入核心，
    顶部和四侧有少量散草乱翘）；颜色干草金黄 d8b75a 为主，
    少量 b8913f 深色草茎增加层次；grain≤0.15，不用 bare=1.0。
    两道麻绳箍绕草捆中部（高度0.28格和0.56格），紧贴表面略微嵌入，
    用 torus 绕整圈（XY 平面圆环，轴向 Z）。"""
    parts = []
    # ─── 实心草捆核心（圆角长方体）─────────────────────────────────
    # 尺寸：1.1×0.9×0.8格；用 bevel 修饰器做圆角，模拟压实的稻草捆
    core = box('hay_core', 1.1 * g, 0.9 * g, 0.8 * g)
    md = core.modifiers.new('bev', 'BEVEL')
    md.width = 0.08 * g
    md.segments = 3
    md.limit_method = 'ANGLE'
    md.angle_limit = math.radians(30)
    apply_mods(core)
    # 颜色：干草金黄；grain 极低不走裸木底层
    core_col = dict(col='d8b75a', wear=0.0, grain=0.08, bare=0.0)
    parts.append(tag(core, core_col))

    # ─── 垂直草茎（细圆柱/扁条，密排在核心表面）────────────────────
    # 草茎参数
    hay_col_a = dict(col='d8b75a', wear=0.0, grain=0.08, bare=0.0)   # 主色
    hay_col_b = dict(col='b8913f', wear=0.0, grain=0.10, bare=0.0)   # 深色草茎（约 20%）
    # 侧面草茎：沿四面外表面密排，高度略超出核心，轻微随机倾斜
    ctr = 0
    side_configs = [
        # (固定轴, 固定值, 扫描轴1范围, 扫描轴2范围, 草茎朝向)
        # +X 面 / -X 面
        ('x',  0.52 * g, 'y', 'z'),
        ('x', -0.52 * g, 'y', 'z'),
        # +Y 面 / -Y 面
        ('y',  0.43 * g, 'x', 'z'),
        ('y', -0.43 * g, 'x', 'z'),
    ]
    step_a = 0.075 * g   # 草茎间距
    r_stem = 0.014 * g   # 草茎半径
    for face_axis, face_val, sa, sb in side_configs:
        if face_axis in ('x',):
            lim_a = (-0.4 * g, 0.4 * g)    # y 范围
            lim_b = (0.0, 0.8 * g)          # z 范围（从底到顶）
        else:
            lim_a = (-0.5 * g, 0.5 * g)    # x 范围
            lim_b = (0.0, 0.8 * g)
        pos_a = lim_a[0]
        while pos_a <= lim_a[1] + 1e-9:
            pos_b = lim_b[0]
            while pos_b <= lim_b[1] + 1e-9:
                tilt_x = rng.uniform(-0.08, 0.08)
                tilt_y = rng.uniform(-0.08, 0.08)
                stem_h = 0.09 * g + rng.uniform(0, 0.04) * g
                offset = rng.uniform(-0.015, 0.015) * g
                if face_axis == 'x':
                    cx = face_val + rng.uniform(0, 0.02) * g * (1 if face_val > 0 else -1)
                    cy = pos_a + offset
                    cz = pos_b + stem_h * 0.5 + rng.uniform(-0.01, 0.01) * g
                else:
                    cx = pos_a + offset
                    cy = face_val + rng.uniform(0, 0.02) * g * (1 if face_val > 0 else -1)
                    cz = pos_b + stem_h * 0.5 + rng.uniform(-0.01, 0.01) * g
                # 草茎沿 Z 轴，轻微倾斜用 bm 直接建
                bm_s = bmesh.new()
                bm_s.verts.new((cx - tilt_x * stem_h, cy - tilt_y * stem_h, cz - stem_h * 0.5))
                bm_s.verts.new((cx + tilt_x * stem_h, cy + tilt_y * stem_h, cz + stem_h * 0.5))
                bm_s.edges.new(bm_s.verts)
                # 体素化后线段太细，改用细圆柱，在原点建后移位
                bm_s.free()
                s = cylinder(f'st{ctr}', r_stem, stem_h, verts=5, c=(cx, cy, cz))
                col = hay_col_a if rng.random() > 0.2 else hay_col_b
                parts.append(tag(s, col))
                ctr += 1
                pos_b += step_a
            pos_a += step_a

    # 顶面散草：少量较高的草茎随机散布
    for _ in range(30):
        cx = rng.uniform(-0.48 * g, 0.48 * g)
        cy = rng.uniform(-0.38 * g, 0.38 * g)
        stem_h = rng.uniform(0.06, 0.14) * g
        tilt_x = rng.uniform(-0.15, 0.15)
        tilt_y = rng.uniform(-0.15, 0.15)
        cz = 0.8 * g + stem_h * 0.5
        s = cylinder(f'st{ctr}', r_stem * rng.uniform(0.8, 1.2), stem_h, verts=5,
                     c=(cx + tilt_x * stem_h, cy + tilt_y * stem_h, cz))
        col = hay_col_a if rng.random() > 0.25 else hay_col_b
        parts.append(tag(s, col))
        ctr += 1

    # ─── 两道麻绳箍（torus 绕整圈，紧贴核心表面，轴向 Z）─────────────
    # 核心在 XY 截面约 ±0.52×±0.43格，绳子略微嵌入（半径略小于核心外壁）
    # 高度：0.28格 和 0.56格
    # 用椭圆环：在 XY 平面构建椭圆环路（近似：用细管绕矩形 4 段，或用 torus 适当 scale）
    # 采用 4 段圆弧（Bezier 圆角矩形逼近），用 tube 函数
    r_rope = 0.018 * g   # 绳截面半径
    for rope_z in (0.28 * g, 0.56 * g):
        # 椭圆矩形环路：4 段直线 + 4 段圆角（直接用稠密折线点列近似圆角）
        pts_rope = []
        RX, RY = 0.52 * g, 0.42 * g    # 绳子路径的矩形半宽（嵌入核心约 0.015g）
        corner_r = 0.06 * g             # 圆角半径
        # 生成矩形圆角路径（逆时针，从 +X+Y 角开始）
        corners_xy = [(RX - corner_r, RY), (RX, RY - corner_r),
                      (RX, -(RY - corner_r)), (RX - corner_r, -RY),
                      (-(RX - corner_r), -RY), (-RX, -(RY - corner_r)),
                      (-RX, RY - corner_r), (-(RX - corner_r), RY)]
        arc_centers = [(RX - corner_r, RY - corner_r),
                       (RX - corner_r, -(RY - corner_r)),
                       (-(RX - corner_r), -(RY - corner_r)),
                       (-(RX - corner_r), RY - corner_r)]
        arc_starts = [math.radians(90), math.radians(0), math.radians(270), math.radians(180)]
        for ci in range(4):
            # 直线段
            if ci == 0:
                # +Y 侧直线
                pts_rope.append((-(RX - corner_r), RY, rope_z, r_rope))
                pts_rope.append((RX - corner_r, RY, rope_z, r_rope))
            elif ci == 1:
                # +X 侧直线
                pts_rope.append((RX, RY - corner_r, rope_z, r_rope))
                pts_rope.append((RX, -(RY - corner_r), rope_z, r_rope))
            elif ci == 2:
                # -Y 侧直线
                pts_rope.append((RX - corner_r, -RY, rope_z, r_rope))
                pts_rope.append((-(RX - corner_r), -RY, rope_z, r_rope))
            else:
                # -X 侧直线
                pts_rope.append((-RX, -(RY - corner_r), rope_z, r_rope))
                pts_rope.append((-RX, RY - corner_r, rope_z, r_rope))
            # 圆弧（4点近似）
            cx_arc, cy_arc = arc_centers[ci]
            a0 = arc_starts[ci]
            for k in range(5):
                a = a0 + math.radians(90) * k / 4
                px = cx_arc + corner_r * math.cos(a)
                py = cy_arc + corner_r * math.sin(a)
                pts_rope.append((px, py, rope_z, r_rope))
        # 闭合（首尾连接）：在 tube 中首尾点重合即可
        pts_rope.append(pts_rope[0])
        rope_ob = tube(f'rope_{int(rope_z * 1000)}', pts_rope, res=8, bevel_res=3)
        parts.append(tag(rope_ob, ROPE))

    return parts


def mech_keg(rng):
    """M_Keg 火药桶：直径0.85格高1.0格，外凸桶板+两道铁箍+警示红漆圈+顶部导火绳。底面中心为原点。"""
    parts = []
    # 桶体：竖圆柱，腰部略鼓出（用锥形叠加模拟）
    # 外形：底/顶半径0.4，腰部半径0.425
    n_seg = 20
    bm = bmesh.new()
    # 建桶体顶点：用分层圆截面，腰部鼓出
    layers = 12
    r_base = 0.425 * g
    r_mid = 0.455 * g   # 腰部鼓
    for i in range(layers + 1):
        t = i / layers
        # 鼓形曲线
        rr = r_base + (r_mid - r_base) * math.sin(t * math.pi)
        z = t * 1.0 * g
        for j in range(n_seg):
            a = 2 * math.pi * j / n_seg
            bm.verts.new((math.cos(a) * rr, math.sin(a) * rr, z))
    bm.verts.ensure_lookup_table()
    # 侧面四边形
    for i in range(layers):
        for j in range(n_seg):
            a = i * n_seg + j
            b = i * n_seg + (j + 1) % n_seg
            c = (i + 1) * n_seg + (j + 1) % n_seg
            d = (i + 1) * n_seg + j
            bm.faces.new((bm.verts[a], bm.verts[b], bm.verts[c], bm.verts[d]))
    # 顶/底盖
    top_verts = [bm.verts[(layers) * n_seg + j] for j in range(n_seg)]
    bot_verts = [bm.verts[j] for j in range(n_seg)]
    bm.faces.new(top_verts)
    bm.faces.new(list(reversed(bot_verts)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    keg_ob = obj_from_bm('keg_body', bm)
    # 在桶侧面刻出桶板缝（垂直槽）
    stave_cuts = []
    for k in range(10):
        a = 2 * math.pi * k / 10
        # 细长条切割体（从内侧切）
        sc = cylinder('sc', 0.025 * g, 1.1 * g, verts=6,
                      c=(math.cos(a) * 0.46 * g, math.sin(a) * 0.46 * g, 0.5 * g))
        stave_cuts.append(sc)
    boolean(keg_ob, stave_cuts)
    boolean(keg_ob, chip_cutters(keg_ob, rng, 3, 0.07 * g))
    parts.append(tag(finish(keg_ob, rng, 0.018 * g, seg=2, jitter=0.004 * g),
                     dict(col='8a6440', wear=0.6, grain=0.9, bare=0.0)))  # 旧木桶

    # 两道铁箍（上下各一）
    for hz in (0.18, 0.82):
        parts.append(tag(torus('hoop', 0.46 * g, 0.022 * g, (0, 0, hz * g), maj=24, mn=7), IRON))

    # 警示红漆圈（桶腰一圈）：用薄圆柱环套在桶上
    warn_ring = cylinder('warn', 0.462 * g, 0.18 * g, verts=28, c=(0, 0, 0.5 * g),
                         r_top=0.462 * g)
    inner_cut = cylinder('wc', 0.44 * g, 0.3 * g, verts=28, c=(0, 0, 0.5 * g))
    boolean(warn_ring, [inner_cut])
    parts.append(tag(warn_ring, dict(col='a05a46', wear=0.3, grain=0.0)))  # brick

    # 导火绳从桶盖中心伸出（弯曲的管状绳子）
    pts = [(0, 0, 1.01 * g, 0.025 * g), (0.02 * g, 0, 1.08 * g, 0.022 * g),
           (0.05 * g, 0.015 * g, 1.14 * g, 0.020 * g)]
    parts.append(tag(tube('fuse', pts, res=8, bevel_res=2), dict(col='c2a878', wear=0.0, grain=0.0)))
    return parts


def mech_bucket(rng):
    """M_Bucket 水桶：宽口木桶，直径1.0格高0.7格，底面中心为原点。
    外圆柱 boolean 挖空内部 → 实心桶壁；桶板缝、两道铁箍、提耳、水面。"""
    parts = []
    # 桶外壳：上宽下窄实心圆柱（r_top=0.5, r_bot=0.4）
    outer = cylinder('outer', 0.4 * g, 0.7 * g, verts=28, r_top=0.5 * g)
    # 内部挖空（上宽下窄，桶壁厚约 0.06g）
    inner = cylinder('inner', 0.34 * g, 0.75 * g, verts=28, r_top=0.44 * g, c=(0, 0, 0.06 * g))
    boolean(outer, [inner])
    # 刻桶板缝（竖向细槽）
    stave_cuts = []
    for k in range(10):
        a = 2 * math.pi * k / 10
        sc = cylinder('sc', 0.018 * g, 0.8 * g, verts=6,
                      c=(math.cos(a) * 0.5 * g, math.sin(a) * 0.5 * g, 0.35 * g))
        stave_cuts.append(sc)
    boolean(outer, stave_cuts)
    boolean(outer, chip_cutters(outer, rng, 2, 0.06 * g))
    parts.append(tag(finish(outer, rng, 0.018 * g, seg=2, jitter=0.004 * g),
                     dict(col='9c7350', wear=0.5, grain=0.9, bare=0.0)))

    # 两道铁箍（套在桶外壁）
    for hz, rr in ((0.14 * g, 0.42 * g), (0.52 * g, 0.51 * g)):
        parts.append(tag(torus('hoop', rr, 0.020 * g, (0, 0, hz), maj=22, mn=7), IRON))

    # 提耳（两侧 D 形铁环）
    for sx in (-1, 1):
        ear_base = box('ear', 0.06 * g, 0.07 * g, 0.07 * g,
                       cx=sx * 0.5 * g, z0=0.56 * g)
        parts.append(tag(ear_base, IRON))
        # 提环轴向 Y（使环在 XZ 平面内、侧面竖立）
        parts.append(tag(torus('handle', 0.11 * g, 0.016 * g,
                               (sx * 0.53 * g, 0, 0.63 * g), axis=(0, 1, 0), maj=14, mn=6), IRON))

    # 水面（实心薄圆柱盖，蓝灰，略低于桶口）
    water = cylinder('water', 0.42 * g, 0.015 * g, verts=22, c=(0, 0, 0.63 * g))
    parts.append(tag(water, dict(col='7a9eb5', wear=0.0, grain=0.0)))
    return parts


def mech_trough(rng):
    """M_Trough 浅水槽：4.0×2.2格×0.25格高，底面中心为原点。木框四周+水面内衬。"""
    parts = []
    W, D, H = 4.0 * g, 2.2 * g, 0.25 * g
    wall_t = 0.14 * g
    # 四面木板框
    # 长边 (Y 向)
    for sx in (-1, 1):
        wl = box('trough_side', wall_t, D - 2 * wall_t, H, cx=sx * (W / 2 - wall_t / 2))
        boolean(wl, chip_cutters(wl, rng, 2, 0.06 * g))
        parts.append(tag(finish(wl, rng, 0.018 * g, seg=2, jitter=0.004 * g),
                         dict(col='8a6440', wear=0.6, grain=0.9, bare=0.0)))
    # 短边 (X 向)
    for sy in (-1, 1):
        wl = box('trough_end', W, wall_t, H, cy=sy * (D / 2 - wall_t / 2))
        boolean(wl, chip_cutters(wl, rng, 2, 0.06 * g))
        parts.append(tag(finish(wl, rng, 0.018 * g, seg=2, jitter=0.004 * g),
                         dict(col='8a6440', wear=0.6, grain=0.9, bare=0.0)))
    # 底板
    bottom = box('trough_bot', W, D, wall_t * 0.6, z0=-wall_t * 0.6)
    parts.append(tag(finish(bottom, rng, 0.015 * g, seg=2, jitter=0.003 * g),
                     dict(col='7a5638', wear=0.4, grain=0.9, bare=0.0)))
    # 四角角撑（榫卯/钉子视觉）
    for sx, sy in ((-1, -1), (-1, 1), (1, -1), (1, 1)):
        brace = box('corner', wall_t * 0.8, wall_t * 0.8, H,
                    cx=sx * (W / 2 - wall_t * 0.4), cy=sy * (D / 2 - wall_t * 0.4))
        parts.append(tag(brace, dict(col='3b3a3c', wear=0.0, grain=0.0, metal=1.0)))
    # 水面（蓝灰，略低于框口）
    water = box('water', W - wall_t * 2.2, D - wall_t * 2.2, 0.01 * g, z0=H - 0.04 * g)
    parts.append(tag(water, dict(col='7a9eb5', wear=0.0, grain=0.0)))
    return parts


def mech_keystone(rng):
    """M_Keystone 楔形拱顶石：顶宽0.7底宽0.45，高0.6厚1.0格，正面有圆圈印记。底面中心为原点。
    整块石头，不分层，石头材质（bare=1.0, grain=0.0 无木纹）。"""
    parts = []
    # 楔形轮廓（XZ 平面：x 水平 z 竖直），沿 Y 挤出 1.0 格厚度
    # 顶面在 z=0.6g（上宽0.7格），底面在 z=0（下宽0.45格）——正视图明显倒梯形楔形
    pts_xz = [(-0.35, 0.6), (0.35, 0.6), (0.225, 0.0), (-0.225, 0.0)]
    stone = prism('keystone', [(x * g, z * g) for x, z in pts_xz], 1.0 * g)
    # 正面圆圈印记（从正面 y=-0.5g 切入 0.08g 深，圆半径0.1格，位于石体高度中心 z=0.3g）
    mark = cylinder('mark', 0.1 * g, 0.1 * g, verts=16, axis='Y', c=(0, -0.46 * g, 0.3 * g))
    boolean(stone, [mark])
    # 石头磕碰缺角（圆润碎块，stone=True 更接近石质破损）
    boolean(stone, chip_cutters(stone, rng, 4, 0.08 * g, stone=True, corner_share=0.7))
    # 石头表面起伏（比木头更大振幅、更随机）
    out = finish(stone, rng, 0.055 * g, seg=2, jitter=0.018 * g, warp=0.015 * g, angle=35)
    parts.append(tag(out, dict(col='b0a898', wear=0.0, grain=0.0, bare=0.0)))  # 石灰色，无木纹，不用木材质底层
    return parts


def mech_bell(rng):
    """M_Bell 铜钟：钟口直径0.7格高0.8格，原点=顶部挂环中心，钟体向 -Z 垂下。
    车削截面：钟口外张，腰部鼓出，顶部收颈；两道凸起钟带；顶部挂环。"""
    parts = []
    # 车削截面点列 (r, z)：z=0 是钟口（底），z=0.8 是顶颈
    # 坐标原点在挂环顶，钟体在 -Z 方向
    # 截面绕 Z 轴旋转 → 偏移到 -Z
    # 钟形轮廓：口部微外展（喇叭口）→ 腰部略收 → 中上略鼓 → 顶颈
    profile = [
        (0.32, 0.0),     # 钟口内缘
        (0.36, 0.03),    # 钟口外展（喇叭最宽处，直径约0.72）
        (0.33, 0.10),    # 收进
        (0.27, 0.26),    # 腰部
        (0.24, 0.40),    # 腰最细
        (0.26, 0.54),    # 中上略鼓
        (0.20, 0.66),    # 上收
        (0.11, 0.75),    # 颈部
        (0.07, 0.80),    # 顶颈
    ]
    # 旋转体：绕 Z 轴，z 坐标偏移到 -Z 方向（钟挂在 -Z）
    n_seg = 28
    n_prof = len(profile)
    bm = bmesh.new()
    for pi, (r, pz) in enumerate(profile):
        for j in range(n_seg):
            a = 2 * math.pi * j / n_seg
            # 钟在 -Z 下垂：z_world = -(挂环偏移 + pz)
            bm.verts.new((math.cos(a) * r * g, math.sin(a) * r * g, -(pz + 0.1) * g))
    bm.verts.ensure_lookup_table()
    # 侧面
    for i in range(n_prof - 1):
        for j in range(n_seg):
            a = i * n_seg + j
            b = i * n_seg + (j + 1) % n_seg
            c = (i + 1) * n_seg + (j + 1) % n_seg
            d = (i + 1) * n_seg + j
            bm.faces.new((bm.verts[a], bm.verts[b], bm.verts[c], bm.verts[d]))
    # 钟口底盖（开口=空，不盖；但要有钟口内壁）
    # 顶盖
    top_vs = [bm.verts[(n_prof - 1) * n_seg + j] for j in range(n_seg)]
    bm.faces.new(top_vs)
    # 钟口口沿（环形底）：外圈 + 内圈（内壁略薄）
    # 内壁：从钟口向内深约 0.1g，半径收0.03g
    inner_r = 0.32 * g
    inner_verts = []
    for j in range(n_seg):
        a = 2 * math.pi * j / n_seg
        v_new = bm.verts.new((math.cos(a) * inner_r, math.sin(a) * inner_r, -0.1 * g))
        inner_verts.append(v_new)
    bm.verts.ensure_lookup_table()
    outer_ring = [bm.verts[j] for j in range(n_seg)]
    # 钟口环面
    for j in range(n_seg):
        bm.faces.new((outer_ring[j], outer_ring[(j + 1) % n_seg],
                      inner_verts[(j + 1) % n_seg], inner_verts[j]))
    # 内壁上盖（连接内圈和钟内壁顶）
    bm.faces.new(list(reversed(inner_verts)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bell_ob = obj_from_bm('bell_body', bm)
    # 顶部挂环（圆环，原点在 z=0 即挂环顶中心）
    parts.append(tag(bell_ob, dict(col='7a8c5a', wear=0.0, grain=0.0, metal=0.6)))  # 铜绿色

    # 两道凸起钟带（箍）
    for hz in (-0.25, -0.55):
        rr_at_z = 0.0
        # 在对应 z 处找截面半径（线性插值）
        z_norm = -hz / 0.8   # 对应 profile 中 z 值
        for pi in range(len(profile) - 1):
            r0, z0 = profile[pi]
            r1, z1 = profile[pi + 1]
            if z0 <= z_norm <= z1:
                t = (z_norm - z0) / (z1 - z0 + 1e-9)
                rr_at_z = (r0 + (r1 - r0) * t) * g
                break
        if rr_at_z < 0.01 * g:
            rr_at_z = 0.2 * g
        parts.append(tag(torus('band', rr_at_z + 0.012 * g, 0.018 * g,
                               (0, 0, hz * g), maj=22, mn=7), dict(col='8a7230', wear=0.0, grain=0.0, metal=0.8)))

    # 顶部挂环（向 +Z，在原点上方）
    ring = torus('hang_ring', 0.065 * g, 0.022 * g, (0, 0, 0.055 * g), axis=(0, 1, 0), maj=16, mn=7)
    parts.append(tag(ring, IRON))
    return parts


def mech_bellframe(rng):
    """M_BellFrame 钟架：宽4.4高2.4格深0.8格，门型木架，三个铁钩在横梁下（x=-1.7,0,+1.7格）。
    两根立柱+一根横梁+斜撑+底脚；report 中记录三钩坐标。"""
    parts = []
    W = 4.4 * g    # 总宽（立柱中心在 x=±2.05格）
    H = 2.4 * g    # 总高
    D = 0.8 * g    # 深度
    bt = 0.18 * g  # 梁柱截面宽

    def frame_beam(name, p0, p1, w=None):
        b = beam(name, p0, p1, w or bt)
        boolean(b, chip_cutters(b, rng, 2, 0.06 * g))
        return tag(finish(b, rng, 0.022 * g, seg=2, jitter=0.005 * g), STAIN)

    # 两根立柱（从底到顶）
    for sx in (-1, 1):
        x = sx * (W / 2 - bt / 2)
        parts.append(frame_beam('post', (x, 0, 0), (x, 0, H)))
        # 前后各一根（深度方向）
        parts.append(frame_beam('post_b', (x, D - bt, 0), (x, D - bt, H)))

    # 横梁（前后）
    for dy in (0, D - bt):
        parts.append(frame_beam('xbeam', (-W / 2 + bt / 2, dy, H - bt / 2), (W / 2 - bt / 2, dy, H - bt / 2)))

    # 侧向联系梁（两侧连立柱前后）
    for sx in (-1, 1):
        x = sx * (W / 2 - bt / 2)
        for hz in (H * 0.35, H * 0.65):
            parts.append(frame_beam('side_beam', (x, 0, hz), (x, D - bt, hz), bt * 0.9))

    # 斜撑（前面两条，从底侧到横梁）
    for sx in (-1, 1):
        x = sx * (W / 2 - bt / 2)
        parts.append(frame_beam('brace', (x, 0, H * 0.4), (x * 0.45, 0, H - bt), bt * 0.75))

    # 底脚撑（柱脚横向踏木）
    for dy in (0, D - bt):
        parts.append(frame_beam('foot', (-W / 2, dy, 0), (W / 2, dy, bt), bt))

    # 三个铁钩（挂钟，间距1.7格：x = -1.7格, 0, +1.7格）
    hook_coords = []
    hook_z = H - bt - 0.04 * g
    for kx in (-1.7 * g, 0, 1.7 * g):
        hook_coords.append((round(kx, 5), round(0.0, 5), round(hook_z, 5)))
        h = torus('hook', 0.055 * g, 0.016 * g, (kx, 0, hook_z), axis=(0, 1, 0), maj=14, mn=6)
        parts.append(tag(h, IRON))
        stem = cylinder('stem', 0.014 * g, 0.07 * g, verts=8, c=(kx, 0, hook_z + 0.04 * g))
        parts.append(tag(stem, IRON))
    return parts, hook_coords


def mech_scalepost(rng):
    """M_ScalePost 秤立柱：高2.0格含底座，顶部轴座；底面中心为原点。"""
    parts = []
    # 底座（加宽）
    base = box('base', 0.9 * g, 0.9 * g, 0.2 * g)
    boolean(base, chip_cutters(base, rng, 3, 0.07 * g))
    parts.append(tag(finish(base, rng, 0.025 * g, seg=2, jitter=0.005 * g, warp=0.01 * g), 'teal'))
    # 立柱（方形截面，渐收）
    post = box('post', 0.5 * g, 0.5 * g, 1.75 * g, z0=0.2 * g)
    boolean(post, chip_cutters(post, rng, 3, 0.08 * g))
    parts.append(tag(finish(post, rng, 0.025 * g, seg=2, jitter=0.006 * g, warp=0.012 * g), STAIN))
    # 顶部轴座（方块，中间有轴孔）
    axle_seat = box('axle_seat', 0.6 * g, 0.6 * g, 0.3 * g, z0=1.95 * g)
    axle_hole = cylinder('ah', 0.06 * g, 0.7 * g, verts=14, axis='X', c=(0, 0, 2.1 * g))
    boolean(axle_seat, [axle_hole])
    parts.append(tag(axle_seat, dict(col='6d958e', wear=0.3, grain=0.0, metal=0.4)))  # teal 铁件
    # 加固斜肋（从立柱到底座）
    for sx in (-1, 1):
        rib = beam('rib', (sx * 0.22 * g, 0, 0.2 * g), (sx * 0.4 * g, 0, 0.6 * g), 0.08 * g)
        parts.append(tag(rib, STAIN))
    return parts


def mech_scalebeam(rng):
    """M_ScaleBeam 秤横杆+两个秤盘：横杆4.5格×0.25格正方截面，原点=旋转轴中心。
    两端铁箍+中心铁轴套；秤盘直径0.9格高0.12格；三根铁链悬挂；秤盘中心在(±2.0格,0,-1.3格)。"""
    parts = []
    # 横杆（方木 0.25×0.25 格截面，沿 X）
    arm = box('arm', 4.5 * g, 0.25 * g, 0.25 * g, z0=-0.125 * g)
    boolean(arm, chip_cutters(arm, rng, 4, 0.07 * g))
    parts.append(tag(finish(arm, rng, 0.018 * g, seg=2, jitter=0.004 * g), 'ochre'))
    # 中心铁轴套（方形夹环：外壳套住横杆 YZ 截面，内腔略大于横杆）
    col_out = box('col_out', 0.3 * g, 0.33 * g, 0.33 * g, z0=-0.165 * g)
    col_in = box('col_in', 0.4 * g, 0.26 * g, 0.26 * g, z0=-0.13 * g)
    boolean(col_out, [col_in])
    parts.append(tag(col_out, IRON))
    # 两端及秤盘
    pan_centers = []
    for sx in (-1, 1):
        side = 'L' if sx < 0 else 'R'
        arm_x = sx * 2.25 * g   # 横杆端点 X
        pan_x = sx * 2.0 * g    # 秤盘中心 X = ±2.0 格
        pan_z = -1.3 * g        # 秤盘中心 Z = -1.3 格（向下1.3格）
        # 端部铁箍（YZ 平面内圆环，套在横杆末端）
        hoop = torus(f'eh_{side}', 0.155 * g, 0.018 * g, (arm_x, 0, 0), axis=(1, 0, 0), maj=14, mn=6)
        parts.append(tag(hoop, IRON))
        # 秤盘（浅圆木盘，直径0.9格高0.12格）
        pan_body = cylinder(f'pan_{side}', 0.45 * g, 0.12 * g, verts=22, c=(pan_x, 0, pan_z))
        pan_inner = cylinder(f'pi_{side}', 0.38 * g, 0.08 * g, verts=22, c=(pan_x, 0, pan_z + 0.04 * g))
        boolean(pan_body, [pan_inner])
        parts.append(tag(finish(pan_body, rng, 0.015 * g, seg=2, jitter=0.003 * g),
                         dict(col='c09a55', wear=0.5, grain=0.8, bare=0.0)))  # ochre 木盘
        pan_centers.append((round(pan_x, 5), 0.0, round(pan_z, 5)))
        # 盘缘铁箍
        parts.append(tag(torus(f'pr_{side}', 0.46 * g, 0.012 * g,
                               (pan_x, 0, pan_z + 0.06 * g), maj=18, mn=5), IRON))
        # 3 根铁链：从横杆端点到秤盘盘沿三个等分点（120°间距）
        rim_r = 0.32 * g   # 链条在盘沿的附着半径
        for k in range(3):
            ang = math.radians(90 + k * 120)
            rx = pan_x + rim_r * math.cos(ang)
            ry = rim_r * math.sin(ang)
            ch = tube(f'ch_{side}{k}',
                      [(arm_x, 0, 0, 0.012 * g), (rx, ry, pan_z, 0.012 * g)],
                      res=4, bevel_res=3)
            parts.append(tag(ch, IRON))
    return parts, pan_centers


if MECH:
    ITEMS = [
        ('M_Hay',       mech_hay,        0.007, 0.8, 1800),
        ('M_Keg',       mech_keg,        0.007, 0.9, 2200),
        ('M_Bucket',    mech_bucket,     0.007, 0.8, 1800),
        ('M_Trough',    mech_trough,     0.009, 0.7, 2600),
        ('M_Keystone',  mech_keystone,   0.006, 0.9, 1600),
        ('M_Bell',      mech_bell,       0.006, 0.4, 2000),
        ('M_BellFrame', mech_bellframe,  0.010, 0.9, 3800),
        ('M_ScalePost', mech_scalepost,  0.007, 0.8, 1800),
        ('M_ScaleBeam', mech_scalebeam,  0.007, 0.6, 2800),
    ]


# ───────────────────────── 合并部件 + 高模 + 属性 ─────────────────────────
def join_parts(parts, name):
    """合并部件（不做布尔），同时记录每个面属于哪个部件；返回 (物体, 每面部件号, 部件属性表)"""
    bm = bmesh.new()
    face_part, attrs = [], []
    for i, p in enumerate(parts):
        tmp = bmesh.new()
        tmp.from_mesh(p.data)
        bmesh.ops.transform(tmp, verts=tmp.verts, matrix=p.matrix_world)
        me = bpy.data.meshes.new('t')
        tmp.to_mesh(me)
        tmp.free()
        n0 = len(bm.faces)
        bm.from_mesh(me)
        bpy.data.meshes.remove(me)
        face_part += [i] * (len(bm.faces) - n0)
        wear, grain, metal, bare = p['pm']
        attrs.append((lin(p['pc']), float(p['pe']), (wear, grain, metal, bare)))
    for p in parts:
        bpy.data.objects.remove(p, do_unlink=True)
    return obj_from_bm(name, bm), face_part, attrs


def remesh(ob, vox):
    md = ob.modifiers.new('vox', 'REMESH')
    md.mode = 'VOXEL'
    md.voxel_size = vox
    md.adaptivity = 0.0
    md.use_smooth_shade = True
    apply_mods(ob)


def damage_p(ob, rng, vox, level):
    """道具版几何磨损（同 ps_wear.damage，按强度缩放、不开裂）"""
    remesh(ob, vox)
    if level > 0:
        me = ob.data
        co, nr = arrays(me)
        lo, hi = co.min(0), co.max(0)
        sz = (hi - lo) / g
        area = 2 * (sz[0] * sz[1] + sz[1] * sz[2] + sz[0] * sz[2])
        flat = np.abs(nr).max(1) > 0.95
        edge = (np.abs(nr).max(1) < 0.9) & (np.abs(nr).max(1) > 0.62)
        idx_f, idx_e, idx_a = np.flatnonzero(flat), np.flatnonzero(edge), np.arange(len(co))
        pick = lambda arr: int(arr[rng.randrange(len(arr))]) if len(arr) else int(rng.randrange(len(co)))
        for _ in range(int(area * 2.0 * level) + 1):
            i = pick(idx_f if len(idx_f) > 50 else idx_a)
            n = nr[i] / np.linalg.norm(nr[i])
            R = rng.uniform(0.025, 0.07) * g
            dep = R * rng.uniform(0.12, 0.25)
            if rng.random() < 0.45:
                press(co, nr, co[i].copy(), n, R * 0.7, dep, t=tangent(n, rng), along=rng.uniform(2.0, 3.0))
            else:
                press(co, nr, co[i].copy(), n, R, dep)
        for _ in range(int(area * 1.2 * level) + 1):
            i = pick(idx_e if len(idx_e) > 50 else idx_a)
            n = nr[i] / np.linalg.norm(nr[i])
            R = rng.uniform(0.025, 0.055) * g
            press(co, nr, co[i].copy(), n, R, R * rng.uniform(0.3, 0.5), side=-0.2)
        for _ in range(int(area * 0.8 * level)):
            i = pick(idx_f if len(idx_f) > 50 else idx_a)
            n = nr[i] / np.linalg.norm(nr[i])
            groove(co, nr, co[i].copy(), n, tangent(n, rng), rng.uniform(0.1, 0.35) * g,
                   rng.uniform(0.006, 0.011) * g, rng.uniform(0.003, 0.006) * g)
        write(me, co)
        remesh(ob, vox * 0.8)              # 消掉压坑造成的自相交
    for p in ob.data.polygons:
        p.use_smooth = True
    return ob


def transfer_attrs(hi, src, face_part, attrs):
    """高模每个顶点取离它最近的原部件的属性，写进颜色属性 PCol（颜色 + 发光）/ PMat（磨损、木纹、金属、裸木）"""
    sm = src.data
    tree = BVHTree.FromPolygons([v.co for v in sm.vertices], [list(p.vertices) for p in sm.polygons])
    co, _ = arrays(hi.data)
    n = len(co)
    pcol = np.zeros((n, 4), np.float32)
    pmat = np.zeros((n, 4), np.float32)
    table_c = np.array([list(a[0]) + [a[1]] for a in attrs], np.float32)
    table_m = np.array([a[2] for a in attrs], np.float32)
    fp = np.array(face_part)
    idx = np.empty(n, np.int64)
    for i in range(n):
        r = tree.find_nearest(Vector(co[i]))
        idx[i] = fp[r[2]] if r[2] is not None else 0
    pcol[:] = table_c[idx]
    pmat[:] = table_m[idx]
    return co, pcol, pmat, idx


def set_attrs(hi, pcol, pmat):
    me = hi.data
    for nm, arr in (('PCol', pcol), ('PMat', pmat)):
        a = me.color_attributes.get(nm) or me.color_attributes.new(nm, 'FLOAT_COLOR', 'POINT')
        a.data.foreach_set('color', arr.ravel())


def sun_pattern(co, pcol):
    """旗面太阳纹（暖金，手绘感：边缘一圈深一点的描线，光芒长短不一）"""
    c = np.array([0.4 * g, 0.0, 0.0])
    d = co - c
    r = np.sqrt(d[:, 0] ** 2 + d[:, 2] ** 2) / g
    ang = np.arctan2(d[:, 2], d[:, 0])
    face = np.abs(d[:, 1]) < 0.2 * g
    gold, dark = np.array(lin('d9a441')), np.array(lin('a86f25'))
    disk = face & (r < 0.1)
    ring = face & (r >= 0.1) & (r < 0.118)
    k = 11
    ray_len = 0.19 + 0.035 * np.cos(ang * k * 0.5 + 1.3)
    frac = np.abs(((ang / (2 * np.pi) * k) % 1.0) - 0.5) * 2      # 0 在光芒中线
    ray = face & (r > 0.13) & (r < ray_len) & (frac < (1 - (r - 0.13) / (ray_len - 0.13 + 1e-6)) * 0.55)
    pcol[disk, :3] = gold
    pcol[ray, :3] = gold
    pcol[ring, :3] = dark


def flame_pattern(co, pcol, pmat):
    """火丸：红漆球上画一圈向上窜的火舌（发光）"""
    d = co / np.linalg.norm(co, axis=1, keepdims=True)
    th = np.arctan2(d[:, 1], d[:, 0])
    nz = np.array([noise.noise(Vector(p) * 3.0) for p in d])
    tongue = 0.5 + 0.5 * np.sin(5 * th + 2.5 * nz)
    h = -0.45 + 0.9 * tongue
    fl = d[:, 2] < h
    core = d[:, 2] < h - 0.3
    pcol[fl, :3] = np.array(lin('e2702a'))
    pcol[core, :3] = np.array(lin('f0aa3c'))
    pcol[fl, 3] = 1.0
    pmat[fl, 0] = 0.0


def ice_shade(co, pcol):
    nz = np.array([noise.noise(Vector(p) / g * 4.0) for p in co])
    t = np.clip(nz * 0.8 + 0.5, 0, 1)[:, None]
    pcol[:, :3] = (1 - t) * np.array(lin('a9d0e3')) + t * np.array(lin('e6f3f8'))


# ───────────────────────── 烘焙材质 ─────────────────────────
class NB:
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

    def noise(self, vec, detail=4.0, rough=0.55, s=(1, 1, 1)):
        mp = self.node('ShaderNodeMapping')
        mp.inputs['Scale'].default_value = s
        self.link(vec, mp.inputs['Vector'])
        nz = self.node('ShaderNodeTexNoise')
        nz.inputs['Scale'].default_value = 1.0
        nz.inputs['Detail'].default_value = detail
        nz.inputs['Roughness'].default_value = rough
        self.link(mp.outputs['Vector'], nz.inputs['Vector'])
        return nz.outputs['Fac']

    def mix(self, fac, a, b):
        m = self.node('ShaderNodeMix', data_type='RGBA', blend_type='MIX')
        if isinstance(fac, (int, float)):
            m.inputs['Factor'].default_value = fac
        else:
            self.link(fac, m.inputs['Factor'])
        for sock, v in ((m.inputs[6], a), (m.inputs[7], b)):
            if isinstance(v, tuple):
                sock.default_value = v
            else:
                self.link(v, sock)
        return m.outputs[2]


def props_material():
    m = bpy.data.materials.new('props_bake')
    m.use_nodes = True
    b = NB(m)
    b.n.clear()
    out = b.node('ShaderNodeOutputMaterial')
    obj = b.node('ShaderNodeTexCoord').outputs['Object']
    inv = 1.0 / g
    pc = b.node('ShaderNodeAttribute', attribute_type='GEOMETRY', attribute_name='PCol')
    pm = b.node('ShaderNodeAttribute', attribute_type='GEOMETRY', attribute_name='PMat')
    sep = b.node('ShaderNodeSeparateColor')
    b.link(pm.outputs['Color'], sep.inputs['Color'])
    wearOK, grainV, metal = sep.outputs[0], sep.outputs[1], sep.outputs[2]
    bare = pm.outputs['Alpha']
    geo = b.node('ShaderNodeNewGeometry')
    bev = b.node('ShaderNodeBevel', samples=8)
    bev.inputs['Radius'].default_value = 0.05 * g
    dot = b.node('ShaderNodeVectorMath', operation='DOT_PRODUCT')
    b.link(bev.outputs['Normal'], dot.inputs[0])
    b.link(geo.outputs['True Normal'], dot.inputs[1])
    edge = b.math('MULTIPLY', b.math('SUBTRACT', 1.0, dot.outputs['Value']), 9.0, clamp=True)
    bevL = b.node('ShaderNodeBevel', samples=8)
    bevL.inputs['Radius'].default_value = 0.025 * g
    dotL = b.node('ShaderNodeVectorMath', operation='DOT_PRODUCT')
    b.link(bevL.outputs['Normal'], dotL.inputs[0])
    b.link(geo.outputs['True Normal'], dotL.inputs[1])
    lineF = b.maprange(b.math('SUBTRACT', 1.0, dotL.outputs['Value']), 0.035, 0.11)
    ao = b.node('ShaderNodeAmbientOcclusion', samples=24, only_local=True)
    ao.inputs['Distance'].default_value = 0.35 * g
    grain = b.noise(obj, 6.0, 0.6, (22 * inv, 22 * inv, 1.4 * inv))
    stroke = b.noise(obj, 2.0, 0.45, (26 * inv, 26 * inv, 3 * inv))
    big = b.noise(obj, 4.0, 0.55, (7 * inv, 7 * inv, 7 * inv))
    patch = b.noise(obj, 3.0, 0.55, (4.5 * inv, 4.5 * inv, 4.5 * inv))
    scuffN = b.noise(obj, 8.0, 0.62, (3 * inv, 3 * inv, 3 * inv))
    dentN = b.node('ShaderNodeTexVoronoi', feature='SMOOTH_F1')
    dentN.inputs['Scale'].default_value = 9 * inv
    b.link(obj, dentN.inputs['Vector'])
    dents = dentN.outputs['Distance']
    # 木头颜色（裸木和掉漆处露出来的）
    ramp = b.node('ShaderNodeValToRGB')
    ramp.color_ramp.elements[0].position = 0.35
    ramp.color_ramp.elements[0].color = tuple(lin('8a6440')) + (1,)
    ramp.color_ramp.elements[1].position = 0.68
    ramp.color_ramp.elements[1].color = tuple(lin('c9a273')) + (1,)
    b.link(grain, ramp.inputs['Fac'])
    wood = ramp.outputs['Color']
    # 裸木：木纹色 × 部件染色（以 d2b48c 为中性，染色深就整体深）
    tint = b.node('ShaderNodeMix', data_type='RGBA', blend_type='MULTIPLY')
    tint.inputs['Factor'].default_value = 1.0
    b.link(wood, tint.inputs[6])
    b.link(pc.outputs['Color'], tint.inputs[7])
    bareCol = b.math('MULTIPLY', 1.0, 1.0)  # 占位，下面用颜色混合
    bareC = b.node('ShaderNodeMix', data_type='RGBA', blend_type='MULTIPLY')
    bareC.inputs['Factor'].default_value = 1.0
    b.link(tint.outputs[2], bareC.inputs[6])
    bareC.inputs[7].default_value = (1.55, 1.55, 1.55, 1)
    # 漆面：漆色 × 笔触明暗 × 一点木纹透出来
    brush = b.maprange(stroke, 0.3, 0.7, 0.4, 0.6, smooth=False)
    shade = b.math('ADD', 0.8, b.math('MULTIPLY', brush, 0.4))
    shade = b.math('MULTIPLY', shade, b.math('ADD', 1.0, b.math('MULTIPLY', b.math('SUBTRACT', grain, 0.5), b.math('MULTIPLY', grainV, 0.5))))
    paintC = b.node('ShaderNodeMix', data_type='RGBA', blend_type='MULTIPLY')
    paintC.inputs['Factor'].default_value = 1.0
    b.link(pc.outputs['Color'], paintC.inputs[6])
    sh = b.node('ShaderNodeCombineColor')
    for i in range(3):
        b.link(shade, sh.inputs[i])
    b.link(sh.outputs['Color'], paintC.inputs[7])
    # 掉漆：棱上的磨损斑 + 零星刮痕，只在允许磨损的部件
    edgeWear = b.math('MULTIPLY', edge, b.maprange(patch, 0.42, 0.62))
    wear = b.math('ADD', edgeWear, b.math('MULTIPLY', b.math('SUBTRACT', big, 0.5), 0.7))
    loss = b.maprange(wear, 0.5, 0.62)
    loss = b.math('MAXIMUM', loss, b.maprange(scuffN, 0.66, 0.71))
    loss = b.math('MULTIPLY', loss, wearOK)
    col = b.mix(loss, paintC.outputs[2], wood)
    col = b.mix(bare, col, bareC.outputs[2])
    # 金属：棱上磨亮
    col = b.mix(b.math('MULTIPLY', b.math('MULTIPLY', edge, metal), 0.45), col, (0.62, 0.6, 0.57, 1))
    # 普通部件棱上干扫提亮一点
    col = b.mix(b.math('MULTIPLY', edgeWear, 0.25), col, (0.9, 0.84, 0.72, 1))
    height = b.math('ADD', b.math('MULTIPLY', b.math('MULTIPLY', grain, grainV), 0.6), b.math('ADD', b.math('MULTIPLY', stroke, 0.2), b.math('MULTIPLY', dents, 0.3)))
    height = b.math('ADD', height, b.math('MULTIPLY', b.math('SUBTRACT', 1.0, loss), b.math('MULTIPLY', wearOK, 0.3)))
    bump = b.node('ShaderNodeBump')
    bump.inputs['Strength'].default_value = 1.0
    bump.inputs['Distance'].default_value = 0.0018
    b.link(height, bump.inputs['Height'])
    bsdf = b.node('ShaderNodeBsdfPrincipled')
    b.link(bump.outputs['Normal'], bsdf.inputs['Normal'])
    comb = b.node('ShaderNodeCombineColor')
    b.link(ao.outputs['AO'], comb.inputs[1])
    b.link(brush, comb.inputs[2])
    em = {}
    for k, src in (('albedo', col), ('mask', comb.outputs['Color']), ('line', lineF), ('emit', pc.outputs['Alpha'])):
        e = b.node('ShaderNodeEmission')
        b.link(src, e.inputs['Color'])
        em[k] = e.name
    w = b.node('ShaderNodeEmission')
    w.inputs['Color'].default_value = (1, 1, 1, 1)
    em['cover'] = w.name
    em['normal'] = bsdf.name
    m['_em'] = em
    m['_out'] = out.name
    return m


def route(m, which):
    t = m.node_tree
    out = t.nodes[m['_out']]
    for l in list(out.inputs['Surface'].links):
        t.links.remove(l)
    t.links.new(t.nodes[m['_em'][which]].outputs[0], out.inputs['Surface'])


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


BAKE_STATS = {}


def island_mask(lows):
    """把所有低模的 UV 三角形画成岛遮罩（像素中心落在三角形里算岛内），用来查漏烘"""
    m = np.zeros((RES, RES), bool)
    for o in lows:
        me = o.data
        uv = me.uv_layers.active.data
        for p in me.polygons:
            li = list(p.loop_indices)
            for k in range(1, len(li) - 1):
                tri = np.array([uv[li[0]].uv[:], uv[li[k]].uv[:], uv[li[k + 1]].uv[:]]) * RES
                x0, y0 = np.floor(tri.min(0)).astype(int)
                x1, y1 = np.ceil(tri.max(0)).astype(int)
                x0, y0 = max(x0, 0), max(y0, 0)
                x1, y1 = min(x1, RES - 1), min(y1, RES - 1)
                if x1 < x0 or y1 < y0:
                    continue
                xs, ys = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
                a, b, c = tri
                d = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
                if abs(d) < 1e-12:
                    continue
                l1 = ((b[1] - c[1]) * (xs - c[0]) + (c[0] - b[0]) * (ys - c[1])) / d
                l2 = ((c[1] - a[1]) * (xs - c[0]) + (a[0] - c[0]) * (ys - c[1])) / d
                inside = (l1 >= 0) & (l2 >= 0) & (l1 + l2 <= 1)
                m[y0:y1 + 1, x0:x1 + 1] |= inside
    return m


def bake(pairs):
    sc = bpy.context.scene
    lows = [l for l, h in pairs]
    for i, (l, h) in enumerate(pairs):
        loc = ((i % 6) * 8 * g, (i // 6) * 8 * g, 0)
        l.location = loc
        h.location = loc
    unwrap_and_pack(lows)
    mat = props_material()
    for l, h in pairs:
        h.data.materials.clear()
        h.data.materials.append(mat)
    tgt = bpy.data.materials.new('target_props')
    tgt.use_nodes = True
    tn = tgt.node_tree.nodes.new('ShaderNodeTexImage')
    tgt.node_tree.nodes.active = tn
    for l in lows:
        l.data.materials.clear()
        l.data.materials.append(tgt)
    passes = (('cover', 'Non-Color'), ('albedo', 'sRGB'), ('emit', 'Non-Color'), ('mask', 'Non-Color'), ('normal', 'Non-Color'), ('line', 'Non-Color'))
    imgs = {}
    for which, cs in passes:
        im = bpy.data.images.new(f'{PREFIX}_{which}', RES, RES, alpha=True, float_buffer=False)
        im.generated_color = (0, 0, 0, 0)
        im.colorspace_settings.name = cs
        imgs[which] = im
    sc.render.bake.margin = 0
    sc.render.bake.use_clear = False
    for which, _ in passes:
        route(mat, which)
        tn.image = imgs[which]
        sc.cycles.samples = {'mask': 40, 'line': 12, 'normal': 6}.get(which, 2)
        for l, h in pairs:
            for o in bpy.data.objects:
                o.hide_render = o not in (l, h)
            bpy.ops.object.select_all(action='DESELECT')
            h.select_set(True)
            l.select_set(True)
            bpy.context.view_layer.objects.active = l
            kw = dict(use_selected_to_active=True, cage_extrusion=0.06 * g, max_ray_distance=0.18 * g, margin=0, use_clear=False)
            if which == 'normal':
                bpy.ops.object.bake(type='NORMAL', normal_space='TANGENT', **kw)
            else:
                bpy.ops.object.bake(type='EMIT', **kw)
        print('烘焙完成', which, flush=True)
    px = {w: np.array(imgs[w].pixels[:], dtype=np.float32).reshape(RES, RES, 4) for w, _ in passes}
    cover = px['cover'][..., 0] > 0.5
    isl = island_mask(lows)
    miss = float((isl & ~cover).sum()) / max(1, int(isl.sum()))
    print('图集覆盖率', round(float(cover.mean()), 3), 'UV 岛内漏烘比例', round(miss * 100, 3), '%', flush=True)
    BAKE_STATS['miss'] = miss
    BAKE_STATS['cover'] = float(cover.mean())
    for w in ('albedo', 'emit', 'mask', 'normal', 'line'):
        px[w] = dilate(px[w], cover)
    px['albedo'][..., 3] = px['emit'][..., 0]
    px['mask'][..., 0] = 0.0
    px['mask'][..., 3] = px['line'][..., 0]
    px['normal'][..., 3] = 1.0
    for w in ('albedo', 'mask', 'normal'):
        out = bpy.data.images.new(f'{PREFIX}_{w}_out', RES, RES, alpha=True, float_buffer=False)
        out.colorspace_settings.name = 'sRGB' if w == 'albedo' else 'Non-Color'
        out.alpha_mode = 'CHANNEL_PACKED'
        out.pixels[:] = px[w].ravel()
        pth = os.path.join(OUT, 'tex', f'{PREFIX}_{w}.png')
        out.filepath_raw = pth
        out.file_format = 'PNG'
        out.save()
        print('存盘', pth, flush=True)
    for o in bpy.data.objects:
        o.hide_render = False


# ───────────────────────── 检查图分块（Eevee） ─────────────────────────
def preview_material():
    m = bpy.data.materials.new('preview')
    m.use_nodes = True
    b = NB(m)
    b.n.clear()
    out = b.node('ShaderNodeOutputMaterial')
    uv = b.node('ShaderNodeUVMap')
    def img(name, cs):
        n = b.node('ShaderNodeTexImage')
        im = bpy.data.images.load(os.path.join(OUT, 'tex', name), check_existing=True)
        im.colorspace_settings.name = cs
        im.alpha_mode = 'CHANNEL_PACKED'
        n.image = im
        b.link(uv.outputs['UV'], n.inputs['Vector'])
        return n
    a = img(f'{PREFIX}_albedo.png', 'sRGB')
    mk = img(f'{PREFIX}_mask.png', 'Non-Color')
    nm = img(f'{PREFIX}_normal.png', 'Non-Color')
    sep = b.node('ShaderNodeSeparateColor')
    b.link(mk.outputs['Color'], sep.inputs['Color'])
    aoMix = b.mix(0.75, (1, 1, 1, 1), (1, 1, 1, 1))
    b.link(sep.outputs[1], aoMix.node.inputs[7])
    col = b.node('ShaderNodeMix', data_type='RGBA', blend_type='MULTIPLY')
    col.inputs['Factor'].default_value = 1.0
    b.link(a.outputs['Color'], col.inputs[6])
    b.link(aoMix, col.inputs[7])
    lineC = b.node('ShaderNodeMix', data_type='RGBA', blend_type='MULTIPLY')
    lineC.inputs['Factor'].default_value = 1.0
    b.link(col.outputs[2], lineC.inputs[6])
    lineC.inputs[7].default_value = (0.36, 0.36, 0.36, 1)
    final = b.node('ShaderNodeMix', data_type='RGBA', blend_type='MIX')
    b.link(b.math('MULTIPLY', mk.outputs['Alpha'], 0.85), final.inputs['Factor'])
    b.link(col.outputs[2], final.inputs[6])
    b.link(lineC.outputs[2], final.inputs[7])
    nmap = b.node('ShaderNodeNormalMap')
    b.link(nm.outputs['Color'], nmap.inputs['Color'])
    bsdf = b.node('ShaderNodeBsdfPrincipled')
    bsdf.inputs['Roughness'].default_value = 0.75
    b.link(final.outputs[2], bsdf.inputs['Base Color'])
    b.link(nmap.outputs['Normal'], bsdf.inputs['Normal'])
    b.link(a.outputs['Color'], bsdf.inputs['Emission Color'])
    b.link(b.math('MULTIPLY', a.outputs['Alpha'], 2.5), bsdf.inputs['Emission Strength'])
    b.link(bsdf.outputs[0], out.inputs['Surface'])
    return m


def clay_material():
    m = bpy.data.materials.new('clay')
    m.use_nodes = True
    m.use_backface_culling = False
    b = NB(m)
    b.n.clear()
    out = b.node('ShaderNodeOutputMaterial')
    geo = b.node('ShaderNodeNewGeometry')
    d = b.node('ShaderNodeBsdfDiffuse')
    d.inputs['Color'].default_value = (0.8, 0.8, 0.8, 1)
    e = b.node('ShaderNodeEmission')
    e.inputs['Color'].default_value = (1, 0, 0, 1)
    e.inputs['Strength'].default_value = 1.0
    mx = b.node('ShaderNodeMixShader')
    b.link(geo.outputs['Backfacing'], mx.inputs['Fac'])
    b.link(d.outputs[0], mx.inputs[1])
    b.link(e.outputs[0], mx.inputs[2])
    b.link(mx.outputs[0], out.inputs['Surface'])
    return m


def render_tiles(objs):
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_EEVEE'
    sc.render.resolution_x = sc.render.resolution_y = 320
    sc.render.film_transparent = False
    try:
        sc.eevee.taa_render_samples = 24
    except Exception:
        pass
    world = bpy.data.worlds.new('w')
    world.use_nodes = True
    world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.62, 0.6, 0.57, 1)
    world.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.9
    sc.world = world
    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN'))
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(50), 0, math.radians(35))
    link(sun)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam'))
    cam.data.lens_unit = 'FOV'
    cam.data.angle = math.radians(30)
    cam.data.clip_start = 0.001
    link(cam)
    sc.camera = cam
    prev, clay = preview_material(), clay_material()
    tiles = os.path.join(SHOTS, 'tiles')
    os.makedirs(tiles, exist_ok=True)
    for o in objs:
        o.location = (0, 0, 0)
    for o in objs:
        for x in bpy.data.objects:
            if x.type == 'MESH':
                x.hide_render = x is not o
        bpy.context.view_layer.update()
        bb = [o.matrix_world @ Vector(c) for c in o.bound_box]
        ctr = sum(bb, Vector()) / 8
        rad = max((p - ctr).length for p in bb)
        dist = rad / math.sin(math.radians(15)) * 1.05
        for v, (dirv, mat) in enumerate(((Vector((0.6, 1.0, 0.55)), prev), (Vector((-0.6, -1.0, 0.55)), prev), (Vector((0.6, 1.0, 0.55)), clay), (Vector((-0.6, -1.0, 0.55)), clay))):
            o.data.materials.clear()
            o.data.materials.append(mat)
            cam.location = ctr + dirv.normalized() * dist
            cam.rotation_euler = (ctr - cam.location).to_track_quat('-Z', 'Y').to_euler()
            sc.render.filepath = os.path.join(tiles, f'{o.name}_{v}.png')
            bpy.ops.render.render(write_still=True)
        o.hide_render = True
    print('检查图分块完成', tiles, flush=True)


def export(objs):
    for o in objs:
        o.location = (0, 0, 0)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    p = os.path.join(OUT, PREFIX + '.fbx')
    bpy.ops.export_scene.fbx(filepath=p, use_selection=True, apply_scale_options='FBX_SCALE_UNITS',
                             axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                             mesh_smooth_type='OFF', use_tspace=True, add_leaf_bones=False,
                             path_mode='STRIP', embed_textures=False, use_custom_props=False)
    print('导出', p, flush=True)


LOW_FROM_SRC = {'K_Flag'}


def src_low(src, name):
    """用合并后的原始部件当低模（三角化 + 加权法线）"""
    src.name = name
    md = src.modifiers.new('tri', 'TRIANGULATE')
    apply_mods(src)
    wn = src.modifiers.new('wn', 'WEIGHTED_NORMAL')
    wn.weight = 50
    apply_mods(src)
    for p in src.data.polygons:
        p.use_smooth = True
    src.data.name = name
    for k in ('pc', 'pm', 'pe'):
        if k in src:
            del src[k]
    return src


def main():
    reset()
    setup_cycles()
    rng = random.Random(1125)
    report, pairs, extra = [], [], {}
    for name, fn, vox, level, target in ITEMS:
        res = fn(rng)
        if isinstance(res, tuple):
            res, extra[name] = res
        src, face_part, attrs = join_parts(res, name + '_SRC')
        hi = src.copy()
        hi.data = src.data.copy()
        hi.name = name + '_HI'
        link(hi)
        damage_p(hi, rng, vox * g, level)
        co, pcol, pmat, idx = transfer_attrs(hi, src, face_part, attrs)
        if name == 'K_Flag':
            sun_pattern(co, pcol)
        elif name == 'B_Fire':
            flame_pattern(co, pcol, pmat)
        elif name == 'B_Ice':
            ice_shade(co, pcol)
        set_attrs(hi, pcol, pmat)
        if name in LOW_FROM_SRC:
            lo = src_low(src, name)
        else:
            bpy.data.objects.remove(src, do_unlink=True)
            lo = make_low(hi, name, target)
        tris, nonman = mesh_report(lo)
        report.append(f'{name:10s} 低模 {tris:5d}  非流形边 {nonman}  高模面 {len(hi.data.polygons)}')
        print(report[-1], flush=True)
        pairs.append((lo, hi))
    bake(pairs)
    lows = [l for l, h in pairs]
    lods = [make_lod(l) for l in lows]
    for lod in lods:
        t1, n1 = mesh_report(lod)
        report.append(f'{lod.name:16s} {t1:5d}  非流形边 {n1}')
    for l, h in pairs:
        bpy.data.objects.remove(h, do_unlink=True)
    for k, v in extra.items():
        if v and isinstance(v[0], (list, tuple)):
            # 多个锚点（如钟架三钩、秤盘两端）
            pts_str = ', '.join(str(tuple(round(c, 5) for c in p)) for p in v)
            report.append(f'{k} 锚点列表（米）: [{pts_str}]')
        else:
            report.append(f'{k} 额外锚点（米）: {tuple(round(x, 5) for x in v)}')
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, PREFIX + '.blend'))
    export(lows + lods)
    open(os.path.join(OUT, 'report.txt'), 'w').write('\n'.join(report) + '\n')
    render_tiles(lows)


main()

# 弹弓攻城：几何磨损（高模）→ 减面（低模 + LOD1）→ 高模烘到低模
# 损伤都做在几何体上：凹痕 / 压坑、棱上豁口、压塌的角、划痕、顺纹裂缝；石块再加风化坑洼。
# 低模保留大的坑和豁口（轮廓上看得见），细的部分烘进法线贴图。
import math
import bpy
import numpy as np
from mathutils import Vector, noise
from ps_lib import G, apply_mods

g = G
VOX = 0.0055 * g           # 体素重建精度（0.28 mm）

# 低模目标三角面数（LOD0）；LOD1 = 30%
TARGET = {
    'W_Cube': 1800, 'W_Slab': 2600, 'W_Beam': 3200, 'W_Pillar': 2600, 'W_Column': 3000, 'W_Arch': 3000,
    'W_Window': 2200, 'W_Battlement': 2600, 'W_Gate': 3800, 'W_Roof': 3400, 'W_Cone': 3400,
    'S_Cube': 1800, 'S_Slab': 2400, 'S_Pillar': 2400, 'S_Arch': 3000, 'S_Battlement': 2600, 'S_Wall': 3600, 'S_Shield': 4400,
}


def arrays(me):
    n = len(me.vertices)
    co = np.empty(n * 3, np.float32)
    me.vertices.foreach_get('co', co)
    nr = np.empty(n * 3, np.float32)
    me.vertex_normals.foreach_get('vector', nr)
    return co.reshape(-1, 3).astype(np.float64), nr.reshape(-1, 3).astype(np.float64)


def write(me, co):
    me.vertices.foreach_set('co', co.astype(np.float32).ravel())
    me.update()


def tangent(n, r):
    """和法线 n 垂直的随机方向"""
    a = np.array([r.uniform(-1, 1), r.uniform(-1, 1), r.uniform(-1, 1)])
    t = a - n * a.dot(n)
    l = np.linalg.norm(t)
    return t / l if l > 1e-6 else np.array([1.0, 0, 0])


def press(co, nr, c, n, R, depth, t=None, along=1.0, side=0.25):
    """在 c 处沿 -n 压一个坑。t 给定时是长条形（沿 t 方向半径 R*along）。只影响朝向相近的面（不压穿到背面）"""
    d = co - c
    if t is None:
        r2 = np.einsum('ij,ij->i', d, d) / (R * R)
    else:
        a = d @ t
        perp = d - np.outer(a, t)
        r2 = (a / (R * along)) ** 2 + np.einsum('ij,ij->i', perp, perp) / (R * R)
    m = (r2 < 1.0) & ((nr @ n) > side)
    w = (1.0 - r2[m]) ** 2
    co[m] -= np.outer(w * depth, n)


def groove(co, nr, c, n, t, L, wdt, depth):
    """沿 t 方向的细槽（划痕 / 裂缝），截面是圆底"""
    d = co - c
    a = np.clip(d @ t, 0.0, L)
    q = d - np.outer(a, t)
    dist = np.sqrt(np.einsum('ij,ij->i', q, q))
    m = (dist < wdt) & ((nr @ n) > 0.4)
    # 两端收尖
    taper = np.minimum(1.0, np.minimum(a[m], L - a[m]) / (0.25 * L + 1e-9) + 0.15)
    co[m] -= np.outer((1.0 - (dist[m] / wdt) ** 2) * depth * taper, n)


def damage(ob, rng, stone, grain):
    """体素重建成高模，然后在几何上做磨损。grain = 木纹方向（0=X, 2=Z）"""
    md = ob.modifiers.new('vox', 'REMESH')
    md.mode = 'VOXEL'
    md.voxel_size = VOX
    md.adaptivity = 0.0
    md.use_smooth_shade = True
    apply_mods(ob)
    me = ob.data
    co, nr = arrays(me)
    lo, hi = co.min(0), co.max(0)
    sz = (hi - lo) / g
    area = 2 * (sz[0] * sz[1] + sz[1] * sz[2] + sz[0] * sz[2])         # 以“格²”计的表面积
    flat = np.abs(nr).max(1) > 0.95
    edge = (np.abs(nr).max(1) < 0.9) & (np.abs(nr).max(1) > 0.62)
    corner = np.abs(nr).max(1) < 0.72
    idx_f, idx_e, idx_c = np.flatnonzero(flat), np.flatnonzero(edge), np.flatnonzero(corner)
    pick = lambda arr: int(arr[rng.randrange(len(arr))]) if len(arr) else int(rng.randrange(len(co)))
    k = 1.35 if stone else 1.0

    # 1) 面上的凹痕 / 压坑（圆的、长条的）
    for _ in range(int(area * 2.4 * k) + 3):
        i = pick(idx_f)
        n = nr[i] / np.linalg.norm(nr[i])
        R = rng.uniform(0.035, 0.1) * g
        dep = R * rng.uniform(0.12, 0.28)
        if rng.random() < 0.45:
            press(co, nr, co[i].copy(), n, R * 0.7, dep, t=tangent(n, rng), along=rng.uniform(2.0, 3.5))
        else:
            press(co, nr, co[i].copy(), n, R, dep)
    # 2) 棱上的豁口（磕出来的缺口，轮廓上看得见）
    for _ in range(int(area * 1.5 * k) + 3):
        i = pick(idx_e)
        n = nr[i] / np.linalg.norm(nr[i])
        R = rng.uniform(0.03, 0.075) * g
        press(co, nr, co[i].copy(), n, R, R * rng.uniform(0.35, 0.6), side=-0.2)
    # 3) 压塌的角
    for _ in range(rng.randint(1, 3)):
        i = pick(idx_c)
        n = nr[i] / np.linalg.norm(nr[i])
        R = rng.uniform(0.08, 0.15) * g
        press(co, nr, co[i].copy(), n, R, R * rng.uniform(0.25, 0.4), side=-0.3)
    # 4) 划痕
    for _ in range(int(area * 1.2) + 1):
        i = pick(idx_f)
        n = nr[i] / np.linalg.norm(nr[i])
        groove(co, nr, co[i].copy(), n, tangent(n, rng), rng.uniform(0.15, 0.55) * g,
               rng.uniform(0.007, 0.013) * g, rng.uniform(0.003, 0.007) * g)
    if not stone:
        # 5) 顺纹裂缝：从一端的棱开始沿木纹方向裂进去
        ax = np.zeros(3)
        ax[grain] = 1.0
        side_f = np.flatnonzero(flat & (np.abs(nr[:, grain]) < 0.2))
        for _ in range(rng.randint(1, 3)):
            if not len(side_f):
                break
            i = pick(side_f)
            c = co[i].copy()
            end = rng.choice([0, 1])
            c[grain] = (lo[grain] if end == 0 else hi[grain])
            t = ax * (1 if end == 0 else -1)
            n = nr[i] / np.linalg.norm(nr[i])
            groove(co, nr, c, n, t, rng.uniform(0.2, 0.5) * g, rng.uniform(0.009, 0.014) * g, rng.uniform(0.02, 0.035) * g)
    else:
        # 5') 石块风化：成片的浅坑洼 + 密集小麻点
        seed = Vector((rng.uniform(0, 99), rng.uniform(0, 99), rng.uniform(0, 99)))
        e = np.array([noise.noise(Vector(p) / g * 2.2 + seed) for p in co])
        co -= nr * (np.clip(e - 0.1, 0, None) * 0.05 * g)[:, None]
        for _ in range(int(area * 10)):
            i = pick(idx_f)
            n = nr[i] / np.linalg.norm(nr[i])
            R = rng.uniform(0.012, 0.03) * g
            press(co, nr, co[i].copy(), n, R, R * 0.4)
    write(me, co)
    # 再做一次体素重建：压坑时薄处可能把表面顶穿到另一侧（自相交，从外面能看到里面的背面），
    # 体素只认里外，穿插的部分自动消掉，得到干净封闭的表面。体素更细，保住细划痕。
    md = ob.modifiers.new('vox2', 'REMESH')
    md.mode = 'VOXEL'
    md.voxel_size = VOX * 0.75
    md.adaptivity = 0.0
    md.use_smooth_shade = True
    apply_mods(ob)
    for p in ob.data.polygons:
        p.use_smooth = True
    return ob


def make_low(high, name, target):
    low = high.copy()
    low.data = high.data.copy()
    low.name = name
    bpy.context.scene.collection.objects.link(low)
    tris = sum(len(p.vertices) - 2 for p in low.data.polygons)
    md = low.modifiers.new('dec', 'DECIMATE')
    md.decimate_type = 'COLLAPSE'
    md.ratio = min(1.0, target / max(1, tris))
    md.use_collapse_triangulate = True
    apply_mods(low)
    fix_flips(low, high)
    wn = low.modifiers.new('wn', 'WEIGHTED_NORMAL')
    wn.weight = 50
    apply_mods(low)
    low.data.name = name
    return low


def fix_flips(low, high):
    """减面后个别小三角形会翻过去（背面朝外，游戏里是针眼小洞）：每个面对照高模最近处的朝向，反了就翻回来"""
    import bmesh
    from mathutils.bvhtree import BVHTree
    dg = bpy.context.evaluated_depsgraph_get()
    tree = BVHTree.FromObject(high, dg)
    bm = bmesh.new()
    bm.from_mesh(low.data)
    bad = []
    for f in bm.faces:
        hit = tree.find_nearest(f.calc_center_median())
        if hit[1] is not None and f.normal.dot(hit[1]) < -0.2:
            bad.append(f)
    if bad:
        bmesh.ops.reverse_faces(bm, faces=bad)
    bm.to_mesh(low.data)
    bm.free()
    print('翻回', low.name, len(bad))


def make_lod(low, ratio=0.3):
    lod = low.copy()
    lod.data = low.data.copy()
    lod.name = low.name + '_LOD1'
    bpy.context.scene.collection.objects.link(lod)
    md = lod.modifiers.new('dec', 'DECIMATE')
    md.decimate_type = 'COLLAPSE'
    md.ratio = ratio
    md.use_collapse_triangulate = True
    apply_mods(lod)
    wn = lod.modifiers.new('wn', 'WEIGHTED_NORMAL')
    wn.weight = 50
    apply_mods(lod)
    lod.data.name = lod.name
    return lod


def dilate(px, cover, iters=16):
    """把烘焙结果向 UV 岛外面扩几圈（代替 Blender 的 margin，避免逐物体烘焙时互相覆盖）"""
    out = px.copy()
    filled = cover.copy()
    for _ in range(iters):
        acc = np.zeros_like(out)
        cnt = np.zeros(cover.shape, np.float32)
        for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1)):
            f = np.roll(np.roll(filled, dy, 0), dx, 1)
            acc += np.roll(np.roll(out, dy, 0), dx, 1) * f[..., None]
            cnt += f
        new = (~filled) & (cnt > 0)
        out[new] = acc[new] / cnt[new][:, None]
        filled |= new
    return out

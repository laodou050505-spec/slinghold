# 弹弓攻城：Blender 程序建模小库（旧木头玩具积木风）
# 约定：Blender Z 向上，原点在积木底面中心，单位米。G = 一个积木单位（玩家城 5 cm，敌城在 Unity 里整体放大）。
# 用法：blender -b -P ps_blocks.py -- <输出目录>
import bpy, bmesh, math, random
from mathutils import Vector, Matrix, noise

G = 0.05


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def link(ob):
    bpy.context.scene.collection.objects.link(ob)
    return ob


def obj_from_bm(name, bm):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    return link(bpy.data.objects.new(name, me))


def box(name, sx, sy, sz, cx=0.0, cy=0.0, z0=0.0):
    """长方体：尺寸 sx×sy×sz（米），底面中心在 (cx, cy, z0)"""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * sx + cx, v.co.y * sy + cy, (v.co.z + 0.5) * sz + z0))
    return obj_from_bm(name, bm)


def cylinder(name, r, h, verts=32, axis='Z', c=(0, 0, 0), r_top=None):
    """圆柱：axis=Z 时底面中心在 c；axis=Y 时轴沿 Y、中心在 c"""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=verts, radius1=r,
                          radius2=r if r_top is None else r_top, depth=h)
    if axis == 'Z':
        bmesh.ops.translate(bm, verts=bm.verts, vec=Vector((0, 0, h / 2)))
    elif axis == 'Y':
        bmesh.ops.rotate(bm, verts=bm.verts, cent=Vector(), matrix=Matrix.Rotation(math.pi / 2, 3, 'X'))
    elif axis == 'X':
        bmesh.ops.rotate(bm, verts=bm.verts, cent=Vector(), matrix=Matrix.Rotation(math.pi / 2, 3, 'Y'))
    bmesh.ops.translate(bm, verts=bm.verts, vec=Vector(c))
    return obj_from_bm(name, bm)


def ico(name, r, c, scale=(1, 1, 1), subdiv=1, rot=None):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=r)
    for v in bm.verts:
        v.co = Vector((v.co.x * scale[0], v.co.y * scale[1], v.co.z * scale[2]))
    if rot is not None:
        bmesh.ops.rotate(bm, verts=bm.verts, cent=Vector(), matrix=rot)
    bmesh.ops.translate(bm, verts=bm.verts, vec=Vector(c))
    return obj_from_bm(name, bm)


def prism(name, pts, depth, c=(0, 0, 0)):
    """把 XZ 平面上的多边形（逆时针）沿 Y 挤出 depth，居中在 y=c.y"""
    bm = bmesh.new()
    vs = [bm.verts.new((x + c[0], -depth / 2 + c[1], z + c[2])) for x, z in pts]
    f = bm.faces.new(vs)
    r = bmesh.ops.extrude_face_region(bm, geom=[f])
    nv = [e for e in r['geom'] if isinstance(e, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, verts=nv, vec=Vector((0, depth, 0)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return obj_from_bm(name, bm)


def apply_mods(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    old = ob.data
    ob.modifiers.clear()
    ob.data = me
    bpy.data.meshes.remove(old)
    return ob


def boolean(ob, cutters, op='DIFFERENCE'):
    """依次布尔，然后删掉切割体"""
    for c in cutters:
        md = ob.modifiers.new('b', 'BOOLEAN')
        md.operation = op
        md.solver = 'EXACT'
        md.object = c
        apply_mods(ob)
    for c in cutters:
        bpy.data.objects.remove(c, do_unlink=True)
    return ob


def join(parts, name):
    """合并多个物体（不做布尔，适合互相插着的部件：瓦片、门钉、屋面板）"""
    base = parts[0]
    bm = bmesh.new()
    for p in parts:
        tmp = bmesh.new()
        tmp.from_mesh(p.data)
        bmesh.ops.transform(tmp, verts=tmp.verts, matrix=p.matrix_world)
        me = bpy.data.meshes.new('t')
        tmp.to_mesh(me)
        tmp.free()
        bm.from_mesh(me)
        bpy.data.meshes.remove(me)
    for p in parts:
        bpy.data.objects.remove(p, do_unlink=True)
    return obj_from_bm(name, bm)


def chip_cutters(ob, rng, n, size, stone=False, corner_share=0.6, min_planes=2):
    """磕碰缺口的切割体：一部分放在凸角上（最容易磕掉），其余放在长棱上。
    只用外轮廓上的角 / 棱（靠近包围盒的 min_planes 个面），门洞、窗洞、凹槽里面不磕。"""
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    lo = Vector((min(v.co.x for v in bm.verts), min(v.co.y for v in bm.verts), min(v.co.z for v in bm.verts)))
    hi = Vector((max(v.co.x for v in bm.verts), max(v.co.y for v in bm.verts), max(v.co.z for v in bm.verts)))
    tol = 0.06 * G

    def outer(p):
        k = 0
        for i in range(3):
            if p[i] - lo[i] < tol or hi[i] - p[i] < tol:
                k += 1
        return k >= min_planes
    corners, edges = [], []
    for v in bm.verts:
        fs = v.link_faces
        if len(fs) >= 3:
            ns = [f.normal for f in fs]
            if max(a.angle(b) for a in ns for b in ns) > math.radians(60) and outer(v.co):
                nn = Vector()
                for f in fs:
                    nn += f.normal
                corners.append((v.co.copy(), nn.normalized()))
    for e in bm.edges:
        if len(e.link_faces) == 2:
            a = e.link_faces[0].normal.angle(e.link_faces[1].normal)
            if a > math.radians(60) and e.calc_length() > size * 2.0 and outer(e.verts[0].co) and outer(e.verts[1].co):
                edges.append((e.verts[0].co.copy(), e.verts[1].co.copy(),
                              (e.link_faces[0].normal + e.link_faces[1].normal).normalized()))
    bm.free()
    out = []
    for i in range(n):
        use_corner = corners and (not edges or rng.random() < corner_share)
        if use_corner:
            p, nrm = rng.choice(corners)
        elif edges:
            a, b, nrm = rng.choice(edges)
            p = a.lerp(b, rng.uniform(0.12, 0.88))
        else:
            break
        s = size * rng.uniform(0.6, 1.35)
        sc = (rng.uniform(0.7, 1.7), rng.uniform(0.7, 1.7), rng.uniform(0.45, 1.0))
        rot = Matrix.Rotation(rng.uniform(0, 6.28), 3, Vector((rng.random(), rng.random(), rng.random())).normalized())
        c = p + nrm * s * (0.4 if stone else 0.5)
        out.append(ico('chip', s, c, sc, subdiv=1 if stone else 0, rot=rot))
    return out


def cutter_box(sx, sy, sz, loc, rot=(0, 0, 0)):
    """可旋转的长方体切割体（中心在 loc）"""
    o = box('cut', sx, sy, sz, z0=-sz / 2)
    o.location = loc
    o.rotation_euler = rot
    bpy.context.view_layer.update()
    return o


def finish(ob, rng, bevel, seg=3, jitter=0.0, warp=0.0, angle=32):
    """磨旧的棱：每条硬棱一道宽窄不一的斜角（平切，像被磨掉 / 削掉），再轻轻倒圆；
    然后整体轻微歪斜 + 低频起伏（手工木块不是绝对方正的）。"""
    me = ob.data
    # 每条硬棱随机一个斜角宽度（权重 0.25～1）
    bm = bmesh.new()
    bm.from_mesh(me)
    lay = bm.edges.layers.float.get('bevel_weight_edge') or bm.edges.layers.float.new('bevel_weight_edge')
    for e in bm.edges:
        if len(e.link_faces) == 2 and e.link_faces[0].normal.angle(e.link_faces[1].normal) > math.radians(angle):
            e[lay] = rng.uniform(0.25, 1.0) if rng.random() < 0.85 else rng.uniform(0.05, 0.2)
        else:
            e[lay] = 0.0
    bm.to_mesh(me)
    bm.free()
    md = ob.modifiers.new('chamfer', 'BEVEL')
    md.width = bevel * 1.5
    md.segments = 1
    md.limit_method = 'WEIGHT'
    md.use_clamp_overlap = True
    apply_mods(ob)
    md = ob.modifiers.new('round', 'BEVEL')
    md.width = bevel * 0.25
    md.segments = max(2, seg - 1)
    md.limit_method = 'ANGLE'
    md.angle_limit = math.radians(20)
    md.use_clamp_overlap = True
    md.profile = 0.6
    apply_mods(ob)
    me = ob.data
    zs = [v.co.z for v in me.vertices]
    zmin, zmax = min(zs), max(zs)
    h = max(1e-4, zmax - zmin)
    sh = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), 0)) * warp
    seed = Vector((rng.uniform(0, 100), rng.uniform(0, 100), rng.uniform(0, 100)))
    for v in me.vertices:
        t = (v.co.z - zmin) / h
        v.co += sh * t
        if jitter > 0:
            q = v.co / G * 1.7 + seed
            v.co += Vector((noise.noise(q), noise.noise(q + Vector((31, 7, 3))), noise.noise(q + Vector((5, 53, 11))))) * jitter
    for p in me.polygons:
        p.use_smooth = True
    return ob


def mesh_report(ob):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    nonman = sum(1 for e in bm.edges if not e.is_manifold)
    tris = sum(len(f.verts) - 2 for f in bm.faces)
    bm.free()
    return tris, nonman

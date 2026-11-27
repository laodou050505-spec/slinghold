// 11-26 真实房间合成后用户同意的三处天空呈现改进：
//   ① 盒沿：天花板开口四周一圈原木框（贴天花板的边框 + 往上伸 14 cm 的内壁）——抬头看开口有厚度，像掀开的木盒盖，
//      而不是贴在天花板上的一张平面贴纸；
//   ② 日出光柱：结局天亮时，几道暖色光柱从开口斜照下来，落在倒塌的王城上，光柱里飘着浮尘；太阳光方向同时对齐光柱；
//   ③ 夜关别比真实房间暗太多（在 SkyDome.Times 里调）+ 烟花炸开时照亮一下积木（SkyDome.Flash）。
using System.Collections.Generic;
using UnityEngine;

namespace PaperSiege
{
    public static class LidRim
    {
        public const float Lip = 0.07f, LipT = 0.022f, WallH = 0.14f, WallT = 0.025f;

        /// <summary>开口（中心 center、天花板高 ceilY、宽 w、长 l）四周的原木框</summary>
        public static GameObject Build(Transform parent, Vector3 center, Vector3 fwd, float ceilY, float w, float l)
        {
            var root = new GameObject("LidRim");
            root.transform.SetParent(parent, false);
            var lib = BlockLibrary.I;
            var beam = lib ? lib.Meshes("W_Beam") : null;
            if (beam == null || !beam.lod1) return root;
            var mat = lib.Paint(Paint.Bare);
            var right = Vector3.Cross(Vector3.up, fwd);
            var c = new Vector3(center.x, ceilY, center.z);
            // 四条边：每条边的中点、沿边方向、朝外方向、边长
            // 四条边：前后两条横跨整个宽度并盖住四个转角，左右两条夹在中间、只到转角为止 —— 转角对接、不重叠
            // （11-27 用户截图：原来四条都伸到转角，两两重叠、共面闪烁，看着像穿模）
            var sides = new (Vector3 mid, Vector3 along, Vector3 outN, float len, bool full)[]
            {
                (c + fwd * (l / 2), right, fwd, w, true),
                (c - fwd * (l / 2), right, -fwd, w, true),
                (c + right * (w / 2), fwd, right, l, false),
                (c - right * (w / 2), fwd, -right, l, false),
            };
            const float bx = 3 * BlockLibrary.G, by = 0.4f * BlockLibrary.G, bz = 0.8f * BlockLibrary.G;   // W_Beam 尺寸（长沿 X、原点在底面）
            foreach (var (mid, along, outN, len, full) in sides)
            {
                // 内壁：竖着的木板，内表面和开口边齐平，从天花板往上伸
                var wall = Plank(root.transform, beam.lod1, mat, "Wall");
                wall.rotation = Quaternion.LookRotation(Vector3.up, -outN);         // 本地 Z = 上，本地 Y = 朝里（木板从原点往 +Y 长厚度）
                wall.position = mid + outN * WallT + Vector3.up * (WallH * 0.5f - 0.01f);
                wall.localScale = new Vector3((full ? len + 2 * WallT : len - 0.002f) / bx, WallT / by, (WallH - (full ? 0f : 0.004f)) / bz);
                // 边框：贴着天花板往外伸的一圈木条（比天花板略低一点点，盖住开口的切边）
                var lip = Plank(root.transform, beam.lod1, mat, "Lip");
                lip.rotation = Quaternion.LookRotation(outN, Vector3.down);           // 本地 Z = 朝外，本地 Y = 朝下（厚度往下长）
                lip.position = mid + outN * (Lip * 0.5f) + Vector3.up * 0.004f;
                lip.localScale = new Vector3((full ? len + 2 * Lip : len - 0.002f) / bx, (LipT - (full ? 0f : 0.002f)) / by, Lip / bz);   // 夹在中间的两条薄 2 mm，底面不和横跨的两条共面
            }
            return root;
        }

        static Transform Plank(Transform parent, Mesh mesh, Material mat, string name)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g.transform;
        }
    }

    /// <summary>结局天亮的光柱 + 浮尘。K = 当前强度（0～1），Dir = 光柱方向（太阳光跟着它）</summary>
    public class LightShafts : MonoBehaviour
    {
        public SkyDome sky;
        public float K { get; private set; }
        public Vector3 Dir { get; private set; } = Vector3.down;
        readonly List<(GameObject go, Material m, float phase)> shafts = new();
        ParticleSystem motes;
        bool built;

        void Update()
        {
            var d = Director.I;
            if (!d || !sky) return;
            float target = d.ending ? Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.3f, 0.85f, sky.dawn)) : 0f;
            if (target > 0 && !built) Build(d);
            if (target <= 0 && K <= 0.001f && built) Clear();
            K = Mathf.MoveTowards(K, target, Time.deltaTime * (target > K ? 0.45f : 0.8f));
            foreach (var (go, m, ph) in shafts)
            {
                if (!go) continue;
                go.SetActive(K > 0.001f);
                m.SetFloat("_Intensity", K * (0.85f + 0.15f * Mathf.Sin(Time.time * 0.6f + ph)));
            }
            if (motes) { var em = motes.emission; em.rateOverTime = 22f * K; }
        }

        void Clear()
        {
            foreach (var s in shafts) if (s.go) Destroy(s.go);
            shafts.Clear();
            if (motes) Destroy(motes.gameObject);
            built = false; K = 0;
        }

        void Build(Director d)
        {
            built = true;
            var A = d.arena;
            var f = A.forward; var r = Vector3.Cross(Vector3.up, f);
            // 11-27 用户：光束不能只在天窗尽头，要自然。原来太阳在前方、光往玩家这边斜，为了照到敌城只能把起点挤到天窗最远那头。
            // 现在：清晨的阳光从玩家身后偏左斜照进来（约 30° 斜度），光柱均匀铺满整片天窗、一起往前落，正好照在倒下的王城那一带；
            // 起点在盒沿里面（天花板以上 12 cm），看着是从天窗照进来的，不是凭空从天花板长出来。
            Dir = (f * 0.52f + r * 0.16f + Vector3.down).normalized;
            float ceil = A.ceilingHeight;
            var sh = Shader.Find("PaperSiege/LightShaft");
            if (!sh) return;
            var rng = new System.Random(7);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var half = sky.LidHalfSize;
            // 分层抖动：沿天窗长边 5 段、短边 3 段，每格一道；再加 3 道很宽很淡的“空气光”垫底
            // 只用天窗中段到远端（纵向 −0.3 ～ 0.95）：靠玩家那一头就在头顶上，光柱会贴着脸、整片冲白（11-27 第一版过曝）
            int nz = 4, nx = 3;
            for (int iz = 0; iz < nz; iz++)
                for (int ix = 0; ix < nx; ix++)
                {
                    if (rng.NextDouble() < 0.3) continue;   // 随机缺几道，不像栅栏
                    float u = ((ix + R(0.15f, 0.85f)) / nx) * 2 - 1, v = Mathf.Lerp(-0.3f, 0.95f, (iz + R(0.15f, 0.85f)) / nz);
                    var top = sky.LidPoint(u * (half.x - 0.15f), v * (half.y - 0.15f)) + Vector3.up * 0.12f;
                    AddShaft(top, (top.y + 0.02f) / -Dir.y, R(0.1f, 0.22f), R(0.38f, 0.55f), r, f, sh, R(0, 10), R(0, 6));
                }
            for (int i = 0; i < 2; i++)
            {
                var top = sky.LidPoint(R(-0.3f, 0.3f) * half.x, (0.15f + i * 0.45f) * half.y) + Vector3.up * 0.12f;
                AddShaft(top, (top.y + 0.02f) / -Dir.y, R(0.6f, 0.8f), 0.16f, r, f, sh, R(0, 10), R(0, 6));
            }
            // 浮尘：天窗下面、光柱经过的那片空间
            var c = sky.LidPoint(0, 0); c.y = ceil * 0.5f; c += Dir * 0.3f;
            motes = Fx.Motes(c, new Vector3(half.x * 1.6f, ceil - 0.4f, half.y * 1.6f), new Color(1f, 0.9f, 0.7f));
            motes.transform.SetParent(transform, true);
            motes.transform.rotation = Quaternion.LookRotation(f, Vector3.up);
        }

        void AddShaft(Vector3 org, float len, float width, float gain, Vector3 r, Vector3 f, Shader sh, float seed, float phase)
        {
            var go = new GameObject("LightShaft");
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(org, Quaternion.LookRotation(Dir, r));
            go.AddComponent<MeshFilter>().sharedMesh = ShaftMesh(len, width * 0.85f, width * 1.15f);
            var mr = go.AddComponent<MeshRenderer>();
            var m = new Material(sh);
            m.SetColor("_Color", Color.Lerp(new Color(1f, 0.82f, 0.55f), new Color(1f, 0.9f, 0.72f), Mathf.Repeat(seed, 1f)) * gain);
            m.SetFloat("_Seed", seed);
            m.SetFloat("_Intensity", 0);
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            shafts.Add((go, m, phase));
        }

        /// <summary>两片交叉的长条（沿本地 +Z 从 0 到 len），上窄下宽；uv.y：0 = 天花板、1 = 地上</summary>
        static Mesh ShaftMesh(float len, float wTop, float wBot)
        {
            var m = new Mesh { name = "Shaft" };
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var n = new List<Vector3>(); var tri = new List<int>();
            for (int q = 0; q < 2; q++)
            {
                var side = q == 0 ? Vector3.right : Vector3.up;
                var nor = q == 0 ? Vector3.up : Vector3.right;
                int b = v.Count;
                v.Add(-side * wTop / 2); v.Add(side * wTop / 2); v.Add(-side * wBot / 2 + Vector3.forward * len); v.Add(side * wBot / 2 + Vector3.forward * len);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1));
                for (int k = 0; k < 4; k++) n.Add(nor);
                tri.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
            }
            m.SetVertices(v); m.SetUVs(0, uv); m.SetNormals(n); m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}

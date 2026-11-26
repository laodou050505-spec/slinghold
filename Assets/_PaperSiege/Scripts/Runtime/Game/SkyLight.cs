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
        const float Lip = 0.07f, LipT = 0.022f, WallH = 0.14f, WallT = 0.025f;

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
            var sides = new (Vector3 mid, Vector3 along, Vector3 outN, float len)[]
            {
                (c + fwd * (l / 2), right, fwd, w),
                (c - fwd * (l / 2), right, -fwd, w),
                (c + right * (w / 2), fwd, right, l),
                (c - right * (w / 2), fwd, -right, l),
            };
            const float bx = 3 * BlockLibrary.G, by = 0.4f * BlockLibrary.G, bz = 0.8f * BlockLibrary.G;   // W_Beam 尺寸（长沿 X、原点在底面）
            foreach (var (mid, along, outN, len) in sides)
            {
                // 内壁：竖着的木板，内表面和开口边齐平，从天花板往上伸
                var wall = Plank(root.transform, beam.lod1, mat, "Wall");
                wall.rotation = Quaternion.LookRotation(Vector3.up, -outN);         // 本地 Z = 上，本地 Y = 朝里（木板从原点往 +Y 长厚度）
                wall.position = mid + outN * WallT + Vector3.up * (WallH * 0.5f - 0.01f);
                wall.localScale = new Vector3((len + 2 * WallT) / bx, WallT / by, WallH / bz);
                // 边框：贴着天花板往外伸的一圈木条（比天花板略低一点点，盖住开口的切边）
                var lip = Plank(root.transform, beam.lod1, mat, "Lip");
                lip.rotation = Quaternion.LookRotation(outN, Vector3.down);           // 本地 Z = 朝外，本地 Y = 朝下（厚度往下长）
                lip.position = mid + outN * (Lip * 0.5f) + Vector3.up * 0.004f;
                lip.localScale = new Vector3((len + 2 * Lip) / bx, LipT / by, Lip / bz);
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
            // 清晨太阳在东边偏左（Times[0].sunAz）→ 光往反方向斜着往下照，约 60° 俯角
            var hs = Quaternion.AngleAxis(SkyDome.Times[0].sunAz, Vector3.up) * f;
            Dir = (-hs * 0.58f + Vector3.down).normalized;
            float ceil = A.ceilingHeight;
            var e = A.enemy ? A.enemy.transform.position : A.tableCenter + f * 3.8f;
            var sh = Shader.Find("PaperSiege/LightShaft");
            if (!sh) return;
            var rng = new System.Random(7);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            for (int i = 0; i < 5; i++)
            {
                var end = new Vector3(e.x, e.y, e.z) + r * R(-0.7f, 0.7f) + f * R(-0.5f, 0.4f);
                var org = end - Dir * ((ceil - end.y) / -Dir.y);
                org = sky.ClampToLid(org, 0.18f);                     // 光一定是从开口里照进来的
                float len = (ceil - e.y) / -Dir.y;
                end = org + Dir * len;
                var go = new GameObject("LightShaft");
                go.transform.SetParent(transform, false);
                go.transform.SetPositionAndRotation(org, Quaternion.LookRotation(Dir, Vector3.Cross(Dir, r).sqrMagnitude > 1e-4f ? r : f));
                go.AddComponent<MeshFilter>().sharedMesh = ShaftMesh(len, R(0.16f, 0.26f), R(0.3f, 0.45f));
                var mr = go.AddComponent<MeshRenderer>();
                var m = new Material(sh);
                m.SetColor("_Color", Color.Lerp(new Color(1f, 0.82f, 0.55f), new Color(1f, 0.9f, 0.72f), R(0, 1)));
                m.SetFloat("_Seed", R(0, 10));
                m.SetFloat("_Intensity", 0);
                mr.sharedMaterial = m;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                shafts.Add((go, m, R(0, 6)));
            }
            // 浮尘：敌城上方、光柱经过的那一块空间里慢慢飘的小亮点
            var mid = new Vector3(e.x, (ceil + e.y) * 0.5f, e.z) - Dir * 0.2f;
            motes = Fx.Motes(mid, new Vector3(1.6f, ceil - e.y - 0.3f, 1.2f), new Color(1f, 0.9f, 0.7f));
            motes.transform.SetParent(transform, true);
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

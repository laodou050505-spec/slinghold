// 积木库：每种零件的网格（LOD0 / LOD1）、标称尺寸、碰撞体、材质。
// 网格和材质引用由编辑器 ProjectSetup.BuildLibrary 写进 Resources/BlockLibrary.asset；尺寸和碰撞体在这里按“格”写死
// （积木按标称格子对齐堆叠，碰撞体用标称尺寸而不是磨损后的网格，堆起来才平整稳定）。
// 单位：格（G）。模型按 1 格 = 0.05 m 建，敌城整体放大时只改 transform 缩放。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PaperSiege
{
    public enum Mat { Wood, Stone }

    [Serializable]
    public class PieceMeshes
    {
        public string id;
        public Mesh lod0, lod1;
    }

    public struct Box
    {
        public Vector3 c, s;   // 中心、尺寸（格）
        public Box(float cx, float cy, float cz, float sx, float sy, float sz) { c = new Vector3(cx, cy, cz); s = new Vector3(sx, sy, sz); }
    }

    public class PieceDef
    {
        public string id;
        public Mat mat;
        public Vector3 size;   // 标称外形（格）：宽 x、高 y、深 z，原点在底面中心
        public Box[] boxes;
        public float Volume;   // 立方格
    }

    [CreateAssetMenu(menuName = "PaperSiege/Block Library", fileName = "BlockLibrary")]
    public class BlockLibrary : ScriptableObject
    {
        public const float G = 0.05f;
        public List<PieceMeshes> meshes = new();
        public Material[] paints = new Material[8];   // 按 Paint 枚举
        public Material propMaterial;                 // 道具图集（弹、帅旗、投石机、弹弓……）

        static BlockLibrary cached;
        public static BlockLibrary I => cached ? cached : (cached = Resources.Load<BlockLibrary>("BlockLibrary"));

        Dictionary<string, PieceMeshes> map;
        public PieceMeshes Meshes(string id)
        {
            if (map == null) { map = new(); foreach (var m in meshes) map[m.id] = m; }
            return map.TryGetValue(id, out var r) ? r : null;
        }
        public Material Paint(Paint p) => paints[(int)p];

        // ── 零件定义（标称尺寸 + 碰撞体）
        static Dictionary<string, PieceDef> defs;
        const float K = 0.97f;   // 水平方向略收一点，相邻块不互相顶

        public static PieceDef Def(string id)
        {
            if (defs == null) Build();
            return defs.TryGetValue(id, out var d) ? d : null;
        }

        public static IEnumerable<PieceDef> All { get { if (defs == null) Build(); return defs.Values; } }

        static void Add(string id, Mat m, Vector3 size, params Box[] boxes)
        {
            var d = new PieceDef { id = id, mat = m, size = size, boxes = boxes.Length > 0 ? boxes : new[] { new Box(0, size.y / 2, 0, size.x * K, size.y, size.z * K) } };
            float v = 0; foreach (var b in d.boxes) v += b.s.x * b.s.y * b.s.z;
            d.Volume = v;
            defs[id] = d;
        }

        static void Build()
        {
            defs = new();
            foreach (var (pre, m) in new[] { ("W_", Mat.Wood), ("S_", Mat.Stone) })
            {
                Add(pre + "Cube", m, new Vector3(1, 1, 1));
                Add(pre + "Slab", m, new Vector3(2, 0.5f, 1));
                Add(pre + "Pillar", m, new Vector3(1, 2, 1));
                Add(pre + "Arch", m, new Vector3(2, 1, 1),
                    new Box(-0.725f, 0.5f, 0, 0.55f * K, 1, K), new Box(0.725f, 0.5f, 0, 0.55f * K, 1, K), new Box(0, 0.825f, 0, 0.9f, 0.35f, K));
                Add(pre + "Battlement", m, new Vector3(2, 0.6f, 1),
                    new Box(0, 0.15f, 0, 2 * K, 0.3f, K), new Box(-0.84f, 0.45f, 0, 0.32f, 0.3f, K), new Box(0, 0.45f, 0, 0.64f, 0.3f, K), new Box(0.84f, 0.45f, 0, 0.32f, 0.3f, K));
            }
            Add("W_Beam", Mat.Wood, new Vector3(3, 0.4f, 0.8f));
            Add("W_Column", Mat.Wood, new Vector3(0.9f, 2, 0.9f), new Box(0, 1, 0, 0.8f, 2, 0.8f));
            Add("W_Window", Mat.Wood, new Vector3(1, 1, 1));
            Add("W_Gate", Mat.Wood, new Vector3(2, 1.5f, 1));
            Add("W_Roof", Mat.Wood, new Vector3(2.16f, 1.0f, 1.2f), new Box(0, 0.22f, 0, 2.0f, 0.44f, 1.1f), new Box(0, 0.64f, 0, 1.9f, 0.4f, 0.55f));
            Add("W_Cone", Mat.Wood, new Vector3(1.3f, 1.95f, 1.3f), new Box(0, 0.4f, 0, 1.1f, 0.8f, 1.1f), new Box(0, 1.2f, 0, 0.6f, 0.8f, 0.6f));
            Add("S_Wall", Mat.Stone, new Vector3(2, 1, 1));
            Add("S_Shield", Mat.Stone, new Vector3(3, 2, 0.5f));
            // 帅旗灯笼：底座 + 旗杆（模型由道具组提供，没有时用占位）
            // 帅旗：底座 + 旗杆 + 旗面（旗面挂在 2.425 格高处、向 +X 伸出 0.9 格；打中旗面也算打中帅旗）
            Add("King", Mat.Wood, new Vector3(0.8f, 2.8f, 0.8f), new Box(0, 0.175f, 0, 0.78f, 0.35f, 0.78f), new Box(0, 1.55f, 0, 0.14f, 2.4f, 0.14f), new Box(0.47f, 2.425f, 0, 0.86f, 0.55f, 0.1f));
        }
    }
}

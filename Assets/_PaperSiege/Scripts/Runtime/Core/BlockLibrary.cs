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
            // ── 关卡机关（11-27：一种弹配几种机关；模型在 Resources/Mech，由 Tools/blender/ps_props.py 的 mech 模式生成）
            Add("M_Hay", Mat.Wood, new Vector3(1.1f, 0.8f, 0.9f));                                                       // 草垛：烧得快、一碰就着
            Add("M_Keg", Mat.Wood, new Vector3(0.85f, 1.0f, 0.85f), new Box(0, 0.5f, 0, 0.8f, 1.0f, 0.8f));                  // 火药桶：着火 / 被炸 → 爆
            Add("M_Bucket", Mat.Wood, new Vector3(1.0f, 0.7f, 1.0f), new Box(0, 0.35f, 0, 0.95f, 0.7f, 0.95f));              // 水桶：冰丸打中 → 周围冻脆
            Add("M_Trough", Mat.Wood, new Vector3(4.0f, 0.22f, 2.2f), new Box(0, 0.11f, 0, 3.95f, 0.22f, 2.15f));          // 浅水池：冰丸打中 → 结冰，上面的塔打滑（碰撞盒顶 = 水面 0.22 格，木框边沿高出 0.12 格只是外观）
            Add("M_Keystone", Mat.Stone, new Vector3(0.7f, 0.6f, 1.0f), new Box(0, 0.3f, 0, 0.6f, 0.6f, 0.95f));            // 拱心石：炸掉 → 拱上的整片塌
            Add("M_Bell", Mat.Stone, new Vector3(0.7f, 0.8f, 0.7f), new Box(0, -0.42f, 0, 0.62f, 0.76f, 0.62f));             // 铜钟（原点在顶上挂环，往下垂）
            Add("M_BellFrame", Mat.Wood, new Vector3(4.4f, 2.4f, 0.8f),
                new Box(-2.05f, 1.15f, 0, 0.3f, 2.3f, 0.3f), new Box(2.05f, 1.15f, 0, 0.3f, 2.3f, 0.3f), new Box(0, 2.25f, 0, 4.4f, 0.3f, 0.35f));   // 钟架（三个挂钩 x = −1.7 / 0 / +1.7：和分裂丸高抛三颗的散开宽度对上）
            Add("M_ScalePost", Mat.Wood, new Vector3(0.9f, 2.0f, 0.9f), new Box(0, 0.1f, 0, 0.9f, 0.2f, 0.9f), new Box(0, 1.05f, 0, 0.5f, 1.9f, 0.5f));   // 吊秤立柱
            Add("M_ScaleBeam", Mat.Wood, new Vector3(4.5f, 1.5f, 0.9f),
                new Box(0, 0, 0, 4.5f, 0.25f, 0.3f), new Box(-2.0f, -1.3f, 0, 0.9f, 0.12f, 0.9f), new Box(2.0f, -1.3f, 0, 0.9f, 0.12f, 0.9f));   // 秤杆 + 两只秤盘（原点在转轴）

            // 帅旗灯笼：底座 + 旗杆（模型由道具组提供，没有时用占位）
            // 帅旗：底座 + 旗杆 + 旗面（旗面挂在 2.425 格高处、向 +X 伸出 0.9 格；打中旗面也算打中帅旗）
            Add("King", Mat.Wood, new Vector3(0.8f, 2.8f, 0.8f), new Box(0, 0.175f, 0, 0.78f, 0.35f, 0.78f), new Box(0, 1.55f, 0, 0.14f, 2.4f, 0.14f), new Box(0.44f, 2.45f, 0, 0.9f, 0.62f, 0.16f));   // 旗面 = 露在外面的命中区（约等于旗子本身，要瞄得准）
        }
    }
}

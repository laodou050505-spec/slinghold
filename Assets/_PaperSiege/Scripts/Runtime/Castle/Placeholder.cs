// 占位外观：正式模型（道具组）还没进来时，用简单形状顶上，保证玩法能先跑。
// 帅旗灯笼：底座木墩 + 旗杆 + 奶白旗 + 发光灯笼。其它缺模型的零件：按标称尺寸的方块。
using UnityEngine;

namespace PaperSiege
{
    public static class Placeholder
    {
        static Material lit, glow;

        static Material Lit(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = c; m.SetFloat("_Smoothness", 0.15f);
            return m;
        }

        static MeshRenderer Part(Transform parent, PrimitiveType t, Vector3 pos, Vector3 scale, Material m)
        {
            var g = GameObject.CreatePrimitive(t);
            Object.Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = scale;
            var r = g.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            return r;
        }

        public static MeshRenderer Build(Transform parent, string piece, PieceDef def, Paint paint)
        {
            const float G = BlockLibrary.G;
            if (piece == "King")
            {
                lit ??= Lit(new Color(0.55f, 0.38f, 0.24f));
                if (!glow) { glow = Lit(new Color(1f, 0.75f, 0.35f)); glow.EnableKeyword("_EMISSION"); glow.SetColor("_EmissionColor", new Color(1f, 0.6f, 0.2f) * 2f); }
                var baseR = Part(parent, PrimitiveType.Cube, new Vector3(0, 0.175f, 0) * G, new Vector3(0.78f, 0.35f, 0.78f) * G, lit);
                Part(parent, PrimitiveType.Cylinder, new Vector3(0, 1.35f, 0) * G, new Vector3(0.1f, 1.0f, 0.1f) * G, lit);
                Part(parent, PrimitiveType.Cube, new Vector3(0.45f, 2.1f, 0) * G, new Vector3(0.8f, 0.5f, 0.03f) * G, Lit(new Color(0.92f, 0.88f, 0.78f)));
                Part(parent, PrimitiveType.Sphere, new Vector3(-0.28f, 1.95f, 0) * G, new Vector3(0.3f, 0.36f, 0.3f) * G, glow);
                return baseR;
            }
            var m = BlockLibrary.I ? BlockLibrary.I.Paint(def != null && def.mat == Mat.Stone ? Paint.Stone : paint) : Lit(Palette.Of(paint));
            // 机关件（模型还没导入时）：按碰撞盒一块块搭，原点不在底面的（铜钟、秤杆）也摆得对
            if (def != null && piece.StartsWith("M_") && def.boxes != null && def.boxes.Length > 0)
            {
                MeshRenderer first = null;
                foreach (var bx in def.boxes) { var r = Part(parent, PrimitiveType.Cube, bx.c * G, bx.s * G, m); if (!first) first = r; }
                return first;
            }
            var s = def != null ? def.size : Vector3.one;
            return Part(parent, PrimitiveType.Cube, new Vector3(0, s.y * 0.5f, 0) * G, s * G, m);
        }
    }
}

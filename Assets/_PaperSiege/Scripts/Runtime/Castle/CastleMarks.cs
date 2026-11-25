// 自己城墙上的署名（方案第 9 节：Tripo 署名印在自己城墙的一块砖上）。
// 贴在模型身上时只放图标、不带字、底下不垫底（品牌规则）；按透明底原样贴，不改色、不拉伸。
using UnityEngine;

namespace PaperSiege
{
    public static class CastleMarks
    {
        static Material mat;

        public static void Credit(Castle c)
        {
            if (!c) return;
            var tex = Resources.Load<Texture2D>("Brand/tripo_mark_147");
            if (!tex) return;
            // 城门左边那块墙（W_Cube，正面朝玩家 = 本地 -Z）
            var b = c.blocks.Find(x => x && x.name == "W_Cube" && !x.isKing);
            if (!b) return;
            if (!mat)
            {
                // 受光（Lit）：图标是印在砖上的，夜里跟着砖一起暗，不能像一张自己发光的贴纸
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.SetFloat("_Smoothness", 0.1f); mat.SetFloat("_Blend", 0);
                mat.SetFloat("_Surface", 1); mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0); mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); mat.renderQueue = 3000;
                mat.SetTexture("_BaseMap", tex); mat.color = new Color(1, 1, 1, 0.92f);
            }
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(q.GetComponent<Collider>());
            q.name = "TripoMark";
            q.transform.SetParent(b.transform, false);
            float s = BlockLibrary.G * 0.55f;
            q.transform.localScale = new Vector3(s, s * tex.height / tex.width, 1);
            q.transform.localPosition = new Vector3(0, BlockLibrary.G * 0.55f, -BlockLibrary.G * 0.5f - 0.0004f);
            q.GetComponent<MeshRenderer>().sharedMaterial = mat;
            q.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}

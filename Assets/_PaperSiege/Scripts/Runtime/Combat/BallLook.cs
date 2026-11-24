// 弹的外观：道具组的 B_* 模型进来后用模型，没有时用带颜色的小球顶上。
using UnityEngine;

namespace PaperSiege
{
    public static class BallLook
    {
        static readonly Material[] mats = new Material[5];

        public static Color Tint(Ammo a) => a switch
        {
            Ammo.Fire => new Color(0.92f, 0.42f, 0.18f),
            Ammo.Ice => new Color(0.75f, 0.9f, 1f),
            Ammo.Bomb => new Color(0.18f, 0.18f, 0.2f),
            Ammo.Split => new Color(0.85f, 0.7f, 0.45f),
            _ => new Color(0.78f, 0.6f, 0.4f),
        };

        public static string MeshId(Ammo a) => a switch { Ammo.Fire => "B_Fire", Ammo.Ice => "B_Ice", Ammo.Bomb => "B_Bomb", Ammo.Split => "B_Split", _ => "B_Stone" };

        public static Transform Build(Transform parent, Ammo a, float radius)
        {
            var lib = BlockLibrary.I;
            var pm = lib ? lib.Meshes(MeshId(a)) : null;
            GameObject g;
            if (pm != null && pm.lod0 && lib.propMaterial)
            {
                g = new GameObject("Look");
                g.AddComponent<MeshFilter>().sharedMesh = pm.lod0;
                g.AddComponent<MeshRenderer>().sharedMaterial = lib.propMaterial;
                g.transform.SetParent(parent, false);
                g.transform.localScale = Vector3.one * (radius / (0.25f * BlockLibrary.G));   // 模型直径 0.5 格
            }
            else
            {
                g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.Destroy(g.GetComponent<Collider>());
                g.transform.SetParent(parent, false);
                g.transform.localScale = Vector3.one * radius * 2f;
                int i = (int)a;
                if (!mats[i])
                {
                    mats[i] = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = Tint(a) };
                    mats[i].SetFloat("_Smoothness", 0.2f);
                    if (a == Ammo.Fire) { mats[i].EnableKeyword("_EMISSION"); mats[i].SetColor("_EmissionColor", new Color(1f, 0.45f, 0.1f) * 1.5f); }
                }
                g.GetComponent<MeshRenderer>().sharedMaterial = mats[i];
            }
            g.name = "Look";
            return g.transform;
        }
    }
}

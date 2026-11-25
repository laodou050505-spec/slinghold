// 弹道虚线：一串小木珠沿预测弹道排开，落点画一个小圈。玩家拉弓预瞄和敌人蓄力预告共用。
using System.Collections.Generic;
using UnityEngine;

namespace PaperSiege
{
    public class ArcDots : MonoBehaviour
    {
        public int count = 22;
        public float beadSize = 0.013f;
        public Color color = new(0.95f, 0.85f, 0.65f, 1f);
        public float alpha = 1f;
        readonly List<Transform> beads = new();
        readonly List<Vector3> path = new();
        Transform ring;
        Material mat;
        float scroll;

        void Awake()
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetFloat("_Surface", 1);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3000;
            for (int i = 0; i < count; i++)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(b.GetComponent<Collider>());
                b.name = "Bead";
                b.transform.SetParent(transform, false);
                b.GetComponent<Renderer>().sharedMaterial = mat;
                b.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                beads.Add(b.transform);
            }
            var r = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(r.GetComponent<Collider>());
            r.name = "Ring";
            r.transform.SetParent(transform, false);
            r.GetComponent<Renderer>().sharedMaterial = mat;
            r.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring = r.transform;
            Hide();
        }

        public void Hide()
        {
            foreach (var b in beads) b.gameObject.SetActive(false);
            if (ring) ring.gameObject.SetActive(false);
        }

        /// <summary>按初始位置 / 速度画弹道（radius = 弹半径，用来做落点检测）</summary>
        public Vector3 Show(Vector3 p, Vector3 v, float radius, float maxT = 2.6f)
        {
            var land = Ballistics.Predict(p, v, radius, path, maxT);
            if (path.Count < 2) { Hide(); return land; }   // 保护：路径只有起点时不画
            // 沿路径等距放珠子（珠子缓慢往前流动，看得出方向）
            float total = 0;
            for (int i = 1; i < path.Count; i++) total += Vector3.Distance(path[i - 1], path[i]);
            scroll = (scroll + Time.deltaTime * 0.6f) % 1f;
            float step = total / count;
            int seg = 1; float acc = 0;
            for (int i = 0; i < count; i++)
            {
                float target = (i + scroll) * step;
                while (seg < path.Count - 1 && acc + Vector3.Distance(path[seg - 1], path[seg]) < target) { acc += Vector3.Distance(path[seg - 1], path[seg]); seg++; }
                float segLen = Mathf.Max(1e-5f, Vector3.Distance(path[seg - 1], path[seg]));
                var pos = Vector3.Lerp(path[seg - 1], path[seg], Mathf.Clamp01((target - acc) / segLen));
                beads[i].gameObject.SetActive(target <= total);
                beads[i].position = pos;
                float s = beadSize * Mathf.Lerp(1f, 0.6f, (float)i / count);
                beads[i].localScale = Vector3.one * s;
            }
            ring.gameObject.SetActive(true);
            ring.position = land + Vector3.up * 0.002f;
            ring.localScale = new Vector3(radius * 5f, 0.0008f, radius * 5f);
            mat.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(color.a * alpha * 1.1f));
            return land;
        }
    }
}

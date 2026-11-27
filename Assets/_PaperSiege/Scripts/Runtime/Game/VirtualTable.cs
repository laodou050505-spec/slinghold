// 虚拟木桌（11-27 用户：玩家没有桌子时头显里怎么显示）。
// 系统空间扫描里读不到玩家面前的真实桌子时，就在玩家身前立一张看得见的木桌（原来头显里只有一块看不见的碰撞体，
// 城堡像悬在半空；编辑器里另有一张方块桌，造成“编辑器里正常、头显里不一样”）。现在编辑器和头显用的是同一张桌。
// 模型：Tools/blender/ps_props.py table 模式 → Resources/Table（M_TableTop 原点在桌面顶面中心、M_TableLeg 长 1 m 原点在顶端、M_TableFoot 脚垫）；
// 模型没同步进来时退回几块方木拼的占位桌。
// 有过程：先落四个脚垫 → 四条腿从地上长到桌高 → 桌面从上方落下“咚”一声扬点灰；换位置时倒着拆掉。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PaperSiege
{
    public class VirtualTable : MonoBehaviour
    {
        const float TopThick = 0.035f, FootH = 0.025f, LegInset = 0.065f;   // 腿中心离桌边 4 cm + 半个腿宽
        Transform top;
        readonly List<Transform> legs = new(), feet = new();
        float legLen;
        bool closing;
        float builtAt;
        /// <summary>完整搭一次大约多久（脚垫 0.3 + 腿 0.45 + 桌面落下 0.32 + 顿一下 0.18）</summary>
        public const float BuildTime = 1.3f;
        public float Remaining => Mathf.Max(0f, builtAt - Time.unscaledTime);

        static Dictionary<string, Mesh> map;
        static Material mat, placeholderWood, placeholderDark;

        static void Load()
        {
            map = new();
            foreach (var m in Resources.LoadAll<Mesh>("Table/table")) map[m.name] = m;
            mat = Resources.Load<Material>("Table/M_Table");
        }
        static bool HasModel => map.ContainsKey("M_TableTop") && map.ContainsKey("M_TableLeg") && map.ContainsKey("M_TableFoot") && mat;

        /// <summary>center = 桌面顶面中心（世界坐标，y = 桌高，地面 y = 0）；size = 桌面长宽（米）</summary>
        public static VirtualTable Build(Transform parent, Vector3 center, Quaternion rot, Vector2 size, bool animate = true)
        {
            if (map == null) Load();
            var root = new GameObject("VirtualTable");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(center, rot);
            var vt = root.AddComponent<VirtualTable>();
            vt.legLen = Mathf.Max(0.2f, center.y - TopThick - FootH);
            vt.Make(size);
            vt.builtAt = Time.unscaledTime + (animate ? BuildTime : 0f);
            if (animate) vt.StartCoroutine(vt.Assemble());
            return vt;
        }

        void Make(Vector2 size)
        {
            bool model = HasModel;
            top = Piece("Top", model ? map["M_TableTop"] : null, Vector3.zero, Vector3.one);
            if (!model) PlaceholderTop(top, size);
            else
            {
                // 模型是 0.9 × 0.6 m；真实尺寸不同时按比例缩放（虚拟桌一般就是 0.9 × 0.6，不缩放）
                top.localScale = new Vector3(size.x / 0.9f, 1f, size.y / 0.6f);
            }
            foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            {
                var xz = new Vector3(sx * (size.x / 2 - LegInset), 0, sz * (size.y / 2 - LegInset));
                // 腿：模型长 1 m、顶端是原点，只在长度方向缩放（木纹顺着腿长，拉伸看不出来）
                var leg = Piece("Leg", model ? map["M_TableLeg"] : null, xz - Vector3.up * TopThick, new Vector3(1f, legLen, 1f));
                if (!model) PlaceholderBox(leg, new Vector3(0, -0.5f, 0), new Vector3(0.05f, 1f, 0.05f), false);
                legs.Add(leg);
                var foot = Piece("Foot", model ? map["M_TableFoot"] : null, xz - Vector3.up * (TopThick + legLen), Vector3.one);
                if (!model) PlaceholderBox(foot, new Vector3(0, -FootH / 2, 0), new Vector3(0.06f, FootH, 0.06f), false);
                feet.Add(foot);
            }
        }

        Transform Piece(string name, Mesh mesh, Vector3 localPos, Vector3 scale)
        {
            var g = new GameObject(name);
            g.transform.SetParent(transform, false);
            g.transform.localPosition = localPos;
            g.transform.localScale = scale;
            if (mesh)
            {
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = g.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
            }
            return g.transform;
        }

        // ── 占位（模型没进来时）：几块木板拼的桌面 + 方腿
        static Material Wood(bool dark)
        {
            if (!placeholderWood)
            {
                placeholderWood = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.55f, 0.39f, 0.25f) };
                placeholderWood.SetFloat("_Smoothness", 0.08f);
                placeholderDark = new Material(placeholderWood) { color = new Color(0.44f, 0.31f, 0.2f) };
            }
            return dark ? placeholderDark : placeholderWood;
        }

        static void PlaceholderBox(Transform parent, Vector3 center, Vector3 size, bool dark)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = center;
            g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = Wood(dark);
        }

        static void PlaceholderTop(Transform t, Vector2 size)
        {
            const int n = 4;
            float w = size.y / n;
            for (int i = 0; i < n; i++)
                PlaceholderBox(t, new Vector3(0, -TopThick / 2, -size.y / 2 + w * (i + 0.5f)), new Vector3(size.x, TopThick, w - 0.004f), i % 2 == 1);
            // 裙板
            PlaceholderBox(t, new Vector3(0, -TopThick - 0.03f, size.y / 2 - 0.05f), new Vector3(size.x - 0.1f, 0.06f, 0.02f), true);
            PlaceholderBox(t, new Vector3(0, -TopThick - 0.03f, -size.y / 2 + 0.05f), new Vector3(size.x - 0.1f, 0.06f, 0.02f), true);
        }

        static float Ease(float k) => 1f - Mathf.Pow(1f - Mathf.Clamp01(k), 3f);

        IEnumerator Assemble()
        {
            // 0：全部藏起来
            foreach (var f in feet) f.localScale = Vector3.zero;
            foreach (var l in legs) l.localScale = new Vector3(1f, 0f, 1f);
            var topPos = top.localPosition; var topScale = top.localScale;
            top.localScale = Vector3.zero;
            float T0 = Time.unscaledTime;
            // 1：脚垫一个个落地（0.25 s）
            for (int i = 0; i < feet.Count; i++)
            {
                int k = i; StartCoroutine(Pop(feet[k], Vector3.one, 0.18f, 0.06f * k));
            }
            yield return Wait(0.3f);
            // 2：四条腿从脚垫上长到桌高（腿的原点在顶端：边长边把原点往上提）
            var footY = new float[legs.Count];
            for (int i = 0; i < legs.Count; i++) footY[i] = legs[i].localPosition.y - legLen;
            for (float t = 0; t < 1f; t += Time.unscaledDeltaTime / 0.45f)
            {
                float e = Ease(t), len = legLen * e;
                for (int i = 0; i < legs.Count; i++)
                {
                    legs[i].localScale = new Vector3(1f, Mathf.Max(0.001f, len), 1f);
                    var p = legs[i].localPosition; p.y = footY[i] + len; legs[i].localPosition = p;
                }
                yield return null;
            }
            for (int i = 0; i < legs.Count; i++) { legs[i].localScale = new Vector3(1f, legLen, 1f); var p = legs[i].localPosition; p.y = footY[i] + legLen; legs[i].localPosition = p; }
            // 3：桌面从 25 cm 上方落下，落定时轻轻一顿 + 咚一声 + 一圈灰
            top.localScale = topScale;
            for (float t = 0; t < 1f; t += Time.unscaledDeltaTime / 0.32f)
            {
                float k = t * t;
                top.localPosition = topPos + Vector3.up * (0.25f * (1f - k));
                yield return null;
            }
            top.localPosition = topPos;
            Sfx.Place(transform.position);
            for (float t = 0; t < 1f; t += Time.unscaledDeltaTime / 0.18f)
            {
                top.localPosition = topPos - Vector3.up * (0.006f * Mathf.Sin(t * Mathf.PI));
                yield return null;
            }
            top.localPosition = topPos;
            Debug.Log($"[木垒][虚拟桌] 搭好 用时 {Time.unscaledTime - T0:0.00}s 桌高 {transform.position.y:0.00}m 模型={HasModel}");
        }

        IEnumerator Pop(Transform t, Vector3 to, float dur, float delay)
        {
            if (delay > 0) yield return Wait(delay);
            for (float k = 0; k < 1f; k += Time.unscaledDeltaTime / dur)
            {
                float s = Ease(k) * (1f + 0.12f * Mathf.Sin(k * Mathf.PI));
                t.localScale = to * s;
                yield return null;
            }
            t.localScale = to;
        }

        static IEnumerator Wait(float s) { float e = Time.unscaledTime + s; while (Time.unscaledTime < e) yield return null; }

        /// <summary>换位置（系统回中后重新摆场地）时：桌面先抬起收掉、腿缩回地面，再销毁</summary>
        public void Dismantle()
        {
            if (closing) return;
            closing = true;
            StopAllCoroutines();
            StartCoroutine(DismantleCo());
        }

        IEnumerator DismantleCo()
        {
            var p0 = top.localPosition; var s0 = top.localScale;
            for (float t = 0; t < 1f; t += Time.unscaledDeltaTime / 0.25f)
            {
                top.localPosition = p0 + Vector3.up * (0.12f * t);
                top.localScale = s0 * (1f - Ease(t));
                yield return null;
            }
            top.gameObject.SetActive(false);
            var y0 = new float[legs.Count];
            for (int i = 0; i < legs.Count; i++) y0[i] = legs[i].localPosition.y - legs[i].localScale.y;
            float len0 = legs.Count > 0 ? legs[0].localScale.y : 0f;
            for (float t = 0; t < 1f; t += Time.unscaledDeltaTime / 0.3f)
            {
                float len = len0 * (1f - Ease(t));
                for (int i = 0; i < legs.Count; i++)
                {
                    legs[i].localScale = new Vector3(1f, Mathf.Max(0.001f, len), 1f);
                    var p = legs[i].localPosition; p.y = y0[i] + len; legs[i].localPosition = p;
                }
                foreach (var f in feet) f.localScale = Vector3.one * (1f - Ease(t));
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}

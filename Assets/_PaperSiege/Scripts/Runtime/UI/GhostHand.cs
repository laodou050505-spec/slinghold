// 半透明示范手（教学用）：PICO SDK 自带的手模型，程序摆姿势——张掌 / 捏（拇指尖用 IK 找食指尖）/ 握拳，
// 按“捏合点”或“掌心”摆到目标位置、朝向。教学导演每帧告诉它摆在哪、什么姿势、合到几成。
using System.Collections.Generic;
using UnityEngine;

namespace PaperSiege
{
    public class GhostHand : MonoBehaviour
    {
        public enum Pose { Open, Pinch, Fist }
        public bool right;
        Transform root, wrist;
        readonly Dictionary<string, Transform> j = new();
        readonly Dictionary<Transform, Quaternion> rest = new();
        readonly Dictionary<Transform, Vector3> curlAxis = new();   // 在父骨骼空间里的弯曲轴（掌心方向）
        Material mat;
        string P => right ? "right_" : "left_";

        public static GhostHand Build(bool right)
        {
            var src = Resources.Load<GameObject>(right ? "Hand/Models/Hand_R" : "Hand/Models/Hand_L");
            var go = new GameObject(right ? "GhostHand_R" : "GhostHand_L");
            var g = go.AddComponent<GhostHand>();
            g.right = right;
            if (!src) { Debug.LogWarning("找不到 PICO 手模型"); return g; }
            var inst = Instantiate(src, go.transform);
            g.root = inst.transform;
            foreach (var t in inst.GetComponentsInChildren<Transform>()) g.j[t.name] = t;
            g.wrist = g.j[g.P + "wrist"];
            g.mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            g.mat.SetFloat("_Surface", 1); g.mat.SetOverrideTag("RenderType", "Transparent");
            g.mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); g.mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            g.mat.SetInt("_ZWrite", 0); g.mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); g.mat.renderQueue = 3001;
            g.mat.color = new Color(1f, 0.93f, 0.82f, 0.42f);
            foreach (var r in inst.GetComponentsInChildren<Renderer>()) { r.sharedMaterial = g.mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            g.CacheRest();
            return g;
        }

        Vector3 PalmN()
        {
            var w = wrist.position;
            var cr = Vector3.Cross(j[P + "index_proximal"].position - w, j[P + "little_proximal"].position - w).normalized;
            return right ? -cr : cr;
        }

        Vector3 FingerDir() => (j[P + "middle_proximal"].position - wrist.position).normalized;

        void CacheRest()
        {
            foreach (var t in j.Values) rest[t] = t.localRotation;
            var n = PalmN();
            foreach (var f in new[] { "index", "middle", "ring", "little", "thumb" })
                foreach (var seg in new[] { "metacarpal", "proximal", "intermediate", "distal" })
                {
                    if (!j.TryGetValue(P + f + "_" + seg, out var b)) continue;
                    var child = NextOf(f, seg);
                    if (!child) continue;
                    var dir = (child.position - b.position).normalized;
                    var axis = Vector3.Cross(dir, n).normalized;   // 绕它转正角 = 手指往掌心弯
                    curlAxis[b] = b.parent.InverseTransformDirection(axis);
                }
        }

        Transform NextOf(string f, string seg)
        {
            string[] order = f == "thumb" ? new[] { "metacarpal", "proximal", "distal", "tip" } : new[] { "metacarpal", "proximal", "intermediate", "distal", "tip" };
            int i = System.Array.IndexOf(order, seg);
            if (i < 0 || i + 1 >= order.Length) return null;
            j.TryGetValue(P + f + "_" + order[i + 1], out var t);
            return t;
        }

        void Curl(string f, string seg, float deg)
        {
            if (!j.TryGetValue(P + f + "_" + seg, out var b) || !curlAxis.TryGetValue(b, out var ax)) return;
            b.rotation = Quaternion.AngleAxis(deg, b.parent.TransformDirection(ax)) * b.rotation;
        }

        /// <summary>摆姿势。k = 合拢程度 0～1（0 = 张开的手）</summary>
        public void SetPose(Pose pose, float k)
        {
            if (!root) return;
            foreach (var kv in rest) kv.Key.localRotation = kv.Value;
            k = Mathf.Clamp01(k);
            // 张开的手也带一点自然弯
            foreach (var f in new[] { "index", "middle", "ring", "little" }) { Curl(f, "proximal", 8); Curl(f, "intermediate", 10); }
            if (pose == Pose.Fist)
            {
                foreach (var f in new[] { "index", "middle", "ring", "little" }) { Curl(f, "proximal", 80 * k); Curl(f, "intermediate", 95 * k); Curl(f, "distal", 55 * k); }
                ThumbTo(j[P + "middle_intermediate"].position, k);
            }
            else if (pose == Pose.Pinch)
            {
                Curl("index", "proximal", 38 * k); Curl("index", "intermediate", 45 * k); Curl("index", "distal", 25 * k);
                foreach (var f in new[] { "middle", "ring", "little" }) { Curl(f, "proximal", 22 * k); Curl(f, "intermediate", 30 * k); }
                ThumbTo(j[P + "index_tip"].position, k);
            }
        }

        /// <summary>拇指 IK（CCD）：拇指尖往 target 靠，k = 靠过去几成</summary>
        void ThumbTo(Vector3 target, float k)
        {
            var tip = j[P + "thumb_tip"];
            var goal = Vector3.Lerp(tip.position, target, k);
            var chain = new[] { j[P + "thumb_distal"], j[P + "thumb_proximal"], j[P + "thumb_metacarpal"] };
            for (int it = 0; it < 10; it++)
                foreach (var b in chain)
                {
                    var a = tip.position - b.position; var c = goal - b.position;
                    if (a.sqrMagnitude < 1e-8f || c.sqrMagnitude < 1e-8f) continue;
                    var q = Quaternion.FromToRotation(a, c);
                    q.ToAngleAxis(out float ang, out var ax);
                    if (ang > 180) ang -= 360;
                    b.rotation = Quaternion.AngleAxis(Mathf.Clamp(ang, -20, 20), ax) * b.rotation;
                }
        }

        public Vector3 PinchPoint => (j[P + "thumb_tip"].position + j[P + "index_tip"].position) * 0.5f;
        public Vector3 PalmCenter => (wrist.position + j[P + "index_proximal"].position + j[P + "little_proximal"].position + j[P + "middle_proximal"].position) * 0.25f;

        /// <summary>把手摆到位：anchor（捏合点 / 掌心）对准 at；手指指向 fingers，掌心朝 palm</summary>
        public void Place(Vector3 at, Vector3 fingers, Vector3 palm, bool byPinch)
        {
            if (!root) return;
            var cur = Quaternion.LookRotation(FingerDir(), PalmN());
            var want = Quaternion.LookRotation(fingers, Vector3.ProjectOnPlane(palm, fingers).normalized);
            root.rotation = want * Quaternion.Inverse(cur) * root.rotation;
            var anchor = byPinch ? PinchPoint : PalmCenter;
            root.position += at - anchor;
        }

        public void SetAlpha(float a) { if (mat) mat.color = new Color(1f, 0.93f, 0.82f, 0.42f * Mathf.Clamp01(a)); }
        public void Show(bool on) => gameObject.SetActive(on);
    }
}

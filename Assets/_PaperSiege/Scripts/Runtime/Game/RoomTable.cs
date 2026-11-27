// 真实房间：从 PICO 系统“空间扫描”读桌子和天花板（语义标签 Table / Ceiling），换成世界坐标。
// 做法照 MonsterCity/RoomTable（真机验证过）：用 PXR_MixedReality 查场景锚点，锚点里存 3D 框 + 位置姿态。
// 读不到（没扫房间 / 没标桌子 / 没给权限）就用默认值：桌高 0.85 m、天花板 2.5 m —— 游戏照常能玩。
using System.Collections.Generic;
using ByteDance.PICO.XR;
using UnityEngine;

namespace PaperSiege
{
    public class RoomTable : MonoBehaviour
    {
        public static RoomTable I { get; private set; }

        public struct Table
        {
            public Vector3 center;        // 桌面中心（世界坐标，y = 桌面高度）
            public Vector3 axisA, axisB;  // 桌面两根水平轴
            public float halfA, halfB;    // 两个方向的半宽
        }

        struct Raw { public Vector3 anchorPos, offset, extent; public Quaternion anchorRot; }

        readonly List<Raw> raws = new(), ceilings = new();
        public Transform space;                 // XR Origin（追踪空间 → 世界）
        public bool TableFound => raws.Count > 0;
        public bool CeilingFound => ceilings.Count > 0;
        public string Status { get; private set; } = "未开始";
        /// <summary>第一次读房间结束了（不管成功失败）——启动时等它再摆场地</summary>
        public bool FirstLoadDone { get; private set; }
        bool providerOn, loading;

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        void Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Load();
#else
            Status = "编辑器里不读（用虚拟桌和默认天花板）";
#endif
        }

        void OnApplicationFocus(bool focus)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (focus && !loading && Status != "未开始") Load();   // 玩家去系统里扫完房间再回来：重新读
#endif
        }

        async void Load()
        {
            if (loading) return;
            loading = true;
            try
            {
                if (!providerOn)
                {
                    var r0 = await PXR_MixedReality.StartSenseDataProvider(PxrSenseDataProviderType.SceneCapture);
                    if (r0 != PxrResult.SUCCESS) { Status = "场景识别启动失败 " + r0; return; }
                    providerOn = true;
                }
                raws.Clear(); ceilings.Clear();
                await Query(PxrSemanticLabel.Table, raws);
                await Query(PxrSemanticLabel.Ceiling, ceilings);
                Status = $"桌子 {raws.Count} 张、天花板 {ceilings.Count} 块";
            }
            catch (System.Exception e) { Status = "场景识别异常 " + e.Message; }
            finally { loading = false; FirstLoadDone = true; Debug.Log("[木垒][房间] " + Status); }
        }

        async System.Threading.Tasks.Task Query(PxrSemanticLabel label, List<Raw> into)
        {
            var r = await PXR_MixedReality.QuerySceneAnchorAsync(new[] { label });
            if (r.result != PxrResult.SUCCESS) return;
            foreach (var h in r.anchorHandleList)
            {
                if (PXR_MixedReality.GetSceneBox3DData(h, out var pos, out _, out var ext) != PxrResult.SUCCESS) continue;
                if (PXR_MixedReality.LocateAnchor(h, out var ap, out var ar) != PxrResult.SUCCESS) continue;
                into.Add(new Raw { anchorPos = ap, anchorRot = ar, offset = pos, extent = ext });
            }
        }

        /// <summary>玩家前方（±70°）、2.2 m 以内、桌面高 0.4～1.3 m 的桌子里最近的一张</summary>
        public bool TryFront(Vector3 head, Vector3 forward, out Table t)
        {
            t = default; float best = float.MaxValue; bool ok = false;
            forward.y = 0; forward.Normalize();
            foreach (var raw in raws)
            {
                var tb = ToWorld(raw);
                var d = tb.center - head; d.y = 0;
                if (d.magnitude > 2.2f || (d.sqrMagnitude > 1e-4f && Vector3.Angle(forward, d) > 70f)) continue;
                if (tb.center.y < 0.4f || tb.center.y > 1.3f) continue;
                if (Mathf.Max(tb.halfA, tb.halfB) < 0.25f || Mathf.Min(tb.halfA, tb.halfB) < 0.18f) continue;   // 太小摆不下城：用虚拟桌
                if (d.magnitude < best) { best = d.magnitude; t = tb; ok = true; }
            }
            return ok;
        }

        /// <summary>玩家头顶上的天花板高度（取玩家正上方、离得最近的那块天花板的下沿）；读不到返回 0</summary>
        public float CeilingAbove(Vector3 head)
        {
            float best = 0;
            foreach (var raw in ceilings)
            {
                var tb = ToWorld(raw);
                float dx = Mathf.Abs(head.x - tb.center.x), dz = Mathf.Abs(head.z - tb.center.z);
                if (dx > tb.halfA || dz > tb.halfB) continue;                       // 玩家不在这一块下面
                // 找这块框 8 个角里最低的那个 y ≈ 天花板下沿
                float low = float.MaxValue;
                for (int k = 0; k < 8; k++)
                {
                    var p = tb.center + tb.axisA * tb.halfA * ((k & 1) == 0 ? -1 : 1) + tb.axisB * tb.halfB * ((k & 2) == 0 ? -1 : 1);
                    low = Mathf.Min(low, tb.center.y);
                }
                if (low > head.y + 0.6f && (best == 0 || low < best)) best = low;   // 至少比头高 60 cm 才算天花板
            }
            return best;
        }

        /// <summary>点 p 正上方那块天花板（房间扫描的盒子，水平两轴 + 半宽）；读不到返回 false</summary>
        public bool TryCeilingBox(Vector3 p, out Table t)
        {
            t = default; bool ok = false; float best = float.MaxValue;
            foreach (var raw in ceilings)
            {
                var tb = ToWorld(raw);
                var d = p - tb.center; d.y = 0;
                float da = Mathf.Abs(Vector3.Dot(d, tb.axisA)), db = Mathf.Abs(Vector3.Dot(d, tb.axisB));
                if (da > tb.halfA || db > tb.halfB) continue;
                if (tb.halfA < 0.8f || tb.halfB < 0.8f) continue;   // 太小的碎块不算
                if (d.magnitude < best) { best = d.magnitude; t = tb; ok = true; }
            }
            return ok;
        }

        Table ToWorld(Raw r)
        {
            Vector3 W(Vector3 p) => space ? space.TransformPoint(p) : p;
            Vector3 D(Vector3 d) => space ? space.TransformDirection(d) : d;
            var c = r.anchorPos + r.anchorRot * r.offset;
            var ax = new[] { r.anchorRot * Vector3.right, r.anchorRot * Vector3.up, r.anchorRot * Vector3.forward };
            var half = new[] { r.extent.x * 0.5f, r.extent.y * 0.5f, r.extent.z * 0.5f };
            int up = 0; float bestUp = -1;
            for (int i = 0; i < 3; i++) { float dd = Mathf.Abs(Vector3.Dot(D(ax[i]).normalized, Vector3.up)); if (dd > bestUp) { bestUp = dd; up = i; } }
            int a = (up + 1) % 3, b = (up + 2) % 3;
            float topY = float.MinValue;
            for (int k = 0; k < 8; k++)
            {
                var p = c + ax[0] * half[0] * ((k & 1) == 0 ? -1 : 1) + ax[1] * half[1] * ((k & 2) == 0 ? -1 : 1) + ax[2] * half[2] * ((k & 4) == 0 ? -1 : 1);
                topY = Mathf.Max(topY, W(p).y);
            }
            var wc = W(c);
            static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.right; }
            return new Table { center = new Vector3(wc.x, topY, wc.z), axisA = Flat(D(ax[a])), axisB = Flat(D(ax[b])), halfA = half[a], halfB = half[b] };
        }
    }
}

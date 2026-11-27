// 编辑器演示：用模拟手循环“抬手架弓 → 抓皮兜 → 慢慢拉开（看预瞄）→ 松手发射 → 看弹飞过去撞城”。
// 菜单 PaperSiege/演示：拉弓发射；赢了 / 弹打光了自动重开这一关。只在编辑器里用。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PaperSiege
{
    public class DemoDriver : MonoBehaviour
    {
        public static bool Requested;
        public static int Level = 4;   // 演示用第五关（城比较完整，打起来好看）
        Director d;
        int targetIdx;

        /// <summary>头显保险（11-27）：这是编辑器里转相机的模拟脚本，真机上一出现就自毁，绝不碰玩家视角</summary>
        void Awake() { if (!Application.isEditor || UnityEngine.XR.XRSettings.isDeviceActive) { enabled = false; Destroy(this); } }

        void Start()
        {
            d = Director.I;
            // 演示时视线抬平一些，让敌城在画面中间（看得清打中的效果）
            var e = d.head.transform.eulerAngles;
            d.head.transform.rotation = Quaternion.Euler(14f, e.y, 0f);
            SimHands.scripted = true;
            SimHands.Hide(0); SimHands.Hide(1);
            d.StartLevel(Level);
            StartCoroutine(Loop());
        }

        Vector3 HeadPt(float fwd, float right, float up)
        {
            var h = d.head.transform;
            var f = Vector3.ProjectOnPlane(h.forward, Vector3.up).normalized; var r = Vector3.Cross(Vector3.up, f);
            return h.position + f * fwd + r * right + Vector3.up * up;
        }

        IEnumerator Move(int side, Vector3 a, Vector3 b, float t, bool pinch)
        {
            for (float k = 0; k < 1f; k += Time.deltaTime / t) { SimHands.Pose(side, Vector3.Lerp(a, b, Mathf.SmoothStep(0, 1, k)), pinch); yield return null; }
            SimHands.Pose(side, b, pinch);
        }

        Block PickTarget()
        {
            var e = d.arena.enemy;
            var list = new List<Block>();
            foreach (var b in e.Alive) if (!b.isKing && b.InPlace) list.Add(b);
            list.Sort((x, y) => y.WorldBounds.max.y.CompareTo(x.WorldBounds.max.y));
            if (list.Count == 0) return e.king;
            return list[(targetIdx++ * 3) % Mathf.Min(list.Count, 8)];
        }

        IEnumerator Loop()
        {
            yield return new WaitForSeconds(0.5f);
            d.catapult.Stop();
            while (true)
            {
                if (d.phase != Director.Phase.Battle || d.ammo.Total == 0)
                {
                    yield return new WaitForSeconds(1.5f);
                    d.StartLevel(d.levelIndex);
                    d.catapult.Stop();
                    yield return new WaitForSeconds(0.8f);
                }
                // 1) 左手从下面抬起来，在胸前偏左捏住 → 出现弓架
                var low = HeadPt(0.25f, -0.16f, -0.55f);
                var bow = HeadPt(0.42f, -0.09f, -0.16f);
                yield return Move(0, low, bow, 0.5f, false);
                SimHands.Pose(0, bow, true);
                yield return new WaitForSeconds(0.5f);
                // 2) 右手伸到皮兜捏住
                var pr = d.sling.PouchRest;
                yield return Move(1, HeadPt(0.25f, 0.18f, -0.4f), pr, 0.5f, false);
                SimHands.Pose(1, pr, true);
                yield return new WaitForSeconds(0.25f);
                // 3) 往右肩方向斜着慢慢拉开，瞄一块敌城的积木。弓架会跟着瞄准方向转、出弹点也跟着动，
                //    所以边拉边按当前出弹点重新反解（和真人看着预瞄线修正一样）
                var target = PickTarget().WorldBounds.center;
                float pull = 0.27f;
                float speed = Mathf.Lerp(GameTuning.I.slingSpeedMin, GameTuning.I.slingSpeedMax, Mathf.InverseLerp(InputTuning.I.pullMin, InputTuning.I.pullMax, pull));
                Vector3 Aim()
                {
                    var fc = d.sling.ForkCenter;
                    var dir = Ballistics.Solve(fc, target, speed, out var lo, out _) ? lo.normalized : d.arena.forward;
                    return fc - dir * pull;
                }
                var to = Aim();
                for (float k = 0; k < 1f; k += Time.deltaTime / 1.1f)
                {
                    to = Aim();
                    SimHands.Pose(1, Vector3.Lerp(pr, to, Mathf.SmoothStep(0, 1, k)), true);
                    yield return null;
                }
                for (float t = 0; t < 0.6f; t += Time.deltaTime) { to = Aim(); SimHands.Pose(1, to, true); yield return null; }   // 停一下，看预瞄弧线
                // 4) 松手 → 皮筋回弹、弹从叉口飞出去
                SimHands.Pose(1, to, false);
                yield return new WaitForSeconds(0.15f);
                SimHands.Hide(1);
                yield return new WaitForSeconds(2.4f);   // 看弹飞过去撞城
                // 5) 左手放下，弓架收起
                SimHands.Pose(0, bow, false);
                yield return Move(0, bow, low, 0.4f, false);
                SimHands.Hide(0);
                yield return new WaitForSeconds(0.6f);
            }
        }
    }
}

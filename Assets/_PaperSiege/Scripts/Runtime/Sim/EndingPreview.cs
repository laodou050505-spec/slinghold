// 编辑器菜单“PaperSiege/直接看结局”：存档进度直接设到第十关，自动开第十关，
// 看几秒对战后把王城帅旗打倒 → 播结局；镜头自动跟着走（看塌城 → 抬头看烟花和天亮 → 转向终章横木），
// 横木出来后镜头交还给桌面试玩（右键拖动转头、左键点木签），可以点“回标题”看封面主按钮变成“从头再来”。
using System.Collections;
using UnityEngine;

namespace PaperSiege
{
    public class EndingPreview : MonoBehaviour
    {
        public static bool Requested;
        Director d;

        /// <summary>头显保险（11-27）：这是编辑器里转相机的模拟脚本，真机上一出现就自毁，绝不碰玩家视角</summary>
        void Awake() { if (!Application.isEditor || UnityEngine.XR.XRSettings.isDeviceActive) { enabled = false; Destroy(this); } }

        IEnumerator Start()
        {
            d = Director.I;
            Progress.Unlocked = Levels.Count - 1;           // 进度直接到最后一关（选关里全开）
            yield return new WaitForSeconds(0.5f);
            d.StartLevel(Levels.Count - 1);
            var t = d.head.transform;
            Look(t, d.arena.enemy.transform.position + Vector3.up * 0.3f, 1f);
            yield return new WaitForSeconds(5.5f);           // 先看城搭起来、投石机开始蓄力
            d.catapult.Stop();
            foreach (var p in Projectile.Live.ToArray()) if (p) Destroy(p.gameObject);
            d.arena.enemy.DamageKing(99, d.arena.enemy.transform.position - d.arena.forward);
            // 1) 盯着王城：晃 → 一层层垮（约 6 秒）
            yield return Turn(t, d.arena.enemy.transform.position + Vector3.up * 0.3f, 0.6f);
            yield return new WaitForSeconds(5.2f);
            // 2) 抬头看天花板：天窗往四周扩开铺满整片天花板，满天烟花（左右慢慢扫一眼）
            var sky = t.position + d.arena.forward * 1.4f + Vector3.up * 2.2f;
            var side = Vector3.Cross(Vector3.up, d.arena.forward);
            yield return Turn(t, sky, 2.0f);
            yield return new WaitForSeconds(1.5f);
            yield return Turn(t, sky + side * 1.6f, 2.5f);
            yield return Turn(t, sky - side * 1.6f + Vector3.up * 0.4f, 3.5f);
            yield return Turn(t, sky, 2.0f);
            // 3) 低头看废墟：晨光照进来，积木一块块化成光点散去
            yield return Turn(t, d.arena.enemy.transform.position + Vector3.up * 0.45f, 2.0f);
            float w = 0; GameObject rb = null;
            while (w < 14f && !(rb = GameObject.Find("ResultBoard"))) { w += Time.deltaTime; yield return null; }
            yield return new WaitForSeconds(0.3f);
            // 4) 转向终章横木
            if (rb) { var r = rb.transform.Find("Result"); yield return Turn(t, (r ? r.position : rb.transform.position) + Vector3.down * 0.12f, 1.6f); }
            Destroy(this);
        }

        static void Look(Transform t, Vector3 at, float k) => t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(at - t.position, Vector3.up), k);

        static IEnumerator Turn(Transform t, Vector3 at, float dur)
        {
            var r0 = t.rotation; var r1 = Quaternion.LookRotation(at - t.position, Vector3.up);
            for (float k = 0; k < 1f; k += Time.deltaTime / dur) { t.rotation = Quaternion.Slerp(r0, r1, Mathf.SmoothStep(0, 1, k)); yield return null; }
            t.rotation = r1;
        }
    }
}

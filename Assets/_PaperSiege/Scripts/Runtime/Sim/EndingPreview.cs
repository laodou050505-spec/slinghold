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

        IEnumerator Start()
        {
            d = Director.I;
            Progress.Unlocked = Levels.Count - 1;           // 进度直接到第十关（选关里十关全开）
            yield return new WaitForSeconds(0.5f);
            d.StartLevel(Levels.Count - 1);
            var t = d.head.transform;
            Look(t, d.arena.enemy.transform.position + Vector3.up * 0.3f, 1f);
            yield return new WaitForSeconds(5.5f);           // 先看城搭起来、投石机开始蓄力
            d.catapult.Stop();
            foreach (var p in Projectile.Live.ToArray()) if (p) Destroy(p.gameObject);
            d.arena.enemy.DamageKing(99, d.arena.enemy.transform.position - d.arena.forward);
            // 1) 盯着王城塌
            yield return Turn(t, d.arena.enemy.transform.position + Vector3.up * 0.2f, 0.6f);
            yield return new WaitForSeconds(2.2f);
            // 2) 慢慢抬头看天花板开口：烟花 → 天亮、日出
            var sky = d.arena.tableCenter + d.arena.forward * 1.6f + Vector3.up * 2.6f;
            yield return Turn(t, sky, 2.5f);
            yield return new WaitForSeconds(6.5f);
            // 3) 低头看一眼塌掉的城在晨光里
            yield return Turn(t, d.arena.enemy.transform.position + Vector3.up * 0.5f, 2.0f);
            float w = 0; GameObject rb = null;
            while (w < 6f && !(rb = GameObject.Find("ResultBoard"))) { w += Time.deltaTime; yield return null; }
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

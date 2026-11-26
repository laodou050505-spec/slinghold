// 提示木牌（实物界面）：一块手刻小木牌挂在一根绳上，浮在视野右前方，毛笔字写提示。
// 换提示有过程：旧牌往上荡出去（淡出）→ 换字 → 新牌从上面落下来、晃几下停住。头转得多了牌子才慢慢跟过去（不黏在脸上）。
using System.Collections;
using TMPro;
using UnityEngine;

namespace PaperSiege
{
    public class HintTag : MonoBehaviour
    {
        public Transform head;
        /// <summary>挂在固定点（比如桌上的演示台上方）；null = 跟着视线浮在右前方</summary>
        public Vector3? pin;
        Transform tag, hang;
        TextMeshPro text;
        LineRenderer cord;
        Vector3 anchor, anchorGoal;
        float swing, swingVel, drop = 1f;
        float lean, yaw; Vector3 jitter;   // 每次换牌随机一点：歪一点、转一点、挂的位置错开几厘米（不死板）
        string current = "";
        Coroutine co;
        const float Scale = 3.2f;          // 模型 6×4 cm → 约 19×13 cm
        const float Cord = 0.07f;

        static readonly System.Collections.Generic.List<HintTag> all = new();
        void OnEnable() { if (!all.Contains(this)) all.Add(this); }
        void OnDestroy() { all.Remove(this); }

        /// <summary>结算 / 回标题时：所有提示木牌往上荡走收起（不和结算横木叠在一起）</summary>
        public static void HideAll() { foreach (var h in all.ToArray()) if (h && h.gameObject.activeSelf) h.Show(""); }

        /// <summary>已分出胜负（结算挂着）时不再挂提示</summary>
        static bool Over => Director.I && (Director.I.phase == Director.Phase.Won || Director.I.phase == Director.Phase.Lost);

        public static HintTag Build(Transform head)
        {
            var h = new GameObject("HintTag").AddComponent<HintTag>();
            h.head = head;
            h.BuildLook();
            h.gameObject.SetActive(false);
            return h;
        }

        void BuildLook()
        {
            hang = new GameObject("Hang").transform;
            hang.SetParent(transform, false);
            tag = new GameObject("Tag").transform;
            tag.SetParent(hang, false);
            tag.localPosition = Vector3.down * Cord;
            tag.localScale = Vector3.one * Scale;
            var lib = BlockLibrary.I;
            var m = lib ? lib.Meshes("H_Tag") : null;
            if (m != null && m.lod0 && lib.propMaterial)
            {
                var g = new GameObject("Look"); g.transform.SetParent(tag, false);
                g.AddComponent<MeshFilter>().sharedMesh = m.lod0;
                g.AddComponent<MeshRenderer>().sharedMaterial = lib.propMaterial;
            }
            else
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(g.GetComponent<Collider>());
                g.transform.SetParent(tag, false); g.transform.localScale = new Vector3(0.06f, 0.04f, 0.004f); g.transform.localPosition = new Vector3(0, -0.014f, 0);
                g.GetComponent<Renderer>().material.color = new Color(0.9f, 0.85f, 0.74f);
            }
            // 字：写在木牌正面（模型正面朝本地 -Z）
            var tgo = new GameObject("Text");
            tgo.transform.SetParent(tag, false);
            tgo.transform.localPosition = new Vector3(0, -0.0175f, -0.0026f);   // 让开顶上的穿绳孔
            text = tgo.AddComponent<TextMeshPro>();
            if (Fonts.Hand) text.font = Fonts.Hand;
            text.rectTransform.sizeDelta = new Vector2(0.054f, 0.026f);
            text.textWrappingMode = TextWrappingModes.NoWrap;   // 只在写好的换行处换行（自动换行会把短句再拆碎、字被缩得很小）
            text.enableAutoSizing = true; text.fontSizeMin = 0.05f; text.fontSizeMax = 0.6f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(0.13f, 0.08f, 0.05f);   // 墨色（深褐，压得住木纹）
            text.lineSpacing = -10f;
            // 绳
            cord = gameObject.AddComponent<LineRenderer>();
            cord.material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(0.55f, 0.45f, 0.32f) };
            cord.widthMultiplier = 0.0012f; cord.positionCount = 2;
            cord.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        Vector3 Goal()
        {
            if (pin.HasValue) return pin.Value;
            var f = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized; var r = Vector3.Cross(Vector3.up, f);
            // 11-26 巡检：原来在眼前偏右 17°、比眼睛略高，正好挡住投石机出手后弹道往上走的那段（第十关还挂在敌城上空）
            // → 挪到投石机（右 12～15°、低 15°）的右下方：约右 25°、低 23°。弹道从投石机往左上走，挡不着；
            //   比补给盘（低 53°）高得多，也不挡自己城。（第一次试低 12°：加上随机偏移，第十关还是盖住了投石机）
            return head.position + f * 0.5f + r * 0.24f - Vector3.up * 0.24f + jitter;
        }

        GestureCard card;
        /// <summary>
        /// 教学用：木牌下面再挂一块手势小木牌（缩小的示范手循环做这一步的动作 + 两个字 + 进度圆钉），
        /// 参考《梦境玩具战》的教学卡：一个会动的手势图 + 一句短话 + 进度点。传 null = 摘掉。
        /// </summary>
        public GestureCard Card(GestureCard.Kind? k, int done = -1, int total = 0)
        {
            if (k == null) { if (card) Destroy(card.gameObject); card = null; return null; }
            if (!card)
            {
                // 木牌下沿：按 H_Tag 网格的包围盒算（模型原点在穿绳孔）
                var lib = BlockLibrary.I;
                var m = lib ? lib.Meshes("H_Tag") : null;
                float bottom = m != null && m.lod0 ? -m.lod0.bounds.min.y * Scale : 0.13f;
                card = GestureCard.Build(hang, Vector3.down * (Cord + bottom + 0.004f), 0.012f, k.Value, 0, GestureCard.H + 0.024f);   // 比速查卡高 2.4 cm：字上面一排进度圆钉
                card.transform.localScale = Vector3.one * 1.6f;   // 约 10×14 cm，和木牌（19×13 cm）配
                card.drop = 0; card.kick = 1;
                StartCoroutine(CardIn());
            }
            else card.SetKind(k.Value);
            if (total > 0) card.SetProgress(done, total);
            return card;
        }

        IEnumerator CardIn()
        {
            yield return new WaitForSeconds(0.25f);
            for (float k = 0; card && k < 1f; k += Time.deltaTime / 0.3f) { card.drop = k; yield return null; }
            if (card) { card.drop = 1; card.kick = 1; }
        }

        /// <summary>换一句提示（中文原文，内部走 Loc）。空串 = 收起</summary>
        public void Show(string zh)
        {
            if (Over) zh = "";
            if (zh == current && gameObject.activeSelf) return;
            if (string.IsNullOrEmpty(zh) && !gameObject.activeSelf) { current = ""; return; }
            current = zh;
            if (co != null) StopCoroutine(co);
            if (!gameObject.activeSelf) { gameObject.SetActive(true); anchor = anchorGoal = Goal(); drop = 1f; }
            co = StartCoroutine(Swap(zh));
        }

        IEnumerator Swap(string zh)
        {
            // 旧牌往上荡出去
            for (float k = drop; k < 1f; k += Time.deltaTime / 0.22f) { drop = k; yield return null; }
            drop = 1f;
            if (string.IsNullOrEmpty(zh)) { gameObject.SetActive(false); yield break; }
            text.text = Loc.T(zh);
            lean = Random.Range(-5f, 5f); yaw = Random.Range(-7f, 7f);
            jitter = new Vector3(Random.Range(-0.03f, 0.03f), Random.Range(-0.02f, 0.025f), 0);
            anchor = anchorGoal = Goal();
            // 新牌从上面落下来，落定后晃几下
            for (float k = 1f; k > 0f; k -= Time.deltaTime / 0.35f) { drop = k; yield return null; }
            drop = 0f;
            swingVel = 3.2f;
        }

        void LateUpdate()
        {
            if (!head) return;
            // 头转了不少才慢慢跟过去
            var g = Goal();
            if (Vector3.Distance(g, anchorGoal) > 0.18f) anchorGoal = g;
            var prev = anchor;
            anchor = Vector3.Lerp(anchor, anchorGoal, 1f - Mathf.Exp(-Time.deltaTime * 2.5f));
            // 绳上的牌子像钟摆：跟随移动时被带着晃
            var acc = (anchor - prev) / Mathf.Max(1e-3f, Time.deltaTime);
            swingVel += (-swing * 40f - swingVel * 3f) * Time.deltaTime - Vector3.Dot(acc, head.right) * 0.6f * Time.deltaTime;
            swing += swingVel * Time.deltaTime;
            var lift = Vector3.up * (drop * drop * 0.25f);
            transform.position = anchor + lift;
            var toHead = Vector3.ProjectOnPlane(head.position - transform.position, Vector3.up);
            transform.rotation = Quaternion.LookRotation(-toHead.normalized, Vector3.up) * Quaternion.Euler(0, yaw, 0);
            hang.localRotation = Quaternion.Euler(0, 0, lean + swing * Mathf.Rad2Deg * 0.35f);
            float a = 1f - drop;
            text.alpha = a;
            tag.localScale = Vector3.one * Scale * Mathf.Lerp(0.85f, 1f, a);
            cord.SetPosition(0, transform.position + Vector3.up * 0.25f);
            cord.SetPosition(1, tag.position);
        }
    }
}

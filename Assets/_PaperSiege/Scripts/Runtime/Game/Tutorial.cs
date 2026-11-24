// 第 1 关边玩边学（方案第 6 节）：每一步挂一块提示木牌 + 半透明示范手循环演示，玩家真的做到了才进下一步。
//   举弓 → 抓皮兜 → 往右肩斜着拉 → 松手发射 → 张手挡开敌弹 → 握拳接住（弹归你）→ 拿积木补缺口 → 自由开打。
// 教学期间：敌方帅旗打不倒、计时暂停、投石机只在要教挡 / 接 / 修的时候由教学来发一颗慢弹。
using System.Collections;
using UnityEngine;

namespace PaperSiege
{
    public class Tutorial : MonoBehaviour
    {
        Director d;
        HintTag tag;
        GhostHand gl, gr;
        public int step;
        public bool done;
        Coroutine ghostCo;

        void Start()
        {
            d = Director.I;
            tag = HintTag.Build(d.head.transform);
            gl = GhostHand.Build(false);
            gr = GhostHand.Build(true);
            gl.Show(false); gr.Show(false);
            StartCoroutine(Run());
        }

        void OnDestroy()
        {
            if (tag) Destroy(tag.gameObject);
            if (gl) Destroy(gl.gameObject);
            if (gr) Destroy(gr.gameObject);
        }

        Transform H => d.head.transform;
        Vector3 Fwd => Vector3.ProjectOnPlane(H.forward, Vector3.up).normalized;
        Vector3 Right => Vector3.Cross(Vector3.up, Fwd);
        Vector3 BowPoint => H.position + H.forward * 0.40f - H.right * 0.10f - H.up * 0.10f;

        void Ghost(IEnumerator loop)
        {
            if (ghostCo != null) StopCoroutine(ghostCo);
            gl.Show(false); gr.Show(false);
            ghostCo = loop != null ? StartCoroutine(loop) : null;
        }

        IEnumerator Run()
        {
            d.arena.enemy.SetKingHP(99);   // 学完防守之前，敌方帅旗打不倒
            d.timerPaused = true;
            yield return new WaitForSeconds(1.0f);

            // 1) 举弓
            step = 1; tag.Show("左手捏住\n举起弹弓");
            Ghost(LoopBow());
            yield return new WaitUntil(() => d.sling.Holding);

            // 2) 抓皮兜
            step = 2; tag.Show("右手\n捏住皮兜");
            Ghost(LoopGrab());
            yield return new WaitUntil(() => d.sling.Drawing);

            // 3) 拉开
            step = 3; tag.Show("往右肩\n斜着拉开");
            Ghost(LoopPull());
            yield return new WaitUntil(() => d.sling.Drawing && d.sling.pull01 > 0.45f);

            // 4) 松手
            step = 4; tag.Show("瞄准对面\n松手！");
            Ghost(null);
            int shots = d.stats.shots;
            yield return new WaitUntil(() => d.stats.shots > shots);
            yield return new WaitForSeconds(2.0f);

            // 5) 挡
            step = 5;
            yield return Defend(false);

            // 6) 接
            step = 6;
            yield return Defend(true);
            tag.Show("接住的弹\n归你了");
            yield return new WaitForSeconds(3.0f);

            // 7) 修城：敌弹打碎自己城上一块 → 从补给盘拿积木补上
            step = 7;
            yield return Repair();

            // 8) 自由开打
            step = 8; tag.Show("打倒对面\n的帅旗！");
            Ghost(null);
            d.arena.enemy.SetKingHP(1);
            if (d.ammo.Total < 3) d.ammo.Add(Ammo.Stone, 3 - d.ammo.Total);
            d.timerPaused = false;
            d.timeLeft = d.level.timeLimit;
            d.catapult.Begin(d.level, d.arena.mine, d.ammo);
            yield return new WaitForSeconds(5f);
            tag.Show("");
            done = true;
        }

        // ── 挡 / 接：投石机朝自己城门上方发一颗慢弹，示范手在弹道上迎着它
        IEnumerator Defend(bool catchIt)
        {
            int blocked0 = d.stats.blocked, caught0 = d.stats.caught;
            for (int attempt = 0; ; attempt++)
            {
                tag.Show(attempt == 0 ? (catchIt ? "再来一发\n握拳接住" : "敌弹来了\n张手去挡") : "再试一次");
                var aim = GateTop();
                d.catapult.FireAt(aim, 2.2f, Ammo.Stone);   // 尽量慢（会被弧顶上限截短）
                // 等弹飞出来，示范手摆到弹道上（离城门约 18 cm 的地方）
                Projectile p = null;
                for (float t = 0; t < 4f && p == null; t += Time.deltaTime) { p = Projectile.Live.Find(x => x && x.from == Side.Enemy && x.state == Projectile.State.Flying); yield return null; }
                if (p != null)
                {
                    var path = new System.Collections.Generic.List<Vector3>();
                    Ballistics.Predict(p.transform.position, p.rb.linearVelocity, p.radius, path);
                    // 示范手迎弹的位置：自己城上方那段弹道里，离城顶约 20 cm 的那一点（胸口偏下，不举过头顶）
                    var c = d.arena.mine.transform.position;
                    float want = aim.y + 0.2f;
                    var meet = aim + Vector3.up * 0.2f;
                    float best = float.MaxValue;
                    foreach (var q in path)
                        if (new Vector2(q.x - c.x, q.z - c.z).magnitude < 0.4f && Mathf.Abs(q.y - want) < best) { best = Mathf.Abs(q.y - want); meet = q; }
                    Ghost(LoopDefend(meet, -(path[Mathf.Min(path.Count - 1, 1)] - path[0]).normalized, catchIt));
                }
                // 等结果：挡 / 接到了，或者弹落地了
                yield return new WaitUntil(() => d.stats.blocked > blocked0 || d.stats.caught > caught0 || p == null || p.state == Projectile.State.Spent);
                yield return new WaitForSeconds(0.5f);
                bool ok = catchIt ? d.stats.caught > caught0 : (d.stats.blocked > blocked0 || d.stats.caught > caught0);
                if (ok) break;
                yield return new WaitForSeconds(1.0f);
            }
            Ghost(null);
            yield return new WaitForSeconds(0.8f);
        }

        Vector3 GateTop()
        {
            var mine = d.arena.mine;
            var gate = mine.blocks.Find(b => b.name == "W_Battlement" && !b.Dead) ?? mine.blocks[0];
            return gate.WorldBounds.center;
        }

        IEnumerator Repair()
        {
            var mine = d.arena.mine;
            var target = mine.blocks.Find(b => b.name == "W_Battlement" && !b.Dead);
            int slot = target ? target.slot : 0;
            // 敌弹打过来、把它打碎
            d.catapult.FireAt(target ? target.WorldBounds.center : GateTop(), 2.0f, Ammo.Stone);
            yield return new WaitForSeconds(2.0f + GameTuning.I.windup);
            if (target && !target.Dead) target.Shatter();
            yield return new WaitForSeconds(0.8f);
            int rep0 = d.stats.repaired;
            tag.Show("城墙破了\n拿积木补上");
            Ghost(LoopRepair(slot));
            yield return new WaitUntil(() => d.stats.repaired > rep0);
            Ghost(null);
            tag.Show("补好了！");
            yield return new WaitForSeconds(1.8f);
        }

        // ── 示范手动作（循环）
        IEnumerator LoopBow()
        {
            gl.Show(true);
            while (true)
            {
                var b = BowPoint;
                var low = b - H.up * 0.22f - Fwd * 0.08f;
                for (float k = 0; k < 1f; k += Time.deltaTime / 0.7f) { PoseL(Vector3.Lerp(low, b, Mathf.SmoothStep(0, 1, k)), 0f); yield return null; }
                for (float k = 0; k < 1f; k += Time.deltaTime / 0.3f) { PoseL(b, k); yield return null; }
                for (float t = 0; t < 0.9f; t += Time.deltaTime) { PoseL(b, 1f); yield return null; }
            }
        }

        void PoseL(Vector3 at, float k)
        {
            gl.SetPose(GhostHand.Pose.Pinch, k);
            gl.Place(at, (H.forward + Vector3.up * 0.6f).normalized, Right, true);   // 手指朝前上、掌心朝右（握着弓架）
        }

        void PoseR(Vector3 at, GhostHand.Pose pose, float k, Vector3? palm = null, Vector3? fingers = null)
        {
            gr.SetPose(pose, k);
            gr.Place(at, fingers ?? (H.forward + Vector3.up * 0.3f).normalized, palm ?? -Right, pose == GhostHand.Pose.Pinch);
        }

        IEnumerator LoopGrab()
        {
            gr.Show(true);
            while (true)
            {
                var pr = d.sling.PouchRest;
                var from = pr - Fwd * 0.12f + Right * 0.12f - Vector3.up * 0.1f;
                for (float k = 0; k < 1f; k += Time.deltaTime / 0.7f) { PoseR(Vector3.Lerp(from, d.sling.PouchRest, Mathf.SmoothStep(0, 1, k)), GhostHand.Pose.Pinch, 0f); yield return null; }
                for (float k = 0; k < 1f; k += Time.deltaTime / 0.3f) { PoseR(d.sling.PouchRest, GhostHand.Pose.Pinch, k); yield return null; }
                for (float t = 0; t < 0.8f; t += Time.deltaTime) { PoseR(d.sling.PouchRest, GhostHand.Pose.Pinch, 1f); yield return null; }
            }
        }

        IEnumerator LoopPull()
        {
            gr.Show(true);
            while (true)
            {
                var fc = d.sling.ForkCenter;
                // 往右肩方向斜着拉：往后 + 往右 + 稍往下（两只手从头显看过去是错开的）
                var to = fc - Fwd * 0.24f + Right * 0.08f - Vector3.up * 0.06f;
                for (float k = 0; k < 1f; k += Time.deltaTime / 1.1f) { PoseR(Vector3.Lerp(d.sling.PouchRest, to, Mathf.SmoothStep(0, 1, k)), GhostHand.Pose.Pinch, 1f); yield return null; }
                for (float t = 0; t < 0.6f; t += Time.deltaTime) { PoseR(to, GhostHand.Pose.Pinch, 1f); yield return null; }
                gr.SetAlpha(0.3f);
                for (float t = 0; t < 0.3f; t += Time.deltaTime) yield return null;
                gr.SetAlpha(1f);
            }
        }

        IEnumerator LoopDefend(Vector3 meet, Vector3 towardBall, bool fist)
        {
            gr.Show(true);
            var palm = towardBall;                       // 掌心迎着来弹
            var fingers = Vector3.ProjectOnPlane(Vector3.up, palm).normalized;
            while (true)
            {
                var from = meet - towardBall * 0.15f - Vector3.up * 0.12f;
                for (float k = 0; k < 1f; k += Time.deltaTime / 0.5f) { PoseR(Vector3.Lerp(from, meet, Mathf.SmoothStep(0, 1, k)), fist ? GhostHand.Pose.Fist : GhostHand.Pose.Open, 0f, palm, fingers); yield return null; }
                if (fist) for (float k = 0; k < 1f; k += Time.deltaTime / 0.25f) { PoseR(meet, GhostHand.Pose.Fist, k, palm, fingers); yield return null; }
                for (float t = 0; t < 0.7f; t += Time.deltaTime) { PoseR(meet, fist ? GhostHand.Pose.Fist : GhostHand.Pose.Open, 1f, palm, fingers); yield return null; }
            }
        }

        IEnumerator LoopRepair(int slot)
        {
            gr.Show(true);
            while (true)
            {
                var piece = GameObject.Find("Patch");
                if (!piece) { yield return null; continue; }
                var a = piece.transform.position + Vector3.up * 0.02f;
                var b = d.arena.mine.SlotCenter(slot) + Vector3.up * 0.02f;
                var down = Vector3.down;
                for (float k = 0; k < 1f; k += Time.deltaTime / 0.3f) { PoseR(a + Vector3.up * 0.06f * (1 - k), GhostHand.Pose.Pinch, k, -Right, Fwd + down); yield return null; }
                for (float k = 0; k < 1f; k += Time.deltaTime / 0.9f) { PoseR(Vector3.Lerp(a, b, Mathf.SmoothStep(0, 1, k)) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.08f, GhostHand.Pose.Pinch, 1f, -Right, Fwd + down); yield return null; }
                for (float k = 1; k > 0f; k -= Time.deltaTime / 0.25f) { PoseR(b, GhostHand.Pose.Pinch, k, -Right, Fwd + down); yield return null; }
                for (float t = 0; t < 0.6f; t += Time.deltaTime) yield return null;
            }
        }
    }
}

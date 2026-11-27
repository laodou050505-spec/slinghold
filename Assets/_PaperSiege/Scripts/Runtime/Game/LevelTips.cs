// 第 2 关以后的“边玩边学”：
//   新弹第一次出现 → 桌上右后方放一个小演示台，样品积木现场演示效果（循环），上方挂木牌写效果；
//   自己的城第一次着火 → “着火了 / 拍两下灭火”；弹第一次打光 → “没弹了 / 接住敌弹来用”。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PaperSiege
{
    public class LevelTips : MonoBehaviour
    {
        Director d;
        HintTag tag, tip;
        Transform stage;
        readonly List<GameObject> samples = new();
        bool saidFire, saidEmpty;

        public static string Name(Ammo a) => a switch { Ammo.Fire => "火丸", Ammo.Ice => "冰丸", Ammo.Bomb => "炸丸", Ammo.Split => "分裂丸", _ => "石丸" };
        public static string Effect(Ammo a) => a switch
        {
            Ammo.Fire => "新弹：火丸\n木头一点就着",
            Ammo.Ice => "新弹：冰丸\n冻脆一碰就碎",
            Ammo.Bomb => "新弹：炸丸\n石墙也能炸开",
            Ammo.Split => "新弹：分裂丸\n空中一分为三",
            _ => "",
        };

        void Start()
        {
            d = Director.I;
            tip = HintTag.Build(d.head.transform);
            if (d.level.newAmmo.HasValue) StartCoroutine(NewAmmo(d.level.newAmmo.Value));
            if (!string.IsNullOrEmpty(d.level.mechTip)) StartCoroutine(MechIntro(d.level.mechTip, d.level.mechPiece));
            if (d.level.newAmmo == Ammo.Ice) iceCatapultTip = true;
        }
        bool iceCatapultTip;

        /// <summary>
        /// 机关登场（11-27）：城搭好后，说明木牌挂到这个机关正上方（在敌城上空、不挡自己城），机关自己闪 3 下暖光；
        /// 12 秒或者机关已经触发就收起。和新弹说明一样只说一句、不打断对局。
        /// </summary>
        IEnumerator MechIntro(string zh, string piece)
        {
            yield return new WaitForSeconds(2.8f);   // 等城搭好
            var e = d.arena.enemy;
            if (!e) yield break;
            var ms = e.blocks.FindAll(b => b && b.def != null && b.def.id == piece);
            if (ms.Count == 0) yield break;
            var bb = ms[0].WorldBounds; foreach (var m in ms) bb.Encapsulate(m.WorldBounds);
            var mt = HintTag.Build(d.head.transform);
            // 木牌挂在机关上方：敌城那么远，木牌要放大才看得清 —— 改挂到玩家和机关连线上、离眼睛 0.9 米处，正好叠在机关上方
            var head = d.head.transform.position;
            var dir = (bb.center + Vector3.up * bb.extents.y + Vector3.up * 0.35f - head).normalized;
            mt.pin = head + dir * 0.9f;
            mt.Show(zh);
            for (float t = 0; t < 12f && d.phase == Director.Phase.Battle; t += Time.deltaTime)
            {
                float k = t < 3.6f ? Mathf.Max(0f, Mathf.Sin(t * Mathf.PI / 0.6f)) * 0.85f : 0f;   // 前 3.6 秒闪 3 下
                foreach (var m in ms) if (m && !m.Dead) m.Hit(k);
                if (ms.TrueForAll(m => !m || m.Dead || m.burning || m.Frozen || m.Displacement > 0.5f) && t > 2f) break;   // 已经触发了
                yield return null;
            }
            mt.Show("");
            yield return new WaitForSeconds(0.6f);
            if (mt) Destroy(mt.gameObject);
        }

        void OnDestroy()
        {
            if (tag) Destroy(tag.gameObject);
            if (tip) Destroy(tip.gameObject);
            if (stage) Destroy(stage.gameObject);
        }

        void Update()
        {
            if (!d || d.phase != Director.Phase.Battle) return;
            if (!saidFire && d.arena.mine.Burning.Count > 0) { saidFire = true; StartCoroutine(Say("着火了\n拍两下灭火", () => d.arena.mine.Burning.Count == 0, 10f)); }
            if (!saidEmpty && d.ammo.Total == 0) { saidEmpty = true; StartCoroutine(Say("没弹了\n接住敌弹来用", () => d.ammo.Total > 0, 8f)); }
            // 冰丸登场那关：说完冰丸的效果后，投石机开始蓄力时提醒一句“冰丸也能冻住投石机”
            if (iceCatapultTip && !stage && d.catapult && d.catapult.Winding && d.ammo.Count(Ammo.Ice) > 0) { iceCatapultTip = false; StartCoroutine(Say("冰丸也能冻住投石机", () => d.catapult.Frozen, 6f)); }
        }

        IEnumerator Say(string zh, System.Func<bool> until, float max)
        {
            tip.Show(zh);
            for (float t = 0; t < max && !until(); t += Time.deltaTime) yield return null;
            yield return new WaitForSeconds(0.6f);
            tip.Show("");
        }

        // ── 新弹演示台
        IEnumerator NewAmmo(Ammo a)
        {
            var A = d.arena;
            var right = Vector3.Cross(Vector3.up, A.forward);
            stage = new GameObject("AmmoDemo").transform;
            stage.SetPositionAndRotation(A.tableCenter + right * 0.31f + A.forward * 0.17f, Quaternion.LookRotation(A.forward));
            tag = HintTag.Build(d.head.transform);
            tag.pin = stage.position + Vector3.up * 0.36f;   // 木牌底边离样品积木留出一拳，摆动时也碰不到
            yield return new WaitForSeconds(1.2f);
            tag.Show(Effect(a));
            StartCoroutine(SwapGhost(a));
            float shown = 0f;
            while (shown < 16f && d.phase == Director.Phase.Battle)
            {
                yield return Demo(a);
                shown += 4.5f;
            }
            tag.Show("");
            yield return new WaitForSeconds(0.6f);
            Clear();
        }

        /// <summary>
        /// 换弹示范（11-26 补：以前新弹出现只说效果、没教怎么换上）：半透明示范手去弹盒里新弹那格捏一下，循环，
        /// 玩家自己换上这种弹（或这种弹用完 / 14 秒）就收起。
        /// </summary>
        IEnumerator SwapGhost(Ammo a)
        {
            yield return new WaitForSeconds(2.5f);
            var box = d.sling ? d.sling.Box : null;
            if (!box || d.ammo.selected == a) yield break;
            var g = GhostHand.Build(true);
            var f = d.arena.forward;
            float t = 0;
            while (t < 14f && g && d.phase == Director.Phase.Battle && d.ammo.selected != a && d.ammo.Count(a) > 0)
            {
                float u = (t % 1.6f) / 1.6f;
                float k = Mathf.Sin(Mathf.Clamp01((u - 0.2f) / 0.55f) * Mathf.PI);
                var slot = box.SlotPos(a);
                g.SetAlpha(Mathf.Clamp01(t / 0.4f));
                g.SetPose(GhostHand.Pose.Pinch, k);
                g.Place(slot + Vector3.up * (0.035f * (1 - k) + 0.004f), (-Vector3.up * 0.6f + f * 0.6f).normalized, (-Vector3.up - f * 0.3f).normalized, true);
                t += Time.deltaTime;
                yield return null;
            }
            if (g) Destroy(g.gameObject);
        }

        void Clear() { foreach (var g in samples) if (g) Destroy(g); samples.Clear(); }

        Block Sample(string piece, Paint p, Vector3 local)
        {
            // 样品块：只是外观 + 状态显示（不进任何城、不参与物理）
            var lib = BlockLibrary.I;
            var go = new GameObject("Sample_" + piece);
            go.transform.SetParent(stage, false);
            go.transform.localPosition = local;
            go.transform.localScale = Vector3.one * 0.7f;
            var b = go.AddComponent<Block>();
            b.def = BlockLibrary.Def(piece); b.paint = p;
            var pm = lib ? lib.Meshes(piece) : null;
            go.AddComponent<MeshFilter>().sharedMesh = pm != null ? pm.lod0 : null;
            b.mr = go.AddComponent<MeshRenderer>();
            b.mr.sharedMaterial = lib ? lib.Paint(b.def.mat == Mat.Stone ? Paint.Stone : p) : null;
            samples.Add(go);
            return b;
        }

        IEnumerator Demo(Ammo a)
        {
            Clear();
            const float G = BlockLibrary.G * 0.7f;
            switch (a)
            {
                case Ammo.Fire:
                {
                    var b1 = Sample("W_Cube", Paint.Ochre, new Vector3(-0.5f * G, 0, 0));
                    var b2 = Sample("W_Cube", Paint.Teal, new Vector3(0.5f * G, 0, 0));
                    yield return new WaitForSeconds(0.4f);
                    Fx.Sparkle(b1.WorldBounds.center, new Color(1f, 0.6f, 0.2f), 0.03f);
                    Fx.AttachFire(b1); b1.burning = true;
                    for (float t = 0; t < 3.6f; t += Time.deltaTime)
                    {
                        b1.burn = Mathf.Min(1f, t / 3.6f);
                        if (t > 1.6f) { if (!b2.burning) { Fx.AttachFire(b2); b2.burning = true; } b2.burn = Mathf.Min(1f, (t - 1.6f) / 3.6f); }
                        yield return null;
                    }
                    Fx.DetachFire(b1); Fx.DetachFire(b2);
                    break;
                }
                case Ammo.Ice:
                {
                    var b1 = Sample("W_Pillar", Paint.Cream, Vector3.zero);
                    yield return new WaitForSeconds(0.4f);
                    Fx.IceBurst(b1.WorldBounds.center, 0.05f);
                    b1.frozenT = 9f;
                    yield return new WaitForSeconds(2.0f);
                    Fx.Shards(b1.WorldBounds, Fx.IceColor, G);   // 冻脆的块一碰就碎
                    b1.gameObject.SetActive(false);
                    yield return new WaitForSeconds(1.6f);
                    break;
                }
                case Ammo.Bomb:
                {
                    var s1 = Sample("S_Cube", Paint.Stone, new Vector3(-0.5f * G, 0, 0));
                    var s2 = Sample("S_Cube", Paint.Stone, new Vector3(0.5f * G, 0, 0));
                    var s3 = Sample("S_Cube", Paint.Stone, new Vector3(0, G, 0));
                    yield return new WaitForSeconds(1.0f);
                    var c = s3.WorldBounds.center;
                    Fx.Explosion(c, 0.06f);
                    foreach (var s in new[] { s1, s2, s3 }) { Fx.Shards(s.WorldBounds, Fx.StoneColor, G); s.gameObject.SetActive(false); }
                    yield return new WaitForSeconds(2.6f);
                    break;
                }
                case Ammo.Split:
                {
                    // 一颗弹抛起来，最高点分成三颗落下
                    var ball = BallLook.Build(stage, Ammo.Split, 0.012f); samples.Add(ball.gameObject);
                    var parts = new Transform[3];
                    for (int i = 0; i < 3; i++) { parts[i] = BallLook.Build(stage, Ammo.Stone, 0.009f); parts[i].gameObject.SetActive(false); samples.Add(parts[i].gameObject); }
                    for (float t = 0; t < 0.7f; t += Time.deltaTime) { float k = t / 0.7f; ball.localPosition = new Vector3(-0.06f + 0.06f * k, 0.12f * Mathf.Sin(k * Mathf.PI * 0.5f) + 0.01f, 0); yield return null; }
                    Fx.Sparkle(ball.position, new Color(1f, 0.9f, 0.6f), 0.02f);
                    ball.gameObject.SetActive(false);
                    var top = ball.localPosition;
                    for (int i = 0; i < 3; i++) parts[i].gameObject.SetActive(true);
                    for (float t = 0; t < 0.7f; t += Time.deltaTime)
                    {
                        float k = t / 0.7f;
                        for (int i = 0; i < 3; i++) parts[i].localPosition = top + new Vector3(0.05f * k, -0.11f * k * k, (i - 1) * 0.04f * k);
                        yield return null;
                    }
                    yield return new WaitForSeconds(1.6f);
                    break;
                }
            }
            yield return new WaitForSeconds(0.5f);
        }
    }
}

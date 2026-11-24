// 难度模拟器（方案第 11 节）：机器人按“真人的准头”和 AI 对打（真实物理、加速时间），每关统计胜率。
//   新手：瞄准误差大、接弹 30%、挡弹 40%、修城慢、不挑弹；熟练：误差小、接 70%、挡 25%（其余都接）、修城快、会挑弹（火烧木头 / 炸石墙 / 高抛越盾墙）。
// 机器人不走手势（手势已经单独测过），直接调用“发射 / 接 / 挡 / 修 / 灭火”的结果，频率和真人能做到的一样（两发之间 ≥ 装弹时间 + 瞄准时间）。
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PaperSiege
{
    public class DifficultyBot : MonoBehaviour
    {
        public struct Skill
        {
            public string name;
            public float aimErr;        // 落点误差（格，敌城格）
            public float catchP, blockP; // 敌弹飞到手边时接住 / 挡开的概率（其余 = 没挡住，打到城上）
            public float aimTime;       // 每发瞄准要多久（秒）
            public float repairEvery;   // 隔多久修一次（秒）
            public float patDelay;      // 着火后多久拍灭（秒）
            public bool smart;          // 会挑弹、会高抛
        }

        public static readonly Skill Novice = new() { name = "新手", aimErr = 1.6f, catchP = 0.3f, blockP = 0.4f, aimTime = 3.5f, repairEvery = 20f, patDelay = 6f, smart = false };
        public static readonly Skill Skilled = new() { name = "熟练", aimErr = 0.8f, catchP = 0.7f, blockP = 0.25f, aimTime = 2.2f, repairEvery = 9f, patDelay = 2.5f, smart = true };

        Director d;
        public Skill skill;
        public bool running;
        public int wins, games;
        public readonly List<float> times = new();
        System.Random rng;

        public static DifficultyBot Attach(Director d) => d.gameObject.GetComponent<DifficultyBot>() ?? d.gameObject.AddComponent<DifficultyBot>();

        public IEnumerator Run(int level, Skill s, int n, int seed, System.Action<string> report)
        {
            d = Director.I; skill = s; rng = new System.Random(seed);
            wins = 0; games = 0; times.Clear();
            running = true;
            for (int g = 0; g < n; g++)
            {
                Random.InitState(seed * 1000 + g);
                d.StartLevel(level);
                yield return null;
                // 教学关：跳过教学（直接进自由开打）
                var tut = d.GetComponent<Tutorial>();
                if (tut) { Destroy(tut); d.arena.enemy.SetKingHP(d.level.kingHP); d.timerPaused = false; d.catapult.Begin(d.level, d.arena.mine, d.ammo); }
                var tips = d.GetComponent<LevelTips>(); if (tips) Destroy(tips);
                float t0 = Time.time;
                yield return Play();
                games++;
                if (d.phase == Director.Phase.Won) { wins++; times.Add(Time.time - t0); }
            }
            running = false;
            report?.Invoke($"第{level + 1}关 {s.name}：{wins}/{games} 胜（{100f * wins / Mathf.Max(1, games):0}%）平均用时 {(times.Count > 0 ? times.Average() : 0):0}s");
        }

        IEnumerator Play()
        {
            float nextShot = skill.aimTime, nextRepair = skill.repairEvery;
            var handled = new HashSet<Projectile>();
            var fireSeen = new Dictionary<Block, float>();
            float t = 0;
            while (d.phase == Director.Phase.Battle)
            {
                float dt = Time.deltaTime; t += dt;
                // ── 防守：敌弹进入离自己城 0.5 m 时，按概率接 / 挡
                foreach (var p in Projectile.Live.ToArray())
                {
                    if (!p || !p.Catchable || handled.Contains(p)) continue;
                    // 只有真的会打到城上的弹才去挡 / 接（真人也不会去接明显打偏的弹）
                    if (Vector3.Distance(p.transform.position, d.arena.mine.transform.position + Vector3.up * 0.15f) > 0.35f) continue;
                    var path = new List<Vector3>();
                    var land = Ballistics.Predict(p.transform.position, p.rb.linearVelocity, p.radius, path, 1.5f);
                    var mb = d.arena.mine.transform.position;
                    if (Vector2.Distance(new Vector2(land.x, land.z), new Vector2(mb.x, mb.z)) > 0.25f) continue;
                    handled.Add(p);
                    double r = rng.NextDouble();
                    if (r < skill.catchP) { d.ammo.Add(p.type); d.stats.caught++; Destroy(p.gameObject); }
                    else if (r < skill.catchP + skill.blockP) { p.Deflect(Vector3.up * 1.5f + d.arena.forward * -0.5f); d.stats.blocked++; }
                }
                // ── 灭火：着火后 patDelay 秒拍灭
                foreach (var b in d.arena.mine.Burning.ToArray())
                {
                    if (!fireSeen.ContainsKey(b)) fireSeen[b] = t;
                    if (t - fireSeen[b] > skill.patDelay) { b.Extinguish(); d.stats.extinguished++; fireSeen.Remove(b); }
                }
                // ── 修城
                if (t >= nextRepair)
                {
                    nextRepair = t + skill.repairEvery;
                    var mine = d.arena.mine;
                    for (int i = 0; i < mine.layout.items.Count; i++)
                        if (mine.SlotEmpty(i) && d.supply.Count > 0) { d.supply.TakeOne(); mine.Refill(i); d.stats.repaired++; break; }
                }
                // ── 进攻
                if (t >= nextShot && d.ammo.Total > 0)
                {
                    nextShot = t + Mathf.Max(GameTuning.I.reload, skill.aimTime) * (0.85f + 0.3f * (float)rng.NextDouble());
                    Shoot();
                }
                yield return null;
            }
        }

        void Shoot()
        {
            var e = d.arena.enemy;
            var alive = e.Alive.Where(b => !b.isKing).ToList();
            // 挑目标：熟练 = 先拆挡在帅旗前面的墙、再打帅旗；新手 = 随便挑高处的块或者直接打旗
            Vector3 target;
            Ammo a = Ammo.Stone;
            var kingPos = e.king.WorldBounds.center + Vector3.up * 0.5f * e.unit;
            bool highArc = false;
            if (skill.smart)
            {
                // 先看这条线能不能打到帅旗的旗面（露在城头的那个命中区）；能打就直接打旗（高抛越墙），
                // 打不到才拆挡住弹道的那一块 —— 和真人看着预瞄线做的事一样
                var flagPt = e.king.WorldBounds.center + Vector3.up * 0.05f;
                var from0 = d.arena.tableCenter + Vector3.up * 0.35f;
                var dirToKing = (flagPt - from0).normalized;
                bool los = !Physics.SphereCast(from0, 0.006f, dirToKing, out var lh, Vector3.Distance(from0, flagPt) - 0.06f, Layers.BlockMask)
                           || (lh.collider.GetComponent<Block>() is Block hb && hb.isKing);
                if (los) { target = flagPt; highArc = true; }
                else
                {
                    // 挡住的那块：沿视线查到的第一个非帅旗积木
                    var blocking = Physics.SphereCast(from0, 0.006f, dirToKing, out var bh, 8f, Layers.BlockMask) ? bh.collider.GetComponent<Block>() : null;
                    var pick = blocking ?? alive.OrderBy(b => Vector3.Distance(b.WorldBounds.center, flagPt)).FirstOrDefault();
                    target = pick ? pick.WorldBounds.center : flagPt;
                    if (pick && pick.def.mat == Mat.Stone && d.ammo.Count(Ammo.Bomb) > 0) a = Ammo.Bomb;
                    else if (pick && pick.Wood && d.ammo.Count(Ammo.Fire) > 0) a = Ammo.Fire;
                    highArc = false;
                }
                if (a == Ammo.Stone && d.ammo.Count(Ammo.Stone) == 0) a = d.ammo.AutoSelect();
            }
            else
            {
                // 新手：知道要打帅旗那一片（真人新手也会往城中间打），但瞄不准、不太会用高抛 —— 误差靠 aimErr 体现
                var nearKing = alive.Where(b => Vector3.Distance(b.WorldBounds.center, kingPos) < 2.5f * e.unit).ToList();
                target = rng.NextDouble() < 0.6 || nearKing.Count == 0
                    ? kingPos
                    : nearKing[rng.Next(nearKing.Count)].WorldBounds.center;
                a = d.ammo.AutoSelect();
                highArc = rng.NextDouble() < 0.6;
            }
            if (d.ammo.Count(a) == 0) a = d.ammo.AutoSelect();
            if (d.ammo.Count(a) == 0) return;
            // 落点误差：水平方向高斯
            float g1 = (float)(rng.NextDouble() + rng.NextDouble() + rng.NextDouble() - 1.5) * 1.15f;
            float g2 = (float)(rng.NextDouble() + rng.NextDouble() + rng.NextDouble() - 1.5) * 1.15f;
            var right = Vector3.Cross(Vector3.up, d.arena.forward);
            target += (right * g1 + d.arena.forward * g2) * skill.aimErr * e.unit;
            var from = d.arena.tableCenter + Vector3.up * 0.35f + d.arena.forward * 0.05f;
            d.ammo.Take(a);
            d.DebugShoot(a, from, target, Mathf.Lerp(GameTuning.I.slingSpeedMin, GameTuning.I.slingSpeedMax, 0.85f), highArc);   // DebugShoot 会计一次射击
        }
    }
}

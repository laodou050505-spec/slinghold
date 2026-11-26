// 总调度：开局（摆场地、搭城、发弹药）→ 对轰（3 分钟）→ 结束（帅旗倒下 / 时间到比完整度）。
using System;
using UnityEngine;

namespace PaperSiege
{
    public class Director : MonoBehaviour
    {
        public static Director I { get; private set; }
        public enum Phase { Idle, Title, Battle, Won, Lost }

        public Arena arena;
        public Camera head;
        public int levelIndex;
        public bool autoStart = true;
        public LevelDef level;
        public Phase phase = Phase.Idle;
        public float timeLeft;
        public bool timerPaused;
        /// <summary>这一局是时间到了结束的（结算板上写“时间到”而不是“失守”）</summary>
        public bool timeUp;
        public readonly AmmoStore ammo = new();
        /// <summary>正在播结局（第十关打下王城之后）</summary>
        public bool ending;
        /// <summary>对局中按了暂停（时间冻住，暂停板挂着）</summary>
        public bool paused;
        public EnemyCatapult catapult;
        public PlayerSling sling;
        public SkyDome sky;
        public Defense defense;
        public SupplyTray supply;
        public Stats stats = new();
        public event Action<Phase> PhaseChanged;
        CoverMenu title;
        ResultBoard result;
        bool laidOut;
        GameTuning T => GameTuning.I;

        [Serializable]
        public class Stats { public int shots, blocked, caught, repaired, extinguished; public float bestShot; }

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        void Start()
        {
            Progress.LoadLanguage();
            Music.Build(this);
            gameObject.AddComponent<HandLostHint>();   // 两手跟丢 2.5 秒 → 提示把手抬到眼前
            gameObject.AddComponent<FocusPause>();     // 摘头显 / 系统菜单 → 冻结对局
            gameObject.AddComponent<PauseMenu>();      // 对局中的“暂停”木签 + 暂停板（继续 / 重来 / 回标题 + 手势速查小木牌）
            if (autoStart) GoTitle();
            // 编辑器里：菜单“演示”→ 自动演示；直接点播放 → 桌面试玩（鼠标键盘模拟双手）
            if (Application.isEditor && HandInput.Sim)
            {
                if (DemoDriver.Requested) gameObject.AddComponent<DemoDriver>();
                else gameObject.AddComponent<DesktopPlay>();
                if (EndingPreview.Requested) { EndingPreview.Requested = false; gameObject.AddComponent<EndingPreview>(); }
            }
        }

        /// <summary>标题页（封面 D：帅旗灯笼小场景）：盒盖合着，房间里只浮着封面那组实物；城和投石机都不摆（封面不出现游戏场景），开局时城一块块搭起来</summary>
        public void GoTitle()
        {
            ending = false;
            paused = false; Time.timeScale = 1f;
            if (result) { result.Close(); result = null; }
            foreach (var p in Projectile.Live.ToArray()) if (p) Destroy(p.gameObject);
            if (!sky) { sky = SkyDome.Build(this, FindAnyObjectByType<Light>()); arena.sky = sky; }
            if (!laidOut) { arena.Layout(); laidOut = true; }
            levelIndex = Progress.Unlocked;
            level = Levels.Get(levelIndex);
            arena.ClearCastles();
            if (catapult) { catapult.Stop(); catapult.gameObject.SetActive(false); }
            foreach (var c in new MonoBehaviour[] { GetComponent<Tutorial>(), GetComponent<LevelTips>() }) if (c) Destroy(c);
            sky.Close();
            if (title) Destroy(title.gameObject);
            // 标题页桌上只放按钮：上一局的补给盘、弹盒收起来（开局时各自的 Setup 会重新摆出来）
            if (supply) supply.HideForTitle();
            if (sling && sling.Box) sling.Box.Show(false);
            title = CoverMenu.Build(this);
            SetPhase(Phase.Title);
        }

        public void StartLevel(int i)
        {
            ending = false;
            paused = false; Time.timeScale = 1f;
            if (title) { title.Close(); title = null; }
            if (result) { result.Close(); result = null; }
            levelIndex = Mathf.Clamp(i, 0, Levels.Count - 1);
            level = Levels.Get(levelIndex);
            foreach (var p in Projectile.Live.ToArray()) if (p) Destroy(p.gameObject);
            if (!sky) { sky = SkyDome.Build(this, FindAnyObjectByType<Light>()); arena.sky = sky; }
            if (!laidOut) { arena.Layout(); laidOut = true; }
            arena.BuildCastles(level);
            ammo.Reset(level.start);
            stats = new Stats();
            sky.OnLevel(level);   // 掀盒盖 + 切这一关的时段（重开同一关也重新掀）
            if (!catapult) catapult = EnemyCatapult.Build(arena.catapultSpot, T.enemyUnit);
            catapult.gameObject.SetActive(true);
            catapult.transform.SetPositionAndRotation(arena.catapultSpot.position, arena.catapultSpot.rotation);
            catapult.flightScale = () => ammo.Total == 0 ? T.mercySlow : 1f;
            timerPaused = false;
            // 同一帧里连开两次局时，上一次加的组件还没真正销毁 → 全部删掉，别只删第一个
            foreach (var c in GetComponents<Tutorial>()) Destroy(c);
            foreach (var c in GetComponents<LevelTips>()) Destroy(c);
            if (level.tutorial) { catapult.Aim(level, arena.mine); gameObject.AddComponent<Tutorial>(); }   // 教学关：投石机由教学控制
            else { catapult.Begin(level, arena.mine, ammo); gameObject.AddComponent<LevelTips>(); }   // 其余关：新弹演示、着火 / 没弹提示
            arena.mine.KingDown += _ => End(false);
            arena.enemy.KingDown += _ => End(true);
            if (!sling) sling = gameObject.AddComponent<PlayerSling>();
            sling.Setup(this);
            if (!defense) defense = gameObject.AddComponent<Defense>();
            defense.Setup(this);
            if (!supply) supply = gameObject.AddComponent<SupplyTray>();
            supply.Setup(this);
            timeLeft = level.timeLimit;
            timeUp = false;
            SetPhase(Phase.Battle);
        }

        void SetPhase(Phase p) { phase = p; PhaseChanged?.Invoke(p); }

        void Update()
        {
            if (phase != Phase.Battle) return;
            if (!timerPaused) timeLeft -= Time.deltaTime;
            // 时间到了 = 没打下对方的帅旗 = 这一关没过（唯一的赢法就是打倒帅旗；
            // 原来“比完整度”会让玩家躲满三分钟就赢，帅旗防护全失去意义）
            if (timeLeft <= 0f) { timeUp = true; End(false); }
        }

        public void End(bool win)
        {
            if (phase != Phase.Battle) return;
            catapult.Stop();
            SetPhase(win ? Phase.Won : Phase.Lost);
            if (sky && !(win && levelIndex == Levels.Count - 1)) sky.OnEnd(win);
            HintTag.HideAll();
            if (win) { Progress.SetStars(levelIndex, ResultBoard.Score(this, true)); if (levelIndex + 1 > Progress.Unlocked) Progress.Unlocked = levelIndex + 1; }
            if (win && levelIndex == Levels.Count - 1) { StartCoroutine(Ending()); return; }
            StartCoroutine(ShowResult(win));
            Debug.Log($"[PaperSiege] 第 {levelIndex + 1} 关 {(win ? "胜" : "负")}  我方完整度 {arena.mine.Integrity:0.00} 敌方 {arena.enemy.Integrity:0.00}  射 {stats.shots} 挡 {stats.blocked} 接 {stats.caught}");
        }

        /// <summary>
        /// 结局（简单版）：王城帅旗倒下 → 剩下的王城整座塌下来（每块轻轻一推，有先后）→ 满天烟花 →
        /// 烟花渐稀、天慢慢亮回清晨（音乐换回清晨那首）→ 终章横木垂下：“全胜”布旗 + “十城尽破”+ 总星数 + 回标题。
        /// </summary>
        System.Collections.IEnumerator Ending()
        {
            ending = true;
            Progress.Cleared = true;
            if (sky) sky.Ending();
            yield return new WaitForSeconds(0.8f);
            var e = arena.enemy;
            if (e)
            {
                var c = e.transform.position;
                var bl = new System.Collections.Generic.List<Block>(e.blocks);
                bl.Sort((x, y) => (y ? y.transform.position.y : 0).CompareTo(x ? x.transform.position.y : 0));   // 从高处先塌
                Sfx.Collapse(c);
                for (int i = 0; i < bl.Count; i++)
                {
                    var b = bl[i];
                    if (!b || b.Dead) continue;
                    b.Wake();
                    if (b.rb)
                    {
                        // 往外散开 + 往上掀一点；正中间的块“往外”几乎为零 → 随机往前或往后倒（11-26 第一版只倒了几座尖塔，城墙还立着）
                        var away = Vector3.ProjectOnPlane(b.transform.position - c, Vector3.up);
                        if (away.magnitude < e.unit * 1.5f) away = arena.forward * (UnityEngine.Random.value < 0.5f ? 1f : -1f);
                        away.Normalize();
                        float up = 0.35f + 0.25f * Mathf.Clamp01((b.transform.position.y - c.y) / (e.unit * 8f));   // 越高的块掀得越起
                        b.rb.AddForce((away * 1.1f + Vector3.up * up + UnityEngine.Random.insideUnitSphere * 0.35f) * b.rb.mass * 1.5f, ForceMode.Impulse);
                        b.rb.AddTorque(UnityEngine.Random.insideUnitSphere * b.rb.mass * 0.05f, ForceMode.Impulse);
                    }
                    if (i % 6 == 0) yield return new WaitForSeconds(0.05f);
                }
            }
            yield return new WaitForSeconds(9.5f);
            if (phase == Phase.Won) result = ResultBoard.Build(this, true, true);
        }

        System.Collections.IEnumerator ShowResult(bool win)
        {
            yield return new WaitForSeconds(win ? 2.5f : 2.0f);   // 先看完倒塌 / 烟花
            if (phase == Phase.Won || phase == Phase.Lost) result = ResultBoard.Build(this, win);
        }

        /// <summary>调试 / 自动测试：从 from 用给定速度大小打 at（取低抛解；打不到就用最大速度 45°）</summary>
        public Projectile DebugShoot(Ammo a, Vector3 from, Vector3 at, float speed, bool high = false)
        {
            Vector3 v;
            if (!Ballistics.Solve(from, at, speed, out var lo, out var hi)) v = (Vector3.ProjectOnPlane(at - from, Vector3.up).normalized + Vector3.up).normalized * speed;
            else v = high ? hi : lo;
            stats.shots++;
            return Projectile.Spawn(a, Side.Player, from, v);
        }
    }
}

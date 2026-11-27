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
        void OnDestroy() { if (I == this) I = null; MRRig.Recentered -= OnRecentered; }

        /// <summary>场地要不要重摆：第一次；系统回中过；或者上次没读到真实桌子、现在房间扫描里有桌子了</summary>
        bool layoutDirty;
        bool NeedLayout(bool atTitle) => !laidOut || layoutDirty || (atTitle && !arena.usingRealTable && RoomTable.I && RoomTable.I.TableFound);
        void DoLayout() { arena.Layout(); laidOut = true; layoutDirty = false; }

        System.Collections.IEnumerator BootWhenReady()
        {
            if (!HandInput.Sim)
            {
                float t0 = Time.unscaledTime, still = 0f;
                var last = head.transform.position;
                string why = "超时";
                while (Time.unscaledTime - t0 < 8f)
                {
                    float el = Time.unscaledTime - t0;
                    var p = head.transform.position;
                    still = (p - last).magnitude < 0.004f ? still + Time.unscaledDeltaTime : 0f;   // 头基本不动了（每帧 < 4 mm）
                    last = p;
                    bool pose = head.transform.localPosition.sqrMagnitude > 1e-4f;             // 追踪已经给过位姿
                    bool floor = MRRig.FloorSet || el > 3f;
                    bool room = !RoomTable.I || RoomTable.I.FirstLoadDone || el > 4f;
                    if (pose && floor && room && still > 0.3f) { why = "就绪"; break; }
                    yield return null;
                }
                MRRig.FixEyeHeight();
                Debug.Log($"[木垒][启动] {why} 用时 {Time.unscaledTime - t0:0.0}s 头={head.transform.position} 地面原点={MRRig.FloorSet} 房间={(RoomTable.I ? RoomTable.I.Status : "无")}");
            }
            GoTitle();
        }

        /// <summary>玩家在系统里“回中”：追踪空间整体挪了，场地要重新摆到玩家面前。标题页上立刻重摆；对局中等下一次开局 / 回标题</summary>
        void OnRecentered()
        {
            layoutDirty = true;
            Debug.Log("[木垒] 系统回中 → 场地待重摆 phase=" + phase);
            if (phase == Phase.Title) GoTitle();
        }

        void Start()
        {
            Progress.LoadLanguage();
            Progress.Migrate();   // 10 关 → 18 关的老存档
            Music.Build(this);
            gameObject.AddComponent<HandLostHint>();   // 两手跟丢 2.5 秒 → 提示把手抬到眼前
            gameObject.AddComponent<FocusPause>();     // 摘头显 / 系统菜单 → 冻结对局
            gameObject.AddComponent<PauseMenu>();      // 对局中的“暂停”木签 + 暂停板（继续 / 重来 / 回标题 + 手势速查小木牌）
            MRRig.Recentered += OnRecentered;
            // 头显上不能第一帧就摆场地：那时头部位姿可能还在原点、地面原点还没设好、房间扫描里的桌子还没读回来（异步）。
            // 等这几样就绪（最多 8 秒）再进标题页；编辑器里立刻进。
            if (autoStart) StartCoroutine(BootWhenReady());
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
            if (NeedLayout(true)) DoLayout();
            levelIndex = Progress.Unlocked;
            level = Levels.Get(levelIndex);
            arena.ClearCastles();
            if (catapult) { catapult.Stop(); catapult.gameObject.SetActive(false); }
            foreach (var c in new MonoBehaviour[] { GetComponent<Tutorial>(), GetComponent<LevelTips>() }) if (c) Destroy(c);
            sky.Close();
            if (title) { title.Close(); title = null; }   // 有收起过程（重摆场地时旧封面也是收起来，不是凭空消失）
            // 标题页桌上只放按钮：上一局的补给盘、弹盒收起来（开局时各自的 Setup 会重新摆出来）
            if (supply) supply.HideForTitle();
            if (sling && sling.Box) sling.Box.Show(false);
            title = CoverMenu.Build(this);
            SetPhase(Phase.Title);
        }

        public void StartLevel(int i)
        {
            ending = false;
            Mech.ResetLevel();
            paused = false; Time.timeScale = 1f;
            if (title) { title.Close(); title = null; }
            if (result) { result.Close(); result = null; }
            levelIndex = Mathf.Clamp(i, 0, Levels.Count - 1);
            level = Levels.Get(levelIndex);
            foreach (var p in Projectile.Live.ToArray()) if (p) Destroy(p.gameObject);
            if (!sky) { sky = SkyDome.Build(this, FindAnyObjectByType<Light>()); arena.sky = sky; }
            if (NeedLayout(false)) DoLayout();
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
        /// <summary>
        /// 结局（11-27 用户：打败之后太仓促，要有一个让它散去的过程）。约 17 秒，五段：
        ///   ① 0～0.8 s 帅旗倒下，静一下；
        ///   ② 0.8～2.4 s 整座王城开始晃，越晃越厉害，缝里一阵阵冒灰（预警）；
        ///   ③ 2.4～5.4 s 从最高的尖塔往下一层层垮掉，每层一阵积木声，最后一大团尘土腾起来（发生）；
        ///   ④ 5.5 s 起满天烟花，8 s 起天慢慢亮、晨光从天窗斜照进来；
        ///   ⑤ 9～15 s 废墟里的积木一块块轻轻飘起、转着缩小，化成金色光点随风散去，投石机也一起散掉 —— 地上空了（收尾）；
        ///   ⑥ 16.5 s 终章横木垂下。
        /// </summary>
        System.Collections.IEnumerator Ending()
        {
            ending = true;
            Progress.Cleared = true;
            if (sky) sky.Ending(5.5f, 8f);
            var e = arena.enemy;
            yield return new WaitForSeconds(0.8f);
            if (!e) { yield return new WaitForSeconds(15f); if (phase == Phase.Won) result = ResultBoard.Build(this, true, true); yield break; }
            float U = e.unit;
            var c = e.transform.position;
            var all = new System.Collections.Generic.List<Block>();
            foreach (var b in e.blocks) if (b && !b.Dead) all.Add(b);
            // ② 晃：还没倒的积木原地抖，幅度越来越大；缝里冒灰
            var home = new System.Collections.Generic.Dictionary<Block, (Vector3 p, Quaternion r)>();
            foreach (var b in all) if (b.rb && b.rb.isKinematic) home[b] = (b.transform.position, b.transform.rotation);
            Sfx.Tremble(c);
            float dustT = 0;
            for (float t = 0; t < 1.6f; t += Time.deltaTime)
            {
                float k = t / 1.6f, amp = Mathf.Lerp(0.02f, 0.09f, k * k) * U;
                foreach (var kv in home)
                {
                    if (!kv.Key || !kv.Key.rb || !kv.Key.rb.isKinematic) continue;
                    float ph = (kv.Key.slot + 3) * 0.37f;   // 每块的晃动错开相位
                    kv.Key.transform.SetPositionAndRotation(
                        kv.Value.p + new Vector3(Mathf.Sin(t * 37f + ph), 0, Mathf.Cos(t * 31f + ph * 1.3f)) * amp,
                        kv.Value.r * Quaternion.Euler(Mathf.Sin(t * 29f + ph) * amp / U * 6f, 0, Mathf.Cos(t * 23f + ph) * amp / U * 6f));
                }
                if ((dustT -= Time.deltaTime) <= 0f && all.Count > 0)
                {
                    dustT = Mathf.Lerp(0.35f, 0.1f, k);
                    var pick = all[UnityEngine.Random.Range(0, all.Count)];
                    if (pick) Fx.Dust(pick.WorldBounds.center, U * 0.8f, 5);
                }
                yield return null;
            }
            foreach (var kv in home) if (kv.Key && kv.Key.rb && kv.Key.rb.isKinematic) kv.Key.transform.SetPositionAndRotation(kv.Value.p, kv.Value.r);
            // ③ 从高到低一层层垮：按高度分 6 层，每层间隔 0.5 s，力度只够让它们往外倒（不炸飞）
            all.Sort((x, y) => (y ? y.transform.position.y : 0).CompareTo(x ? x.transform.position.y : 0));
            int layers = 6;
            for (int L = 0; L < layers; L++)
            {
                int i0 = all.Count * L / layers, i1 = all.Count * (L + 1) / layers;
                var lb = new Bounds(c, Vector3.zero); bool any = false;
                for (int i = i0; i < i1; i++)
                {
                    var b = all[i];
                    if (!b || b.Dead) continue;
                    b.Wake();
                    if (!b.rb) continue;
                    var away = Vector3.ProjectOnPlane(b.transform.position - c, Vector3.up);
                    if (away.magnitude < U * 1.5f) away = arena.forward * (UnityEngine.Random.value < 0.5f ? 1f : -1f);
                    away.Normalize();
                    float hk = Mathf.Clamp01((b.transform.position.y - c.y) / (U * 8f));
                    b.rb.AddForce((away * (0.8f + 0.5f * hk) + Vector3.up * 0.2f + UnityEngine.Random.insideUnitSphere * 0.3f) * b.rb.mass * 1.4f, ForceMode.Impulse);   // 11-27 第一版力太小，城墙大半还立着
                    b.rb.AddTorque(UnityEngine.Random.insideUnitSphere * b.rb.mass * 0.04f, ForceMode.Impulse);
                    if (!any) { lb = b.WorldBounds; any = true; } else lb.Encapsulate(b.WorldBounds);
                }
                if (any) { Sfx.Hit(Ammo.Stone, L < 2 ? Mat.Wood : Mat.Stone, lb.center); Fx.Dust(lb.center, U * 1.2f, 8); }
                if (L == 1) Sfx.Collapse(c);
                yield return new WaitForSeconds(0.5f);
            }
            yield return new WaitForSeconds(0.6f);
            Fx.Dust(c + Vector3.up * U, U * 3.5f, 26);   // 一大团尘土
            // ④ 烟花、天亮由 SkyDome.Ending 按时间放；这里等到 9 s
            yield return new WaitForSeconds(2.4f);
            // ⑤ 散去：一块块飘起、缩小、化成光点（离玩家远的先走，最后是帅旗）
            var rest = new System.Collections.Generic.List<Block>();
            foreach (var b in e.blocks) if (b && !b.Dead) rest.Add(b);
            var head = this.head ? this.head.transform.position : Vector3.zero;
            rest.Sort((x, y) => (Vector3.Distance(y.transform.position, head) + UnityEngine.Random.value * 0.6f).CompareTo(Vector3.Distance(x.transform.position, head) + UnityEngine.Random.value * 0.6f));
            var kingB = e.king;
            if (kingB) { rest.Remove(kingB); rest.Add(kingB); }
            float span = 5.0f;
            for (int i = 0; i < rest.Count; i++)
            {
                if (rest[i]) StartCoroutine(Disperse(rest[i], U, rest[i] == kingB));
                if (i == rest.Count / 2 && catapult) StartCoroutine(DisperseObject(catapult.transform, U * 4f));
                yield return new WaitForSeconds(span / Mathf.Max(1, rest.Count));
            }
            yield return new WaitForSeconds(2.0f);
            if (phase == Phase.Won) result = ResultBoard.Build(this, true, true);
        }

        /// <summary>一块积木散去：停住物理 → 轻轻飘起 4～8 cm、慢慢转 → 缩小到没，同时冒一小撮金色光点</summary>
        System.Collections.IEnumerator Disperse(Block b, float U, bool king)
        {
            if (!b) yield break;
            if (b.rb) { b.rb.isKinematic = true; b.rb.detectCollisions = false; }
            var t0 = b.transform; var p0 = t0.position; var r0 = t0.rotation; var s0 = t0.localScale;
            var spin = UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(40f, 90f);
            float rise = UnityEngine.Random.Range(0.04f, 0.08f) * (king ? 1.6f : 1f), dur = king ? 1.6f : 1.1f;
            var drift = Vector3.ProjectOnPlane(UnityEngine.Random.onUnitSphere, Vector3.up) * 0.03f + arena.forward * 0.02f;
            Fx.Sparkle(b.WorldBounds.center, new Color(1f, 0.86f, 0.55f), U * 0.5f);
            for (float t = 0; t < dur && b; t += Time.deltaTime)
            {
                float k = t / dur, e = Mathf.SmoothStep(0, 1, k);
                t0.SetPositionAndRotation(p0 + Vector3.up * rise * e + drift * e, r0 * Quaternion.Euler(spin * k));
                t0.localScale = s0 * (1f - Mathf.Pow(Mathf.Clamp01((k - 0.25f) / 0.75f), 1.5f));
                if (k > 0.55f && k - Time.deltaTime / dur <= 0.55f) Fx.Sparkle(t0.position, new Color(1f, 0.9f, 0.65f), U * 0.35f);
                yield return null;
            }
            if (b) b.gameObject.SetActive(false);
        }

        System.Collections.IEnumerator DisperseObject(Transform tr, float size)
        {
            if (!tr) yield break;
            var p0 = tr.position; var s0 = tr.localScale;
            Fx.Dust(p0 + Vector3.up * size * 0.3f, size * 0.25f, 10);
            for (float t = 0; t < 1.4f && tr; t += Time.deltaTime)
            {
                float k = t / 1.4f;
                tr.position = p0 + Vector3.up * 0.05f * Mathf.SmoothStep(0, 1, k);
                tr.localScale = s0 * (1f - Mathf.Pow(Mathf.Clamp01((k - 0.2f) / 0.8f), 1.5f));
                yield return null;
            }
            if (tr) { tr.localScale = s0; tr.gameObject.SetActive(false); }
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

// 总调度：开局（摆场地、搭城、发弹药）→ 对轰（3 分钟）→ 结束（帅旗倒下 / 时间到比完整度）。
using System;
using UnityEngine;

namespace PaperSiege
{
    public class Director : MonoBehaviour
    {
        public static Director I { get; private set; }
        public enum Phase { Idle, Battle, Won, Lost }

        public Arena arena;
        public Camera head;
        public int levelIndex;
        public bool autoStart = true;
        public LevelDef level;
        public Phase phase = Phase.Idle;
        public float timeLeft;
        public readonly AmmoStore ammo = new();
        public EnemyCatapult catapult;
        public PlayerSling sling;
        public Defense defense;
        public SupplyTray supply;
        public Stats stats = new();
        public event Action<Phase> PhaseChanged;
        bool laidOut;
        GameTuning T => GameTuning.I;

        [Serializable]
        public class Stats { public int shots, blocked, caught, repaired, extinguished; public float bestShot; }

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; }

        void Start()
        {
            if (autoStart) StartLevel(levelIndex);
            if (Application.isEditor && DemoDriver.Requested) gameObject.AddComponent<DemoDriver>();
        }

        public void StartLevel(int i)
        {
            levelIndex = Mathf.Clamp(i, 0, Levels.Count - 1);
            level = Levels.Get(levelIndex);
            foreach (var p in Projectile.Live.ToArray()) if (p) Destroy(p.gameObject);
            if (!laidOut) { arena.Layout(); laidOut = true; }
            arena.BuildCastles(level);
            ammo.Reset(level.start);
            stats = new Stats();
            if (!catapult) catapult = EnemyCatapult.Build(arena.catapultSpot, T.enemyUnit);
            catapult.transform.SetPositionAndRotation(arena.catapultSpot.position, arena.catapultSpot.rotation);
            catapult.flightScale = () => ammo.Total == 0 ? T.mercySlow : 1f;
            catapult.Begin(level, arena.mine, ammo);
            arena.mine.KingDown += _ => End(false);
            arena.enemy.KingDown += _ => End(true);
            if (!sling) sling = gameObject.AddComponent<PlayerSling>();
            sling.Setup(this);
            if (!defense) defense = gameObject.AddComponent<Defense>();
            defense.Setup(this);
            if (!supply) supply = gameObject.AddComponent<SupplyTray>();
            supply.Setup(this);
            timeLeft = level.timeLimit;
            SetPhase(Phase.Battle);
        }

        void SetPhase(Phase p) { phase = p; PhaseChanged?.Invoke(p); }

        void Update()
        {
            if (phase != Phase.Battle) return;
            timeLeft -= Time.deltaTime;
            if (timeLeft <= 0f) End(arena.enemy.Integrity < arena.mine.Integrity);
        }

        public void End(bool win)
        {
            if (phase != Phase.Battle) return;
            catapult.Stop();
            SetPhase(win ? Phase.Won : Phase.Lost);
            Debug.Log($"[PaperSiege] 第 {levelIndex + 1} 关 {(win ? "胜" : "负")}  我方完整度 {arena.mine.Integrity:0.00} 敌方 {arena.enemy.Integrity:0.00}  射 {stats.shots} 挡 {stats.blocked} 接 {stats.caught}");
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

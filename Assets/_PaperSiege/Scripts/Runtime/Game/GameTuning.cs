// 玩法数值（同事装机后可直接调）：Assets/_PaperSiege/Resources/GameTuning.asset
using UnityEngine;

namespace PaperSiege
{
    [CreateAssetMenu(menuName = "PaperSiege/Game Tuning", fileName = "GameTuning")]
    public class GameTuning : ScriptableObject
    {
        [Header("场地")]
        [Tooltip("玩家城每格多少米")] public float playerUnit = 0.05f;
        [Tooltip("敌城每格多少米（比玩家城大，3 米外也看得清）。改大小不改难度：玩家弹的撞击质量按它的立方自动换算")] public float enemyUnit = 0.115f;
        [Tooltip("敌城离玩家城多远（米）")] public float enemyDistance = 3.2f;
        [Tooltip("房间小时最近距离（米）")] public float enemyDistanceMin = 1.8f;
        [Tooltip("没读到桌子时的虚拟桌高（米）")] public float tableHeight = 0.85f;

        [Header("弹")]
        [Tooltip("弹的重力倍率（<1 弧线更慢更好看、更好接）")] public float shotGravity = 0.55f;
        [Tooltip("玩家弹半径（米）")] public float playerBallRadius = 0.014f;
        [Tooltip("敌弹半径（米），大一点好接")] public float enemyBallRadius = 0.02f;
        [Tooltip("玩家弹质量（kg，按敌城每格 9 cm 时的手感；实际按 enemyUnit 的立方换算）")] public float playerBallMass = 0.3f;
        public const float ImpactRefUnit = 0.09f;
        /// <summary>打敌城用的弹质量：敌城积木按体积变重，弹也按同样比例变重，每发打塌的块数不变</summary>
        public float PlayerImpactMass => playerBallMass * Mathf.Pow(enemyUnit / ImpactRefUnit, 3);
        [Tooltip("敌弹打我方城的威力（相对玩家弹打敌城；1 = 一样强。玩家还能挡 / 接，所以默认略弱）")] public float enemyPower = 0.8f;
        /// <summary>敌弹撞击质量：按我方城积木体积换算，让一发石丸打我方城的效果 ≈ 玩家石丸打敌城 × enemyPower</summary>
        public float EnemyImpactMass => playerBallMass * Mathf.Pow(playerUnit / ImpactRefUnit, 3) * enemyPower
                                        * (slingSpeedMax / Mathf.Max(1f, EnemyImpactSpeed)) * 1.0f;
        /// <summary>敌弹落到我方城时的大致速度（米/秒）</summary>
        public float EnemyImpactSpeed => 3.2f;
        [Tooltip("命中时额外给被打的块的冲量倍率")] public float hitImpulse = 1.5f;
        [Tooltip("命中后叫醒周围多大范围（格）")] public float wakeRadius = 1.4f;
        [Tooltip("冰冻持续（秒）")] public float freezeSeconds = 8f;
        [Tooltip("冰丸影响半径（格）")] public float iceRadius = 1.5f;
        [Tooltip("炸丸半径（格）")] public float bombRadius = 2.2f;
        [Tooltip("炸丸推力（每 kg 的速度变化 m/s）")] public float bombForce = 2.2f;
        [Tooltip("分裂丸分成几颗 / 张开角度")] public int splitCount = 3; public float splitAngle = 9f;

        [Header("弹弓")]
        [Tooltip("拉到最小时的出膛速度（米/秒）")] public float slingSpeedMin = 2.0f;
        [Tooltip("拉满时的出膛速度（米/秒）")] public float slingSpeedMax = 6.2f;
        [Tooltip("两发之间至少隔多久（秒）")] public float reload = 2f;

        [Header("敌人")]
        [Tooltip("敌弹飞行时间（秒），越长越慢越好接")] public Vector2 enemyFlight = new(1.7f, 2.1f);
        [Tooltip("蓄力预警时长（秒）")] public float windup = 1.2f;
        [Tooltip("玩家没弹时：下一发来得更晚（开火间隔倍率）")] public float mercySlow = 1.5f;
        [Tooltip("玩家没弹时：吸住范围加大（米）")] public float mercyCatchRadius = 0.27f;
        [Tooltip("敌弹弧顶离地最高多少米（不让玩家仰头追弹、不穿过天花板）")] public float enemyApexMax = 2.2f;

        [Header("补给")]
        public int supplyStart = 3;
        public int supplyMax = 5;
        [Tooltip("每隔多久补一块（秒）")] public float supplyEvery = 20f;

        static GameTuning cached;
        public static GameTuning I
        {
            get
            {
                if (!cached) cached = Resources.Load<GameTuning>("GameTuning");
                if (!cached) cached = CreateInstance<GameTuning>();
                return cached;
            }
        }
    }
}

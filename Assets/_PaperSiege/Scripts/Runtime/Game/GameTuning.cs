// 玩法数值（同事装机后可直接调）：Assets/_PaperSiege/Resources/GameTuning.asset
using UnityEngine;

namespace PaperSiege
{
    [CreateAssetMenu(menuName = "PaperSiege/Game Tuning", fileName = "GameTuning")]
    public class GameTuning : ScriptableObject
    {
        [Header("场地")]
        [Tooltip("玩家城每格多少米")] public float playerUnit = 0.05f;
        [Tooltip("敌城每格多少米（比玩家城大，3 米外也看得清）")] public float enemyUnit = 0.09f;
        [Tooltip("敌城离玩家城多远（米）")] public float enemyDistance = 3.0f;
        [Tooltip("房间小时最近距离（米）")] public float enemyDistanceMin = 1.8f;
        [Tooltip("没读到桌子时的虚拟桌高（米）")] public float tableHeight = 0.85f;

        [Header("弹")]
        [Tooltip("弹的重力倍率（<1 弧线更慢更好看、更好接）")] public float shotGravity = 0.55f;
        [Tooltip("玩家弹半径（米）")] public float playerBallRadius = 0.014f;
        [Tooltip("敌弹半径（米），大一点好接")] public float enemyBallRadius = 0.02f;
        [Tooltip("玩家弹质量（kg）")] public float playerBallMass = 0.3f;
        [Tooltip("敌弹质量（kg）")] public float enemyBallMass = 0.12f;
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
        [Tooltip("玩家没弹时敌弹飞行时间倍率")] public float mercySlow = 1.35f;

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

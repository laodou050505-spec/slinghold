// 所有手势判定阈值（通用规范第 4 条：放在一个 ScriptableObject 里，同事装机后直接调）。
// 资源：Assets/_PaperSiege/Resources/InputTuning.asset；运行时没找到资源就用这里的默认值。
using UnityEngine;

namespace PaperSiege
{
    [CreateAssetMenu(menuName = "PaperSiege/Input Tuning", fileName = "InputTuning")]
    public class InputTuning : ScriptableObject
    {
        [Header("捏合（滞回）")]
        [Tooltip("拇指尖到食指尖小于这个距离算捏住（米）。WarTable 真机实测：真捏时读数 2.1～3.2 cm")]
        public float pinchOn = 0.028f;   // 11-27：照《摘星》11-26 头显实测（同一台机器）：4 cm 时自然张开的手也会误触发
        [Tooltip("大于这个距离才算松开（米）。真机松开的手读数 5.1～6.6 cm")]
        public float pinchOff = 0.042f;  // 同上：5.5 cm 松手判定太晚，像粘住了
        [Tooltip("捏着且手几乎不动超过这么久，按“手指自然蜷着、其实已经松手”处理（秒）。拉弓瞄准时可能长时间不动，所以给长一点")]
        public float staleAfter = 12f;

        [Header("握拳 / 张掌")]
        [Tooltip("指尖到手腕 ÷ 指根到手腕 小于它算这根手指蜷起来")]
        public float curlRatio = 1.22f;
        [Tooltip("大于它算这根手指伸直")]
        public float extendRatio = 1.45f;

        [Header("速度")]
        [Tooltip("掌心速度平滑时间常数（秒），规范：取最近 80 ms")]
        public float velSmooth = 0.08f;
        [Tooltip("手部位置平滑时间常数（秒），压住追踪抖动")]
        public float handSmoothing = 0.03f;

        [Header("跟丢")]
        [Tooltip("丢帧这么久以内保持上一帧（秒）")]
        public float lostGrace = 0.2f;
        [Tooltip("跟丢超过这么久提示“把手抬到眼前”（秒）")]
        public float lostPrompt = 2.5f;

        [Header("弹弓")]
        [Tooltip("右手捏合点离皮兜多近算捏住皮兜（米）")]
        public float pouchGrab = 0.10f;
        [Tooltip("拉开小于它不算拉开，松手也不发射（米）")]
        public float pullMin = 0.05f;
        [Tooltip("拉满的距离（米）")]
        public float pullMax = 0.35f;
        [Tooltip("左手跟丢后弓架原地停多久（秒）")]
        public float bowHoldOnLost = 1.0f;
        [Tooltip("右手跟丢超过这么久自动松弓且不发射（秒）")]
        public float releaseOnLost = 1.0f;

        [Header("挡 / 接")]
        [Tooltip("敌弹离掌心多近被“吸住”（米）")]
        public float catchRadius = 0.20f;
        [Tooltip("吸住后悬停多久；这段时间里握拳 = 接住，一直张着 = 挡开（秒）")]
        public float catchHover = 0.6f;

        [Header("灭火")]
        [Tooltip("拍打判定的手掌速度（米/秒）")]
        public float patSpeed = 0.9f;   // 11-27：1.2 m/s 要拍得很猛（真人对着小玩具城拍不了那么重），降到 0.9
        [Tooltip("拍到着火块的距离（米）")]
        public float patRadius = 0.07f;

        [Header("修城")]
        [Tooltip("捏合点离补给块多近算捏到（米）")]
        public float grabRadius = 0.04f;
        [Tooltip("松手时离缺口多近吸附过去（米）")]
        public float snapDistance = 0.04f;

        [Header("眼动")]
        [Tooltip("拉弓时弹道往看着的那块吸附的最大角度")]
        public float gazeSnap = 4f;

        static InputTuning cached;
        public static InputTuning I
        {
            get
            {
                if (!cached) cached = Resources.Load<InputTuning>("InputTuning");
                if (!cached) cached = CreateInstance<InputTuning>();
                return cached;
            }
        }
    }
}

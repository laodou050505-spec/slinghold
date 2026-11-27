// 头显性能设置（11-27 用户：一定要保证不掉帧不卡顿，头显里会晕）。只在真机生效，编辑器里只做帧时监测。
//   1) 刷新率固定 72 Hz（每帧预算 13.9 ms）：稳定 72 帧比在 90 / 45 之间来回跳舒服得多（万圣节真机实测）；
//   2) CPU / GPU 性能档设成“持续高”（SUSTAINED_HIGH）：不让系统为了省电降频，结局烟花这种峰值时刻也有余量；
//      不用 BOOST（短时超频，过一会儿发热会被强制降频，反而突然掉帧）；
//   3) 注视点渲染关闭（眼动 / 固定都关）：万圣节真机反馈——高清区和降分辨率区交界有一道跟着眼睛动的“截面”，看着晕；
//   4) 眼睛缓冲渲染比例保持 1.0、相机后期处理关；
//   5) 帧时监测：真机日志每 5 秒打一行（平均 / 最差帧时、超预算帧数），装机后用 adb logcat 看“[木垒][帧]”。
//      连续 3 秒超预算会自动降一档开销（Degrade：烟花同屏上限减半、阴影距离缩短），保证不会持续掉帧。
using UnityEngine;
using UnityEngine.XR;
#if UNITY_ANDROID && !UNITY_EDITOR
using ByteDance.PICO.XR;
#endif

namespace PaperSiege
{
    public class HeadsetPerf : MonoBehaviour
    {
        public const float RefreshRate = 72f;
        const float Budget = 1f / RefreshRate;

        /// <summary>开销档：0 = 正常；1 = 已降一档（烟花同屏上限减半、阴影距离缩短）</summary>
        public static int Tier { get; private set; }
        /// <summary>开机预热期间最长的一帧（秒）</summary>
        public static float PrewarmWorst;

        float logT, worst, sum; int frames, over, overRun;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            Tier = 0;
            var go = new GameObject("HeadsetPerf");
            DontDestroyOnLoad(go);
            go.AddComponent<HeadsetPerf>();
        }

        void Start() => Apply();
        void OnApplicationPause(bool paused) { if (!paused) Apply(); }   // 从后台回来系统可能把性能档复位

        void Apply()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = (int)RefreshRate;
#if UNITY_ANDROID && !UNITY_EDITOR
            XRSettings.eyeTextureResolutionScale = 1f;
            int rr = PXR_Plugin.System.UPxr_SetSystemDisplayFrequency(RefreshRate);
            int cpu = PXR_System.SetPerformanceLevels(PxrPerfSettings.CPU, PxrSettingsLevel.SUSTAINED_HIGH);
            int gpu = PXR_System.SetPerformanceLevels(PxrPerfSettings.GPU, PxrSettingsLevel.SUSTAINED_HIGH);
            bool etfr = PXR_FoveationRendering.SetFoveationLevel(FoveationLevel.None, true);
            bool ffr = PXR_FoveationRendering.SetFoveationLevel(FoveationLevel.None, false);
            Debug.Log($"[木垒][性能] 刷新率 {RefreshRate}(r={rr}) CPU/GPU 持续高({cpu}/{gpu}) 注视点渲染关(etfr={etfr},ffr={ffr}) 渲染比例 1.0");
#endif
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f || dt > 0.5f) return;   // 切后台 / 断点回来的那一帧不算
            frames++; sum += dt; worst = Mathf.Max(worst, dt);
            bool late = dt > Budget * 1.25f;     // 超过 17.4 ms = 真机上会丢一帧
            if (late) over++;
            overRun = late ? overRun + 1 : Mathf.Max(0, overRun - 2);
            // 持续超预算（约 3 秒里大部分帧都超）：降一档开销。只降不升，避免来回跳
            if (overRun > 3 * RefreshRate && Tier == 0 && XRSettings.isDeviceActive) Degrade();
            if (!Prewarm.Done && dt > PrewarmWorst) PrewarmWorst = dt;
            if ((logT += dt) >= 5f)
            {
                if (XRSettings.isDeviceActive || Debug.isDebugBuild)
                    Debug.Log($"[木垒][帧] 平均 {sum / frames * 1000f:0.0}ms 最差 {worst * 1000f:0.0}ms 超预算 {over}/{frames} 档 {Tier} 烟花 {Fx.FireworksAlive}");
                logT = 0; worst = 0; sum = 0; frames = 0; over = 0;
            }
        }

        static void Degrade()
        {
            Tier = 1;
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
                urp.shadowDistance = Mathf.Min(urp.shadowDistance, 3.5f);
            Debug.LogWarning("[木垒][性能] 连续超预算 → 降一档：烟花同屏上限减半、阴影距离 3.5 m");
        }
    }
}

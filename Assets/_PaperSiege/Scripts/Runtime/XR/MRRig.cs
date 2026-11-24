// MR 启动（照《筑天》/ WarTable MRRig）：头显上开启 PICO 视频透视（相机清屏为全透明，真实房间就是背景），追踪原点设为地面。
// 应用从后台恢复时 PICO 会关掉透视，需要重新打开。编辑器里不做任何事。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace PaperSiege
{
    public class MRRig : MonoBehaviour
    {
        public Camera cam;
        float retry;
        bool floorSet;
        public static bool FloorSet { get; private set; }

        void Start() => Apply();
        void OnApplicationPause(bool paused) { if (!paused) Apply(); }

        void Apply()
        {
            if (Application.isEditor || !XRSettings.isDeviceActive) return;
            ByteDance.PICO.XR.PXR_Manager.EnableVideoSeeThrough = true;
            if (!cam) return;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
        }

        void Update()
        {
            if (floorSet || !XRSettings.isDeviceActive) return;
            retry -= Time.unscaledDeltaTime;
            if (retry > 0) return;
            retry = 1f;
            var subs = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subs);
            foreach (var s in subs)
                if ((s.GetSupportedTrackingOriginModes() & TrackingOriginModeFlags.Floor) != 0)
                    floorSet |= s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
            FloorSet = floorSet;
        }
    }
}

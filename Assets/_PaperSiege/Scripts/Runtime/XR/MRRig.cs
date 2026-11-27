// MR 启动（照《筑天》/ WarTable MRRig；11-27 按《摘星》11-26 真机修复补强）：
//   1. 头显上开启 PICO 视频透视（相机清屏为全透明，真实房间就是背景），追踪原点设为地面；
//   2. 头部 6DoF 追踪自检：TrackedPoseDriver 的位置 / 旋转 / 追踪状态绑定缺了就补上；如果它没在更新相机、而底层中心眼已有位姿，
//      改成每帧（含渲染前）直接把中心眼位姿写给相机兜底 —— 《摘星》真机上就是相机不跟头动（视角锁死）；
//   3. 真机上一律关掉编辑器用的桌面试玩 / 模拟手，防止它们改相机或冒充手；
//   4. 应用从后台恢复 / 重新获得焦点时 PICO 会关掉透视，直接通知底层重新打开。编辑器里不做任何事。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;

namespace PaperSiege
{
    public class MRRig : MonoBehaviour
    {
        public Camera cam;
        float retry, poseCheck;
        bool floorSet, directPose;
        UnityEngine.XR.InputDevice centerEye;
        TrackedPoseDriver tpd;
        public static bool FloorSet { get; private set; }
        /// <summary>系统回中（追踪原点被挪动）——场地要重新摆到玩家面前</summary>
        public static event System.Action Recentered;
        static MRRig inst;
        bool eyeFixed;
        readonly List<XRInputSubsystem> hooked = new();

        static bool OnDevice => !Application.isEditor || XRSettings.isDeviceActive;

        void Awake() { inst = this; if (!cam) cam = Camera.main; EnsureHeadTracking(); }
        void OnDestroy() { foreach (var s in hooked) if (s != null) s.trackingOriginUpdated -= OnOriginUpdated; if (inst == this) inst = null; }
        void OnOriginUpdated(XRInputSubsystem s) { Debug.Log("[PaperSiege] 追踪原点更新（系统回中）"); Recentered?.Invoke(); }

        /// <summary>
        /// 启动时调用：一直没设成“地面原点”（追踪原点在开机时的头部位置，头高≈0）时，把 XR 原点整体抬到眼高 1.55 m，
        /// 不然桌子、敌城都会摆到地板下面。设成了地面原点就什么都不做。
        /// </summary>
        public static void FixEyeHeight()
        {
            var r = inst;
            if (!r || !r.cam || !OnDevice || r.floorSet || r.eyeFixed) return;
            var rig = r.cam.transform.parent;
            float y = r.cam.transform.position.y;
            if (!rig || y > 0.6f) return;
            rig.position += Vector3.up * (1.55f - y);
            r.eyeFixed = true;
            Debug.LogWarning($"[PaperSiege] 没拿到地面原点（头高 {y:0.00} m）→ XR 原点抬高到眼高 1.55 m");
        }
        void OnEnable() { Application.onBeforeRender += BeforeRender; }
        void OnDisable() { Application.onBeforeRender -= BeforeRender; }
        void Start() { EnsureHeadTracking(); Apply(); }
        void OnApplicationPause(bool paused) { if (!paused) { EnsureHeadTracking(); Apply(); } }
        void OnApplicationFocus(bool focus) { if (focus) Apply(); }

        void EnsureHeadTracking()
        {
            if (!cam) cam = Camera.main;
            if (!cam || !OnDevice) return;
            // 真机：桌面试玩 / 模拟手一律关掉（它们会改相机朝向、冒充手）
            foreach (var dp in FindObjectsByType<DesktopPlay>()) dp.enabled = false;
            foreach (var sh in FindObjectsByType<SimHands>()) sh.enabled = false;
            tpd = cam.GetComponent<TrackedPoseDriver>();
            if (!tpd) tpd = cam.gameObject.AddComponent<TrackedPoseDriver>();
            if (tpd.positionInput.action == null || tpd.positionInput.action.bindings.Count == 0)
                tpd.positionInput = new InputActionProperty(new InputAction("HeadPos", InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
            if (tpd.rotationInput.action == null || tpd.rotationInput.action.bindings.Count == 0)
                tpd.rotationInput = new InputActionProperty(new InputAction("HeadRot", InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
            if (tpd.trackingStateInput.action == null || tpd.trackingStateInput.action.bindings.Count == 0)
                tpd.trackingStateInput = new InputActionProperty(new InputAction("HeadState", InputActionType.Value, "<XRHMD>/trackingState", expectedControlType: "Integer"));
            tpd.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            tpd.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            tpd.enabled = !directPose;
            tpd.positionInput.action?.Enable(); tpd.rotationInput.action?.Enable(); tpd.trackingStateInput.action?.Enable();
        }

        void Apply()
        {
            if (!XRSettings.isDeviceActive) return;
            ByteDance.PICO.XR.PXR_Manager.EnableVideoSeeThrough = true;
            // EnableVideoSeeThrough 只在值变化时才调底层；从后台切回来时底层已经关了透视，要直接再开一次
            ByteDance.PICO.XR.PXR_Plugin.Boundary.UPxr_SetSeeThroughBackground(true);
            if (!cam) return;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
        }

        bool ReadCenterEye(out Vector3 p, out Quaternion q)
        {
            p = default; q = Quaternion.identity;
            if (!centerEye.isValid) centerEye = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            return centerEye.isValid
                && centerEye.TryGetFeatureValue(UnityEngine.XR.CommonUsages.centerEyePosition, out p)
                && centerEye.TryGetFeatureValue(UnityEngine.XR.CommonUsages.centerEyeRotation, out q);
        }

        void BeforeRender()
        {
            if (!directPose || !cam || !XRSettings.isDeviceActive) return;
            if (ReadCenterEye(out var p, out var q)) cam.transform.SetLocalPositionAndRotation(p, q);
        }

        void Update()
        {
            if (!XRSettings.isDeviceActive) return;
            // 自检：相机一直停在原点（追踪组件没更新它），而底层中心眼已有位姿 → 改为直接同步
            if (!directPose && cam && (poseCheck += Time.unscaledDeltaTime) > 0.5f)
            {
                poseCheck = 0f;
                bool camIdle = cam.transform.localPosition.sqrMagnitude < 1e-6f && Quaternion.Angle(cam.transform.localRotation, Quaternion.identity) < 0.05f;
                if (camIdle && ReadCenterEye(out var p, out var q) && (p.sqrMagnitude > 1e-6f || Quaternion.Angle(q, Quaternion.identity) > 0.05f))
                {
                    directPose = true;
                    if (tpd) tpd.enabled = false;
                    Debug.LogWarning("[PaperSiege] 头部追踪组件没在更新相机，改为直接同步中心眼位姿");
                }
            }
            if (directPose && ReadCenterEye(out var p2, out var q2)) cam.transform.SetLocalPositionAndRotation(p2, q2);
            // 订阅系统回中（每个追踪子系统订一次）
            if (hooked.Count == 0)
            {
                var all = new List<XRInputSubsystem>();
                SubsystemManager.GetSubsystems(all);
                foreach (var s in all) { s.trackingOriginUpdated += OnOriginUpdated; hooked.Add(s); }
            }
            if (floorSet || eyeFixed) return;   // 已经手动抬过眼高就别再切地面原点（会抬两次）
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

// 头显输入：PICO 手部追踪 + 眼动。读法照《筑天》HandInput / WarTable MRInput（真机验证过：坐标系翻转、捏合滞回、
// 系统捏合标记、冻住的旧数据当丢失、长时间不动的捏当已松手），握拳 / 掌心法线 / 掌心速度照 MonsterCity/MonsterInput。
// 编辑器里没有头显：SimHands 往 SimHand 里写两只假手，玩法代码只认这里的 Hand 数据，真机和编辑器走同一条路。
using ByteDance.PICO.XR;
using UnityEngine;
using UnityEngine.XR;

namespace PaperSiege
{
    /// <summary>松手的原因：真张开手指 / 捏太久没动（防粘连）/ 手跟丢</summary>
    public enum ReleaseKind { None, Open, Stale, Lost }

    public struct Hand
    {
        public bool tracked;
        public bool pinch;
        public bool fist;              // 四指里至少三根蜷起来（握拳）
        public bool open;              // 四指里至少三根伸直（张掌）
        public Vector3 pinchPoint;     // 拇指尖和食指尖的中点（平滑后）
        public Quaternion rot;         // 手的朝向（掌心关节）
        public Vector3 palm;           // 掌心位置
        public Vector3 palmN;          // 掌心朝向（法线）
        public Vector3 vel;            // 掌心速度（约 80 ms 平滑）
        public float tipGap;           // 拇指尖到食指尖（米）
        public float lostFor;          // 跟丢了多久（秒），跟着时为 0
    }

    [DefaultExecutionOrder(-100)]
    public class HandInput : MonoBehaviour
    {
        public static HandInput Instance { get; private set; }
        public Camera cam;
        /// <summary>XR 原点（追踪空间 → 世界）</summary>
        public Transform origin;

        public readonly Hand[] hands = new Hand[2];
        public readonly bool[] pinchDown = new bool[2];
        public readonly ReleaseKind[] released = new ReleaseKind[2];
        public readonly bool[] fistDown = new bool[2];
        public bool EyeTracked { get; private set; }
        public Ray Gaze { get; private set; }
        public Vector3 Head => cam ? cam.transform.position : Vector3.up * 1.6f;
        public string Diag { get; private set; } = "";

        // ── 编辑器假手（SimHands 写入）。真机上 XRSettings.isDeviceActive 时不用。
        public static bool Sim => Application.isEditor || !XRSettings.isDeviceActive;
        public static readonly Hand[] SimHand = new Hand[2];
        /// <summary>测试脚本可以指定假眼动射线（null = 没有眼动，退回头部朝向）</summary>
        public static Ray? SimGaze;

        InputTuning T => InputTuning.I;
        readonly bool[] rawPinch = new bool[2], stale = new bool[2], rawFist = new bool[2];
        readonly float[] pinchT0 = new float[2], anchorT = new float[2], lostT = { -9f, -9f };
        readonly Vector3[] anchorP = new Vector3[2];
        readonly Hand[] last = new Hand[2];
        readonly Vector3[] lastPalm = new Vector3[2];
        readonly bool[] hasLastPalm = new bool[2];
        readonly Vector3[] freezeSig = new Vector3[2]; readonly float[] frozenFor = new float[2];
        const float FrozenAfter = 0.3f;
        HandJointLocations locs = new() { jointLocations = new HandJointLocation[(int)HandJoint.JointMax] };
        HandAimState aim;
        EyeTrackingData eye = new() { eyeDatas = new PerEyeData[(int)PerEyeUsage.EyeCount] };
        bool eyeStarted; float eyeRetry;
        Vector3 gazeDir;

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }
        void OnDisable()
        {
            if (eyeStarted) { var stop = new EyeTrackingStopInfo(); PXR_MotionTracking.StopEyeTracking(ref stop); eyeStarted = false; }
        }

        Vector3 World(Vector3 p) => origin ? origin.TransformPoint(p) : p;
        Quaternion WorldRot(Quaternion q) => origin ? origin.rotation * q : q;

        void Update()
        {
            for (int s = 0; s < 2; s++) { pinchDown[s] = false; released[s] = ReleaseKind.None; fistDown[s] = false; }
            if (Sim) { SimUpdate(); return; }
            bool handsOn = PXR_HandTracking.GetActiveInputDevice() == ActiveInputDevice.HandTrackingActive;
            for (int s = 0; s < 2; s++)
            {
                bool was = hands[s].pinch, wasFist = hands[s].fist;
                if (handsOn && ReadHand(s == 0 ? HandType.HandLeft : HandType.HandRight, s, out var h))
                {
                    lostT[s] = Time.time;
                    h = Smooth(last[s], h);
                    Velocity(s, ref h);
                    hands[s] = last[s] = h;
                    if (was && !h.pinch) released[s] = stale[s] ? ReleaseKind.Stale : ReleaseKind.Open;
                }
                else
                {
                    float lost = Time.time - lostT[s];
                    var keep = last[s]; keep.lostFor = lost; keep.vel = Vector3.zero;
                    if (lost < T.lostGrace) hands[s] = keep;       // 丢一两帧：保持上一帧（不松开捏合）
                    else
                    {
                        keep.tracked = false; keep.pinch = false; keep.fist = false; hands[s] = keep; rawPinch[s] = false; hasLastPalm[s] = false;
                        if (was) released[s] = ReleaseKind.Lost;
                    }
                }
                if (!was && hands[s].pinch) pinchDown[s] = true;
                if (!wasFist && hands[s].fist) fistDown[s] = true;
            }
            ReadGaze();
            Diag = $"L={State(0)}  R={State(1)}  eye={EyeTracked}";
        }

        string State(int s) => !hands[s].tracked ? "丢" : hands[s].pinch ? "捏" : hands[s].fist ? "拳" : hands[s].open ? "掌" : "松";

        Hand Smooth(Hand prev, Hand h)
        {
            if (!prev.tracked) return h;
            float k = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.001f, T.handSmoothing));
            h.pinchPoint = Vector3.Lerp(prev.pinchPoint, h.pinchPoint, k);
            h.palm = Vector3.Lerp(prev.palm, h.palm, k);
            h.rot = Quaternion.Slerp(prev.rot, h.rot, k);
            return h;
        }

        /// <summary>掌心速度：位置差分再做一阶平滑（单帧追踪抖动不会被当成拍）</summary>
        void Velocity(int s, ref Hand h)
        {
            float dt = Mathf.Max(1e-3f, Time.deltaTime);
            if (hasLastPalm[s])
            {
                var v = (h.palm - lastPalm[s]) / dt;
                float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.005f, T.velSmooth));
                h.vel = Vector3.Lerp(hands[s].vel, v, k);
            }
            else h.vel = Vector3.zero;
            lastPalm[s] = h.palm; hasLastPalm[s] = true;
        }

        void SimUpdate()
        {
            for (int s = 0; s < 2; s++)
            {
                bool was = hands[s].pinch, wasFist = hands[s].fist;
                var h = SimHand[s];
                if (!h.tracked) { h.lostFor = hands[s].tracked ? 0.001f : hands[s].lostFor + Time.deltaTime; hasLastPalm[s] = false; }
                else Velocity(s, ref h);
                if (h.rot == default) h.rot = Quaternion.identity;
                hands[s] = h;
                if (!was && h.pinch) pinchDown[s] = true;
                if (was && !h.pinch) released[s] = h.tracked ? ReleaseKind.Open : ReleaseKind.Lost;
                if (!wasFist && h.fist) fistDown[s] = true;
            }
            EyeTracked = SimGaze.HasValue;
            if (SimGaze.HasValue) Gaze = SimGaze.Value;
            else if (cam) Gaze = new Ray(cam.transform.position, cam.transform.forward);
        }

        bool ReadHand(HandType type, int side, out Hand h)
        {
            h = default;
            if (!PXR_HandTracking.GetJointLocations(type, ref locs) || locs.isActive == 0 || locs.jointLocations == null) return false;
            const ulong need = (ulong)HandLocationStatus.PositionValid;
            bool Ok(HandJoint j) => ((ulong)locs.jointLocations[(int)j].locationStatus & need) != 0;
            if (!(Ok(HandJoint.JointWrist) && Ok(HandJoint.JointThumbTip) && Ok(HandJoint.JointIndexTip))) return false;
            Vector3 P(HandJoint j) => World(locs.jointLocations[(int)j].pose.Position.ToVector3());
            var wrist = P(HandJoint.JointWrist); var thumb = P(HandJoint.JointThumbTip);
            var tips = new[] { P(HandJoint.JointIndexTip), P(HandJoint.JointMiddleTip), P(HandJoint.JointRingTip), P(HandJoint.JointLittleTip) };
            var prox = new[] { P(HandJoint.JointIndexProximal), P(HandJoint.JointMiddleProximal), P(HandJoint.JointRingProximal), P(HandJoint.JointLittleProximal) };
            // 冻住的旧数据（手离开视野后系统还在报最后一帧）：腕 + 拇指尖 + 食指尖连续 0.3 秒纹丝不动 → 当丢失
            var sig = wrist + thumb * 2f + tips[0] * 3f;
            if ((sig - freezeSig[side]).sqrMagnitude < 1e-10f) frozenFor[side] += Time.unscaledDeltaTime; else { frozenFor[side] = 0f; freezeSig[side] = sig; }
            if (frozenFor[side] > FrozenAfter) return false;
            var pj = locs.jointLocations[(int)HandJoint.JointPalm].pose;
            var q = WorldRot(new Quaternion(pj.Orientation.x, pj.Orientation.y, -pj.Orientation.z, -pj.Orientation.w));
            float d = Vector3.Distance(thumb, tips[0]);
            bool sys = PXR_HandTracking.GetAimState(type, ref aim) && ((ulong)aim.aimStatus & (ulong)HandAimStatus.AimIndexPinching) != 0;
            int ext = 0, curled = 0;
            for (int i = 0; i < 4; i++)
            {
                float r = Vector3.Distance(tips[i], wrist) / Mathf.Max(1e-3f, Vector3.Distance(prox[i], wrist));
                if (r > T.extendRatio) ext++;
                if (r < T.curlRatio) curled++;
            }
            // 握拳（滞回）：三根以上蜷起来算握上，少于两根才算松开
            rawFist[side] = rawFist[side] ? curled >= 2 : curled >= 3;
            bool was = rawPinch[side];
            var pp = (thumb + tips[0]) * 0.5f;
            if (stale[side])
            {
                rawPinch[side] = false;
                if (d > T.pinchOff) stale[side] = false;
            }
            else
            {
                // 握拳时拇指食指也会挨在一起：握拳不算捏（接弹靠握拳，不能误判成捏）
                bool start = d < T.pinchOn && !rawFist[side];
                rawPinch[side] = (sys && d < T.pinchOff && !rawFist[side]) || (was ? d < T.pinchOff : start);
                if (rawPinch[side] && !was) { pinchT0[side] = Time.time; anchorP[side] = pp; anchorT[side] = Time.time; }
                if (rawPinch[side])
                {
                    if ((pp - anchorP[side]).sqrMagnitude > 0.025f * 0.025f) { anchorP[side] = pp; anchorT[side] = Time.time; }
                    if (Time.time - pinchT0[side] > T.staleAfter && Time.time - anchorT[side] > 1.5f) { rawPinch[side] = false; stale[side] = true; }
                }
            }
            var palm = (wrist + prox[0] + prox[1] + prox[3]) * 0.25f;
            // 掌心法线：食指根、小指根相对手腕的叉乘（左右手方向相反；纯几何，和坐标系手性无关）
            var cr = Vector3.Cross(prox[0] - wrist, prox[3] - wrist).normalized;
            h = new Hand
            {
                tracked = true, pinch = rawPinch[side], fist = rawFist[side] && !rawPinch[side], open = ext >= 3,
                pinchPoint = pp, rot = q, palm = palm, palmN = side == 0 ? cr : -cr, tipGap = d,
            };
            return true;
        }

        void ReadGaze()
        {
            var t = cam ? cam.transform : transform;
            EyeTracked = false;
            // 第一次启动时系统会弹授权框，授权前取数失败 → 反复重试，不能失败一次就整局退回头部朝向（规范 3.3）
            if (!eyeStarted && (eyeRetry -= Time.unscaledDeltaTime) <= 0f)
            {
                eyeRetry = 2f;
                var start = new EyeTrackingStartInfo { needCalibration = 0, mode = EyeTrackingMode.PXR_ETM_BOTH };
                eyeStarted = PXR_MotionTracking.StartEyeTracking(ref start) == 0;
            }
            Ray target = new(t.position, t.forward);
            if (eyeStarted)
            {
                var info = new EyeTrackingDataGetInfo { displayTime = 0, flags = EyeTrackingDataGetFlags.PXR_EYE_POSITION | EyeTrackingDataGetFlags.PXR_EYE_ORIENTATION };
                if (PXR_MotionTracking.GetEyeTrackingData(ref info, ref eye) == 0 && eye.eyeDatas != null)
                {
                    var cmb = eye.eyeDatas[(int)PerEyeUsage.Combined];
                    if (cmb.isPoseValid != 0 && !(cmb.isOpennessValid != 0 && cmb.openness < 0.2f))
                    {
                        var o = new Vector3(cmb.pose.position.x, cmb.pose.position.y, -cmb.pose.position.z);
                        var q = new Quaternion(cmb.pose.orientation.x, cmb.pose.orientation.y, -cmb.pose.orientation.z, -cmb.pose.orientation.w);
                        target = new Ray(World(o), (WorldRot(q) * Vector3.forward).normalized);
                        EyeTracked = true;
                    }
                }
            }
            float k = 1f - Mathf.Exp(-Time.deltaTime / 0.035f);
            gazeDir = gazeDir == Vector3.zero ? target.direction : Vector3.Slerp(gazeDir, target.direction, k);
            Gaze = new Ray(target.origin, gazeDir.normalized);
        }
    }
}

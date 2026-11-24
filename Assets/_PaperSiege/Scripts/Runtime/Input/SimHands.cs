// 编辑器模拟头和手（规范第 5 条：没有头显，这是验收的主要手段）。
// 手动试玩：
//   鼠标 = 当前手的位置（沿视线、离眼睛 handDist 米；滚轮调远近）；
//   左键按住 = 捏；X = 切换“捏住锁定”（松开鼠标也保持捏，用来一只手拿着弓、另一只手拉）；
//   G 按住 = 握拳；V = 拍一下（手掌快速往下）；H = 隐藏 / 显示当前手（模拟跟丢）；
//   Tab = 换左右手（另一只手停在原处，保持它的捏 / 拳状态）；
//   右键拖动 = 转头；按住右键时 W/S/A/D 走动、Space/Ctrl 升降。
// 自动测试：scripted = true 后由测试脚本直接调 Pose / Hide。
using UnityEngine;
using UnityEngine.InputSystem;

namespace PaperSiege
{
    [DefaultExecutionOrder(-200)]
    public class SimHands : MonoBehaviour
    {
        public Camera cam;
        public static bool scripted;
        public int active = 1;
        public float handDist = 0.45f;
        public float moveSpeed = 0.6f;
        readonly bool[] latch = new bool[2], hidden = new bool[2];
        readonly Vector3[] pos = new Vector3[2];
        float patT = -1f; Vector3 patFrom;
        Transform[] views;
        Material[] viewMats;

        /// <summary>直接摆一只假手。palmN 默认朝前（手心对着远处），fist / open 互斥</summary>
        public static void Pose(int side, Vector3 p, bool pinch = false, bool fist = false, Vector3? palmN = null, Quaternion? rot = null)
        {
            var n = palmN ?? Vector3.forward;
            HandInput.SimHand[side] = new Hand
            {
                tracked = true, pinch = pinch, fist = fist && !pinch, open = !pinch && !fist,
                pinchPoint = p, palm = p - n * 0.03f, palmN = n,
                rot = rot ?? Quaternion.LookRotation(n, Vector3.up), tipGap = pinch ? 0.01f : 0.08f,
            };
        }
        public static void Hide(int side) => HandInput.SimHand[side] = default;

        void Start()
        {
            if (!HandInput.Sim) { enabled = false; return; }
            views = new Transform[2];
            viewMats = new Material[2];
            for (int s = 0; s < 2; s++)
            {
                var v = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                v.name = s == 0 ? "SimHand_L" : "SimHand_R";
                Destroy(v.GetComponent<Collider>());
                var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                m.SetFloat("_Surface", 1); m.SetOverrideTag("RenderType", "Transparent");
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = 3000;
                v.GetComponent<Renderer>().sharedMaterial = m;
                v.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                viewMats[s] = m;
                views[s] = v.transform;
                pos[s] = cam.transform.position + cam.transform.forward * handDist + cam.transform.right * (s == 0 ? -0.15f : 0.15f) - Vector3.up * 0.2f;
            }
        }

        void Update()
        {
            if (!cam) return;
            if (!scripted) Manual();
            if (views == null) return;
            for (int s = 0; s < 2; s++)
            {
                var h = HandInput.SimHand[s];
                var sl = Director.I ? Director.I.sling : null;
                bool slingHas = sl && ((s == 0 && sl.Holding) || (s == 1 && sl.Drawing));
                views[s].gameObject.SetActive(h.tracked && !slingHas);
                if (!h.tracked) continue;
                views[s].position = h.pinch ? h.pinchPoint : h.palm;
                views[s].rotation = Quaternion.LookRotation(cam.transform.forward) * Quaternion.Euler(90, 0, 0);   // 圆片正对镜头
                float d = h.fist ? 0.034f : 0.026f;
                views[s].localScale = new Vector3(d, 0.001f, d);
                viewMats[s].color = h.pinch ? new Color(1f, 0.75f, 0.5f, 0.45f) : h.fist ? new Color(0.95f, 0.55f, 0.5f, 0.5f) : new Color(1f, 0.95f, 0.9f, 0.35f);
            }
        }

        void Manual()
        {
            var kb = Keyboard.current; var ms = Mouse.current;
            if (kb == null || ms == null || !Application.isFocused) return;   // 编辑器没焦点时不读键盘（否则别的窗口打字会把头挪走）
            float dt = Time.deltaTime;
            var t = cam.transform;
            if (ms.rightButton.isPressed)
            {
                var fwd = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized; var right = Vector3.Cross(Vector3.up, fwd);
                var mv = Vector3.zero;
                if (kb.wKey.isPressed) mv += fwd; if (kb.sKey.isPressed) mv -= fwd;
                if (kb.dKey.isPressed) mv += right; if (kb.aKey.isPressed) mv -= right;
                if (kb.spaceKey.isPressed) mv += Vector3.up; if (kb.leftCtrlKey.isPressed) mv -= Vector3.up;
                t.position += mv * (moveSpeed * dt);
                var d = ms.delta.ReadValue() * 0.15f;
                var e = t.eulerAngles;
                t.rotation = Quaternion.Euler(Mathf.Clamp(Mathf.DeltaAngle(0, e.x) - d.y, -80, 80), e.y + d.x, 0);
            }
            if (kb.tabKey.wasPressedThisFrame) active = 1 - active;
            if (kb.xKey.wasPressedThisFrame) latch[active] = !latch[active];
            if (kb.hKey.wasPressedThisFrame) hidden[active] = !hidden[active];
            handDist = Mathf.Clamp(handDist + ms.scroll.ReadValue().y * 0.0005f, 0.15f, 0.9f);
            var ray = cam.ScreenPointToRay(ms.position.ReadValue());
            pos[active] = ray.GetPoint(handDist);
            // 拍：0.15 秒内手掌往下 12 cm
            if (kb.vKey.wasPressedThisFrame) { patT = 0f; patFrom = pos[active]; }
            var p = pos[active];
            if (patT >= 0f) { patT += dt; p = patFrom - Vector3.up * Mathf.Min(0.12f, patT / 0.15f * 0.12f); if (patT > 0.25f) patT = -1f; }
            for (int s = 0; s < 2; s++)
            {
                if (hidden[s]) { Hide(s); continue; }
                bool pinch = latch[s] || (s == active && ms.leftButton.isPressed);
                bool fist = s == active && kb.gKey.isPressed;
                var palmN = patT >= 0f && s == active ? Vector3.down : (cam.transform.forward);
                Pose(s, s == active ? p : pos[s], pinch, fist, palmN);
            }
        }
    }
}

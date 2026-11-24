// 桌面试玩（编辑器里没有头显时，用鼠标键盘体验手势玩法）：把鼠标 / 键盘翻译成两只“模拟手”，玩法代码和头显上走同一条路。
//   右键拖动 = 转头；W/A/S/D = 走动
//   左键按住往后拖 = 右手捏住皮兜往后拉（往下拖 = 往上瞄、往左拖 = 往右瞄，拖得越远越满）；松开 = 发射
//   鼠标 = 右手位置（没拉弓时）：移到飞来的敌弹上 = 张手挡；按住 G = 握拳接
//   E 按住 = 右手在鼠标指的桌面上捏（拿补给积木），拖到缺口松开 = 修城；V = 往下拍（灭火）
//   1～5 = 换弹；B = 收起 / 举起弓；N / P = 下一关 / 上一关；R = 重来
using UnityEngine;
using UnityEngine.InputSystem;

namespace PaperSiege
{
    public class DesktopPlay : MonoBehaviour
    {
        Director d;
        float clickT;
        static Vector3 SimHand1Point() => HandInput.SimHand[1].pinchPoint;
        bool bowUp = true, drawing;
        Vector2 press;
        float patT = -1f; Vector3 patFrom;
        GUIStyle box, title;

        void Start()
        {
            d = Director.I;
            SimHands.scripted = true;
            var e = d.head.transform.eulerAngles;
            d.head.transform.rotation = Quaternion.Euler(14f, e.y, 0f);   // 桌面上视线抬平一点，敌城在画面中间
            // 从标题页开始（Director 自己进标题）
        }

        Vector3 CamPt(float fwd, float right, float up)
        {
            var t = d.head.transform;
            return t.position + t.forward * fwd + t.right * right + t.up * up;
        }

        void Update()
        {
            var kb = Keyboard.current; var ms = Mouse.current;
            if (kb == null || ms == null || !d) return;
            if (!Application.isFocused) return;
            var cam = d.head;
            var t = cam.transform;
            // ── 头：右键拖动转头、WASD 走动
            if (ms.rightButton.isPressed)
            {
                var dl = ms.delta.ReadValue() * 0.15f;
                var e = t.eulerAngles;
                t.rotation = Quaternion.Euler(Mathf.Clamp(Mathf.DeltaAngle(0, e.x) - dl.y, -60, 70), e.y + dl.x, 0);
            }
            var fwd = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized; var right = Vector3.Cross(Vector3.up, fwd);
            var mv = Vector3.zero;
            if (kb.wKey.isPressed) mv += fwd; if (kb.sKey.isPressed) mv -= fwd;
            if (kb.dKey.isPressed) mv += right; if (kb.aKey.isPressed) mv -= right;
            t.position += mv * (0.6f * Time.deltaTime);
            // ── 关卡 / 弹
            if (kb.nKey.wasPressedThisFrame) d.StartLevel((d.levelIndex + 1) % Levels.Count);
            if (kb.pKey.wasPressedThisFrame) d.StartLevel((d.levelIndex + Levels.Count - 1) % Levels.Count);
            if (kb.rKey.wasPressedThisFrame) d.StartLevel(d.levelIndex);
            for (int i = 0; i < 5; i++)
                if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame && d.ammo.Count((Ammo)i) > 0) d.ammo.selected = (Ammo)i;
            if (kb.bKey.wasPressedThisFrame) bowUp = !bowUp;
            // ── 左手：弓架举在视野左前方
            if (bowUp && d.phase == Director.Phase.Battle) SimHands.Pose(0, CamPt(0.40f, -0.10f, -0.10f), true);
            else SimHands.Hide(0);
            // ── 右手
            var sling = d.sling;
            var mp = ms.position.ReadValue();
            // 点按钮：左键点到木头按钮 = 右手在那儿捏一下
            if (ms.leftButton.wasPressedThisFrame && Physics.Raycast(cam.ScreenPointToRay(mp), out var bh, 5f, 1 << 12))
            {
                SimHands.Pose(1, bh.point, true);
                clickT = 0.08f;
                return;
            }
            if (clickT > 0f) { clickT -= Time.deltaTime; SimHands.Pose(1, SimHand1Point(), clickT > 0f); return; }
            if (bowUp && sling && ms.leftButton.wasPressedThisFrame && sling.state == PlayerSling.State.Held)
            {
                drawing = true; press = mp;
                SimHands.Pose(1, sling.PouchRest, true);   // 先在皮兜处捏住
                return;
            }
            if (drawing)
            {
                var hand = HandFromDrag((mp - press) / Mathf.Max(1, Screen.height));
                bool hold = ms.leftButton.isPressed;
                SimHands.Pose(1, hand, hold);
                if (!hold) drawing = false;   // 松开：这一帧 pinch=false → 弹弓发射
                return;
            }
            var ray = cam.ScreenPointToRay(mp);
            if (kb.eKey.isPressed)
            {
                // 修城：右手捏在鼠标指着的桌面 / 积木上方
                var p = Physics.Raycast(ray, out var hit, 3f, Layers.SolidMask) ? hit.point + Vector3.up * 0.02f : ray.GetPoint(0.45f);
                SimHands.Pose(1, p, true);
                return;
            }
            if (kb.vKey.wasPressedThisFrame && patT < 0f)
            {
                patFrom = Physics.Raycast(ray, out var hit, 3f, Layers.SolidMask) ? hit.point + Vector3.up * 0.1f : ray.GetPoint(0.45f);
                patT = 0f;
            }
            if (patT >= 0f)
            {
                patT += Time.deltaTime;
                SimHands.Pose(1, patFrom - Vector3.up * Mathf.Min(0.13f, patT / 0.08f * 0.13f), false, false, Vector3.down);
                if (patT > 0.2f) patT = -1f;
                return;
            }
            // 平时：右手张开跟着鼠标（离眼睛约 35 cm），按住 G 握拳
            SimHands.Pose(1, ray.GetPoint(0.35f), false, kb.gKey.isPressed, -ray.direction);
        }

        /// <summary>
        /// 拖动 → 右手位置：从皮兜往后（朝镜头）拉，拖动方向让手往同方向偏 —— 和真弹弓一样，手往下弹就往上飞。
        /// dd = 拖动量（以屏幕高度为 1）。拖 1 个屏幕高 ≈ 拉 1 米（上限 pullMax）
        /// </summary>
        public Vector3 HandFromDrag(Vector2 dd)
        {
            var t = d.head.transform;
            var fc = d.sling.ForkCenter;
            float len = 0.03f + Mathf.Min(InputTuning.I.pullMax - 0.03f, dd.magnitude * 1.0f);
            return fc - t.forward * len + (t.right * dd.x + t.up * dd.y) * 0.9f;
        }

        void TitleHint()
        {
            int fs = Mathf.Max(14, Screen.height / 52);
            var st = new GUIStyle(GUI.skin.box) { fontSize = fs, padding = new RectOffset(fs, fs, fs / 2, fs / 2) };
            st.normal.textColor = new Color(0.98f, 0.94f, 0.86f);
            GUI.Box(new Rect(fs, fs, fs * 34, fs * 2.4f), "桌面试玩：左键点桌上的积木按钮（开始 / 选关 / 地球仪 = 切换语言 / 音乐音效）", st);
        }

        static string Pips(Castle c) => new string('●', c.kingHP) + new string('○', c.kingMaxHP - c.kingHP);

        void OnGUI()
        {
            if (!d || d.level == null) return;
            if (d.phase == Director.Phase.Title) { TitleHint(); return; }
            int fs = Mathf.Max(14, Screen.height / 52);   // 字号跟着窗口高度（高分屏上也看得清）
            if (box == null || box.fontSize != fs)
            {
                var bg = new Texture2D(1, 1); bg.SetPixel(0, 0, new Color(0.08f, 0.06f, 0.05f, 0.72f)); bg.Apply();
                box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = fs, wordWrap = true, padding = new RectOffset(fs, fs, fs * 2 / 3, fs * 2 / 3) };
                box.normal.background = bg; box.normal.textColor = new Color(0.98f, 0.94f, 0.86f);
                title = new GUIStyle(GUI.skin.label) { fontSize = fs * 2, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                title.normal.textColor = new Color(1f, 0.92f, 0.7f);
            }
            var a = d.ammo;
            string[] names = { "石", "火", "冰", "炸", "分裂" };
            var am = "";
            for (int i = 0; i < 5; i++) am += (a.selected == (Ammo)i ? $"[{i + 1}{names[i]}×{a.Count((Ammo)i)}]" : $" {i + 1}{names[i]}×{a.Count((Ammo)i)} ") + " ";
            var help =
                $"《木垒》桌面试玩　{d.level.title}　剩 {Mathf.Max(0, d.timeLeft):0} 秒\n" +
                $"弹：{am}\n" +
                $"我方帅旗 {Pips(d.arena.mine)}　敌方帅旗 {Pips(d.arena.enemy)}　我方完整 {d.arena.mine.Integrity:P0}　敌方完整 {d.arena.enemy.Integrity:P0}\n" +
                $"射 {d.stats.shots}　挡 {d.stats.blocked}　接 {d.stats.caught}　修 {d.stats.repaired}\n\n" +
                "左键按住往后拖：拉弓（往下拖=往上瞄，往左拖=往右瞄，越远越满）　松开：发射\n" +
                "鼠标移到飞来的敌弹上：挡　按住 G：握拳接住\n" +
                "按住 E：在桌上捏积木，拖到缺口松开=修城　V：往下拍（灭火）\n" +
                "1～5：换弹　B：收 / 举弓　右键拖：转头　WASD：走动　N/P：换关　R：重来";
            GUI.Box(new Rect(fs, fs, fs * 44, fs * 13f), help, box);
        }
    }
}

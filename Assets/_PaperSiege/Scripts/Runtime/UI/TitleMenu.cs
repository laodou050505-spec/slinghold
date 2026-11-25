// 标题页（实物界面，方案第 9 节）：
//   字标：玩具积木块上刻字，摆在一根用两根绳吊在天花板（盒盖）下的长木梁上 ——
//         中文 = 两块大积木“木”“垒”错落摆放 + 下面一排小积木 SLINGHOLD；英文 = 一排大积木 SLINGHOLD + 两块小积木“木垒”；切换语言时积木一块块翻过来换。
//   木梁下挂小木牌：副标题“弹弓攻城 / A Tabletop Siege”；再下面挂署名牌：TRIPO 标志，另起一行“Tripo XR 出品 / Tripo XR Developers”。
//   桌上（自己城的前面）：开始（继续第 N 关）、10 块选关积木（解锁的上漆、写关号、星星钉；没解锁的是裸木）、地球仪 = 切换语言、音乐 / 音效开关。
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace PaperSiege
{
    public class TitleMenu : MonoBehaviour
    {
        Director d;
        Transform sign, zhSet, enSet, desk;
        readonly List<WoodButton> levelBtns = new();
        readonly List<WoodButton> buttons = new();
        TextMeshPro subtitle, credit;
        WoodButton startBtn, musicBtn, sfxBtn, handBtn;
        LineRenderer cordL, cordR;
        bool english;

        public static TitleMenu Build(Director d)
        {
            var m = new GameObject("TitleMenu").AddComponent<TitleMenu>();
            m.d = d;
            m.BuildSign();
            m.BuildDesk();
            m.english = Loc.English;
            m.ApplyLanguage(true);
            return m;
        }

        Vector3 F => d.arena.forward;
        Vector3 R => Vector3.Cross(Vector3.up, d.arena.forward);

        // ── 字标
        Transform Block(Transform parent, string ch, Paint paint, float size, Vector3 pos, float yaw, float roll, TMP_FontAsset font)
        {
            var g = new GameObject("Glyph_" + ch);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.Euler(0, yaw, roll);
            var lib = BlockLibrary.I;
            var pm = lib ? lib.Meshes("W_Cube") : null;
            var lk = new GameObject("Look"); lk.transform.SetParent(g.transform, false);
            if (pm != null)
            {
                lk.AddComponent<MeshFilter>().sharedMesh = pm.lod0;
                lk.AddComponent<MeshRenderer>().sharedMaterial = lib.Paint(paint);
                lk.transform.localScale = Vector3.one * (size / BlockLibrary.G);
            }
            var tg = new GameObject("Ch"); tg.transform.SetParent(g.transform, false);
            tg.transform.localPosition = new Vector3(0, size * 0.5f, -size * 0.5f - 0.0012f);
            var t = tg.AddComponent<TextMeshPro>();
            t.font = font; t.text = ch;
            t.rectTransform.sizeDelta = new Vector2(size * 0.9f, size * 0.9f);
            t.enableAutoSizing = true; t.fontSizeMin = 0.01f; t.fontSizeMax = 3f;
            t.alignment = TextAlignmentOptions.Center;
            t.color = new Color(0.17f, 0.11f, 0.07f);
            return g.transform;
        }

        void BuildSign()
        {
            var a = d.arena;
            sign = new GameObject("TitleSign").transform;
            sign.SetParent(transform, false);
            var head = d.head.transform.position;
            sign.position = new Vector3(a.tableCenter.x, head.y - 0.02f, a.tableCenter.z) + F * 0.95f;
            sign.rotation = Quaternion.LookRotation(F, Vector3.up);
            // 木梁
            var lib = BlockLibrary.I;
            var beam = lib ? lib.Meshes("W_Beam") : null;
            var bg = new GameObject("Beam"); bg.transform.SetParent(sign, false);
            if (beam != null)
            {
                bg.AddComponent<MeshFilter>().sharedMesh = beam.lod0;
                bg.AddComponent<MeshRenderer>().sharedMaterial = lib.Paint(Paint.Cream);
                bg.transform.localScale = new Vector3(0.82f / (3 * BlockLibrary.G), 0.035f / (0.4f * BlockLibrary.G), 0.09f / (0.8f * BlockLibrary.G));
            }
            // 两根吊绳（到天花板）
            cordL = Cord(); cordR = Cord();
            var hand = Fonts.Hand;
            // 中文字标：“木”（灰青，稍左低，往左歪）“垒”（赭黄，稍右高，往右歪），下面一排小积木 SLINGHOLD
            zhSet = new GameObject("ZH").transform; zhSet.SetParent(sign, false);
            Block(zhSet, "木", Paint.Teal, 0.12f, new Vector3(-0.13f, 0.0175f, 0), -6, 2, hand);
            Block(zhSet, "垒", Paint.Ochre, 0.12f, new Vector3(0.0f, 0.0175f + 0.014f, 0.01f), 7, -3, hand);
            Row(zhSet, "SLINGHOLD", 0.036f, new Vector3(0.085f, 0.0175f, -0.026f), hand, true);   // 靠木梁前沿（不被梁边挡住字）
            // 英文字标：一排大积木 SLINGHOLD（高低错落），右边两块小积木“木垒”
            enSet = new GameObject("EN").transform; enSet.SetParent(sign, false);
            Row(enSet, "SLINGHOLD", 0.064f, new Vector3(-0.07f, 0.0175f, -0.008f), hand, false);   // 9 块 × 6.66 cm ≈ 0.6 m，中心偏左给“木垒”留位
            Block(enSet, "木", Paint.Teal, 0.036f, new Vector3(0.295f, 0.0175f, -0.024f), -4, 0, hand);
            Block(enSet, "垒", Paint.Ochre, 0.036f, new Vector3(0.336f, 0.0175f, -0.022f), 5, 0, hand);
            // 副标题小木牌（挂在木梁下）
            subtitle = Tag(sign, new Vector3(-0.17f, -0.0175f, 0), new Vector2(0.16f, 0.1f), Fonts.Hand, 0.9f);
            // 署名牌：标志，另起一行文字（标志原样、不改色不拉伸）
            credit = CreditPlate(sign, new Vector3(0.18f, -0.0175f, 0));
        }

        void Row(Transform parent, string word, float size, Vector3 start, TMP_FontAsset font, bool small)
        {
            var paints = new[] { Paint.Cream, Paint.Brick, Paint.Teal, Paint.Ochre, Paint.Blue, Paint.Sage, Paint.Cream, Paint.Brick, Paint.Teal };
            float w = size * 1.04f;
            float x0 = start.x - (word.Length - 1) * w * 0.5f;
            if (small) x0 = start.x;   // 小字从起点往右排
            for (int i = 0; i < word.Length; i++)
            {
                float lift = small ? 0 : (i % 3 == 1 ? 0.012f : i % 3 == 2 ? 0.004f : 0f);
                Block(parent, word[i].ToString(), paints[i % paints.Length], size, new Vector3(x0 + i * w, start.y + lift, start.z), (i * 37 % 9) - 4, (i * 53 % 7) - 3, font);
            }
        }

        LineRenderer Cord()
        {
            var lr = new GameObject("Cord").AddComponent<LineRenderer>();
            lr.transform.SetParent(sign, false);
            lr.material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(0.55f, 0.45f, 0.32f) };
            lr.widthMultiplier = 0.0018f; lr.positionCount = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return lr;
        }

        /// <summary>
        /// 挂牌：木牌模型（6×4 cm）等比放大到宽 size.x（不拉伸）；字写在牌子正面。
        /// 牌子模型的正面朝本地 -Z、穿绳孔在原点（顶部），牌面在 y ∈ [-0.034, 0.006] × k、x ∈ ±0.03 × k。
        /// </summary>
        TextMeshPro Tag(Transform parent, Vector3 pos, Vector2 size, TMP_FontAsset font, float scaleMax)
        {
            const float Hang = 0.025f;   // 挂绳长
            float k = size.x / 0.06f;
            var g = new GameObject("Tag"); g.transform.SetParent(parent, false);
            g.transform.localPosition = pos + Vector3.down * Hang;   // g 的原点 = 穿绳孔
            var lib = BlockLibrary.I; var m = lib ? lib.Meshes("H_Tag") : null;
            if (m != null && lib.propMaterial)
            {
                var lk = new GameObject("Look"); lk.transform.SetParent(g.transform, false);
                lk.AddComponent<MeshFilter>().sharedMesh = m.lod0; lk.AddComponent<MeshRenderer>().sharedMaterial = lib.propMaterial;
                lk.transform.localScale = Vector3.one * k;
            }
            // 字：牌面中间（让开穿绳孔），牌子正面再往前 0.6 mm
            var tg = new GameObject("Text"); tg.transform.SetParent(g.transform, false);
            tg.transform.localPosition = new Vector3(0, -0.017f * k, -0.0026f * k);
            var t = tg.AddComponent<TextMeshPro>();
            t.font = font;
            t.rectTransform.sizeDelta = new Vector2(0.052f * k, 0.024f * k);
            t.enableAutoSizing = true; t.fontSizeMin = 0.01f; t.fontSizeMax = scaleMax;
            t.alignment = TextAlignmentOptions.Center; t.textWrappingMode = TextWrappingModes.NoWrap;
            t.color = new Color(0.2f, 0.13f, 0.08f);
            // 挂绳（木梁 → 穿绳孔）
            var lr = g.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(0.55f, 0.45f, 0.32f) };
            lr.widthMultiplier = 0.0012f; lr.positionCount = 2;
            lr.SetPosition(0, new Vector3(0, Hang, 0)); lr.SetPosition(1, Vector3.zero);
            g.AddComponent<SwayTag>();
            return t;
        }

        TextMeshPro CreditPlate(Transform parent, Vector3 pos)
        {
            const float W = 0.15f;
            float k = W / 0.06f;
            var t = Tag(parent, pos, new Vector2(W, 0.1f), Fonts.Brand, 0.3f);
            // 标志在上、文字另起一行在下
            t.rectTransform.sizeDelta = new Vector2(0.046f * k, 0.008f * k);
            t.transform.localPosition = new Vector3(0, -0.0255f * k, -0.0026f * k);
            t.color = new Color(0.16f, 0.12f, 0.1f);
            var logo = Resources.Load<Texture2D>("Brand/tripo_logo_h_800_black");
            if (logo)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(q.GetComponent<Collider>());
                q.name = "TripoLogo";
                q.transform.SetParent(t.transform.parent, false);
                float lw = 0.036f * k;
                q.transform.localScale = new Vector3(lw, lw * logo.height / logo.width, 1);   // 原样宽高比
                q.transform.localPosition = new Vector3(0, -0.0145f * k, -0.0026f * k);
                var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                m.SetFloat("_Surface", 1); m.SetOverrideTag("RenderType", "Transparent");
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = 3000;
                m.SetTexture("_BaseMap", logo); m.color = Color.white;
                q.GetComponent<MeshRenderer>().sharedMaterial = m;
            }
            return t;
        }

        // ── 桌上的按钮
        void BuildDesk()
        {
            var a = d.arena;
            desk = new GameObject("Desk").transform;
            desk.SetParent(transform, false);
            desk.SetPositionAndRotation(a.tableCenter - F * 0.215f, Quaternion.LookRotation(F, Vector3.up));
            // 开始（继续）
            startBtn = WoodButton.Build("W_Slab", Paint.Brick, new Vector3(0.12f, 0.04f, 0.06f), "开始", desk, 0.5f);
            startBtn.transform.localPosition = new Vector3(-0.32f, 0.02f, 0);
            startBtn.SetInk(new Color(0.98f, 0.94f, 0.86f));
            startBtn.onPress = () => d.StartLevel(Progress.Unlocked);
            buttons.Add(startBtn);
            // 选关：10 块小积木
            for (int i = 0; i < Levels.Count; i++)
            {
                int lv = i;
                bool open = i <= Progress.Unlocked;
                var p = open ? Palette.WoodPaints[i % Palette.WoodPaints.Length] : Paint.Bare;
                var b = WoodButton.Build("W_Cube", p, new Vector3(0.036f, 0.036f, 0.036f), (i + 1).ToString(), desk, 0.4f);
                b.transform.localPosition = new Vector3(-0.21f + i * 0.042f, 0.018f, 0);
                b.interactable = open;
                b.pressRadius = 0.03f;
                b.onPress = () => d.StartLevel(lv);
                if (!open) b.SetInk(new Color(0.45f, 0.36f, 0.27f, 0.5f));
                // 星星钉：积木顶上一排小圆钉（拿到几颗星就钉几颗）
                for (int k = 0; k < Progress.Stars(i); k++)
                {
                    var peg = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(peg.GetComponent<Collider>());
                    peg.transform.SetParent(b.transform, false);
                    peg.transform.localScale = new Vector3(0.006f, 0.002f, 0.006f);
                    peg.transform.localPosition = new Vector3((k - 1) * 0.009f, 0.0195f, 0);
                    peg.GetComponent<Renderer>().sharedMaterial = StarMat;
                }
                levelBtns.Add(b); buttons.Add(b);
            }
            // 地球仪：切换语言（图标无字；下面挂小牌“切换语言 / Language”）
            var globe = BuildGlobe(desk, new Vector3(0.27f, 0, 0f));
            globe.onPress = () => { Loc.English = !Loc.English; Progress.SaveLanguage(); ApplyLanguage(false); };
            buttons.Add(globe);
            // 音乐 / 音效
            musicBtn = WoodButton.Build("W_Cube", Paint.Sage, new Vector3(0.05f, 0.03f, 0.035f), "音乐", desk, 0.3f);
            musicBtn.transform.localPosition = new Vector3(0.345f, 0.015f, 0f);
            musicBtn.onPress = () => { Progress.Music = !Progress.Music; Refresh(); };
            sfxBtn = WoodButton.Build("W_Cube", Paint.Blue, new Vector3(0.05f, 0.03f, 0.035f), "音效", desk, 0.3f);
            sfxBtn.transform.localPosition = new Vector3(0.405f, 0.015f, 0f);
            sfxBtn.onPress = () => { Progress.Sfx = !Progress.Sfx; Refresh(); };
            buttons.Add(musicBtn); buttons.Add(sfxBtn);
            // 单手模式（兜底）：第二排，靠近玩家
            handBtn = WoodButton.Build("W_Cube", Paint.Ochre, new Vector3(0.11f, 0.022f, 0.035f), "双手弓", desk, 0.3f);
            handBtn.transform.localPosition = new Vector3(0.375f, 0.011f, -0.085f);
            handBtn.SetInk(new Color(0.24f, 0.16f, 0.10f));   // 赭黄底上用深褐字，看得清
            handBtn.onPress = () => { Progress.OneHand = !Progress.OneHand; Refresh(); };
            buttons.Add(handBtn);
            Refresh();
        }

        static Material starMat;
        static Material StarMat { get { if (!starMat) { starMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.95f, 0.78f, 0.35f) }; starMat.SetFloat("_Metallic", 0.6f); starMat.SetFloat("_Smoothness", 0.55f); } return starMat; } }

        WoodButton BuildGlobe(Transform parent, Vector3 pos)
        {
            var b = WoodButton.Build("W_Cube", Paint.Cream, new Vector3(0.05f, 0.015f, 0.05f), "", parent);
            b.transform.localPosition = pos + Vector3.up * 0.0075f;
            b.size = new Vector3(0.06f, 0.08f, 0.06f);   // 捏地球也算
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(ball.GetComponent<Collider>());
            ball.transform.SetParent(b.transform, false);
            ball.transform.localScale = Vector3.one * 0.045f;
            ball.transform.localPosition = new Vector3(0, 0.035f, 0);
            ball.GetComponent<Renderer>().sharedMaterial = BlockLibrary.I ? BlockLibrary.I.Paint(Paint.Teal) : null;
            // 经纬线（无字图标）
            var lm = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = new Color(0.25f, 0.18f, 0.12f) };
            for (int k = 0; k < 4; k++)
            {
                var lr = new GameObject("Meridian").AddComponent<LineRenderer>();
                lr.transform.SetParent(ball.transform, false); lr.useWorldSpace = false; lr.loop = true;
                lr.material = lm; lr.widthMultiplier = 0.0011f; lr.positionCount = 32;
                for (int i = 0; i < 32; i++)
                {
                    float t = i / 32f * Mathf.PI * 2;
                    var v = k < 2 ? Quaternion.Euler(0, k * 90 + 45, 0) * new Vector3(Mathf.Cos(t), Mathf.Sin(t), 0) * 0.505f
                                  : new Vector3(Mathf.Cos(t) * Mathf.Cos((k - 2.5f) * 0.6f), Mathf.Sin((k - 2.5f) * 0.6f), Mathf.Sin(t) * Mathf.Cos((k - 2.5f) * 0.6f)) * 0.505f;
                    lr.SetPosition(i, v);
                }
            }
            var spin = ball.AddComponent<Spin>(); spin.speed = 12f;
            // 小牌
            var tag = Tag(b.transform, new Vector3(0, -0.004f, -0.033f), new Vector2(0.06f, 0.04f), Fonts.Brand, 0.15f);
            tag.text = "切换语言 / Language";
            tag.transform.parent.GetComponent<LineRenderer>().enabled = false;
            tag.transform.parent.localRotation = Quaternion.Euler(-60, 0, 0);
            Destroy(tag.transform.parent.GetComponent<SwayTag>());
            return b;
        }

        void Refresh()
        {
            startBtn.SetText(Progress.Unlocked == 0 ? "开始" : "继续");
            musicBtn.SetText(Progress.Music ? "音乐" : "音乐 ×");
            sfxBtn.SetText(Progress.Sfx ? "音效" : "音效 ×");
            if (handBtn) handBtn.SetText(Progress.OneHand ? "单手弓" : "双手弓");
        }

        void ApplyLanguage(bool instant)
        {
            english = Loc.English;
            subtitle.text = english ? "A Tabletop Siege" : "弹弓攻城";
            credit.text = english ? "Tripo XR Developers" : "Tripo XR 出品";
            foreach (var b in buttons) b.Relocalize();
            Refresh();
            StopAllCoroutines();
            StartCoroutine(Flip(english ? zhSet : enSet, english ? enSet : zhSet, instant));
        }

        IEnumerator Flip(Transform from, Transform to, bool instant)
        {
            if (instant) { from.gameObject.SetActive(false); to.gameObject.SetActive(true); yield break; }
            // 旧的一块块翻下去，新的一块块翻上来（有过程）
            to.gameObject.SetActive(true);
            foreach (Transform c in to) c.localScale = Vector3.zero;
            float t = 0;
            while (t < 1.2f)
            {
                t += Time.deltaTime;
                int i = 0;
                foreach (Transform c in from) { float k = Mathf.Clamp01((t - i * 0.05f) / 0.25f); c.localScale = Vector3.one * (1 - k); i++; }
                i = 0;
                foreach (Transform c in to) { float k = Mathf.Clamp01((t - 0.35f - i * 0.05f) / 0.3f); float o = 1 + Mathf.Sin(k * Mathf.PI) * 0.15f; c.localScale = Vector3.one * k * o; i++; }
                yield return null;
            }
            from.gameObject.SetActive(false);
            foreach (Transform c in from) c.localScale = Vector3.one;
            foreach (Transform c in to) c.localScale = Vector3.one;
        }

        void LateUpdate()
        {
            // 吊绳：木梁两端 → 天花板
            float ceil = d.arena.ceilingHeight;
            var l = sign.TransformPoint(new Vector3(-0.37f, 0.0175f, 0)); var r = sign.TransformPoint(new Vector3(0.37f, 0.0175f, 0));
            cordL.SetPosition(0, l); cordL.SetPosition(1, new Vector3(l.x, ceil, l.z));
            cordR.SetPosition(0, r); cordR.SetPosition(1, new Vector3(r.x, ceil, r.z));
            cordL.useWorldSpace = cordR.useWorldSpace = true;
            // 木梁轻轻晃
            sign.rotation = Quaternion.LookRotation(F, Vector3.up) * Quaternion.Euler(0, Mathf.Sin(Time.time * 0.7f) * 1.2f, Mathf.Sin(Time.time * 0.9f) * 0.6f);
        }

        public void Close() { StartCoroutine(CloseRun()); }

        IEnumerator CloseRun()
        {
            foreach (var b in buttons) b.interactable = false;
            // 木梁往上收回天花板，桌上的按钮沉下去
            var s0 = sign.position; var d0 = desk.position;
            for (float k = 0; k < 1f; k += Time.deltaTime / 0.6f)
            {
                float e = k * k;
                sign.position = s0 + Vector3.up * e * 1.2f;
                desk.position = d0 - Vector3.up * e * 0.06f;
                yield return null;
            }
            Destroy(gameObject);
        }
    }

    public class Spin : MonoBehaviour { public float speed = 20f; void Update() => transform.Rotate(Vector3.up, speed * Time.deltaTime, Space.Self); }

    public class SwayTag : MonoBehaviour
    {
        Quaternion rest; float seed;
        void Start() { rest = transform.localRotation; seed = Random.value * 10; }
        void Update() => transform.localRotation = rest * Quaternion.Euler(0, 0, Mathf.Sin(Time.time * 1.3f + seed) * 2.5f);
    }
}

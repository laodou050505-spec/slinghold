// 封面（11-25 定稿方向 D：帅旗灯笼小场景）—— 浮在真实房间里的一组实物，不是面板：
//   旧木旗杆（转 180°：布旗在左、灯笼在右）→ 横杆上挂一面奶白布旗（上：原样 TRIPO 图标 + 署名一行；下：积木拼字标题）
//   → 右侧横臂挂一盏点着的纸灯笼 → 杆中段伸出一根挂木签的横臂，四块木签 = 菜单按钮（看着 / 指着 + 捏 = 按）
//   → 左边十来块旧漆积木 + 一颗石弹像刚被一发打中那样慢动作散开，几张攻城档案纸页飘着，红色点线弹道 + 叉号。
// 出现、换页、收起都有过程（旗子从横杆上放下来、木签一块块落下、积木从打中的那一点散开 / 收起时反过来）。
// 页面：主菜单（开始 / 继续、选择关卡、设置、退出）→ 设置（切换语言、单手 / 双手弓、声音、返回）→ 选关（10 块编号积木 + 返回）。
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace PaperSiege
{
    public class CoverMenu : MonoBehaviour
    {
        Director d;
        Transform root, pole, lanternPivot, arm, fx;
        BannerCloth cloth;
        Light lamp;
        readonly List<HangTag> tags = new();
        readonly List<WoodButton> levelBtns = new();
        readonly List<Drift> drifts = new();
        readonly List<LineRenderer> arcs = new();
        enum Page { Main, Settings, Levels }
        Page page = Page.Main;
        bool busy;
        public float Appear { get; private set; }     // 0 → 1：出现过程（测试用）

        // ── 布局（root 坐标：原点 = 旗杆底，+Z 背离玩家，+X 玩家右手边）
        public const float Scale = 1.3f;                // 整组放大（模型按 1:1 建，头显里 0.9 m 外要看得清标题）
        const float BarY = 0.845f;                      // 挂旗横杆高度
        const float BannerW = 0.26f;
        static readonly Vector3 BannerTop = new(-0.168f, BarY - 0.009f, 0f);
        static readonly Vector3 LanternHook = new(0.1775f, 0.711f, 0f);
        const float ArmY = 0.40f;                       // 木签横臂（杆上铁箍的高度）
        static readonly float[] Knots = { 0.07f, 0.13f, 0.19f, 0.25f };      // 横臂上四个挂绳结（米，沿横臂）
        static readonly float[] Cords = { 0.02f, 0.08f, 0.14f, 0.20f };      // 四根挂绳长（木签上下错开成一列，最低一块也要高过桌面）
        static readonly Vector3 Impact = new(-0.33f, 0.47f, 0.03f);         // 积木散开的“被打中”那一点

        public static CoverMenu Build(Director d)
        {
            var m = new GameObject("CoverMenu").AddComponent<CoverMenu>();
            m.d = d;
            m.BuildAll();
            m.StartCoroutine(m.Open());
            return m;
        }

        // ───────────────────── 资源
        static Dictionary<string, Mesh> meshes;
        public static Mesh M(string id)
        {
            if (meshes == null)
            {
                meshes = new();
                foreach (var x in Resources.LoadAll<Mesh>("Cover/cover")) meshes[x.name] = x;
            }
            return meshes.TryGetValue(id, out var r) ? r : null;
        }
        public static Material Mat(string n) => Resources.Load<Material>("Cover/" + n);

        static GameObject Part(string name, Transform parent, Mesh mesh, Material mat)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            if (mesh) { g.AddComponent<MeshFilter>().sharedMesh = mesh; g.AddComponent<MeshRenderer>().sharedMaterial = mat; }
            return g;
        }

        Vector3 F => d.arena.forward;

        void BuildAll()
        {
            var head = d.head.transform.position;
            var right = Vector3.Cross(Vector3.up, F);
            root = new GameObject("Cover").transform;
            root.SetParent(transform, false);
            // 布旗中心（root 里 y≈0.555）放在眼睛高度；封面最低的东西（root 里 y≈0.15）要高过桌面 5 cm 以上（头显里真桌子一般 0.7～0.8 m）
            float baseY = Mathf.Max(head.y - 0.555f * Scale, d.arena.tableCenter.y + 0.05f - 0.15f * Scale);
            root.SetPositionAndRotation(new Vector3(head.x, baseY, head.z) + F * 0.92f + right * 0.11f, Quaternion.LookRotation(F, Vector3.up));
            root.localScale = Vector3.one * Scale;
            var cover = Mat("M_Cover");

            // 旗杆（模型里布旗横杆在 +X、灯笼臂在 -X；转 180° 后布旗在左、灯笼在右，和效果图 D 一致）
            pole = Part("Pole", root, M("CV_Pole"), cover).transform;
            pole.localRotation = Quaternion.Euler(0, 180, 0);

            // 布旗
            var bg = new GameObject("Banner");
            bg.transform.SetParent(root, false);
            bg.transform.localPosition = BannerTop;
            cloth = bg.AddComponent<BannerCloth>();
            cloth.Init(BannerW, BannerW * 2304f / 1024f, Mat(Loc.English ? "M_Banner_en" : "M_Banner_zh"));
            // 布旗绑在横杆上的三道绳圈
            foreach (float x in new[] { -0.285f, -0.168f, -0.05f }) RopeLoop(root, new Vector3(x, BarY, 0), 0.0125f);

            // 灯笼（挂钩处摆动）+ 暖光
            lanternPivot = new GameObject("LanternPivot").transform;
            lanternPivot.SetParent(root, false);
            lanternPivot.localPosition = LanternHook;
            Part("Lantern", lanternPivot, M("CV_Lantern"), cover);
            lamp = new GameObject("LanternLight").AddComponent<Light>();
            lamp.transform.SetParent(lanternPivot, false);
            lamp.transform.localPosition = new Vector3(0, -0.075f, 0);
            lamp.type = LightType.Point; lamp.color = new Color(1f, 0.68f, 0.38f); lamp.range = 0.9f; lamp.intensity = 0f;
            lamp.shadows = LightShadows.None;

            // 木签横臂：从杆上的铁箍伸向玩家右前方（模型沿 -X；转 -135° 后指向“右 + 朝玩家”）
            arm = Part("TagArm", root, M("CV_TagArm"), cover).transform;
            arm.localPosition = new Vector3(0, ArmY, 0);
            arm.localRotation = Quaternion.Euler(0, -158, 0);   // 主要往右、少一点朝玩家：木签那一列和旗杆错开
            for (int i = 0; i < 4; i++)
            {
                var knot = root.InverseTransformPoint(arm.TransformPoint(new Vector3(-Knots[i], -0.004f, 0)));
                var t = HangTag.Build(root, knot, Cords[i], M("CV_Tag"), cover, i);
                tags.Add(t);
            }

            fx = new GameObject("Debris").transform;
            fx.SetParent(root, false);
            BuildDebris();
            BuildScraps();
            BuildArcs();
            SetPage(Page.Main, true);
        }

        void RopeLoop(Transform parent, Vector3 c, float r)
        {
            var lr = new GameObject("Rope").AddComponent<LineRenderer>();
            lr.transform.SetParent(parent, false);
            lr.useWorldSpace = false; lr.loop = true; lr.positionCount = 14;
            lr.sharedMaterial = Mat("M_Cord"); lr.widthMultiplier = 0.0035f;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            for (int i = 0; i < 14; i++)
            {
                float a = i / 14f * Mathf.PI * 2;
                lr.SetPosition(i, c + new Vector3(0, Mathf.Sin(a) * r, Mathf.Cos(a) * r));
            }
        }

        // ───────────────────── 散开的积木 + 石弹
        void BuildDebris()
        {
            var lib = BlockLibrary.I;
            if (!lib) return;
            (string id, Paint p, Vector3 pos, Vector3 rot, float s)[] list =
            {
                ("W_Cube", Paint.Teal, new(-0.44f, 0.66f, 0.02f), new(20, 35, 10), 1.4f),
                ("W_Cone", Paint.Brick, new(-0.47f, 0.90f, 0.12f), new(-25, 0, 30), 1.3f),
                ("W_Arch", Paint.Ochre, new(-0.62f, 0.50f, 0.06f), new(10, -20, -15), 1.2f),
                ("W_Cube", Paint.Cream, new(-0.39f, 0.40f, -0.10f), new(35, 10, 40), 1.15f),
                ("W_Window", Paint.Teal, new(-0.53f, 0.27f, 0.02f), new(-15, 25, 5), 1.1f),
                ("W_Cone", Paint.Teal, new(-0.33f, 0.21f, -0.15f), new(60, 0, -20), 1.05f),
                ("W_Cube", Paint.Brick, new(-0.17f, 0.17f, -0.06f), new(15, 50, 25), 1.15f),
                ("W_Column", Paint.Sage, new(-0.78f, 0.36f, 0.22f), new(70, 10, 0), 0.95f),
                ("W_Slab", Paint.Ochre, new(-0.22f, 0.30f, -0.18f), new(-10, 20, -30), 0.95f),
                ("W_Cube", Paint.Ochre, new(-0.60f, 0.76f, 0.20f), new(5, -30, 55), 0.9f),
            };
            foreach (var (id, p, pos, rot, s) in list)
            {
                var pm = lib.Meshes(id);
                if (pm == null || !pm.lod0) continue;
                var piv = new GameObject("Piece_" + id).transform;
                piv.SetParent(fx, false);
                piv.localPosition = pos; piv.localRotation = Quaternion.Euler(rot);
                var look = Part("Look", piv, pm.lod0, lib.Paint(p)).transform;
                look.localScale = Vector3.one * s;
                look.localPosition = -pm.lod0.bounds.center * s;     // 绕自身中心转
                drifts.Add(Drift.Add(piv.gameObject, Impact));
            }
            var ball = lib.Meshes("B_Stone");
            if (ball != null && ball.lod0)
            {
                var piv = new GameObject("Ball").transform;
                piv.SetParent(fx, false); piv.localPosition = new Vector3(-0.33f, 0.58f, -0.13f);
                var look = Part("Look", piv, ball.lod0, lib.propMaterial).transform;
                look.localScale = Vector3.one * 2.4f; look.localPosition = -ball.lod0.bounds.center * 2.4f;
                drifts.Add(Drift.Add(piv.gameObject, Impact));
            }
        }

        // ───────────────────── 纸页
        void BuildScraps()
        {
            (int i, float w, float aspect, Vector3 pos, Vector3 rot)[] list =
            {
                (1, 0.16f, 560f / 768f, new(-0.74f, 1.0f, 0.26f), new(8, 18, -9)),
                (3, 0.13f, 640f / 720f, new(-0.70f, 0.16f, -0.06f), new(-12, 30, 7)),
                (2, 0.12f, 560f / 640f, new(0.50f, 0.42f, 0.18f), new(10, -28, 6)),
            };
            foreach (var (i, w, aspect, pos, rot) in list)
            {
                var g = new GameObject("Scrap_" + i);
                g.transform.SetParent(fx, false);
                g.transform.localPosition = pos; g.transform.localRotation = Quaternion.Euler(rot);
                g.AddComponent<MeshFilter>().sharedMesh = CurledQuad(w, w * aspect, 0.012f + i * 0.003f);
                var mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = Mat("M_Scrap_" + i);
                drifts.Add(Drift.Add(g, Impact, 0.6f, false));   // 纸页：小幅来回摆，不整圈转（会转到背面）
            }
        }

        /// <summary>微微卷起的纸：沿宽度方向弯一点（中间凹、两边翘），正面朝 -Z</summary>
        public static Mesh CurledQuad(float w, float h, float curl)
        {
            const int nx = 10, ny = 6;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int y = 0; y <= ny; y++)
                for (int x = 0; x <= nx; x++)
                {
                    float u = x / (float)nx, t = y / (float)ny;
                    float z = curl * (Mathf.Pow(2 * u - 1, 2) - 0.3f) + curl * 0.3f * Mathf.Sin(t * 3f);
                    v.Add(new Vector3((u - 0.5f) * w, (t - 0.5f) * h, -z)); uv.Add(new Vector2(u, t));
                }
            for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx; x++)
                {
                    int a = y * (nx + 1) + x, b = a + 1, c = a + nx + 1, e = c + 1;
                    tri.AddRange(new[] { a, c, b, b, c, e });
                }
            var m = new Mesh(); m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        // ───────────────────── 红色点线弹道 + 叉号
        void BuildArcs()
        {
            Arc(new Vector3(-0.78f, 0.12f, 0.16f), Impact, 0.26f, true);
            Arc(new Vector3(-0.80f, 0.52f, 0.20f), new Vector3(-0.50f, 0.86f, 0.06f), 0.12f, true);
            // 第三条从木签下方掠过（11-27：原来弹道最高点正好在木签那一列，虚线穿过“设置”两个字），从旗杆前面经过、不穿杆
            Arc(new Vector3(-0.24f, 0.0f, -0.06f), new Vector3(0.36f, 0.0f, 0.02f), 0.06f, true);
        }

        void Arc(Vector3 a, Vector3 b, float apex, bool cross)
        {
            var lr = new GameObject("Arc").AddComponent<LineRenderer>();
            lr.transform.SetParent(fx, false);
            lr.useWorldSpace = false; lr.positionCount = 32;
            // 11-27 用户：“封面的辅助线怎么没了”——其实一直在，只是 5.5 mm 宽、每米 70 个小点，在头显分辨率下糊没了 → 加宽到 1 cm、点放大、颜色压深
            var mat = new Material(Mat("M_Dots")); mat.mainTextureScale = new Vector2(34f, 1f);   // 每米约 34 个点
            mat.color = new Color(0.62f, 0.22f, 0.16f, 1f);
            lr.sharedMaterial = mat; lr.textureMode = LineTextureMode.Tile; lr.widthMultiplier = 0.010f;
            lr.alignment = LineAlignment.View; lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            for (int i = 0; i < 32; i++)
            {
                float t = i / 31f;
                lr.SetPosition(i, Vector3.Lerp(a, b, t) + Vector3.up * (4 * apex * t * (1 - t)));
            }
            arcs.Add(lr);
            if (cross)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(q.GetComponent<Collider>());
                q.name = "Cross"; q.transform.SetParent(lr.transform, false);
                q.transform.localPosition = b; q.transform.localScale = Vector3.one * 0.03f;
                q.GetComponent<MeshRenderer>().sharedMaterial = Mat("M_Cross");
                q.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        // ───────────────────── 页面
        void SetPage(Page p, bool instant)
        {
            page = p;
            string[] keys = p switch
            {
                Page.Main => new[] { Progress.Cleared ? "从头再来" : Progress.Unlocked == 0 ? "开始游戏" : "继续游戏", "选择关卡", "设置", "退出游戏" },   // 通关后：从第一关重新打（选关仍全开）
                Page.Settings => new[] { "切换语言\nLanguage", Progress.OneHand ? "单手弓" : "双手弓", SoundKey(), "返回" },
                _ => new[] { "返回", null, null, null },
            };
            System.Action[] acts = p switch
            {
                Page.Main => new System.Action[] { () => Begin(Progress.Cleared ? 0 : Progress.Unlocked), () => Go(Page.Levels), () => Go(Page.Settings), Quit },
                Page.Settings => new System.Action[] { ToggleLanguage, () => { Progress.OneHand = !Progress.OneHand; tags[1].Retext(Progress.OneHand ? "单手弓" : "双手弓"); }, CycleSound, () => Go(Page.Main) },
                _ => new System.Action[] { () => Go(Page.Main), null, null, null },
            };
            for (int i = 0; i < tags.Count; i++) tags[i].Set(keys[i], acts[i], instant, i * 0.08f);
            ShowLevels(p == Page.Levels, instant);
        }

        void Go(Page p) { if (!busy) SetPage(p, false); }

        static string SoundKey() => Progress.Music && Progress.Sfx ? "声音：开" : Progress.Sfx ? "声音：只留音效" : "声音：关";
        void CycleSound()
        {
            if (Progress.Music && Progress.Sfx) Progress.Music = false;
            else if (Progress.Sfx) Progress.Sfx = false;
            else { Progress.Music = true; Progress.Sfx = true; }
            tags[2].Retext(SoundKey());
        }

        void ToggleLanguage()
        {
            Loc.English = !Loc.English; Progress.SaveLanguage();
            foreach (var t in tags) t.Relocalize();
            foreach (var b in levelBtns) b.Relocalize();
            StartCoroutine(SwapBanner());
        }

        /// <summary>换语言：布旗卷上去 → 换印花 → 再放下来</summary>
        IEnumerator SwapBanner()
        {
            for (float t = 1; t > 0; t -= Time.unscaledDeltaTime / 0.35f) { cloth.reveal = Mathf.SmoothStep(0, 1, t); yield return null; }
            cloth.reveal = 0; cloth.SetMaterial(Mat(Loc.English ? "M_Banner_en" : "M_Banner_zh"));
            for (float t = 0; t < 1; t += Time.unscaledDeltaTime / 0.6f) { cloth.reveal = Mathf.SmoothStep(0, 1, t); yield return null; }
            cloth.reveal = 1;
        }

        void Quit()
        {
            Debug.Log("[封面] 退出游戏");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void Begin(int level) { if (!busy) d.BeginFromTitle(level); }   // 先收封面 + 搭虚拟桌，再开局

        // 选关：10 块编号积木，在布旗下方排两行（解锁的上漆、写关号、星星钉；没解锁的是裸木）
        void ShowLevels(bool on, bool instant)
        {
            if (on && levelBtns.Count == 0)
            {
                var row = new GameObject("Levels").transform; row.SetParent(root, false);
                for (int i = 0; i < Levels.Count; i++)
                {
                    int lv = i;
                    bool open = i <= Progress.Unlocked;
                    var p = open ? Palette.WoodPaints[i % Palette.WoodPaints.Length] : Paint.Bare;
                    var b = WoodButton.Build("W_Cube", p, new Vector3(0.048f, 0.048f, 0.048f), (i + 1).ToString(), row, 0.5f);
                    int c = i % 6, r = i / 6;   // 18 关：6 列 × 3 排（一排就是两个时段）
                    b.transform.localPosition = new Vector3(0.08f + c * 0.064f, 0.29f - r * 0.078f, -0.08f - c * 0.011f);
                    b.transform.localRotation = Quaternion.Euler(0, -8f - c * 2f, (i * 37 % 7) - 3);
                    b.remote = true; b.interactable = open; b.pressRadius = 0.035f;
                    b.onPress = () => Begin(lv);
                    if (!open) b.SetInk(new Color(0.45f, 0.36f, 0.27f, 0.55f));
                    for (int k = 0; k < Progress.Stars(i); k++)
                    {
                        var peg = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(peg.GetComponent<Collider>());
                        peg.transform.SetParent(b.transform, false);
                        peg.transform.localScale = new Vector3(0.007f, 0.0022f, 0.007f);
                        peg.transform.localPosition = new Vector3((k - 1) * 0.012f, 0.0262f, 0);
                        peg.GetComponent<Renderer>().sharedMaterial = StarMat;
                    }
                    b.transform.localScale = Vector3.zero;
                    levelBtns.Add(b);
                }
            }
            for (int i = 0; i < levelBtns.Count; i++) StartCoroutine(Pop(levelBtns[i].transform, on, instant ? 0 : 0.15f + i * 0.04f));
        }

        static Material starMat;
        static Material StarMat { get { if (!starMat) { starMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.95f, 0.78f, 0.35f) }; starMat.SetFloat("_Metallic", 0.6f); starMat.SetFloat("_Smoothness", 0.55f); } return starMat; } }

        static IEnumerator Pop(Transform t, bool on, float delay)
        {
            if (delay > 0) yield return new WaitForSecondsRealtime(delay);
            float from = t.localScale.x, to = on ? 1f : 0f;
            for (float k = 0; k < 1f; k += Time.unscaledDeltaTime / 0.25f)
            {
                float e = on ? 1f + Mathf.Sin(k * Mathf.PI) * 0.18f : 1f;
                t.localScale = Vector3.one * Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, k)) * e;
                yield return null;
            }
            t.localScale = Vector3.one * to;
        }

        // ───────────────────── 出现 / 收起
        IEnumerator Open()
        {
            busy = true;
            var poleRest = Vector3.zero;
            cloth.reveal = 0; Appear = 0;
            foreach (var dr in drifts) { dr.spread = 0; dr.transform.localScale = Vector3.zero; }
            foreach (var t in tags) { t.drop = 0; t.Hold(true); }   // 出现过程中木签不高亮、不能按（看得见却捏不动会让人以为坏了）
            foreach (var a in arcs) a.enabled = false;
            pole.gameObject.SetActive(true);
            for (float t = 0; t < 2.0f; t += Time.unscaledDeltaTime)
            {
                Appear = t / 2.0f;
                float k = Mathf.Clamp01(t / 0.6f);
                root.localScale = Vector3.one * Scale;
                pole.localPosition = poleRest + Vector3.down * (1 - EaseOut(k)) * 0.25f;   // 旗杆从下面升上来
                arm.localPosition = new Vector3(0, ArmY, 0) + pole.localPosition;
                lanternPivot.localPosition = LanternHook + pole.localPosition;
                cloth.transform.localPosition = BannerTop + pole.localPosition;
                cloth.reveal = Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - 0.5f) / 0.8f));        // 布旗放下来
                lamp.intensity = Mathf.Clamp01((t - 0.9f) / 0.5f) * 0.9f;                          // 灯笼点亮
                for (int i = 0; i < tags.Count; i++) tags[i].drop = EaseOut(Mathf.Clamp01((t - 1.0f - i * 0.12f) / 0.4f));   // 木签一块块落下来
                foreach (var dr in drifts) { dr.spread = EaseOut(Mathf.Clamp01((t - 0.7f) / 0.9f)); dr.transform.localScale = Vector3.one * Mathf.Clamp01(dr.spread * 1.6f); }   // 积木从打中那一点由小变大散开
                foreach (var a in arcs) a.enabled = t > 1.1f;
                yield return null;
            }
            pole.localPosition = poleRest; cloth.transform.localPosition = BannerTop;
            arm.localPosition = new Vector3(0, ArmY, 0); lanternPivot.localPosition = LanternHook;
            cloth.reveal = 1; Appear = 1;
            foreach (var t in tags) { t.drop = 1; t.Hold(false); }
            foreach (var dr in drifts) { dr.spread = 1; dr.transform.localScale = Vector3.one; }
            busy = false;
        }

        static float EaseOut(float k) => 1 - (1 - k) * (1 - k) * (1 - k);

        public void Close() { busy = true; StopAllCoroutines(); StartCoroutine(CloseRun()); }

        IEnumerator CloseRun()
        {
            foreach (var t in tags) t.button.interactable = false;
            foreach (var b in levelBtns) b.interactable = false;
            float l0 = lamp.intensity;
            for (float t = 0; t < 0.9f; t += Time.unscaledDeltaTime)
            {
                float k = t / 0.9f;
                foreach (var tg in tags) tg.drop = 1 - Mathf.Clamp01(k * 2.2f);                   // 木签收回横臂
                foreach (var dr in drifts) dr.spread = 1 + EaseIn(k) * 1.6f;                      // 积木继续往外飞散
                foreach (var dr in drifts) dr.transform.localScale = Vector3.one * (1 - EaseIn(Mathf.Clamp01((k - 0.3f) / 0.7f)));
                foreach (var b in levelBtns) b.transform.localScale = Vector3.one * (1 - Mathf.Clamp01(k * 2f));
                cloth.reveal = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01(k * 1.6f));             // 布旗卷回横杆
                lamp.intensity = l0 * (1 - k);
                foreach (var a in arcs) a.enabled = k < 0.4f;
                root.localScale = Vector3.one * Scale * (1 - EaseIn(Mathf.Clamp01((k - 0.55f) / 0.45f)) * 0.98f);   // 最后整组收走
                yield return null;
            }
            Destroy(gameObject);
        }

        static float EaseIn(float k) => k * k * k;

        void Update()
        {
            if (!root) return;
            float t = Time.unscaledTime;
            // 灯笼轻轻摆、灯光微微跳
            lanternPivot.localRotation = Quaternion.Euler(Mathf.Sin(t * 1.1f) * 2.5f, 0, Mathf.Sin(t * 0.83f + 1f) * 3.5f);
            if (!busy) lamp.intensity = 0.9f * (0.94f + 0.06f * Mathf.PerlinNoise(t * 3f, 0.3f));
        }

        // 测试用
        public IReadOnlyList<HangTag> Tags => tags;
        public IReadOnlyList<WoodButton> LevelButtons => levelBtns;
        public bool Busy => busy;
    }

    /// <summary>一块挂在横臂上的木签：挂绳 + 木签（WoodButton，远程可按）+ 字 + 悬停时一道红铅笔勾；换页时转一圈换字</summary>
    public class HangTag : MonoBehaviour
    {
        public WoodButton button;
        public float drop = 1f;       // 0 = 收在横臂上，1 = 落到位
        TextMeshPro text;
        LineRenderer cord, tick;
        Transform swing;
        float cordLen, seed, kick, tickA;
        string key;
        const float TagCenter = 0.022f;    // 穿绳孔到木签中心
        const float TextY = -0.0227f;      // 字块中心（模型坐标：原点在上穿绳孔上方，上下两孔之间的版心中心）

        public static HangTag Build(Transform parent, Vector3 knot, float cordLen, Mesh mesh, Material mat, int index)
        {
            var h = new GameObject("Hang_" + index).AddComponent<HangTag>();
            h.transform.SetParent(parent, false);
            h.transform.localPosition = knot;
            h.cordLen = cordLen; h.seed = index * 1.7f;
            h.swing = new GameObject("Swing").transform; h.swing.SetParent(h.transform, false);
            h.cord = new GameObject("Cord").AddComponent<LineRenderer>();
            h.cord.transform.SetParent(h.transform, false);
            h.cord.useWorldSpace = true; h.cord.positionCount = 2; h.cord.widthMultiplier = 0.0022f;
            h.cord.sharedMaterial = Resources.Load<Material>("Cover/M_Cord");
            h.cord.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var bgo = new GameObject("Tag");
            bgo.transform.SetParent(h.swing, false);
            bgo.transform.localPosition = new Vector3(0, -cordLen - TagCenter, 0);
            var look = new GameObject("Look");
            look.transform.SetParent(bgo.transform, false);
            look.transform.localPosition = new Vector3(0, TagCenter, 0);
            look.transform.localRotation = Quaternion.Euler(0, 0, (index % 2 == 0 ? 1 : -1) * 2.5f);
            look.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = look.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            // 字：木签正面（-Z），比中心略低一点（让开穿绳孔）
            var tg = new GameObject("Text"); tg.transform.SetParent(look.transform, false);
            // 版心 = 上孔下沿（−0.85 cm）到下孔上沿（−3.68 cm）：中心 −2.27 cm、高 2.8 cm。字块上下左右都居中（11-27：原来低了 4 mm，文字框还高过版心会压到孔）
            tg.transform.localPosition = new Vector3(0, TextY, -0.0053f);
            h.text = tg.AddComponent<TextMeshPro>();
            h.text.font = Fonts.Type;
            h.text.rectTransform.sizeDelta = new Vector2(0.112f, 0.024f);
            h.text.enableAutoSizing = true; h.text.fontSizeMin = 0.02f; h.text.fontSizeMax = 0.26f;
            h.text.fontStyle = FontStyles.Bold;
            h.text.alignment = TextAlignmentOptions.Center; h.text.textWrappingMode = TextWrappingModes.NoWrap; h.text.lineSpacing = -32f;
            h.text.color = new Color(0.13f, 0.08f, 0.05f);   // 深褐墨：在木纹上看得清
            // 悬停：红铅笔在字下面划一道
            h.tick = new GameObject("Tick").AddComponent<LineRenderer>();
            h.tick.transform.SetParent(look.transform, false);
            h.tick.useWorldSpace = false; h.tick.positionCount = 6; h.tick.widthMultiplier = 0.0022f;
            h.tick.sharedMaterial = new Material(Resources.Load<Material>("Cover/M_Cord")) { color = new Color(0.66f, 0.29f, 0.23f) };
            h.tick.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            for (int i = 0; i < 6; i++) { float u = i / 5f; h.tick.SetPosition(i, new Vector3(-0.046f + 0.092f * u, TextY - 0.0105f + Mathf.Sin(u * 7f) * 0.0012f - u * 0.002f, -0.0056f)); }   // 字下面一道，不碰底孔
            h.tick.enabled = false;
            h.button = WoodButton.Attach(bgo, look.transform, mr, new Vector3(0.13f, 0.052f, 0.012f), h.text, null);
            h.button.remote = true; h.button.pressRadius = 0.05f;
            h.button.onHover = hov => { if (hov) h.kick = 1f; };
            return h;
        }

        public void Set(string zh, System.Action act, bool instant, float delay)
        {
            if (instant) { Apply(zh, act); return; }
            if (zh == null && key == null) { gameObject.SetActive(false); return; }   // 本来就藏着
            gameObject.SetActive(true);
            StartCoroutine(Flip(zh, act, delay));
        }

        void Apply(string zh, System.Action act)
        {
            key = zh;
            if (zh == null) { gameObject.SetActive(false); return; }
            gameObject.SetActive(true);
            button.SetText(zh);
            button.onPress = act;
            button.interactable = act != null;
        }

        System.Action held;
        /// <summary>暂时不响应（出现过程中）；松开后恢复原来的按下动作</summary>
        public void Hold(bool on) { if (on) { held = button.onPress ?? held; button.interactable = false; } else { button.interactable = button.onPress != null; } }

        public void Retext(string zh) { key = zh; StartCoroutine(Flip(zh, button.onPress, 0)); }
        public void Relocalize() { button.Relocalize(); }
        public string Key => key;
        /// <summary>挂绳长度（木签中心到绳结）</summary>
        public float CordLen { get => cordLen; set => cordLen = value; }

        /// <summary>换字：木签绕挂绳转一圈，转到侧面（看不见字）时换</summary>
        IEnumerator Flip(string zh, System.Action act, float delay)
        {
            if (delay > 0) yield return new WaitForSecondsRealtime(delay);
            if (zh != null && key == null) { drop = 0; Apply(zh, act); for (float k = 0; k < 1; k += Time.unscaledDeltaTime / 0.35f) { drop = 1 - (1 - k) * (1 - k); yield return null; } drop = 1; yield break; }
            if (zh == null) { button.interactable = false; for (float k = 0; k < 1; k += Time.unscaledDeltaTime / 0.3f) { drop = 1 - k; yield return null; } Apply(null, null); drop = 1; yield break; }
            bool swapped = false;
            button.interactable = false;
            for (float k = 0; k < 1f; k += Time.unscaledDeltaTime / 0.5f)
            {
                swing.localRotation = Quaternion.Euler(0, Mathf.SmoothStep(0, 360, k), 0);
                if (!swapped && k > 0.25f) { swapped = true; Apply(zh, act); button.interactable = false; }
                yield return null;
            }
            swing.localRotation = Quaternion.identity;
            if (!swapped) Apply(zh, act);
            button.interactable = act != null;
        }

        void LateUpdate()
        {
            float t = Time.unscaledTime;
            kick = Mathf.MoveTowards(kick, 0, Time.unscaledDeltaTime * 0.9f);
            float sway = Mathf.Sin(t * 1.3f + seed) * 2.0f + Mathf.Sin(t * 7f) * kick * 6f;
            float len = cordLen * drop;
            var tagT = swing.GetChild(0);
            tagT.localPosition = new Vector3(0, -len - 0.022f, 0);
            tagT.localRotation = Quaternion.Euler(0, Mathf.Sin(t * 0.6f + seed) * 6f, sway);
            float s = Mathf.Clamp01(drop * 1.6f);
            tagT.localScale = Vector3.one * Mathf.Max(0.001f, s);
            cord.enabled = drop > 0.02f;
            cord.SetPosition(0, transform.position);
            cord.SetPosition(1, tagT.TransformPoint(new Vector3(0, 0.022f, 0)));
            bool hov = WoodButton.Hovered == button && button.interactable;
            tickA = Mathf.MoveTowards(tickA, hov ? 1 : 0, Time.unscaledDeltaTime * 5f);
            tick.enabled = tickA > 0.01f;
            if (tick.enabled) { tick.widthMultiplier = 0.0022f * tickA; }
        }
    }

    /// <summary>布旗：细分网格，顶边绑在横杆上；下摆随风慢慢摆（顶点动画）；reveal = 放下多少（卷起的部分收成一卷挂在横杆下）</summary>
    public class BannerCloth : MonoBehaviour
    {
        public float reveal = 1f;
        const int NX = 18, NY = 44;
        Mesh mesh; MeshRenderer mr;
        Vector3[] rest, verts;
        float w, h;

        public void Init(float width, float height, Material mat)
        {
            w = width; h = height;
            mesh = new Mesh { name = "BannerCloth" }; mesh.MarkDynamic();
            rest = new Vector3[(NX + 1) * (NY + 1)]; verts = new Vector3[rest.Length];
            var uv = new Vector2[rest.Length]; var tri = new List<int>();
            for (int y = 0; y <= NY; y++)
                for (int x = 0; x <= NX; x++)
                {
                    int i = y * (NX + 1) + x;
                    float u = x / (float)NX, v = y / (float)NY;
                    rest[i] = new Vector3((u - 0.5f) * w, -v * h, 0);
                    uv[i] = new Vector2(u, 1 - v);
                }
            for (int y = 0; y < NY; y++)
                for (int x = 0; x < NX; x++)
                {
                    int a = y * (NX + 1) + x, b = a + 1, c = a + NX + 1, e = c + 1;
                    tri.AddRange(new[] { a, b, c, b, e, c });
                }
            mesh.vertices = rest; mesh.uv = uv; mesh.SetTriangles(tri, 0);
            mesh.bounds = new Bounds(new Vector3(0, -h / 2, 0), new Vector3(w * 1.5f, h * 1.2f, 0.2f));
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            mr = gameObject.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.receiveShadows = false;   // 盒盖 / 横杆的影子会横着切过标题，布旗只受光不接影子
            Update();
        }

        public void SetMaterial(Material m) { if (mr) mr.sharedMaterial = m; }

        void Update()
        {
            if (mesh == null) return;
            float t = Time.unscaledTime, r = Mathf.Clamp01(reveal);
            for (int y = 0; y <= NY; y++)
            {
                float v = y / (float)NY;
                float hang = Mathf.Pow(v, 1.25f);                      // 越往下摆得越多
                for (int x = 0; x <= NX; x++)
                {
                    int i = y * (NX + 1) + x;
                    float u = x / (float)NX;
                    var p = rest[i];
                    float wave = Mathf.Sin(t * 1.25f + v * 4.2f + u * 1.7f) * 0.010f + Mathf.Sin(t * 0.7f + u * 5.1f + v * 1.3f) * 0.005f;
                    p.z += wave * hang;
                    p.x += Mathf.Sin(t * 0.9f + v * 2.0f) * 0.004f * hang;
                    if (v > r)
                    {
                        // 还没放下来的部分：卷成一卷，挂在已放下部分的底边
                        float over = (v - r) * h;
                        float ang = over / 0.006f;
                        p.y = -r * h + Mathf.Sin(ang) * 0.006f - 0.006f;
                        p.z = (1 - Mathf.Cos(ang)) * 0.006f + wave * Mathf.Pow(r, 1.25f);
                    }
                    verts[i] = p;
                }
            }
            mesh.vertices = verts;
            mesh.RecalculateNormals();
        }
    }

    /// <summary>封面上飘着的东西：从“打中”的那一点往外慢慢散开（spread 0 → 1），之后极慢地漂、转</summary>
    public class Drift : MonoBehaviour
    {
        public float spread = 1f;
        Vector3 home, from; Quaternion rot0; Vector3 axis; float speed, phase, amp; bool spin;

        public static Drift Add(GameObject g, Vector3 impact, float ampScale = 1f, bool spin = true)
        {
            var d = g.AddComponent<Drift>();
            d.spin = spin;
            d.home = g.transform.localPosition; d.from = impact; d.rot0 = g.transform.localRotation;
            var rng = new System.Random(g.name.GetHashCode() ^ (int)(d.home.x * 1000));
            d.axis = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f).normalized;
            d.speed = 3f + (float)rng.NextDouble() * 6f; d.phase = (float)rng.NextDouble() * 10f; d.amp = 0.008f * ampScale;
            return d;
        }

        void Update()
        {
            float t = Time.unscaledTime;
            var dir = (home - from);
            var p = from + dir * spread + Vector3.up * Mathf.Sin(t * 0.5f + phase) * amp + dir.normalized * Mathf.Sin(t * 0.31f + phase) * amp;
            transform.localPosition = p;
            transform.localRotation = spin ? Quaternion.AngleAxis((t + phase * 10f) * speed * 0.35f, axis) * rot0
                                           : rot0 * Quaternion.Euler(Mathf.Sin(t * 0.7f + phase) * 6f, Mathf.Sin(t * 0.45f + phase) * 9f, Mathf.Sin(t * 0.6f + phase * 2f) * 4f);
        }
    }
}

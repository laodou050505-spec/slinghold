// 结算（11-26 重做，和封面同一套实物语言，不用一整块木板）：
//   从天花板开口垂下一根横木（两根吊绳）→ 横木左边挂一面小布旗：积木拼字的“胜利”（奶白旗）/“失守”“时间到”（火燎焦边旗）
//   → 横木右边的绳圈上，按钮木签（下一关 / 重来 / 回标题）首尾相连挂成一串，看着 / 指着 + 捏 = 按（和封面木签一样）；
//   → 战报木签接着挂在最下面那块按钮的底孔上，继续往下串：关名 / 射挡接修 / 三颗铜星 + 短词条件（拿到的是金星）。
//     每块只写一行短字、粗体墨字（11-26 用户：挂在布旗下面奇怪；字要好认）。
//   11-26 用户：原来的打字机纸条和布旗、木签不搭（还是别在半空、没真挂住）→ 全部换成同一种木签 + 绳，横木换成带绑绳 / 绳圈的 CV_Stick。
// 整组放在自己城的右前上方，不挡城。出现：横木落下来轻轻一弹 → 布旗放下 → 战报木签一块块荡下来 → 星一颗颗钉上 → 按钮木签落下；收起：反过来往上收。
//   三星：① 赢；② 接住 ≥ 3 发；③ 自己的帅旗一下没挨。
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace PaperSiege
{
    public class ResultBoard : MonoBehaviour
    {
        Director d;
        Transform root;
        readonly List<ReportLink> links = new();
        BannerCloth flag;
        LineRenderer cordL, cordR;
        readonly List<Transform> stars = new();
        readonly List<HangTag> tags = new();
        public int earned;
        bool[] got = new bool[3];
        bool finale;
        const float StickHalf = 0.15f;      // 吊绳绑在横木两端的绑绳处（CV_Stick ±3 格）
        const float FlagX = -0.07f, BtnX = 0.075f;   // 横木上两道绳圈（FBX 导入后 X 反向）
        const float LinkScale = 1.1f;       // 战报木签相对按钮木签（0.82）再放大 → 约 0.9，13 cm 宽的牌子上一行字
        const float HoleTop = -0.004f, HoleBottom = -0.04f;
        const float LinkGap = 0.026f;       // 木签之间的绳长（模型坐标）：底孔到下沿约 0.8 cm，绳要比这长才看得出是串起来的   // CV_Tag 两个穿绳孔（模型坐标，原点在顶孔上方）

        public static int Score(Director d, bool win)
        {
            if (!win) return 0;
            int s = 1;
            if (d.stats.caught >= 3) s++;
            if (d.arena.mine.kingHP >= d.arena.mine.kingMaxHP) s++;
            return s;
        }

        public static ResultBoard Build(Director d, bool win) => Build(d, win, false);

        /// <summary>finale = 终章（第十关打下王城、天亮之后）：“全胜”布旗 + “十城尽破” + 十关总星数，只留“回标题”</summary>
        public static ResultBoard Build(Director d, bool win, bool finale)
        {
            var r = new GameObject("ResultBoard").AddComponent<ResultBoard>();
            r.d = d;
            r.earned = Score(d, win);
            r.got = finale ? new[] { true } : new[] { win, win && d.stats.caught >= 3, win && d.arena.mine.kingHP >= d.arena.mine.kingMaxHP };
            r.finale = finale;
            r.BuildLook(win);
            r.StartCoroutine(r.Open());
            return r;
        }

        Vector3 F => d.arena.forward;

        TextMeshPro Text(Transform parent, Vector3 pos, Vector2 size, float max, TextAlignmentOptions al, Color col)
        {
            var g = new GameObject("Text"); g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            var t = g.AddComponent<TextMeshPro>();
            t.font = Fonts.Type;
            t.rectTransform.sizeDelta = size;
            t.enableAutoSizing = true; t.fontSizeMin = 0.01f; t.fontSizeMax = max;
            t.alignment = al; t.textWrappingMode = TextWrappingModes.NoWrap;
            t.color = col;
            return t;
        }

        void BuildLook(bool win)
        {
            var head = d.head.transform.position;
            var right = Vector3.Cross(Vector3.up, F);
            root = new GameObject("Result").transform;
            root.SetParent(transform, false);
            var pos = head + F * 0.55f + right * 0.23f;
            pos.y = Mathf.Max(head.y + 0.04f, d.arena.tableCenter.y + 0.5f);   // 按钮 + 战报一整串约 40 cm，最低的木签也要高过桌面
            root.position = pos;
            var look = pos - head; look.y *= 0.3f;
            root.rotation = Quaternion.LookRotation(look, Vector3.up) * Quaternion.Euler(0, 0, -2.5f);   // 横木微微斜一点

            var cover = CoverMenu.Mat("M_Cover");
            // 横木：两端绑绳 + 漆头，中间两道绳圈（布旗 / 按钮串各挂一处）
            var stick = new GameObject("Stick");
            stick.transform.SetParent(root, false);
            stick.AddComponent<MeshFilter>().sharedMesh = CoverMenu.M("CV_Stick") ?? CoverMenu.M("CV_TagArm");
            stick.AddComponent<MeshRenderer>().sharedMaterial = cover;
            cordL = Cord(); cordR = Cord();

            // 小布旗
            string key = finale ? "end" : win ? "win" : d.timeUp ? "time" : "lost";
            var fg = new GameObject("Flag"); fg.transform.SetParent(root, false);
            fg.transform.localPosition = new Vector3(FlagX, -0.012f, 0);
            flag = fg.AddComponent<BannerCloth>();
            flag.Init(0.15f, 0.15f * 640f / 512f, CoverMenu.Mat($"M_Result_{key}_{(Loc.English ? "en" : "zh")}"));

            // 按钮木签：从右边绳圈往下首尾相连挂成一串
            var btns = new List<(string zh, System.Action act)>();
            if (win && d.levelIndex < Levels.Count - 1) btns.Add(("下一关", () => d.StartLevel(d.levelIndex + 1)));
            if (!finale) btns.Add(("重来", () => d.StartLevel(d.levelIndex)));
            btns.Add(("回标题", () => d.GoTitle()));
            Transform parent = root; Vector3 knot = new Vector3(BtnX, -0.004f, 0);
            for (int i = 0; i < btns.Count; i++)
            {
                var t = HangTag.Build(parent, knot, i == 0 ? 0.03f : LinkGap, CoverMenu.M("CV_Tag"), cover, i);
                if (i == 0) t.transform.localScale = Vector3.one * 0.82f;
                t.Set(btns[i].zh, btns[i].act, true, 0);
                t.drop = 0; t.Hold(true);
                tags.Add(t);
                parent = t.transform.Find("Swing/Tag/Look"); knot = new Vector3(0, HoleBottom, 0);   // 下一块挂在这块的底孔上
            }

            // 战报木签：接着挂在最下面那块按钮的底孔上（不能按，只看）
            var hole = new GameObject("BottomHole").transform;
            hole.SetParent(parent, false); hole.localPosition = new Vector3(0, HoleBottom, 0);
            var ink = new Color(0.13f, 0.08f, 0.05f);
            var s = d.stats;
            // 正面能写字的带：两个穿绳孔之间，y -0.008 ～ -0.036（模型坐标）
            if (finale)
            {
                // 终章：“十城尽破” → 一颗金星 + 十关总星数
                var f0 = ReportLink.Build(hole, LinkGap + 0.016f, cover, 0, LinkScale);
                f0.Text(new Vector3(0, -0.022f, 0), new Vector2(0.118f, 0.03f), 0.5f, ink).text = Loc.T("十城尽破");   // 四个字，字号放到和“回标题”差不多
                links.Add(f0);
                var f1 = ReportLink.Build(f0.BottomHole, LinkGap, cover, 1, 1.15f);
                var st = Star(f1.look, new Vector3(-0.03f, -0.022f, -0.0058f), true);
                st.localScale = Vector3.zero; stars.Add(st);
                f1.Text(new Vector3(0.012f, -0.022f, 0), new Vector2(0.07f, 0.026f), 0.32f, ink).text = $"{N(Progress.TotalStars)} / {N(Levels.Count * 3)}";
                links.Add(f1);
                return;
            }
            var l0 = ReportLink.Build(hole, LinkGap + 0.016f, cover, 0, LinkScale);   // 和按钮组之间的绳长一点，两组分得开
            l0.Text(new Vector3(0, -0.022f, 0), new Vector2(0.112f, 0.028f), 0.32f, ink).text = Loc.T(d.level.title);
            links.Add(l0);
            var l1 = ReportLink.Build(l0.BottomHole, LinkGap, cover, 1, 1f);
            l1.Text(new Vector3(0, -0.022f, 0), new Vector2(0.112f, 0.028f), 0.32f, ink).text =
                string.Format(Loc.T("射{0} 挡{1} 修{2}"), N(s.shots), N(s.blocked), N(s.repaired));   // “接”写在星的条件里（接 2/3）
            links.Add(l1);
            var l2 = ReportLink.Build(l1.BottomHole, LinkGap, cover, 2, 1.15f);   // 三颗星 + 三个短词挤在一块，这块放大一点，字才够大
            string[] conds = { Loc.T("夺旗"), string.Format(Loc.T("接{0}/3"), N(Mathf.Min(s.caught, 99))), Loc.T("无伤") };
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * 0.041f;
                var st = Star(l2.look, new Vector3(x, -0.0118f, -0.0058f), got[i]);
                st.localRotation = Quaternion.Euler(0, 0, (i - 1) * 9f);
                st.localScale = Vector3.zero; stars.Add(st);
                // 没拿到的也用深墨色（浅褐字落在木纹上几乎看不见），拿没拿到只看星是金是灰
                var tt = l2.Text(new Vector3(x, -0.0285f, 0), new Vector2(0.04f, 0.016f), 0.3f, ink);
                tt.text = conds[i];
            }
            links.Add(l2);
        }

        /// <summary>数字放大一点，和汉字差不多高（黑体的数字比汉字矮）</summary>
        static string N(int v) => $"<size=110%>{v}</size>";

        LineRenderer Cord()
        {
            var lr = new GameObject("Cord").AddComponent<LineRenderer>();
            lr.transform.SetParent(root, false);
            lr.useWorldSpace = true; lr.positionCount = 2; lr.widthMultiplier = 0.0022f;
            lr.sharedMaterial = CoverMenu.Mat("M_Cord");
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return lr;
        }

        static Material gold, bare;
        static Material StarMat(bool g)
        {
            if (g && gold) return gold;
            if (!g && bare) return bare;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = g ? new Color(0.95f, 0.76f, 0.3f) : new Color(0.62f, 0.5f, 0.38f);
            m.SetFloat("_Metallic", g ? 0.55f : 0f); m.SetFloat("_Smoothness", g ? 0.6f : 0.15f);
            return g ? (gold = m) : (bare = m);
        }

        static Mesh starMesh;
        Transform Star(Transform parent, Vector3 pos, bool g)
        {
            if (!starMesh)
            {
                // 五角星：10 个外 / 内顶点，挤出 3 mm 厚（钉在纸上的小铜星）
                starMesh = new Mesh();
                var v = new List<Vector3>(); var tri = new List<int>();
                float h = 0.0015f;
                for (int side = 0; side < 2; side++)
                {
                    v.Add(new Vector3(0, 0, side == 0 ? -h : h));
                    for (int i = 0; i < 10; i++) { float ang = Mathf.PI / 2 + i * Mathf.PI / 5; float r = i % 2 == 0 ? 0.0065f : 0.0028f; v.Add(new Vector3(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r, side == 0 ? -h : h)); }
                }
                for (int i = 0; i < 10; i++) { int a = 1 + i, b = 1 + (i + 1) % 10; tri.AddRange(new[] { 0, b, a }); tri.AddRange(new[] { 11, 11 + a, 11 + b }); tri.AddRange(new[] { a, b, 11 + b, a, 11 + b, 11 + a }); }
                starMesh.SetVertices(v); starMesh.SetTriangles(tri, 0); starMesh.RecalculateNormals();
            }
            var go = new GameObject("Star"); go.transform.SetParent(parent, false); go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = starMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = StarMat(g);
            return go.transform;
        }

        void LateUpdate()
        {
            if (!root) return;
            float ceil = d.arena.ceilingHeight;
            var l = root.TransformPoint(new Vector3(-StickHalf, 0, 0)); var r = root.TransformPoint(new Vector3(StickHalf, 0, 0));
            cordL.SetPosition(0, l); cordL.SetPosition(1, new Vector3(l.x, ceil, l.z));
            cordR.SetPosition(0, r); cordR.SetPosition(1, new Vector3(r.x, ceil, r.z));
        }

        IEnumerator Open()
        {
            var end = root.localPosition;
            flag.reveal = 0;
            foreach (var lk in links) lk.drop = 0;
            // 1) 横木从上面落下来，轻轻一弹
            for (float k = 0; k < 1f; k += Time.unscaledDeltaTime / 0.42f)
            {
                float e = 1 + 2.2f * Mathf.Pow(k - 1, 3) + 1.2f * Mathf.Pow(k - 1, 2);   // 带一点回弹
                root.localPosition = end + Vector3.up * 0.45f * (1 - e);
                yield return null;
            }
            root.localPosition = end;
            // 2) 布旗放下来
            for (float k = 0; k < 1f; k += Time.unscaledDeltaTime / 0.38f) { flag.reveal = Mathf.SmoothStep(0, 1, k); yield return null; }
            flag.reveal = 1;
            // 3) 按钮木签一块块落下，接着战报木签一块接一块荡下来
            yield return DropTags();
            foreach (var lk in links)
            {
                for (float k = 0; k < 1f; k += Time.unscaledDeltaTime / 0.2f) { lk.drop = k; yield return null; }
                lk.drop = 1; lk.kick = 1f;
            }
            // 4) 星一颗颗钉上
            for (int i = 0; i < stars.Count; i++)
            {
                yield return new WaitForSecondsRealtime(0.08f);
                for (float k = 0; k < 1f; k += Time.unscaledDeltaTime / 0.16f) { float o = 1 + Mathf.Sin(k * Mathf.PI) * 0.4f; stars[i].localScale = Vector3.one * 1.25f * k * o; yield return null; }
                stars[i].localScale = Vector3.one * 1.25f;
                if (got[i]) { Fx.Sparkle(stars[i].position, new Color(1f, 0.85f, 0.45f), 0.02f); Sfx.Star(stars[i].position); }
            }
        }

        // 木签一块块落下来，落到位就能按
        IEnumerator DropTags()
        {
            for (int i = 0; i < tags.Count; i++)
            {
                var t = tags[i];
                for (float k = 0; k < 1f; k += Time.unscaledDeltaTime / 0.22f) { t.drop = 1 - (1 - k) * (1 - k); yield return null; }
                t.drop = 1; t.Hold(false);
            }
        }

        public void Close() { StopAllCoroutines(); foreach (var t in tags) t.Hold(true); StartCoroutine(CloseRun()); }

        IEnumerator CloseRun()
        {
            var p0 = root.localPosition;
            for (float k = 0; k < 1f; k += Time.unscaledDeltaTime / 0.45f)
            {
                foreach (var t in tags) t.drop = 1 - Mathf.Clamp01(k * 2.5f);
                flag.reveal = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01(k * 2f));
                foreach (var lk in links) lk.drop = 1 - Mathf.Clamp01(k * 2.2f);
                root.localPosition = p0 + Vector3.up * 0.5f * k * k;      // 整组往上收回天花板
                yield return null;
            }
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// 战报木签（不能按）：和按钮木签同一个模型，顶孔用短绳挂在上一块的底孔 / 布旗扣眼上，
    /// 自己像钟摆轻轻晃，下面挂着的跟着一起晃（层层嵌套）。drop 0 → 1 = 从侧着收起荡到正面挂好。
    /// </summary>
    public class ReportLink : MonoBehaviour
    {
        public Transform look;
        public float drop = 1f, kick;
        LineRenderer cord;
        float gap, seed, scale;
        public Transform BottomHole { get; private set; }

        public static ReportLink Build(Transform parent, float gap, Material mat, int index, float scale, Material matOverride = null)
        {
            var l = new GameObject("Link_" + index).AddComponent<ReportLink>();
            l.transform.SetParent(parent, false);
            l.gap = gap; l.seed = index * 2.3f + 0.7f; l.scale = scale;
            l.transform.localScale = Vector3.one * scale;
            l.look = new GameObject("Look").transform;
            l.look.SetParent(l.transform, false);
            l.look.gameObject.AddComponent<MeshFilter>().sharedMesh = CoverMenu.M("CV_Tag");
            l.look.gameObject.AddComponent<MeshRenderer>().sharedMaterial = matOverride ? matOverride : mat;
            l.BottomHole = new GameObject("BottomHole").transform;
            l.BottomHole.SetParent(l.look, false);
            l.BottomHole.localPosition = new Vector3(0, -0.04f, 0);
            l.cord = new GameObject("Cord").AddComponent<LineRenderer>();
            l.cord.transform.SetParent(l.transform, false);
            l.cord.useWorldSpace = true; l.cord.positionCount = 3; l.cord.widthMultiplier = 0.0018f;
            l.cord.sharedMaterial = CoverMenu.Mat("M_Cord");
            l.cord.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return l;
        }

        /// <summary>写在木签正面（-Z）的字：pos 是模型坐标下文字中心</summary>
        public TextMeshPro Text(Vector3 pos, Vector2 size, float max, Color col)
        {
            var g = new GameObject("Text"); g.transform.SetParent(look, false);
            g.transform.localPosition = new Vector3(pos.x, pos.y, -0.0053f);
            var t = g.AddComponent<TextMeshPro>();
            t.font = Fonts.Sans ? Fonts.Sans : Fonts.Type;   // 思源黑体：Courier 的数字比汉字小一圈，加粗后“0”像小写 o，认不清
            t.rectTransform.sizeDelta = size;
            t.enableAutoSizing = true; t.fontSizeMin = 0.01f; t.fontSizeMax = max;
            t.fontStyle = FontStyles.Bold;   // 和按钮木签同一种字：粗体墨字，木纹上好认
            t.alignment = TextAlignmentOptions.Center; t.textWrappingMode = TextWrappingModes.NoWrap;
            t.color = col;
            return t;
        }

        void LateUpdate()
        {
            float t = Time.unscaledTime;
            kick = Mathf.MoveTowards(kick, 0, Time.unscaledDeltaTime * 1.2f);
            float e = Mathf.Clamp01(drop);
            // 收起时侧着（绕挂绳转 80°）、缩小；荡下来时转正，落定后被 kick 带着晃两下
            float sway = Mathf.Sin(t * 1.15f + seed) * 1.8f + Mathf.Sin(t * 6f + seed) * kick * 7f;
            look.localPosition = new Vector3(0, -gap * e + 0.004f, 0);   // 顶孔（模型 y = -0.004）落在绳下端
            transform.localRotation = Quaternion.Euler(0, (1 - e) * 80f + Mathf.Sin(t * 0.55f + seed) * 5f, sway);
            look.localScale = Vector3.one * Mathf.Max(0.001f, Mathf.Clamp01(e * 1.5f));
            cord.enabled = e > 0.05f;
            // 绳：从上一块的底孔穿下来，到这块的顶孔（中间稍往前鼓一点，像真绳圈）
            var a = transform.position; var b = look.TransformPoint(new Vector3(0, -0.004f, 0));
            cord.SetPosition(0, a); cord.SetPosition(1, (a + b) * 0.5f - look.forward * 0.0015f * scale); cord.SetPosition(2, b);
        }
    }
}

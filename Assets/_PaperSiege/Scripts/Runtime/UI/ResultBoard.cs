// 结算（11-26 重做，和封面同一套实物语言，不用一整块木板）：
//   从天花板开口垂下一根横木（两根吊绳）→ 横木左边挂一面小布旗：积木拼字的“胜利”（奶白旗）/“失守”“时间到”（火燎焦边旗）
//   → 一张打字机战报纸条斜着别在布旗下摆上：关名、射挡接修、三条星的条件（每条前面一颗星，拿到的是金星）
//   → 横木右边挂几块木签（下一关 / 重来 / 回标题），高低错开，看着 / 指着 + 捏 = 按（和封面木签一样）。
// 整组放在自己城的右前上方，不挡城。出现：横木落下来轻轻一弹 → 布旗放下 → 纸条翻过来别上 → 星一颗颗钉上 → 木签落下；收起：反过来往上收。
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
        Transform root, note;
        BannerCloth flag;
        LineRenderer cordL, cordR;
        readonly List<Transform> stars = new();
        readonly List<HangTag> tags = new();
        public int earned;
        bool[] got = new bool[3];
        const float StickHalf = 0.17f;

        public static int Score(Director d, bool win)
        {
            if (!win) return 0;
            int s = 1;
            if (d.stats.caught >= 3) s++;
            if (d.arena.mine.kingHP >= d.arena.mine.kingMaxHP) s++;
            return s;
        }

        public static ResultBoard Build(Director d, bool win)
        {
            var r = new GameObject("ResultBoard").AddComponent<ResultBoard>();
            r.d = d;
            r.earned = Score(d, win);
            r.got = new[] { win, win && d.stats.caught >= 3, win && d.arena.mine.kingHP >= d.arena.mine.kingMaxHP };
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
            pos.y = Mathf.Max(head.y - 0.02f, d.arena.tableCenter.y + 0.33f);   // 最低的木签也要高过桌面
            root.position = pos;
            var look = pos - head; look.y *= 0.3f;
            root.rotation = Quaternion.LookRotation(look, Vector3.up) * Quaternion.Euler(0, 0, -2.5f);   // 横木微微斜一点

            var cover = CoverMenu.Mat("M_Cover");
            // 横木（封面木签横臂的模型：沿 -X 0.28 m → 放大到 0.34 m 居中）
            var stick = new GameObject("Stick");
            stick.transform.SetParent(root, false);
            stick.transform.localPosition = new Vector3(StickHalf, 0, 0);
            stick.transform.localScale = Vector3.one * (2 * StickHalf / 0.2825f);
            stick.AddComponent<MeshFilter>().sharedMesh = CoverMenu.M("CV_TagArm");
            stick.AddComponent<MeshRenderer>().sharedMaterial = cover;
            cordL = Cord(); cordR = Cord();

            // 小布旗
            string key = win ? "win" : d.timeUp ? "time" : "lost";
            var fg = new GameObject("Flag"); fg.transform.SetParent(root, false);
            fg.transform.localPosition = new Vector3(-0.075f, -0.012f, 0);
            flag = fg.AddComponent<BannerCloth>();
            flag.Init(0.15f, 0.15f * 640f / 512f, CoverMenu.Mat($"M_Result_{key}_{(Loc.English ? "en" : "zh")}"));

            // 战报纸条：斜着别在布旗下摆上，一颗铜别针
            var ng = new GameObject("Note"); ng.transform.SetParent(root, false);
            note = ng.transform;
            note.localPosition = new Vector3(-0.02f, -0.285f, -0.012f);   // 只压住布旗下摆的布条，不挡结果大字
            note.localScale = Vector3.one * 1.35f;
            note.localRotation = Quaternion.Euler(0, 0, -5f);
            var paper = new GameObject("Paper"); paper.transform.SetParent(note, false);
            paper.AddComponent<MeshFilter>().sharedMesh = CoverMenu.CurledQuad(0.12f, 0.12f * 640f / 560f, 0.006f);
            paper.AddComponent<MeshRenderer>().sharedMaterial = CoverMenu.Mat("M_Note");
            var pin = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(pin.GetComponent<Collider>());
            pin.transform.SetParent(note, false); pin.transform.localPosition = new Vector3(0, 0.058f, -0.004f); pin.transform.localScale = Vector3.one * 0.007f;
            pin.GetComponent<MeshRenderer>().sharedMaterial = StarMat(true);
            var ink = new Color(0.1f, 0.07f, 0.05f);
            float zf = -0.0075f;
            Text(note, new Vector3(0, 0.038f, zf), new Vector2(0.1f, 0.016f), 0.16f, TextAlignmentOptions.Left, ink).text = Loc.T(d.level.title);
            var s = d.stats;
            Text(note, new Vector3(0, 0.018f, zf), new Vector2(0.1f, 0.016f), 0.2f, TextAlignmentOptions.Left, ink).text = string.Format(Loc.T("射 {0}　挡 {1}"), s.shots, s.blocked);
            Text(note, new Vector3(0, 0.002f, zf), new Vector2(0.1f, 0.016f), 0.2f, TextAlignmentOptions.Left, ink).text = string.Format(Loc.T("接 {0}　修 {1}"), s.caught, s.repaired);
            string[] conds = { "打下对方帅旗", "接住 3 发", "帅旗没挨打" };
            for (int i = 0; i < 3; i++)
            {
                float y = -0.02f - i * 0.016f;
                var st = Star(note, new Vector3(-0.043f, y, zf - 0.002f), got[i]);
                st.localScale = Vector3.zero; stars.Add(st);
                var tt = Text(note, new Vector3(0.008f, y, zf), new Vector2(0.084f, 0.013f), 0.12f, TextAlignmentOptions.Left, got[i] ? ink : new Color(0.42f, 0.35f, 0.28f));
                tt.text = Loc.T(conds[i]);
            }
            // 红铅笔：拿到星的那几条下面划一道
            for (int i = 0; i < 3; i++)
            {
                if (!got[i]) continue;
                var lr = new GameObject("RedPencil").AddComponent<LineRenderer>();
                lr.transform.SetParent(note, false); lr.useWorldSpace = false; lr.positionCount = 5; lr.widthMultiplier = 0.0011f;
                lr.sharedMaterial = new Material(CoverMenu.Mat("M_Cord")) { color = new Color(0.66f, 0.29f, 0.23f) };
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                float y = -0.02f - i * 0.016f - 0.0068f;
                for (int k = 0; k < 5; k++) { float u = k / 4f; lr.SetPosition(k, new Vector3(-0.034f + 0.07f * u, y + Mathf.Sin(u * 6f + i) * 0.0006f, zf - 0.0004f)); }
            }

            // 木签：横木右半边，高低错开
            var btns = new List<(string zh, System.Action act)>();
            if (win && d.levelIndex < Levels.Count - 1) btns.Add(("下一关", () => d.StartLevel(d.levelIndex + 1)));
            btns.Add(("重来", () => d.StartLevel(d.levelIndex)));
            btns.Add(("回标题", () => d.GoTitle()));
            float[] xs = { 0.07f, 0.115f, 0.16f };
            float[] cords = { 0.02f, 0.075f, 0.13f };
            for (int i = 0; i < btns.Count; i++)
            {
                var t = HangTag.Build(root, new Vector3(xs[i], -0.004f, -0.004f), cords[i], CoverMenu.M("CV_Tag"), cover, i);
                t.transform.localScale = Vector3.one * 0.85f;
                t.Set(btns[i].zh, btns[i].act, true, 0);
                t.drop = 0; t.Hold(true);
                tags.Add(t);
            }
        }

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
            var l = root.TransformPoint(new Vector3(-StickHalf + 0.01f, 0, 0)); var r = root.TransformPoint(new Vector3(StickHalf - 0.01f, 0, 0));
            cordL.SetPosition(0, l); cordL.SetPosition(1, new Vector3(l.x, ceil, l.z));
            cordR.SetPosition(0, r); cordR.SetPosition(1, new Vector3(r.x, ceil, r.z));
        }

        IEnumerator Open()
        {
            var end = root.localPosition;
            flag.reveal = 0;
            note.gameObject.SetActive(false);
            var noteRot = note.localRotation;
            // 1) 横木从上面落下来，轻轻一弹
            for (float k = 0; k < 1f; k += Time.deltaTime / 0.42f)
            {
                float e = 1 + 2.2f * Mathf.Pow(k - 1, 3) + 1.2f * Mathf.Pow(k - 1, 2);   // 带一点回弹
                root.localPosition = end + Vector3.up * 0.45f * (1 - e);
                yield return null;
            }
            root.localPosition = end;
            // 2) 布旗放下来
            for (float k = 0; k < 1f; k += Time.deltaTime / 0.38f) { flag.reveal = Mathf.SmoothStep(0, 1, k); yield return null; }
            flag.reveal = 1;
            // 3) 纸条翻过来别上
            note.gameObject.SetActive(true);
            for (float k = 0; k < 1f; k += Time.deltaTime / 0.22f) { note.localRotation = noteRot * Quaternion.Euler(0, (1 - Mathf.SmoothStep(0, 1, k)) * 90f, 0); yield return null; }
            note.localRotation = noteRot;
            // 4) 星一颗颗钉上（同时木签开始往下落）
            StartCoroutine(DropTags());
            for (int i = 0; i < 3; i++)
            {
                yield return new WaitForSeconds(0.1f);
                for (float k = 0; k < 1f; k += Time.deltaTime / 0.18f) { float o = 1 + Mathf.Sin(k * Mathf.PI) * 0.4f; stars[i].localScale = Vector3.one * k * o; yield return null; }
                stars[i].localScale = Vector3.one;
                if (got[i]) { Fx.Sparkle(stars[i].position, new Color(1f, 0.85f, 0.45f), 0.02f); Sfx.Place(stars[i].position); }
            }
        }

        // 木签一块块落下来，落到位就能按
        IEnumerator DropTags()
        {
            for (int i = 0; i < tags.Count; i++)
            {
                var t = tags[i];
                for (float k = 0; k < 1f; k += Time.deltaTime / 0.25f) { t.drop = 1 - (1 - k) * (1 - k); yield return null; }
                t.drop = 1; t.Hold(false);
            }
        }

        public void Close() { StopAllCoroutines(); foreach (var t in tags) t.Hold(true); StartCoroutine(CloseRun()); }

        IEnumerator CloseRun()
        {
            var p0 = root.localPosition;
            for (float k = 0; k < 1f; k += Time.deltaTime / 0.45f)
            {
                foreach (var t in tags) t.drop = 1 - Mathf.Clamp01(k * 2.5f);
                flag.reveal = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01(k * 2f));
                note.localScale = Vector3.one * (1 - Mathf.Clamp01(k * 2.2f));
                root.localPosition = p0 + Vector3.up * 0.5f * k * k;      // 整组往上收回天花板
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}

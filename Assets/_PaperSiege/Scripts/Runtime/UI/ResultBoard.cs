// 结算（方案第 9 节）：一块手写战报木板从桌子后面慢慢升起来（有过程），毛笔字：胜利 / 失守、射 / 挡 / 接 / 修；三颗木星星（拿到的上金漆、钉上去时弹一下）；
// 下面三块按钮积木：下一关（只在赢了时）/ 重来 / 回标题。
//   三星：① 赢；② 接住 ≥ 3 发；③ 自己的帅旗一下没挨。
using System.Collections;
using TMPro;
using UnityEngine;

namespace PaperSiege
{
    public class ResultBoard : MonoBehaviour
    {
        Director d;
        Transform board;
        readonly Transform[] stars = new Transform[3];
        public int earned;

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
            r.BuildLook(win);
            r.StartCoroutine(r.Rise());
            return r;
        }

        Vector3 F => d.arena.forward;

        TextMeshPro Text(Transform parent, Vector3 pos, Vector2 size, float max, TextAlignmentOptions al = TextAlignmentOptions.Center)
        {
            var g = new GameObject("Text"); g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            var t = g.AddComponent<TextMeshPro>();
            t.font = Fonts.Hand;
            t.rectTransform.sizeDelta = size;
            t.enableAutoSizing = true; t.fontSizeMin = 0.01f; t.fontSizeMax = max;
            t.alignment = al; t.textWrappingMode = TextWrappingModes.NoWrap;
            t.color = new Color(0.2f, 0.13f, 0.08f);
            return t;
        }

        void BuildLook(bool win)
        {
            var a = d.arena;
            board = new GameObject("Board").transform;
            board.SetParent(transform, false);
            // 木板：W_Slab 竖起来当板子（45 × 28 cm），立在桌面后半，上沿约在视线下方
            var lib = BlockLibrary.I; var slab = lib ? lib.Meshes("W_Slab") : null;
            var lk = new GameObject("Plank"); lk.transform.SetParent(board, false);
            if (slab != null)
            {
                lk.AddComponent<MeshFilter>().sharedMesh = slab.lod0;
                lk.AddComponent<MeshRenderer>().sharedMaterial = lib.Paint(Paint.Cream);
                // Slab 2×0.5×1 格（宽 x、高 y、深 z）→ 转 90° 竖起来：宽 0.45、高 0.28、厚 0.02
                lk.transform.localRotation = Quaternion.Euler(-90, 0, 0);
                lk.transform.localScale = new Vector3(0.45f / (2 * BlockLibrary.G), 0.02f / (0.5f * BlockLibrary.G), 0.28f / (1 * BlockLibrary.G));
                // 实测：偏移 (0, -0.14, 0.0125) 时板子中心在 (0, -0.141, 0.002)；偏移 (0, -0.14, 0.01) 时在 (0, +0.139, -0.010)。
                // 两次 y 偏移相同但中心差 0.28 → 说明第一次测的是没改之前的旧代码（-0.14 + 原 Slab 原点在底面）。按第二次实测线性推：
                // 要中心在 (0, 0, ~0.0105)（板子 2 cm 厚，前面在 z≈0.0005，字在 -0.0025 前面 3 mm）→ 偏移 = (0, -0.14 + 0.141, 0.0125 + 0.0085)
                lk.transform.localPosition = new Vector3(0, 0.001f, 0.021f);
            }
            float zf = -0.0025f;   // 板子正面（朝玩家）
            Text(board, new Vector3(0, 0.08f, zf), new Vector2(0.3f, 0.07f), 1.2f).text = Loc.T(win ? "胜利！" : d.timeUp ? "时间到……" : "失守……");
            Text(board, new Vector3(0, 0.035f, zf), new Vector2(0.38f, 0.03f), 0.5f).text = Loc.T(d.level.title);
            var s = d.stats;
            Text(board, new Vector3(0, -0.015f, zf), new Vector2(0.4f, 0.035f), 0.6f).text =
                string.Format(Loc.T("射 {0}　挡 {1}　接 {2}　修 {3}"), s.shots, s.blocked, s.caught, s.repaired);
            // 三颗星（木头五角星：拿到的金漆，没拿到的裸木轮廓）
            for (int i = 0; i < 3; i++)
            {
                var st = Star(board, new Vector3((i - 1) * 0.07f, -0.07f, zf - 0.004f), i < earned);
                stars[i] = st;
                st.localScale = Vector3.zero;
            }
            // 三条星的条件（小字）
            Text(board, new Vector3(0, -0.112f, zf), new Vector2(0.4f, 0.026f), 0.45f).text = Loc.T("赢　·　接住 3 发　·　帅旗没挨打");
            // 按钮
            var row = new GameObject("Buttons").transform; row.SetParent(board, false);
            row.localPosition = new Vector3(0, -0.17f, -0.03f);
            int n = 0;
            void Btn(string zh, Paint p, System.Action act)
            {
                var b = WoodButton.Build("W_Slab", p, new Vector3(0.1f, 0.035f, 0.05f), zh, row, 0.45f);
                b.transform.localPosition = new Vector3((n++ - (win && d.levelIndex < Levels.Count - 1 ? 1 : 0.5f)) * 0.12f, 0, 0);
                b.SetInk(p == Paint.Cream ? new Color(0.2f, 0.13f, 0.08f) : new Color(0.98f, 0.94f, 0.86f));
                b.onPress = act;
            }
            if (win && d.levelIndex < Levels.Count - 1) Btn("下一关", Paint.Brick, () => d.StartLevel(d.levelIndex + 1));
            Btn("重来", Paint.Teal, () => d.StartLevel(d.levelIndex));
            Btn("回标题", Paint.Cream, () => d.GoTitle());
            // 摆位置：玩家和自己的城之间（桌子前沿上方、离眼睛约 45 cm、比视线低 25 cm），板面朝向眼睛 —— 不被城挡住，也不贴脸
            var head = d.head.transform.position;
            var flatF = F;
            var pos = head + flatF * 0.45f;
            pos.y = Mathf.Max(a.tableCenter.y + 0.2f, head.y - 0.25f);
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(pos - head, Vector3.up);
        }

        static Mesh starMesh;
        Transform Star(Transform parent, Vector3 pos, bool gold)
        {
            if (!starMesh)
            {
                // 五角星：10 个外 / 内顶点，挤出 6 mm 厚
                starMesh = new Mesh();
                var v = new System.Collections.Generic.List<Vector3>(); var tri = new System.Collections.Generic.List<int>();
                float h = 0.003f;
                for (int side = 0; side < 2; side++)
                {
                    v.Add(new Vector3(0, 0, side == 0 ? -h : h));
                    for (int i = 0; i < 10; i++) { float ang = Mathf.PI / 2 + i * Mathf.PI / 5; float r = i % 2 == 0 ? 0.028f : 0.012f; v.Add(new Vector3(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r, side == 0 ? -h : h)); }
                }
                for (int i = 0; i < 10; i++) { int a = 1 + i, b = 1 + (i + 1) % 10; tri.AddRange(new[] { 0, b, a }); tri.AddRange(new[] { 11, 11 + a, 11 + b }); tri.AddRange(new[] { a, b, 11 + b, a, 11 + b, 11 + a }); }
                starMesh.SetVertices(v); starMesh.SetTriangles(tri, 0); starMesh.RecalculateNormals();
            }
            var g = new GameObject("Star"); g.transform.SetParent(parent, false); g.transform.localPosition = pos;
            g.AddComponent<MeshFilter>().sharedMesh = starMesh;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = gold ? new Color(0.95f, 0.76f, 0.3f) : new Color(0.62f, 0.5f, 0.38f);
            m.SetFloat("_Metallic", gold ? 0.55f : 0f); m.SetFloat("_Smoothness", gold ? 0.6f : 0.15f);
            g.AddComponent<MeshRenderer>().sharedMaterial = m;
            return g.transform;
        }

        IEnumerator Rise()
        {
            // 从桌面下面慢慢升上来
            var end = board.localPosition;
            // 从桌面那么低的地方升上来（有过程），升起时从透明缩小到正常
            board.localPosition = end - Vector3.up * 0.25f;
            board.localScale = Vector3.one * 0.6f;
            yield return new WaitForSeconds(0.4f);
            for (float k = 0; k < 1f; k += Time.deltaTime / 0.6f)
            {
                float e = 1 - (1 - k) * (1 - k);
                board.localPosition = end - Vector3.up * 0.25f * (1 - e);
                board.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, e);
                yield return null;
            }
            board.localScale = Vector3.one;
            board.localPosition = end;
            // 星星一颗颗钉上去
            for (int i = 0; i < 3; i++)
            {
                yield return new WaitForSeconds(0.25f);
                for (float k = 0; k < 1f; k += Time.deltaTime / 0.25f) { float o = 1 + Mathf.Sin(k * Mathf.PI) * 0.35f; stars[i].localScale = Vector3.one * k * o; yield return null; }
                stars[i].localScale = Vector3.one;
                if (i < earned) Fx.Sparkle(stars[i].position, new Color(1f, 0.85f, 0.45f), 0.03f);
            }
        }

        public void Close() => Destroy(gameObject);
    }
}

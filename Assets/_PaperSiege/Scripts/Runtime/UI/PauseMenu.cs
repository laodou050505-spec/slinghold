// 暂停（11-26 用户：对局中要有暂停、返回）。
//   对局中：自己城左上方从天花板垂下一块小木签“暂停”（和右边的提示木牌对称）。只能伸手直接捏到它才算（不接受看着 / 指着隔空按），
//     拉弓、接弹、拿补给时的捏合都离它 15 cm 以上，不会误碰。
//   按下：时间冻住（弹停在半空、投石机停住、计时停），两手的弹弓 / 接挡 / 补城都不响应；
//     天花板再垂下暂停板：左边一根横木挂两排“手势小木牌”（每块一只缩小的示范手在循环做动作 + 两个字），
//     右边一根短横木挂一串木签：继续 / 重来 / 回标题。
//   继续：暂停板收回天花板，时间用 1 秒从慢到正常恢复（和摘头显回来一样，给一点反应时间）。
// 手势小木牌也就是“速查表”：不写长句，一个动作一块牌（11-26 用户：要让玩家知道所有手势，但别写一大段文字）。
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace PaperSiege
{
    public class PauseMenu : MonoBehaviour
    {
        Director d;
        HangTag pauseTag;
        Transform tagRoot;
        bool tagShown;
        GameObject board;
        Coroutine resume;

        void Start() { d = Director.I; }

        // ───────────────────────── 对局中的“暂停”小木签
        void BuildTag()
        {
            tagRoot = new GameObject("PauseTagRoot").transform;
            pauseTag = HangTag.Build(tagRoot, Vector3.zero, 1f, CoverMenu.M("CV_Tag"), CoverMenu.Mat("M_Cover"), 1);
            pauseTag.button.remote = false;          // 只能直接捏到
            pauseTag.button.pressRadius = 0.045f;
            pauseTag.transform.localScale = Vector3.one * 0.75f;
            pauseTag.Set("暂停", Pause, true, 0);
            pauseTag.drop = 0;
        }

        void PlaceTag()
        {
            var a = d.arena;
            var f = a.forward; var r = Vector3.Cross(Vector3.up, f);
            // 自己城左上方：桌心往左 27 cm、往近 5 cm，木签中心约在桌面上 22 cm（眼下 38 cm 左右）
            var tagCenter = a.tableCenter - r * 0.27f - f * 0.05f + Vector3.up * 0.22f;
            var knot = new Vector3(tagCenter.x, a.ceilingHeight, tagCenter.z);
            tagRoot.position = knot;
            var look = tagCenter - d.head.transform.position; look.y = 0;
            tagRoot.rotation = Quaternion.LookRotation(look.sqrMagnitude > 1e-4f ? look : f, Vector3.up);
            // 木签缩到 0.75：绳长按缩放换算，让木签停在 tagCenter
            pauseTag.CordLen = (a.ceilingHeight - tagCenter.y) / 0.75f - 0.022f;
        }

        void Update()
        {
            if (!d) return;
            bool want = d.phase == Director.Phase.Battle && !d.paused && !d.ending && d.arena && d.arena.mine;
            if (want && !pauseTag) BuildTag();
            if (want != tagShown && pauseTag)
            {
                tagShown = want;
                if (want) PlaceTag();
                StopCoroutine(nameof(TagDrop));
                StartCoroutine(TagDrop(want));
            }
            if (board && d.phase != Director.Phase.Battle) CloseNow();   // 重来 / 回标题 / 结算：暂停板直接撤掉
        }

        IEnumerator TagDrop(bool show)
        {
            pauseTag.button.interactable = false;
            float from = pauseTag.drop, to = show ? 1f : 0f;
            for (float k = 0; k < 1f; k += Time.unscaledDeltaTime / (show ? 0.9f : 0.45f))
            {
                pauseTag.drop = Mathf.Lerp(from, to, show ? 1 - (1 - k) * (1 - k) : k * k);
                yield return null;
            }
            pauseTag.drop = to;
            pauseTag.button.interactable = show;
        }

        // ───────────────────────── 暂停 / 继续
        public void Pause()
        {
            if (d.paused || d.phase != Director.Phase.Battle) return;
            if (resume != null) { StopCoroutine(resume); resume = null; }
            d.paused = true;
            Time.timeScale = 0f;
            Sfx.StopCreak();
            BuildBoard();
        }

        void Resume()
        {
            if (!d.paused || resume != null) return;
            resume = StartCoroutine(ResumeRun());
        }

        IEnumerator ResumeRun()
        {
            yield return CloseBoard();
            d.paused = false;
            for (float t = 0; t < 1f; t += Time.unscaledDeltaTime) { Time.timeScale = Mathf.SmoothStep(0.05f, 1f, t); yield return null; }
            Time.timeScale = 1f;
            resume = null;
        }

        void Restart() { CloseNow(); d.StartLevel(d.levelIndex); }
        void ToTitle() { CloseNow(); d.GoTitle(); }

        void CloseNow()
        {
            if (resume != null) { StopCoroutine(resume); resume = null; }
            if (board) Destroy(board);
            board = null;
            d.paused = false;
            Time.timeScale = 1f;
        }

        // ───────────────────────── 暂停板
        readonly List<HangTag> btns = new();
        readonly List<GestureCard> cards = new();
        Transform boardRoot;
        LineRenderer[] cords;
        Vector3 boardRest;
        const float StickHalf = 0.15f;

        void BuildBoard()
        {
            btns.Clear(); cards.Clear();
            board = new GameObject("PauseBoard");
            var head = d.head.transform.position;
            var f = Vector3.ProjectOnPlane(d.head.transform.forward, Vector3.up);
            if (f.sqrMagnitude < 1e-4f) f = d.arena.forward;
            f.Normalize();
            boardRoot = new GameObject("Root").transform;
            boardRoot.SetParent(board.transform, false);
            var pos = head + f * 0.62f + Vector3.up * 0.02f;
            pos.y = Mathf.Max(pos.y, d.arena.tableCenter.y + 0.42f);
            boardRoot.position = boardRest = pos;
            boardRoot.rotation = Quaternion.LookRotation(f, Vector3.up) * Quaternion.Euler(0, 0, -1.5f);
            var cover = CoverMenu.Mat("M_Cover");
            var stickMesh = CoverMenu.M("CV_Stick") ?? CoverMenu.M("CV_TagArm");

            // 左：手势横木
            anchors = new Vector3[4];
            var sg = Stick(boardRoot, new Vector3(-0.1f, 0, 0), 1f, stickMesh, cover, 0);
            // 右：按钮横木（短一点）
            var sb = Stick(boardRoot, new Vector3(0.2f, 0.01f, 0), 0.55f, stickMesh, cover, 1);
            cords = new[] { Cord(), Cord(), Cord(), Cord() };

            // 按钮：继续 / 重来 / 回标题，首尾相连挂成一串
            var defs = new (string zh, System.Action act)[] { ("继续", Resume), ("重来", Restart), ("回标题", ToTitle) };
            Transform parent = sb; Vector3 knot = new Vector3(0, -0.004f, 0);
            for (int i = 0; i < defs.Length; i++)
            {
                var t = HangTag.Build(parent, knot, i == 0 ? 0.025f : 0.026f, CoverMenu.M("CV_Tag"), cover, i);
                if (i == 0) t.transform.localScale = Vector3.one * 0.82f;   // 和结算的按钮木签一样大（后面几块挂在前一块下面，跟着同一缩放）
                t.Set(defs[i].zh, defs[i].act, true, 0);
                t.drop = 0; t.Hold(true);
                btns.Add(t);
                parent = t.transform.Find("Swing/Tag/Look"); knot = new Vector3(0, -0.04f, 0);
            }

            // 手势小木牌：上排 4 块直接挂在横木上，下排 3 块的绳从上排的缝里穿下来
            var kinds = new[] { GestureCard.Kind.Raise, GestureCard.Kind.Draw, GestureCard.Kind.Swap, GestureCard.Kind.Block, GestureCard.Kind.Catch, GestureCard.Kind.Repair, GestureCard.Kind.PatOut };
            for (int i = 0; i < kinds.Length; i++)
            {
                bool top = i < 4;
                float x = top ? (i - 1.5f) * 0.074f : (i - 5) * 0.074f;   // 牌宽 6.8 cm，缝 6 mm（下排的绳从缝里穿下去）
                float cordLen = top ? 0.018f : 0.018f + GestureCard.H + 0.016f;
                cards.Add(GestureCard.Build(sg, new Vector3(x, -0.004f, 0), cordLen, kinds[i], i));
            }
            StartCoroutine(OpenBoard());
        }

        Vector3[] anchors;

        /// <summary>横木：外观按 scale 缩短；返回不缩放的挂点（子物体挂上去不会被拉扁）</summary>
        Transform Stick(Transform parent, Vector3 at, float scale, Mesh mesh, Material mat, int slot)
        {
            var holder = new GameObject("StickHolder").transform;
            holder.SetParent(parent, false);
            holder.localPosition = at;
            var g = new GameObject("Stick");
            g.transform.SetParent(holder, false);
            g.transform.localScale = new Vector3(scale, 1, 1);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            g.AddComponent<MeshRenderer>().sharedMaterial = mat;
            anchors[slot * 2] = at + new Vector3(-StickHalf * scale, 0, 0);
            anchors[slot * 2 + 1] = at + new Vector3(StickHalf * scale, 0, 0);
            return holder;
        }

        LineRenderer Cord()
        {
            var lr = new GameObject("Cord").AddComponent<LineRenderer>();
            lr.transform.SetParent(board.transform, false);
            lr.useWorldSpace = true; lr.positionCount = 2; lr.widthMultiplier = 0.0022f;
            lr.sharedMaterial = CoverMenu.Mat("M_Cord");
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return lr;
        }

        void LateUpdate()
        {
            if (!board || cords == null) return;
            float ceil = d.arena.ceilingHeight;
            for (int i = 0; i < 4; i++)
            {
                var p = boardRoot.TransformPoint(anchors[i]);
                cords[i].SetPosition(0, p); cords[i].SetPosition(1, new Vector3(p.x, ceil, p.z));
            }
        }

        IEnumerator OpenBoard()
        {
            foreach (var c in cards) c.drop = 0;
            for (float k = 0; k < 1f; k += Time.unscaledDeltaTime / 0.42f)
            {
                float e = 1 + 2.2f * Mathf.Pow(k - 1, 3) + 1.2f * Mathf.Pow(k - 1, 2);
                boardRoot.position = boardRest + Vector3.up * 0.45f * (1 - e);
                yield return null;
            }
            boardRoot.position = boardRest;
            for (int i = 0; i < btns.Count; i++)
            {
                for (float k = 0; k < 1f; k += Time.unscaledDeltaTime / 0.2f) { btns[i].drop = 1 - (1 - k) * (1 - k); yield return null; }
                btns[i].drop = 1; btns[i].Hold(false);
            }
            foreach (var c in cards)
            {
                for (float k = 0; k < 1f; k += Time.unscaledDeltaTime / 0.12f) { c.drop = k; yield return null; }
                c.drop = 1; c.kick = 1;
            }
        }

        IEnumerator CloseBoard()
        {
            if (!board) yield break;
            foreach (var t in btns) t.Hold(true);
            var p0 = boardRoot.position;
            for (float k = 0; k < 1f; k += Time.unscaledDeltaTime / 0.4f)
            {
                foreach (var t in btns) t.drop = 1 - Mathf.Clamp01(k * 2.5f);
                foreach (var c in cards) c.drop = 1 - Mathf.Clamp01(k * 2.5f);
                boardRoot.position = p0 + Vector3.up * 0.5f * k * k;
                yield return null;
            }
            Destroy(board); board = null;
        }
    }

    /// <summary>
    /// 手势小木牌：一块奶白小积木牌，上半部一只缩小的示范手循环做动作，下面两个字。道具一律用游戏里的模型（11-27 用户：换弹的弹、补城的积木、灭火的火要和游戏里一样）。
    /// 动作逐个对照识别规则（PlayerSling / Defense / SupplyTray）：
    ///   举弓 = 左手捏住 → 捏合点出现弹弓，一直捏着弓就在；
    ///   拉放 = 右手在皮兜旁捏住 → 捏着往后拉（越远越有劲，捏着不放就是蓄力）→ 张开手指 = 发射；
    ///   换弹 = 右手去弹盒里捏一下那一格；挡 = 弹飞到手边时张着手；接 = 弹飞到手边时握拳；
    ///   补城 = 从补给盘捏起积木，拿到城墙缺口松开；灭火 = 手掌往下拍着火的积木，拍两下。
    /// </summary>
    public class GestureCard : MonoBehaviour
    {
        public enum Kind { Raise, Draw, Swap, Block, Catch, Repair, PatOut }
        public const float W = 0.068f, H = 0.08f;
        float h = H;                         // 这块牌子的高（教学卡高一点，字上面留一排进度圆钉）
        readonly List<Transform> dots = new();
        public float drop = 1f, kick;
        Kind kind, shown;
        Transform swing, look, props;
        GhostHand hand;
        LineRenderer cord, bandL, bandR;
        TextMeshPro label;
        float cordLen, seed, spin;
        // 道具
        Transform fork, pouch, ball, block, wallL, wallR;
        Vector3 ballScale = Vector3.one;
        ParticleSystem flame;
        const float FS = 0.2f;               // 弹弓缩放：模型 16.5 cm 高 → 3.3 cm（牌面上 y −0.012 ～ −0.056 是手和道具的活动区，下面是字）

        public static string Label(Kind k) => k switch
        {
            Kind.Raise => "举弓", Kind.Draw => "拉放", Kind.Swap => "换弹", Kind.Block => "挡",
            Kind.Catch => "接", Kind.Repair => "补城", _ => "灭火",
        };

        public static GestureCard Build(Transform parent, Vector3 knot, float cordLen, Kind kind, int index, float height = H)
        {
            var c = new GameObject("Gesture_" + kind).AddComponent<GestureCard>();
            c.transform.SetParent(parent, false);
            c.transform.localPosition = knot;
            c.kind = kind; c.cordLen = cordLen; c.seed = index * 1.37f; c.h = height;
            c.swing = new GameObject("Swing").transform; c.swing.SetParent(c.transform, false);
            c.look = new GameObject("Card").transform; c.look.SetParent(c.swing, false);
            var lib = BlockLibrary.I;
            var pm = lib ? lib.Meshes("W_Cube") : null;
            var def = BlockLibrary.Def("W_Cube");
            var body = new GameObject("Body"); body.transform.SetParent(c.look, false);
            var size = new Vector3(W, height, 0.01f);
            if (pm != null && pm.lod0)
            {
                body.AddComponent<MeshFilter>().sharedMesh = pm.lod0;
                body.AddComponent<MeshRenderer>().sharedMaterial = lib.Paint(Paint.Bare);   // 原木色牌子：奶白的补给积木和白色小手在上面都看得清
                var ds = def != null ? def.size * BlockLibrary.G : Vector3.one * BlockLibrary.G;
                body.transform.localScale = new Vector3(size.x / ds.x, size.y / ds.y, size.z / ds.z);
                body.transform.localPosition = new Vector3(0, -height, 0);
            }
            var tg = new GameObject("Label"); tg.transform.SetParent(c.look, false);
            tg.transform.localPosition = new Vector3(0, -height + 0.0125f, -0.0062f);
            var t = tg.AddComponent<TextMeshPro>();
            c.label = t;
            t.font = Fonts.Sans ? Fonts.Sans : Fonts.Type;
            t.fontStyle = FontStyles.Bold;
            t.rectTransform.sizeDelta = new Vector2(W - 0.008f, 0.017f);
            t.enableAutoSizing = true; t.fontSizeMin = 0.01f; t.fontSizeMax = 0.16f;
            t.alignment = TextAlignmentOptions.Center; t.textWrappingMode = TextWrappingModes.NoWrap;
            t.color = new Color(0.97f, 0.93f, 0.84f);   // 原木色牌子上用奶白字（深墨色字在原木上对比不够）
            t.outlineWidth = 0.18f; t.outlineColor = new Color32(60, 38, 24, 255);
            c.cord = new GameObject("Cord").AddComponent<LineRenderer>();
            c.cord.transform.SetParent(c.transform, false);
            c.cord.useWorldSpace = true; c.cord.positionCount = 2; c.cord.widthMultiplier = 0.0015f;
            c.cord.sharedMaterial = CoverMenu.Mat("M_Cord");
            c.cord.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            c.Rebuild();
            return c;
        }

        public void SetKind(Kind k)
        {
            if (k == kind) return;
            kind = k;
            StopCoroutine(nameof(Flip));
            StartCoroutine(nameof(Flip));
        }

        System.Collections.IEnumerator Flip()
        {
            bool swapped = false;
            for (float t = 0; t < 1f; t += Time.unscaledDeltaTime / 0.45f)
            {
                spin = Mathf.SmoothStep(0, 360, t);
                if (!swapped && t > 0.25f) { swapped = true; Rebuild(); }
                yield return null;
            }
            spin = 0;
            if (!swapped) Rebuild();
        }

        // ── 道具（游戏里的模型）
        static Transform Mesh(Transform parent, string id, Material mat, float scale, string name)
        {
            var lib = BlockLibrary.I;
            var m = lib ? lib.Meshes(id) : null;
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            if (m != null && m.lod0)
            {
                g.AddComponent<MeshFilter>().sharedMesh = m.lod0;
                var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = mat ? mat : lib.propMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            g.transform.localScale = Vector3.one * scale;
            return g.transform;
        }

        static Transform Cube(Transform parent, Paint p, float edge, string name)
        {
            var lib = BlockLibrary.I;
            return Mesh(parent, "W_Cube", lib ? lib.Paint(p) : null, edge / BlockLibrary.G, name);
        }

        LineRenderer Band(string name)
        {
            var lr = new GameObject(name).AddComponent<LineRenderer>();
            lr.transform.SetParent(props, false);
            lr.useWorldSpace = true; lr.positionCount = 2; lr.widthMultiplier = 0.0012f;
            lr.sharedMaterial = CoverMenu.Mat("M_Cord");
            lr.startColor = lr.endColor = new Color(0.45f, 0.28f, 0.16f);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return lr;
        }

        void Rebuild()
        {
            shown = kind;
            label.text = Loc.T(Label(kind));
            if (hand) Destroy(hand.gameObject);
            if (props) Destroy(props.gameObject);
            fork = pouch = ball = block = wallL = wallR = null; bandL = bandR = null; flame = null;
            props = new GameObject("Props").transform; props.SetParent(look, false);
            hand = GhostHand.Build(kind != Kind.Raise);
            hand.transform.SetParent(look, false);
            hand.transform.localScale = Vector3.one * 0.19f;
            hand.Tint(new Color(0.96f, 0.86f, 0.72f, 0.95f));
            switch (kind)
            {
                case Kind.Raise:
                    fork = Mesh(props, "S_Fork", null, FS, "Fork");
                    break;
                case Kind.Draw:
                    fork = Mesh(props, "S_Fork", null, FS, "Fork");
                    fork.localPosition = P0(-0.016f, -0.058f); fork.localRotation = Quaternion.Euler(0, 0, 8f);
                    pouch = Mesh(props, "S_Pouch", null, FS, "Pouch");
                    ball = BallLook.Build(props, Ammo.Stone, 0.0028f); ballScale = ball.localScale;
                    bandL = Band("BandL"); bandR = Band("BandR");
                    break;
                case Kind.Swap:
                    ball = BallLook.Build(props, Ammo.Fire, 0.004f);
                    break;
                case Kind.Block:
                case Kind.Catch:
                    ball = BallLook.Build(props, Ammo.Stone, 0.004f);
                    break;
                case Kind.Repair:
                    // 一道有缺口的小墙（左右两块赭黄 / 灰青积木）+ 补给盘里那种奶白积木
                    wallL = Cube(props, Paint.Ochre, 0.012f, "WallL"); wallL.localPosition = P0(0.004f, -0.056f);
                    wallR = Cube(props, Paint.Teal, 0.012f, "WallR"); wallR.localPosition = P0(0.0285f, -0.056f);
                    block = Cube(props, Paint.Cream, 0.0115f, "Patch");
                    break;
                default:
                    block = Cube(props, Paint.Bare, 0.012f, "Burning");
                    block.localPosition = P0(0.002f, -0.056f);
                    flame = Fx.MiniFlame(props, P0(0.002f, -0.044f, -0.009f), 0.015f);   // 火苗底边贴在积木顶面
                    break;
            }
        }

        // 牌面本地坐标（-z 朝玩家），P0 用于道具的本地位置，P 用于示范手的世界位置
        static Vector3 P0(float x, float y, float z = -0.009f) => new Vector3(x, y, z);
        Vector3 P(float x, float y, float z = -0.009f) => look.TransformPoint(new Vector3(x, y, z));
        Vector3 D(Vector3 v) => look.TransformDirection(v).normalized;

        /// <summary>进度圆钉：已经学会的砖红、正在学的金色、还没到的本色木头（教学卡用）</summary>
        public void SetProgress(int done, int total)
        {
            while (dots.Count < total)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(g.GetComponent<Collider>());
                g.transform.SetParent(look, false);
                g.transform.localScale = new Vector3(0.0056f, 0.0056f, 0.002f);
                dots.Add(g.transform);
            }
            float span = Mathf.Min(W - 0.012f, (total - 1) * 0.0068f);
            for (int i = 0; i < dots.Count; i++)
            {
                dots[i].gameObject.SetActive(i < total);
                dots[i].localPosition = new Vector3(total > 1 ? -span / 2 + span * i / (total - 1) : 0, -h + 0.026f, -0.0055f);
                var col = i < done ? new Color(0.66f, 0.29f, 0.23f) : i == done ? new Color(0.95f, 0.76f, 0.3f) : new Color(0.78f, 0.7f, 0.58f);
                var r = dots[i].GetComponent<Renderer>();
                if (!r.sharedMaterial || r.sharedMaterial.color != col) r.sharedMaterial = DotMat(col);
            }
        }
        static readonly Dictionary<Color, Material> dotMats = new();
        static Material DotMat(Color c)
        {
            if (!dotMats.TryGetValue(c, out var m)) dotMats[c] = m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = c };
            return m;
        }

        void LateUpdate()
        {
            float t = Time.unscaledTime;
            kick = Mathf.MoveTowards(kick, 0, Time.unscaledDeltaTime);
            float e = Mathf.Clamp01(drop);
            look.localPosition = new Vector3(0, -cordLen * e, 0);
            look.localScale = Vector3.one * Mathf.Max(0.001f, Mathf.Clamp01(e * 1.6f));
            swing.localRotation = Quaternion.Euler(0, Mathf.Sin(t * 0.5f + seed) * 4f + spin, Mathf.Sin(t * 1.2f + seed) * 1.5f + Mathf.Sin(t * 7f) * kick * 6f);
            cord.enabled = e > 0.05f;
            cord.SetPosition(0, transform.position);
            cord.SetPosition(1, look.position);
            bool vis = e > 0.6f;
            hand.gameObject.SetActive(vis); props.gameObject.SetActive(vis);
            if (vis) Animate(t + seed * 0.3f);
        }

        void Animate(float t)
        {
            if (!hand) return;
            float cyc = shown == Kind.Draw ? 2.8f : 2.2f, u = (t % cyc) / cyc;
            var up = D(Vector3.up); var toward = D(Vector3.back); var right = D(Vector3.right);
            switch (shown)
            {
                case Kind.Raise:   // 左手从下面抬上来、捏住 → 捏合点上出现弹弓（握把在手里）；松开时弓收掉
                {
                    float lift = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0f, 0.25f, u));
                    float k = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.25f, 0.4f, u)) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.86f, 1f, u)));
                    var at = P(-0.008f, -0.054f + 0.008f * lift);
                    hand.SetPose(GhostHand.Pose.Pinch, k);
                    hand.Place(at, (up + toward * 0.3f).normalized, right, true);
                    // 弹弓：握把末端在捏合点，叉口朝上；捏住时由小变大出现
                    fork.position = hand.PinchPoint;
                    fork.rotation = look.rotation * Quaternion.Euler(0, 0, -6f);
                    fork.localScale = Vector3.one * FS * Mathf.SmoothStep(0, 1, k);
                    break;
                }
                case Kind.Draw:    // 右手到皮兜旁捏住 → 捏着往右下拉（蓄力，皮筋绷紧）→ 张开手指 → 弹飞出去、皮兜弹回
                {
                    var tipL = fork.TransformPoint(PropAnchors.ForkTipL * BlockLibrary.G);
                    var tipR = fork.TransformPoint(PropAnchors.ForkTipR * BlockLibrary.G);
                    var rest = (tipL + tipR) * 0.5f - up * 0.004f - toward * 0.004f;
                    var pulled = rest + right * 0.024f - up * 0.007f + toward * 0.008f;   // 往右（右肩方向）拉，别往下压到字
                    float pinch = u < 0.14f ? 0 : u < 0.22f ? (u - 0.14f) / 0.08f : u < 0.72f ? 1 : 0;
                    float pull = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.24f, 0.55f, u));
                    float snap = Mathf.InverseLerp(0.72f, 0.8f, u);
                    var pp = u < 0.72f ? Vector3.Lerp(rest, pulled, pull) : Vector3.Lerp(pulled, rest, Mathf.SmoothStep(0, 1, snap));
                    pouch.position = pp; pouch.rotation = look.rotation;
                    bandL.SetPosition(0, tipL); bandL.SetPosition(1, pp);
                    bandR.SetPosition(0, tipR); bandR.SetPosition(1, pp);
                    // 弹：在皮兜里 → 松手后往左上飞出牌子（渐小）
                    if (ball)
                    {
                        if (u < 0.72f) { ball.position = pp - toward * 0.002f; ball.localScale = ballScale; ball.gameObject.SetActive(true); }
                        else { float f = Mathf.InverseLerp(0.72f, 0.95f, u); ball.position = Vector3.Lerp(rest, rest - right * 0.03f + up * 0.03f, f); ball.localScale = ballScale * (1 - f); ball.gameObject.SetActive(f < 0.98f); }
                    }
                    // 手：先在皮兜旁，捏住、拉、张开后停在原地
                    var handAt = u < 0.72f ? pp : pulled;
                    hand.SetPose(GhostHand.Pose.Pinch, pinch);
                    hand.Place(handAt + right * 0.002f, (-right * 0.55f + up * 0.45f + toward * 0.2f).normalized, -toward, true);
                    break;
                }
                case Kind.Swap:    // 右手捏一下一颗火丸（弹盒里那一格）
                {
                    float k = Mathf.Sin(Mathf.Clamp01((u - 0.2f) / 0.55f) * Mathf.PI);
                    var bp = P(0.004f, -0.05f);
                    ball.position = bp;
                    hand.SetPose(GhostHand.Pose.Pinch, k);
                    hand.Place(bp + up * (0.012f * (1 - k) + 0.002f), (-up * 0.7f + toward * 0.5f).normalized, -toward, true);
                    break;
                }
                case Kind.Block:
                case Kind.Catch:
                {
                    var pn = (up * 0.5f + right * 0.75f + toward * 0.3f).normalized;
                    var palm = P(-0.006f, -0.036f);
                    var stop = palm + pn * 0.009f;
                    var from = palm + pn * 0.04f;
                    float a = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0f, 0.4f, u));
                    if (shown == Kind.Block)
                    {
                        hand.SetPose(GhostHand.Pose.Open, 0);
                        float b = Mathf.InverseLerp(0.55f, 0.85f, u);
                        ball.position = u < 0.55f ? Vector3.Lerp(from, stop, a) : Vector3.Lerp(stop, from + up * 0.01f, b);
                        ball.gameObject.SetActive(u < 0.85f);
                    }
                    else
                    {
                        float k = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.42f, 0.56f, u)) * (1 - Mathf.InverseLerp(0.9f, 1f, u));
                        hand.SetPose(k > 0.01f ? GhostHand.Pose.Fist : GhostHand.Pose.Open, k);
                        ball.position = Vector3.Lerp(from, stop, a);
                        ball.gameObject.SetActive(u < 0.5f);
                    }
                    hand.Place(palm, (up * 0.85f - right * 0.5f).normalized, pn, false);
                    break;
                }
                case Kind.Repair:  // 从左下捏起奶白积木 → 挪到两块墙中间的缺口 → 松开
                {
                    float pinch = u < 0.15f ? 0 : u < 0.25f ? (u - 0.15f) / 0.1f : u < 0.7f ? 1 : Mathf.Clamp01(1 - (u - 0.7f) / 0.08f);
                    float mv = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.28f, 0.65f, u));
                    var a0 = P(-0.022f, -0.056f); var gap = P(0.01625f, -0.056f);   // 积木原点在底面：和两边墙块底面平齐
                    var at = Vector3.Lerp(a0, gap, mv) + up * Mathf.Sin(mv * Mathf.PI) * 0.012f;
                    block.position = u < 0.25f ? a0 : u < 0.78f ? at : gap;
                    block.rotation = look.rotation;
                    hand.SetPose(GhostHand.Pose.Pinch, pinch);
                    hand.Place(block.position + up * 0.0125f, (-up * 0.6f + toward * 0.5f).normalized, -toward, true);   // 捏在积木顶上
                    break;
                }
                default:           // 灭火：手掌朝下拍两下，第二下拍完火缩下去，过一会儿再烧起来（循环）
                {
                    float p1 = Mathf.Exp(-Mathf.Pow((u - 0.3f) / 0.05f, 2)), p2 = Mathf.Exp(-Mathf.Pow((u - 0.52f) / 0.05f, 2));
                    var blk = block.position + up * 0.012f;   // 积木顶面
                    hand.SetPose(GhostHand.Pose.Open, 0);
                    hand.Place(blk + up * (0.02f - 0.016f * (p1 + p2)), (-right + toward * 0.3f).normalized, (-up * 0.55f - toward * 0.85f).normalized, false);   // 手往前倾：正面看得到手背（掌心完全朝下时只是一条线）
                    bool burning = u < 0.55f || u > 0.9f;
                    if (flame)
                    {
                        if (burning && !flame.isEmitting) flame.Play();
                        if (!burning && flame.isEmitting) flame.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    }
                    break;
                }
            }
        }
    }
}

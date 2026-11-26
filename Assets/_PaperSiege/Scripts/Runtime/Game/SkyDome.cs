// 天空（方案第 7 节）：一关一个时段（清晨 → 上午 → 午后 → 黄昏 → 入夜 → 烟火夜），关内跟着战局变：
//   开场：头顶一片“积木盒盖”木板先透出几道缝（预警）→ 一条条翻起来飞走（发生）→ 露出当前时段的天空（收尾）；
//   最后 30 秒：太阳明显往下沉、天色变暖（当倒计时用）；敌方完整度 < 30% 或帅旗只剩 1 点：敌城上空的云打转；
//   赢：烟花、天色往下一个时段走一小步；输：天变暗、木板一条条合回来。
// 真实房间：天空只在“盒盖”那片区域看得到（穹顶画在最远处，木板 = 天花板的替身先挡住它；木板飞走的地方露出天空）。
// 编辑器里没有透视画面：占位房间的墙 / 地板照旧，头顶的占位天花板由木板代替。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PaperSiege
{
    public class SkyDome : MonoBehaviour
    {
        [System.Serializable]
        public struct Look
        {
            public Color zenith, horizon, sun, cloud, cloudShade, light, ambient;
            public float sunElev, sunAz, sunSize, cover, stars, moon, lightI;
        }

        // 6 个时段（手调；颜色低饱和、不过曝）
        public static readonly Look[] Times =
        {
            new() { zenith = H("8fb1d6"), horizon = H("f2d9b0"), sun = H("fff0cf"), cloud = H("fff7ea"), cloudShade = H("d9c7c4"), light = H("ffe6c4"), ambient = H("c9cdd6"), sunElev = 14, sunAz = -35, sunSize = 0.05f, cover = 0.35f, stars = 0, moon = 0, lightI = 1.05f },   // 清晨
            new() { zenith = H("6f9fd8"), horizon = H("d9e6ee"), sun = H("fffaf0"), cloud = H("ffffff"), cloudShade = H("c7cfdc"), light = H("fff6e8"), ambient = H("cfd6e2"), sunElev = 42, sunAz = -20, sunSize = 0.045f, cover = 0.42f, stars = 0, moon = 0, lightI = 1.25f },  // 上午
            new() { zenith = H("7aa3d1"), horizon = H("f0dcbc"), sun = H("fff1d8"), cloud = H("fff6e6"), cloudShade = H("d6c3b6"), light = H("ffeed6"), ambient = H("d2cfcc"), sunElev = 34, sunAz = 25, sunSize = 0.045f, cover = 0.55f, stars = 0, moon = 0, lightI = 1.2f },    // 午后
            new() { zenith = H("5a6fa8"), horizon = H("f0a874"), sun = H("ffcf8a"), cloud = H("f7c9a8"), cloudShade = H("8f6f86"), light = H("ffc89a"), ambient = H("a8a0b4"), sunElev = 6, sunAz = 30, sunSize = 0.07f, cover = 0.45f, stars = 0.05f, moon = 0, lightI = 0.95f },  // 黄昏
            new() { zenith = H("1f2a4a"), horizon = H("4b4f78"), sun = H("e8ecff"), cloud = H("5a6188"), cloudShade = H("2b3050"), light = H("b9c6ff"), ambient = H("6e7596"), sunElev = 38, sunAz = -30, sunSize = 0.04f, cover = 0.3f, stars = 1, moon = 1, lightI = 0.55f },     // 入夜（月亮）
            new() { zenith = H("17203f"), horizon = H("3c3a66"), sun = H("f2eeff"), cloud = H("4b507a"), cloudShade = H("242845"), light = H("c4c8ff"), ambient = H("6a6f94"), sunElev = 44, sunAz = 20, sunSize = 0.035f, cover = 0.2f, stars = 1, moon = 1, lightI = 0.5f },        // 烟火夜
        };

        static Color H(string h) => Palette.Hex(h);

        public Director d;
        public Light sun;
        Material sky;
        Transform dome;
        readonly List<Transform> boards = new();
        readonly List<Vector3> boardHome = new();
        readonly List<Quaternion> boardRot = new();
        float open;            // 0 = 盒盖合着，1 = 全开（按木板逐条展开）
        float openTarget;
        float tod, todFrom, todTo;   // 当前时段（可以是小数，用于赢了往下走一小步）
        float sink, swirl, fireworksT = -1;
        int level = -1;
        Coroutine fw;

        public static SkyDome Build(Director d, Light sun)
        {
            var s = new GameObject("Sky").AddComponent<SkyDome>();
            s.d = d; s.sun = sun;
            s.BuildDome();
            return s;
        }

        void BuildDome()
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(g.GetComponent<Collider>());
            g.name = "SkyDome";
            g.transform.SetParent(transform, false);
            g.transform.localScale = Vector3.one * 40f;
            sky = new Material(Shader.Find("PaperSiege/Sky"));
            var r = g.GetComponent<MeshRenderer>();
            r.sharedMaterial = sky;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            dome = g.transform;
        }

        /// <summary>按房间摆盒盖：天花板高度 ceilY，盖在玩家和敌城之间那片区域上方（宽 w、长 l）</summary>
        public void BuildLid(Vector3 center, Vector3 fwd, float ceilY, float w, float l)
        {
            foreach (var b in boards) if (b) Destroy(b.gameObject);
            boards.Clear(); boardHome.Clear(); boardRot.Clear();
            var lib = BlockLibrary.I;
            var beam = lib ? lib.Meshes("W_Beam") : null;
            var right = Vector3.Cross(Vector3.up, fwd);
            int n = Mathf.Max(6, Mathf.RoundToInt(l / 0.42f));
            float bw = l / n;
            var paints = new[] { Paint.Cream, Paint.Teal, Paint.Ochre, Paint.Cream, Paint.Sage, Paint.Brick };
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("LidBoard");
                go.transform.SetParent(transform, false);
                var pos = center + fwd * ((i + 0.5f) * bw - l * 0.5f) + Vector3.up * ceilY;
                // W_Beam：3 × 0.4 × 0.8 格（长沿 X）→ 拉成一条跨过天花板的长木板（长 w、宽 bw、厚 4 cm）
                var rot = Quaternion.LookRotation(fwd, Vector3.down);   // 正面朝下（朝玩家）
                go.transform.SetPositionAndRotation(pos, rot);
                if (beam != null && beam.lod1)
                {
                    go.AddComponent<MeshFilter>().sharedMesh = beam.lod1;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = lib.Paint(paints[i % paints.Length]);
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    go.transform.localScale = new Vector3(w / (3 * BlockLibrary.G), 0.04f / (0.4f * BlockLibrary.G), bw * 0.97f / (0.8f * BlockLibrary.G));
                    go.transform.position -= Vector3.up * 0.02f;
                }
                boards.Add(go.transform);
                boardHome.Add(go.transform.position);
                boardRot.Add(go.transform.rotation);
            }
            ApplyLid();
            // 天空遮罩：只在这片开口里画天空（头显上四周是真实房间）
            sky.SetVector("_LidCenter", new Vector4(center.x, ceilY, center.z, 0));
            sky.SetVector("_LidRight", right);
            sky.SetVector("_LidFwd", fwd);
            sky.SetVector("_LidHalf", new Vector4(w * 0.5f, l * 0.5f, 0, 0));
            lidCenter = center; lidFwd = fwd; lidRight = right; lidCeil = ceilY; lidHalf = new Vector2(w * 0.5f, l * 0.5f);
            // 烟花粒子（PaperSiege/SkyParticle）用同一个开口遮罩
            Shader.SetGlobalVector("_SkyLidCenter", new Vector4(center.x, ceilY, center.z, 0));
            Shader.SetGlobalVector("_SkyLidRight", right);
            Shader.SetGlobalVector("_SkyLidFwd", fwd);
            Shader.SetGlobalVector("_SkyLidHalf", new Vector4(w * 0.5f, l * 0.5f, 0, 0));
        }

        Vector3 lidCenter, lidFwd = Vector3.forward, lidRight = Vector3.right; float lidCeil = 2.5f; Vector2 lidHalf = Vector2.one;
        float idleFw;

        /// <summary>开口外的天上随机一点（烟花放这里，从头显看过去落在开口里）</summary>
        Vector3 SkyPoint()
        {
            var head = d.head ? d.head.transform.position : Vector3.up * 1.5f;
            var p = lidCenter + lidFwd * Random.Range(0.1f, 0.7f) * lidHalf.y + lidRight * Random.Range(-0.6f, 0.6f) * lidHalf.x;
            p.y = lidCeil;
            // 沿“眼睛 → 开口里那一点”的视线再往外推 1～2 米：看起来在开口外的天上
            var dir = (p - head).normalized;
            var q = p + dir * Random.Range(4.0f, 6.0f);
            q.y = Mathf.Max(q.y, lidCeil + 2.0f);   // 整朵烟花（半径约 1.5 m）都在天花板以上，火星不会落进房间
            return q;
        }

        public void OnLevel(LevelDef lv)
        {
            todFrom = todTo = tod = Mathf.Clamp(lv.timeOfDay, 0, Times.Length - 1); level = lv.index;
            dawn = 0;
            open = 0;
            sink = 0; swirl = 0;
            openTarget = 0; open = 0; ApplyLid();
            StopAllCoroutines();
            StartCoroutine(OpenLid());
        }

        IEnumerator OpenLid()
        {
            yield return new WaitForSeconds(0.6f);
            // 预警：木板之间透出几道缝（木板稍稍错开、晃一下）
            for (float t = 0; t < 0.9f; t += Time.deltaTime) { crack = Mathf.SmoothStep(0, 1, t / 0.9f); ApplyLid(); yield return null; }
            // 发生：从敌城那头开始，一条条翻起来飞走
            openTarget = 1f;
        }

        /// <summary>标题页：盒盖合上（有过程）、回到房间原来的光</summary>
        public void Close()
        {
            StopAllCoroutines();
            openTarget = 0f; crack = 0f;
            dawn = 0;
            level = -2;   // 下次开局一定重新掀盖
        }

        public void OnEnd(bool win)
        {
            StopAllCoroutines();
            if (win)
            {
                todTo = Mathf.Min(Times.Length - 1, tod + 0.35f);   // 天色往下一个时段走一小步
                fw = StartCoroutine(Fireworks(6f));
            }
            else { openTarget = 0f; crack = 0f; }   // 输：木板一条条合回来
        }

        float crack;

        /// <summary>天亮进度（结局用）：0 = 当前时段，1 = 清晨、太阳升到清晨的高度</summary>
        public float dawn;

        /// <summary>
        /// 结局（第十关打下王城）：先一阵比平时密的烟花（约 7 秒，越放越稀），烟花还没放完天就开始泛白，
        /// 约 9 秒里星星、月亮淡掉，天边转暖，太阳从地平线下面升到清晨的高度。打了一整天，又是新的一天。
        /// </summary>
        public void Ending()
        {
            StopAllCoroutines();
            openTarget = 1f;
            StartCoroutine(EndingRun());
        }

        IEnumerator EndingRun()
        {
            float t = 0;
            StartCoroutine(DawnRun(4.5f, 9f));
            while (t < 7.5f)
            {
                Fx.Firework(SkyPoint(), Color.HSVToRGB(Random.value, 0.45f, 1f));
                if (t < 3f) Fx.Firework(SkyPoint(), Color.HSVToRGB(Random.value, 0.4f, 1f));   // 头三秒一次两朵
                float w = Mathf.Lerp(0.25f, 1.1f, t / 7.5f);                                     // 越放越稀
                t += w;
                yield return new WaitForSeconds(w);
            }
        }

        IEnumerator DawnRun(float delay, float dur)
        {
            yield return new WaitForSeconds(delay);
            for (float t = 0; t < dur; t += Time.deltaTime) { dawn = Mathf.SmoothStep(0, 1, t / dur); yield return null; }
            dawn = 1f;
        }

        void ApplyLid()
        {
            int n = boards.Count;
            for (int i = 0; i < n; i++)
            {
                var b = boards[i];
                if (!b) continue;
                // 第 i 条的展开进度（敌城那头先开）
                float k = Mathf.Clamp01(open * (n + 2) - (n - 1 - i));
                k = k * k * (3 - 2 * k);
                var home = boardHome[i];
                var right = boardRot[i] * Vector3.right;
                var fwd = boardRot[i] * Vector3.forward;
                // 缝：每条往前后错开一点、轻轻歪
                float wob = crack * (1 - k) * 0.01f * Mathf.Sin(i * 1.7f + Time.time * 3f);
                // 翻起来：绕木板一侧的长边转 110°，同时往上、往外飞走、缩小
                var pivot = home + fwd * 0.2f;
                var rot = Quaternion.AngleAxis(-110f * k, right) * boardRot[i];
                var pos = pivot + Quaternion.AngleAxis(-110f * k, right) * (home - pivot) + Vector3.up * (k * k * 1.6f) + fwd * (k * k * 0.8f) + Vector3.up * wob;
                b.SetPositionAndRotation(pos, rot);
                b.gameObject.SetActive(k < 0.999f);
            }
        }

        IEnumerator Fireworks(float dur)
        {
            float t = 0;
            while (t < dur)
            {
                Fx.Firework(SkyPoint(), Color.HSVToRGB(Random.value, 0.45f, 1f));
                float w = Random.Range(0.25f, 0.6f);
                t += w;
                yield return new WaitForSeconds(w);
            }
        }

        void Update()
        {
            if (!d || d.level == null) return;
            // 掀盖 / 时段由 Director.StartLevel 显式调 OnLevel（重开同一关也要重新掀一遍）
            float dt = Time.deltaTime;
            // 盒盖展开 / 合上（4 秒展开、3 秒合上）
            open = Mathf.MoveTowards(open, openTarget, dt / (openTarget > open ? 4f : 3f));
            ApplyLid();
            // 最后 30 秒太阳下沉
            float sinkT = d.phase == Director.Phase.Battle && !d.timerPaused ? Mathf.InverseLerp(30f, 0f, d.timeLeft) : sink;
            sink = Mathf.MoveTowards(sink, sinkT, dt * 0.2f);
            // 敌城快倒：云打转
            float sw = 0;
            if (d.phase == Director.Phase.Battle && d.arena.enemy && (d.arena.enemy.Integrity < 0.3f || (d.arena.enemy.kingHP == 1 && d.arena.enemy.kingMaxHP > 1))) sw = 1;
            swirl = Mathf.MoveTowards(swirl, sw, dt * 0.4f);
            tod = Mathf.MoveTowards(tod, todTo, dt * 0.08f);
            // 烟火夜：整关都有零星烟花（每 2～4 秒一朵）
            if (tod > 4.6f && dawn <= 0f && open > 0.9f && d.phase == Director.Phase.Battle && (idleFw -= dt) <= 0f)
            {
                idleFw = Random.Range(2f, 4f);
                Fx.Firework(SkyPoint(), Color.HSVToRGB(Random.value, 0.4f, 1f));
            }
            // 输了天变暗
            float dim = d.phase == Director.Phase.Lost ? 0.45f : 0f;
            Apply(tod, sink, swirl, dim);
            if (dome && d.head) dome.position = d.head.transform.position;
        }

        static Look Mix(Look A, Look B, float k)
        {
            Color L(Color x, Color y) => Color.Lerp(x, y, k);
            float F(float x, float y) => Mathf.Lerp(x, y, k);
            return new Look
            {
                zenith = L(A.zenith, B.zenith), horizon = L(A.horizon, B.horizon), sun = L(A.sun, B.sun), cloud = L(A.cloud, B.cloud), cloudShade = L(A.cloudShade, B.cloudShade),
                light = L(A.light, B.light), ambient = L(A.ambient, B.ambient),
                sunElev = F(A.sunElev, B.sunElev), sunAz = F(A.sunAz, B.sunAz), sunSize = F(A.sunSize, B.sunSize), cover = F(A.cover, B.cover),
                stars = F(A.stars, B.stars), moon = F(A.moon, B.moon), lightI = F(A.lightI, B.lightI),
            };
        }

        void Apply(float t, float sinkK, float swirlK, float dim)
        {
            int a0 = Mathf.FloorToInt(t), b0 = Mathf.Min(Times.Length - 1, a0 + 1);
            var cur = Mix(Times[a0], Times[b0], t - a0);
            if (dawn > 0f)
            {
                // 天亮：月亮 / 星星先淡（前 40%），天色和光随后转到清晨；太阳在东边从地平线下 8° 升到清晨的高度
                var morning = Times[0];
                float moonOff = Mathf.Clamp01(dawn / 0.4f);
                cur = Mix(cur, morning, dawn);
                cur.moon = Mathf.Lerp(Times[a0].moon, 0f, moonOff); cur.stars = Mathf.Lerp(Times[a0].stars, 0f, moonOff);
                cur.sunAz = morning.sunAz;
                cur.sunElev = Mathf.Lerp(-8f, morning.sunElev, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.3f, 1f, dawn)));
                cur.sun = dawn < 0.4f ? Color.Lerp(Times[a0].sun, morning.sun, dawn / 0.4f) : morning.sun;
            }
            var A = cur; var B = cur;
            float k = 0f;
            Color L(Color x, Color y) => Color.Lerp(x, y, k);
            float F(float x, float y) => Mathf.Lerp(x, y, k);
            var zen = L(A.zenith, B.zenith); var hor = L(A.horizon, B.horizon); var sunC = L(A.sun, B.sun);
            float elev = F(A.sunElev, B.sunElev), az = F(A.sunAz, B.sunAz);
            // 下沉：太阳往地平线压 70%，天色往暖橙走一点
            elev = Mathf.Lerp(elev, Mathf.Min(elev, 4f), sinkK * 0.7f);
            var warm = new Color(1f, 0.72f, 0.5f);
            hor = Color.Lerp(hor, hor * warm * 1.1f, sinkK * 0.5f);
            zen = Color.Lerp(zen, zen * 0.85f, sinkK * 0.4f);
            zen = Color.Lerp(zen, zen * 0.4f, dim); hor = Color.Lerp(hor, hor * 0.5f, dim);
            var fwd = d.arena ? d.arena.forward : Vector3.forward;
            var sunDir = Quaternion.AngleAxis(az, Vector3.up) * Quaternion.AngleAxis(-elev, Vector3.Cross(Vector3.up, fwd)) * fwd;
            sky.SetColor("_Zenith", zen); sky.SetColor("_Horizon", hor); sky.SetColor("_SunColor", sunC);
            sky.SetVector("_SunDir", sunDir);
            sky.SetFloat("_SunSize", F(A.sunSize, B.sunSize));
            sky.SetColor("_CloudColor", L(A.cloud, B.cloud)); sky.SetColor("_CloudShade", L(A.cloudShade, B.cloudShade));
            sky.SetFloat("_CloudCover", F(A.cover, B.cover));
            sky.SetFloat("_Stars", F(A.stars, B.stars)); sky.SetFloat("_Moon", F(A.moon, B.moon));
            sky.SetFloat("_Swirl", swirlK);
            if (d.arena && d.arena.enemy && d.head) sky.SetVector("_SwirlDir", (d.arena.enemy.transform.position + Vector3.up * 1.5f - d.head.transform.position).normalized);
            // 灯光跟着时段（只在天空打开时完全生效：盒盖合着 = 房间原来的光）
            if (sun)
            {
                float lo = Mathf.SmoothStep(0, 1, open);
                sun.color = Color.Lerp(new Color(1f, 0.95f, 0.86f), L(A.light, B.light) * Color.Lerp(Color.white, warm, sinkK * 0.5f), lo);
                sun.intensity = Mathf.Lerp(1.2f, F(A.lightI, B.lightI) * (1 - dim * 0.5f), lo);
                var ld = -sunDir; if (ld.y > -0.35f) ld = new Vector3(ld.x, -0.35f, ld.z).normalized;   // 光别太平（积木阴影太长）
                sun.transform.rotation = Quaternion.Slerp(Quaternion.Euler(48, -30, 0), Quaternion.LookRotation(ld), lo);
                var amb = L(A.ambient, B.ambient) * (1 - dim * 0.4f);
                RenderSettings.ambientSkyColor = Color.Lerp(new Color(0.80f, 0.82f, 0.88f), amb, lo);
                RenderSettings.ambientEquatorColor = Color.Lerp(new Color(0.72f, 0.70f, 0.68f), amb * 0.9f, lo);
            }
        }

        public float Open => open;
    }
}

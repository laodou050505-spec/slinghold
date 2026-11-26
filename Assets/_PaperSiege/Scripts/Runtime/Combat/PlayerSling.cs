// 玩家的弹弓（方案第 4 节）：
//   左手在胸前捏住 → 手里出现 Y 形弓架（捏着不放弓就一直在）；
//   右手在皮兜附近捏住往后拉 → 蓄力（5～35 cm），显示预瞄弧线；右手松开 → 发射（方向 = 皮兜 → 弓架）。
//   弓架旁浮着弹盒，右手捏哪颗装哪颗；没选或选的没了就自动换成还有的。
//   眼动：拉弓时看着敌城某一块，弹道往那块轻轻吸（≤ 4°）；没有眼动就完全按弹弓姿态。
//   跟丢：左手丢了弓架原地停 1 秒；右手丢了超过 1 秒自动松弓、不发射。捏太久不动的“粘连松开”也只松弓、不发射。
using UnityEngine;

namespace PaperSiege
{
    [DefaultExecutionOrder(10)]
    public class PlayerSling : MonoBehaviour
    {
        public enum State { Hidden, Held, Drawing }
        public State state = State.Hidden;
        public float pull01;
        public Vector3 aimDir = Vector3.forward;
        Director d;
        HandInput input;
        Transform fork, pouch, loaded;
        LineRenderer bandL, bandR;
        ArcDots preview;
        AmmoBox box;
        float cooldown, leftLostT, rightLostT;
        Vector3 forkPos;
        Quaternion forkRot;
        Ammo loadedType = Ammo.Stone;
        Vector3 releasePos;
        float springT = 9f;   // 松手后皮兜回弹计时
        float passT = -1f;    // 皮兜越过叉口的时刻（-1 = 还没越过）
        Projectile flying;    // 刚射出去、还在皮兜里的那颗
        float launchSpeed;
        const float PouchInner = 0.0019f;   // 皮兜模型中线内侧兜面离原点 1.9 mm（朝弓架一侧）
        /// <summary>弹心在皮兜原点前方多远：贴着兜面坐在兜里，皮兜从后面包住弹（不穿模）</summary>
        float BallOffset => PouchInner + GameTuning.I.playerBallRadius + 0.0006f;
        /// <summary>皮兜在 pp 时，兜里那颗弹的中心</summary>
        Vector3 BallAt(Vector3 pp) => pp + aimDir * BallOffset;
        InputTuning IT => InputTuning.I;
        GameTuning T => GameTuning.I;

        /// <summary>单手模式：弓架固定在桌上的木柱顶上，只用右手拉（左手腾出来挡 / 接）</summary>
        public bool OneHand { get; private set; }
        Transform post;
        const int PostBlocks = 3;   // 立柱 = 3 块圆柱积木叠起来（约 30 cm）

        public bool Holding => state != State.Hidden;
        public bool Drawing => state == State.Drawing;
        public Vector3 ForkCenter => fork ? fork.TransformPoint((PropAnchors.ForkTipL + PropAnchors.ForkTipR) * (0.5f * BlockLibrary.G)) : forkPos;
        public Vector3 PouchRest => ForkCenter - aimDir * 0.03f;
        public AmmoBox Box => box;

        public void Setup(Director director)
        {
            d = director;
            input = HandInput.Instance;
            if (!fork) BuildLook();
            box.PlaceOnTable(d.arena);
            state = State.Hidden;
            cooldown = 0;
            OneHand = Progress.OneHand;
            PlacePost();
            Show(false);
        }

        /// <summary>单手模式的立柱：桌上自己城的前面偏左，3 块圆柱积木叠起来，弓架插在顶上</summary>
        void PlacePost()
        {
            if (!post)
            {
                post = new GameObject("OneHandPost").transform;
                var lib = BlockLibrary.I;
                var m = lib ? lib.Meshes("W_Column") : null;
                for (int i = 0; i < PostBlocks; i++)
                {
                    var g = new GameObject("Column" + i);
                    g.transform.SetParent(post, false);
                    g.transform.localPosition = Vector3.up * (i * 2f * BlockLibrary.G);
                    g.transform.localRotation = Quaternion.Euler(0, i * 37f, 0);   // 每块转一点，纹理和磕痕不重样
                    // 积木网格本身按米建（1 格 = 5 cm 已经在网格里），不用再缩放
                    if (m != null && m.lod0 && lib)
                    {
                        g.AddComponent<MeshFilter>().sharedMesh = m.lod0;
                        g.AddComponent<MeshRenderer>().sharedMaterial = lib.Paint(i % 2 == 0 ? Paint.Ochre : Paint.Cream);
                    }
                }
            }
            var a = d.arena;
            var right = Vector3.Cross(Vector3.up, a.forward);
            post.SetPositionAndRotation(a.tableCenter - a.forward * 0.17f - right * 0.12f, Quaternion.LookRotation(a.forward));
            post.gameObject.SetActive(false);
        }

        Vector3 PostTop => post.position + Vector3.up * (PostBlocks * 2f * BlockLibrary.G + 0.012f);

        void BuildLook()
        {
            var lib = BlockLibrary.I;
            var fm = lib ? lib.Meshes("S_Fork") : null;
            fork = new GameObject("SlingFork").transform;
            if (fm != null && fm.lod0 && lib.propMaterial)
            {
                var g = new GameObject("Look");
                g.transform.SetParent(fork, false);
                g.AddComponent<MeshFilter>().sharedMesh = fm.lod0;
                g.AddComponent<MeshRenderer>().sharedMaterial = lib.propMaterial;
            }
            else
            {
                var wood = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.5f, 0.34f, 0.2f) };
                const float G = BlockLibrary.G;
                Cyl(fork, new Vector3(0, 0.2f, 0) * G, new Vector3(0.28f, 1.0f, 0.28f) * G, Quaternion.identity, wood);
                foreach (var tip in new[] { PropAnchors.ForkTipL, PropAnchors.ForkTipR })
                {
                    var a = new Vector3(0, 1.2f, 0) * G; var b = tip * G;
                    Cyl(fork, (a + b) * 0.5f, new Vector3(0.22f * G, (b - a).magnitude * 0.5f, 0.22f * G), Quaternion.FromToRotation(Vector3.up, (b - a).normalized), wood);
                }
            }
            pouch = new GameObject("Pouch").transform;
            var pm = lib ? lib.Meshes("S_Pouch") : null;
            if (pm != null && pm.lod0 && lib.propMaterial)
            {
                var g = new GameObject("Look"); g.transform.SetParent(pouch, false);
                g.AddComponent<MeshFilter>().sharedMesh = pm.lod0; g.AddComponent<MeshRenderer>().sharedMaterial = lib.propMaterial;
            }
            else
            {
                var leather = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.35f, 0.22f, 0.14f) };
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(g.GetComponent<Collider>());
                g.transform.SetParent(pouch, false); g.transform.localScale = new Vector3(0.035f, 0.02f, 0.006f);
                g.GetComponent<Renderer>().sharedMaterial = leather;
            }
            bandL = Band("BandL"); bandR = Band("BandR");
            preview = new GameObject("AimPreview").AddComponent<ArcDots>();
            box = AmmoBox.Build(d.ammo);
        }

        static void Cyl(Transform parent, Vector3 pos, Vector3 scale, Quaternion rot, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localRotation = rot; g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = m;
        }

        LineRenderer Band(string name)
        {
            var lr = new GameObject(name).AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            lr.widthMultiplier = 0.004f;
            lr.positionCount = 2;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return lr;
        }

        void Show(bool on)
        {
            if (!fork) return;
            fork.gameObject.SetActive(on); pouch.gameObject.SetActive(on);
            if (post) post.gameObject.SetActive(on && OneHand);
            bandL.enabled = bandR.enabled = on;
            if (!on) { preview.Hide(); if (loaded) loaded.gameObject.SetActive(false); }
        }

        void Update()
        {
            if (Director.I && Director.I.paused) return;   // 暂停：手上的操作一律不响应
            if (!d || d.phase != Director.Phase.Battle) { if (state != State.Hidden) { state = State.Hidden; Show(false); } return; }
            input ??= HandInput.Instance;
            if (!input) return;
            cooldown -= Time.deltaTime;
            var L = input.hands[0]; var R = input.hands[1];
            bool leftFree = !d.supply || !d.supply.Holding(0);
            bool rightFree = !d.supply || !d.supply.Holding(1);

            // ── 单手模式：弓架一直插在立柱上
            if (OneHand)
            {
                if (state == State.Hidden) { state = State.Held; Show(true); aimDir = HeadForward(); }
                forkPos = PostTop;
            }
            // ── 左手：架弓
            else if (state == State.Hidden)
            {
                if (leftFree && L.tracked && L.pinch && !(d.supply && d.supply.ClaimedThisFrame(0))) { state = State.Held; Show(true); leftLostT = 0; aimDir = HeadForward(); }
            }
            else
            {
                if (L.tracked && L.pinch) { leftLostT = 0; forkPos = L.pinchPoint; }
                else if (!L.tracked && L.lostFor > 0) { leftLostT += Time.deltaTime; if (leftLostT > IT.bowHoldOnLost) Drop(); }
                else Drop();   // 左手松开捏合：收弓
            }
            if (state == State.Hidden) return;

            // ── 右手：在桌上的弹盒里捏一颗 = 换弹（不架弓时也可以先选好）
            if (rightFree && input.pinchDown[1] && state != State.Drawing)
            {
                var pick = box.Pick(R.pinchPoint, 0.03f);
                if (pick.HasValue && d.ammo.Count(pick.Value) > 0) { d.ammo.selected = pick.Value; box.Tick(); }
            }
            // ── 右手：抓皮兜 / 拉 / 放
            if (state == State.Held)
            {
                if (springT >= 0.45f) aimDir = Vector3.Slerp(aimDir, HeadForward(), Time.deltaTime * 4f);   // 刚松手、皮兜还在回弹时锁住方向（否则皮兜会斜着蹭进飞出的弹）
                if (rightFree && input.pinchDown[1])
                {
                    if (!box.Pick(R.pinchPoint, 0.03f).HasValue && Vector3.Distance(R.pinchPoint, PouchRest) < IT.pouchGrab && cooldown <= 0f) { state = State.Drawing; rightLostT = 0; Sfx.Grab(PouchRest); }
                }
            }
            else if (state == State.Drawing)
            {
                if (R.tracked && R.pinch)
                {
                    rightLostT = 0;
                    var pull = R.pinchPoint - ForkCenter;
                    float len = pull.magnitude;
                    if (len > 1e-3f) aimDir = -pull / len;
                    pull01 = Mathf.InverseLerp(IT.pullMin, IT.pullMax, len);
                }
                else if (!R.tracked)
                {
                    rightLostT += Time.deltaTime;
                    if (rightLostT > IT.releaseOnLost) Cancel();
                }
                else if (input.released[1] == ReleaseKind.Open) Release();
                else Cancel();   // 粘连松开：只松弓，不发射
            }
            Pose();
        }

        Vector3 HeadForward()
        {
            var h = d.head ? d.head.transform : transform;
            return (h.forward + Vector3.up * 0.15f).normalized;
        }

        void Pose()
        {
            // 弓架朝向：前方 = 瞄准方向；上方略往外斜（教学示范的“手斜一点”，两手不互相挡）
            var up = Quaternion.AngleAxis(-12f, aimDir) * Vector3.up;
            forkRot = Quaternion.LookRotation(aimDir, up);
            fork.SetPositionAndRotation(forkPos, forkRot);
            float s = 1f;
            fork.localScale = Vector3.one * s;
            var tipL = fork.TransformPoint(PropAnchors.ForkTipL * BlockLibrary.G);
            var tipR = fork.TransformPoint(PropAnchors.ForkTipR * BlockLibrary.G);
            Vector3 pp = PouchRest;
            if (state == State.Drawing) pp = ClampPull(input.hands[1].pinchPoint);
            else if (springT < 0.45f)
            {
                // 松手：皮兜兜着弹一起往前冲，到叉口弹脱兜飞出；之后皮兜在叉口前后晃两下停住（衰减振荡）
                springT += Time.deltaTime;
                // 皮兜跟在弹的后面（兜面贴着弹，不穿进弹里），直到皮兜自己越过叉口才开始回弹。
                // 按“位置”判断而不是按时间：画面上的弹有物理插值，比物理位置最多滞后一步，按时间切换会让皮兜跳到弹前面去。
                if (passT < 0f)
                {
                    pp = flying ? flying.transform.position - aimDir * BallOffset : ForkCenter;
                    if (!flying || Vector3.Dot(pp - ForkCenter, aimDir) >= 0f) passT = springT;
                }
                if (passT >= 0f)
                {
                    float t = springT - passT;
                    pp = ForkCenter + aimDir * (0.035f * Mathf.Exp(-t / 0.06f) * Mathf.Sin(t * 42f)) - aimDir * 0.03f * (1f - Mathf.Exp(-t / 0.05f));
                }
            }
            else pp = PouchRest;
            pouch.SetPositionAndRotation(pp, forkRot * Quaternion.Euler(0, 180, 0));   // 模型兜口朝本地 -Z，转过来朝向弓架
            bandL.SetPosition(0, tipL); bandL.SetPosition(1, pouch.TransformPoint(PropAnchors.PouchHoleR));
            bandR.SetPosition(0, tipR); bandR.SetPosition(1, pouch.TransformPoint(PropAnchors.PouchHoleL));
            var c = Color.Lerp(new Color(0.42f, 0.27f, 0.17f), new Color(1f, 0.62f, 0.3f), state == State.Drawing ? pull01 : 0f);
            bandL.material.color = bandR.material.color = c;
            bandL.widthMultiplier = bandR.widthMultiplier = Mathf.Lerp(0.0045f, 0.003f, state == State.Drawing ? pull01 : 0f);
            box.Tick();
            // 皮兜里的弹
            var type = d.ammo.AutoSelect();
            bool hasAmmo = d.ammo.Count(type) > 0 && cooldown <= 0f;
            if (!loaded || loadedType != type) { if (loaded) Destroy(loaded.gameObject); loaded = BallLook.Build(pouch, type, T.playerBallRadius); loaded.name = "LoadedBall"; loadedType = type; }
            loaded.gameObject.SetActive(hasAmmo);
            loaded.position = BallAt(pp);
            // 预瞄
            if (state == State.Drawing && pull01 > 0f && hasAmmo) preview.Show(BallAt(pp), LaunchVelocity(), T.playerBallRadius);
            else preview.Hide();
            if (state == State.Drawing) Sfx.Creak(pp, pull01);
        }

        Vector3 ClampPull(Vector3 p)
        {
            var v = p - ForkCenter;
            return ForkCenter + Vector3.ClampMagnitude(v, IT.pullMax);
        }

        public Vector3 LaunchVelocity()
        {
            float speed = Mathf.Lerp(T.slingSpeedMin, T.slingSpeedMax, pull01);
            var v = aimDir * speed;
            // 眼动辅助：看着敌城某一块时往那里轻轻吸
            if (input.EyeTracked && Physics.Raycast(input.Gaze, out var hit, 10f, Layers.BlockMask))
            {
                var b = hit.collider.GetComponent<Block>();
                if (b && b.castle && b.castle.side == Side.Enemy && Ballistics.Solve(ForkCenter, hit.point, speed, out var lo, out var hi))
                {
                    var sol = Vector3.Angle(v, lo) < Vector3.Angle(v, hi) ? lo : hi;
                    v = Vector3.RotateTowards(v, sol, IT.gazeSnap * Mathf.Deg2Rad, 0f);
                }
            }
            return v;
        }

        void Release()
        {
            var type = d.ammo.AutoSelect();
            bool fire = pull01 > 0f && cooldown <= 0f && d.ammo.Count(type) > 0;
            if (fire)
            {
                var v = LaunchVelocity();
                d.ammo.Take(type);
                // 弹从皮兜所在处出发，沿“皮兜 → 两叉中点”穿过叉口（叉内侧间距约 7 cm，弹直径 2.8 cm，碰不到叉）
                var pouchAt = ClampPull(input.hands[1].pinchPoint);
                var from = BallAt(pouchAt);
                flying = Projectile.Spawn(type, Side.Player, from, v);
                launchSpeed = v.magnitude;
                releasePos = pouchAt;
                springT = 0f;
                passT = -1f;
                d.stats.shots++;
                cooldown = T.reload;
                Sfx.StopCreak();
                Sfx.Launch(ForkCenter, pull01);
            }
            state = State.Held;
            pull01 = 0;
        }

        void Cancel() { state = State.Held; pull01 = 0; Sfx.StopCreak(); }
        void Drop() { state = State.Hidden; pull01 = 0; Show(false); Sfx.StopCreak(); }

        /// <summary>自动测试用：直接按“左手位置 / 右手拉到的位置”算出会怎么飞</summary>
        public static Vector3 VelocityFrom(Vector3 forkCenter, Vector3 pouchPos)
        {
            var IT = InputTuning.I; var T = GameTuning.I;
            var pull = pouchPos - forkCenter;
            float k = Mathf.InverseLerp(IT.pullMin, IT.pullMax, pull.magnitude);
            return -pull.normalized * Mathf.Lerp(T.slingSpeedMin, T.slingSpeedMax, k);
        }
    }
}

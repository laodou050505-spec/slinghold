// 敌人的投石机：每 4～6 秒一发。开火前蓄力约 1.2 秒（投臂往后拉、发亮、画出淡淡的弹道预告），只瞄玩家的城，不瞄人。
// 目标：玩家城里的块，40% 打托着帅旗的那一列，其余挑高处的块；加一点随机误差（随关卡变小）。
using System.Linq;
using UnityEngine;

namespace PaperSiege
{
    public class EnemyCatapult : MonoBehaviour
    {
        public Castle target;
        public LevelDef level;
        public AmmoStore playerAmmo;
        public bool active;
        public float unit = 0.09f;
        public System.Func<float> flightScale;   // 玩家没弹时变慢等
        float timer, windT = -1f;
        int shotIndex;
        Vector3 aimVel;
        Ammo nextAmmo;
        Transform frame, arm, cup;
        ArcDots telegraph;
        Material glowMat;
        float armRest = 0f, armBack = 10f, armThrow = -135f;   // 绕本地 X 转；投臂静止时朝 +Z（背对玩家），负角 = 往上、往玩家方向甩
        GameTuning T => GameTuning.I;

        public static EnemyCatapult Build(Transform spot, float unit)
        {
            var go = new GameObject("EnemyCatapult");
            go.transform.SetPositionAndRotation(spot.position, spot.rotation);
            var c = go.AddComponent<EnemyCatapult>();
            c.unit = unit;
            c.BuildLook();
            var tg = new GameObject("Telegraph");
            c.telegraph = tg.AddComponent<ArcDots>();
            c.telegraph.color = new Color(1f, 0.55f, 0.3f, 0.45f);
            c.telegraph.beadSize = 0.012f;
            return c;
        }

        void BuildLook()
        {
            var lib = BlockLibrary.I;
            float s = unit / BlockLibrary.G;
            var fm = lib ? lib.Meshes("C_Frame") : null;
            if (fm != null && fm.lod0 && lib.propMaterial)
            {
                frame = Mesh("Frame", fm.lod0, transform, Vector3.zero, s);
                var am = lib.Meshes("C_Arm");
                var axle = new GameObject("Axle").transform;
                axle.SetParent(transform, false);
                axle.localPosition = PropAnchors.CatapultAxle * unit;   // 锚点单位是“格”：× 每格米数
                arm = axle;
                if (am != null) Mesh("Arm", am.lod0, axle, Vector3.zero, s);
                var wm = lib.Meshes("C_Wheel");
                if (wm != null) foreach (var w in PropAnchors.CatapultWheels) Mesh("Wheel", wm.lod0, transform, w * unit, s);
                cup = new GameObject("Cup").transform;
                cup.SetParent(axle, false);
                cup.localPosition = PropAnchors.CatapultCup * unit;
            }
            else
            {
                // 占位：木框 + 投臂 + 勺
                var wood = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.55f, 0.38f, 0.24f) };
                frame = Box("Frame", transform, new Vector3(0, 0.25f, 0) * unit, new Vector3(2f, 0.4f, 3f) * unit, wood);
                Box("UprightL", transform, new Vector3(-0.8f, 1.1f, 0) * unit, new Vector3(0.25f, 1.6f, 0.4f) * unit, wood);
                Box("UprightR", transform, new Vector3(0.8f, 1.1f, 0) * unit, new Vector3(0.25f, 1.6f, 0.4f) * unit, wood);
                var axle = new GameObject("Axle").transform;
                axle.SetParent(transform, false);
                axle.localPosition = new Vector3(0, 1.7f, 0) * unit;
                arm = axle;
                Box("Arm", axle, PropAnchors.CatapultCup * 0.5f * unit, new Vector3(0.2f, 0.2f, 1.5f) * unit, wood);
                cup = Box("Cup", axle, PropAnchors.CatapultCup * unit, new Vector3(0.6f, 0.3f, 0.6f) * unit, wood);
                glowMat = cup.GetComponent<Renderer>().material;
            }
            arm.localRotation = Quaternion.Euler(armRest, 0, 0);
        }

        static Transform Mesh(string name, Mesh m, Transform parent, Vector3 pos, float s)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = Vector3.one * s;
            g.AddComponent<MeshFilter>().sharedMesh = m;
            g.AddComponent<MeshRenderer>().sharedMaterial = BlockLibrary.I.propMaterial;
            return g.transform;
        }

        static Transform Box(string name, Transform parent, Vector3 pos, Vector3 size, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(g.GetComponent<Collider>());
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = m;
            return g.transform;
        }

        /// <summary>只设目标、不自动开火（教学关开头）</summary>
        public void Aim(LevelDef lv, Castle tgt) { level = lv; target = tgt; active = false; windT = -1f; }

        public void Begin(LevelDef lv, Castle tgt, AmmoStore ammo)
        {
            level = lv; target = tgt; playerAmmo = ammo; active = true; shotIndex = 0;
            timer = Random.Range(lv.fireEvery.x, lv.fireEvery.y) + 2f;   // 开局多给 2 秒
            windT = -1f;
        }

        public void Stop() { active = false; windT = -1f; telegraph.Hide(); arm.localRotation = Quaternion.Euler(armRest, 0, 0); }

        void Update()
        {
            if (!target) return;
            if (!active && windT < 0f) return;   // 停火时只处理教学指定的那一发
            if (windT < 0f)
            {
                timer -= Time.deltaTime;
                if (timer <= 0f) StartWindup();
                arm.localRotation = Quaternion.Slerp(arm.localRotation, Quaternion.Euler(armRest, 0, 0), Time.deltaTime * 3f);
                return;
            }
            windT += Time.deltaTime;
            float k = Mathf.Clamp01(windT / T.windup);
            // 蓄力：投臂往后压、勺发亮、预告弧线淡入
            arm.localRotation = Quaternion.Euler(Mathf.Lerp(armRest, armBack, Mathf.SmoothStep(0, 1, k)), 0, 0);
            if (glowMat) glowMat.color = Color.Lerp(new Color(0.55f, 0.38f, 0.24f), new Color(1f, 0.6f, 0.25f), k);
            telegraph.alpha = k;
            telegraph.Show(cup.position, aimVel, T.enemyBallRadius);
            if (k >= 1f) Fire();
        }

        /// <summary>教学用：指定打哪、飞多久、什么弹（照样先蓄力 + 预告弧线）</summary>
        public void FireAt(Vector3 aim, float flight, Ammo a)
        {
            if (windT >= 0f) return;
            windT = 0f;
            nextAmmo = a;
            aimVel = Ballistics.VelocityFor(cup.position, aim, Mathf.Min(flight, MaxFlight(cup.position, aim)));
            Sfx.Windup(transform.position);
        }

        /// <summary>弧顶不超过 enemyApexMax 时最长能飞多久（飞得越久弧越高）</summary>
        float MaxFlight(Vector3 a, Vector3 b)
        {
            float g = -Ballistics.G.y;
            float h = Mathf.Max(0.2f, T.enemyApexMax - Mathf.Max(a.y, b.y));
            // 两端高度差不大时近似：弧顶比端点高 g·t²/8
            return Mathf.Sqrt(8f * h / g);
        }

        void StartWindup()
        {
            windT = 0f;
            nextAmmo = level.enemyAmmo[shotIndex % level.enemyAmmo.Length];
            var aim = PickTarget();
            float flight = Mathf.Min(Random.Range(T.enemyFlight.x, T.enemyFlight.y), MaxFlight(cup.position, aim));
            aimVel = Ballistics.VelocityFor(cup.position, aim, flight);
            Sfx.Windup(transform.position);
        }

        Vector3 PickTarget()
        {
            var alive = target.Alive.Where(b => !b.isKing).ToList();
            if (alive.Count == 0) return target.king ? target.king.WorldBounds.center : target.transform.position;
            Block pick;
            var kp = target.king ? target.king.transform.position : target.transform.position;
            var underKing = alive.Where(b => { var c = b.WorldBounds.center; return new Vector2(c.x - kp.x, c.z - kp.z).magnitude < 0.8f * target.unit && c.y < kp.y; }).ToList();
            if (underKing.Count > 0 && Random.value < 0.4f) pick = underKing[Random.Range(0, underKing.Count)];
            else
            {
                var top = alive.OrderByDescending(b => b.WorldBounds.max.y).Take(Mathf.Max(3, alive.Count / 2)).ToList();
                pick = top[Random.Range(0, top.Count)];
            }
            var e = Random.insideUnitSphere * level.aimError * target.unit;
            return pick.WorldBounds.center + e;
        }

        void Fire()
        {
            windT = -1f;
            shotIndex++;
            telegraph.Hide();
            if (glowMat) glowMat.color = new Color(0.55f, 0.38f, 0.24f);
            arm.localRotation = Quaternion.Euler(armThrow, 0, 0);
            Projectile.Spawn(nextAmmo, Side.Enemy, cup.position, aimVel);
            Sfx.Launch(cup.position, 1f);
            if (level != null) timer = Random.Range(level.fireEvery.x, level.fireEvery.y) * (flightScale != null ? flightScale() : 1f);   // 玩家没弹时间隔拉长
        }
    }

    /// <summary>道具模型上的关键点（格，Unity 坐标），道具组回报后填入</summary>
    public static class PropAnchors
    {
        // 投石机：正面（朝玩家）是本地 -Z；投臂静止时向后（+Z）下倾，绕本地 X 转负角往上、往前甩
        public static Vector3 CatapultAxle = new(0, 1.55f, 0);
        public static Vector3 CatapultCup = new(0, -0.566f, 1.36f);
        public static Vector3[] CatapultWheels = { new(-0.84f, 0.35f, -1f), new(0.84f, 0.35f, -1f), new(-0.84f, 0.35f, 1f), new(0.84f, 0.35f, 1f) };
        // 弹弓：皮筋起点（叉尖绕绳中心）
        public static Vector3 ForkTipL = new(-0.814f, 1.988f, 0), ForkTipR = new(0.79f, 1.968f, -0.02f);
        // 帅旗：布旗挂点、灯笼挂钩（米，按 1 格 = 0.05 m 的模型尺寸）
        public static Vector3 FlagAttach = new(0, 0.12125f, 0), LanternHook = new(-0.023f, 0.0965f, 0);
        // 皮兜两个穿孔（米）
        public static Vector3 PouchHoleL = new(-0.015f, 0, -0.0045f), PouchHoleR = new(0.015f, 0, -0.0045f);
    }
}

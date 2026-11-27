// 场地：桌面 + 地面（碰撞体）、两座城、敌人投石机的位置。
// 编辑器里没有真实房间：玩家站在原点面朝 +Z，桌子在身前，另外摆一个占位房间（只在编辑器里看得见）。
// 头显上：桌子从系统空间扫描读（RoomTable，D3 接入），读不到就在身前生成一张虚拟木桌。
using UnityEngine;

namespace PaperSiege
{
    public class Arena : MonoBehaviour
    {
        public Transform head;
        public Vector3 tableCenter;     // 桌面中心（世界，桌面高度）
        public Vector2 tableSize = new(0.9f, 0.6f);
        public Vector3 forward = Vector3.forward;   // 玩家面朝的水平方向（朝敌城）
        public Castle mine, enemy;
        public float ceilingHeight = 2.5f;   // 真实房间的天花板高度（D3 从空间扫描读，读不到用 2.5 m）
        public SkyDome sky;
        public Transform catapultSpot;  // 投石机位置（敌城前侧，地面上）
        GameObject env;
        GameTuning T => GameTuning.I;

        /// <summary>按玩家当前站位摆场地（开局 / 重来时调用）</summary>
        public void Layout()
        {
            var hp = head ? head.position : new Vector3(0, 1.5f, 0);
            var f = head ? Vector3.ProjectOnPlane(head.forward, Vector3.up) : Vector3.forward;
            forward = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
            // 真实桌子：系统空间扫描里有玩家面前合适的桌子就用它（高度 / 尺寸按真实的）；
            // 没有（编辑器里、或玩家没扫房间）就在身前放一张虚拟木桌
            var room = RoomTable.I;
            if (room && room.TryFront(hp, forward, out var t))
            {
                var toPlayer = hp - t.center; toPlayer.y = 0;
                bool aIsDepth = Mathf.Abs(Vector3.Dot(toPlayer, t.axisA)) * t.halfB > Mathf.Abs(Vector3.Dot(toPlayer, t.axisB)) * t.halfA;
                var nearAxis = aIsDepth ? t.axisA : t.axisB;
                float nearHalf = aIsDepth ? t.halfA : t.halfB;
                var dir = nearAxis * Mathf.Sign(Vector3.Dot(toPlayer, nearAxis));
                tableCenter = t.center + dir * Mathf.Max(0f, nearHalf - 0.2f);
                tableSize = new Vector2(
                    Mathf.Clamp((aIsDepth ? t.halfB : t.halfA) * 2f - 0.04f, 0.45f, 0.9f),
                    Mathf.Clamp(nearHalf * 2f - 0.04f, 0.3f, 0.6f));
                usingRealTable = true;
            }
            else
            {
                // 虚拟桌高按玩家眼高定：站着（眼高约 1.6 m）≈ 0.95 m，坐着（约 1.2 m）≈ 0.6 m；读不到合理眼高时用默认 0.85 m
                float th = hp.y > 0.9f && hp.y < 2.3f ? Mathf.Clamp(hp.y - 0.62f, 0.6f, 0.95f) : T.tableHeight;
                tableCenter = new Vector3(hp.x, th, hp.z) + forward * 0.5f;
                tableSize = new Vector2(0.9f, 0.6f);
                usingRealTable = false;
                // 已经有一张虚拟桌、新位置只差一点（玩家挪了几步 / 系统事件误报）：沿用旧桌，不拆了重搭
                if (vtable && Vector3.Distance(vtable.transform.position, tableCenter) < 0.25f
                    && Vector3.Angle(vtable.transform.forward, forward) < 15f)
                {
                    tableCenter = vtable.transform.position;
                    forward = vtable.transform.forward;
                }
            }
            // 天花板：系统扫描里读到就用它（盒盖 / 字标 / 天空开口都以它为准），否则默认 2.5 m
            float ceil = room ? room.CeilingAbove(hp) : 0f;
            ceilingHeight = ceil > hp.y + 0.6f ? ceil : 2.5f;
            enemyDistance = FitEnemyDistance();
            BuildEnv();
        }

        /// <summary>敌城离桌子多远（米）：默认 GameTuning.enemyDistance；房间小、安全区放不下时往近缩（最近 enemyDistanceMin）</summary>
        public float enemyDistance = 3.2f;
        /// <summary>测试用：模拟一个安全区多边形（世界坐标 xz）；null = 读系统安全区</summary>
        public static Vector2[] testBoundary;

        /// <summary>
        /// 敌城（含右边的投石机）要整块落在系统安全区里：从默认距离往近试，取第一个放得下的距离。
        /// 读不到安全区（编辑器、没设边界）就用默认距离。城不缩小——缩小会改积木质量，连带改难度。
        /// </summary>
        float FitEnemyDistance()
        {
            float far = T.enemyDistance, near = Mathf.Min(T.enemyDistanceMin, far);
            var poly = testBoundary ?? ReadBoundary();
            if (poly == null || poly.Length < 3) return far;
            var c = new Vector2(tableCenter.x, tableCenter.z);
            var f = new Vector2(forward.x, forward.z).normalized;
            var r = new Vector2(f.y, -f.x);   // 右 = Cross(up, forward)
            float u = T.enemyUnit;
            // 敌城占地：左右各约 6 格、前后各约 4 格；投石机在右边 7.2 格、往玩家方向 2 格
            Vector2[] probe = { Vector2.zero, r * 6f * u, -r * 6f * u, f * 4f * u, -f * 4f * u + r * 6f * u, -f * 4f * u - r * 6f * u, r * 8.4f * u - f * 2f * u };
            for (float d = far; d >= near - 1e-3f; d -= 0.05f)
            {
                bool ok = true;
                foreach (var p in probe) if (!Inside(poly, c + f * d + p)) { ok = false; break; }
                if (ok) { Debug.Log($"[木垒] 安全区：敌城距离 {d:0.00} m"); return d; }
            }
            Debug.Log($"[木垒] 安全区太小，敌城按最近距离 {near:0.00} m 摆放");
            return near;
        }

        static bool Inside(Vector2[] poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) inside = !inside;
            return inside;
        }

        /// <summary>系统安全区：优先矩形游玩区（PlayArea），没有就用外轮廓；坐标从追踪空间换到世界</summary>
        Vector2[] ReadBoundary()
        {
            if (!UnityEngine.XR.XRSettings.isDeviceActive) return null;
            try
            {
                var dim = ByteDance.PICO.XR.PXR_Boundary.GetDimensions(ByteDance.PICO.XR.BoundaryType.PlayArea);
                var pts = dim.x > .1f && dim.z > .1f ? ByteDance.PICO.XR.PXR_Boundary.GetGeometry(ByteDance.PICO.XR.BoundaryType.PlayArea) : null;
                if (pts == null || pts.Length < 3) pts = ByteDance.PICO.XR.PXR_Boundary.GetGeometry(ByteDance.PICO.XR.BoundaryType.OuterBoundary);
                if (pts == null || pts.Length < 3) return null;
                var space = RoomTable.I ? RoomTable.I.space : null;
                var poly = new Vector2[pts.Length];
                for (int i = 0; i < pts.Length; i++) { var w = space ? space.TransformPoint(pts[i]) : pts[i]; poly[i] = new Vector2(w.x, w.z); }
                return poly;
            }
            catch (System.Exception e) { Debug.LogWarning("[木垒] 读不到安全区：" + e.Message); return null; }
        }

        /// <summary>这一局用的是不是系统扫描出来的真桌子</summary>
        public bool usingRealTable { get; private set; }
        VirtualTable vtable;
        bool tableWanted;

        /// <summary>
        /// 虚拟桌立起 / 收起（有真实桌子时什么都不做）。返回桌子还要多久才搭好（秒），调用方据此推迟往桌上放东西。
        /// 封面（标题页）收起；对局开始前立起。animate = false 时瞬间出现（测试 / 编辑器快捷键直接开局）。
        /// </summary>
        public float ShowTable(bool on, bool animate = true)
        {
            tableWanted = on;
            if (!on) { if (vtable) vtable.Dismantle(); vtable = null; return 0f; }
            if (usingRealTable) return 0f;
            if (vtable) return vtable.Remaining;
            vtable = VirtualTable.Build(transform, tableCenter, Quaternion.LookRotation(forward, Vector3.up), tableSize, animate);
            return animate ? VirtualTable.BuildTime : 0f;
        }

        void BuildEnv()
        {
            if (env) Destroy(env);
            env = new GameObject("Env");
            env.transform.SetParent(transform, false);
            var rot = Quaternion.LookRotation(forward, Vector3.up);
            // 地面（无限大的一块）
            var floor = new GameObject("Floor") { layer = Layers.Env };
            floor.transform.SetParent(env.transform, false);
            floor.transform.position = new Vector3(tableCenter.x, -0.05f, tableCenter.z);
            floor.AddComponent<BoxCollider>().size = new Vector3(30, 0.1f, 30);
            // 桌面
            var table = new GameObject("TableTop") { layer = Layers.Env };
            table.transform.SetParent(env.transform, false);
            table.transform.SetPositionAndRotation(tableCenter - Vector3.up * 0.02f, rot);
            table.AddComponent<BoxCollider>().size = new Vector3(tableSize.x, 0.04f, tableSize.y);
            // 看得见的桌子：读到真实桌子就不画；没有就立一张虚拟木桌（编辑器、头显同一张，搭建有过程）。
            // 11-27 用户：封面上不要虚拟桌 → 只在对局时立（ShowTable），这里只处理“已经立着、位置变了”的情况
            bool keep = vtable && !usingRealTable && (vtable.transform.position - tableCenter).sqrMagnitude < 0.0004f
                        && Quaternion.Angle(vtable.transform.rotation, rot) < 2f;
            if (!keep && vtable) { vtable.Dismantle(); vtable = null; }
            if (tableWanted && !usingRealTable && !vtable) vtable = VirtualTable.Build(transform, tableCenter, rot, tableSize);
            // 编辑器占位房间（墙 / 地板 / 天花板替身，只在编辑器里）
            if (HandInput.Sim) EditorRoom(rot);
            // 盒盖：盖在玩家和敌城之间那片天花板上（宽 2.4 m、长 = 敌城距离 + 1.2 m）
            if (sky) sky.BuildLid(new Vector3(tableCenter.x, 0, tableCenter.z) + forward * (enemyDistance * 0.5f + 0.2f), forward, ceilingHeight, 2.4f, enemyDistance + 1.2f);
            catapultSpot = new GameObject("CatapultSpot").transform;
            catapultSpot.SetParent(env.transform, false);
        }

        void EditorRoom(Quaternion rot)
        {
            Prim("RoomFloor", new Vector3(tableCenter.x, -0.051f, tableCenter.z) + forward * 1.5f, new Vector3(6, 0.1f, 7.5f), rot, new Color(0.56f, 0.45f, 0.34f));
            Prim("RoomWall", new Vector3(tableCenter.x, 1.25f, tableCenter.z) + forward * 5.2f, new Vector3(6, 2.5f, 0.1f), rot, new Color(0.86f, 0.84f, 0.8f));
            Prim("RoomWallL", new Vector3(tableCenter.x, 1.25f, tableCenter.z) - Vector3.Cross(Vector3.up, forward) * 3f + forward * 1.5f, new Vector3(0.1f, 2.5f, 7.5f), rot, new Color(0.83f, 0.81f, 0.77f));
            Prim("RoomWallR", new Vector3(tableCenter.x, 1.25f, tableCenter.z) + Vector3.Cross(Vector3.up, forward) * 3f + forward * 1.5f, new Vector3(0.1f, 2.5f, 7.5f), rot, new Color(0.83f, 0.81f, 0.77f));
            // 占位天花板：四块拼成一个框，中间让出天空开口（开口在结局时会扩到整片天花板 → 跟着 SkyDome.MaskChanged 重新摆）
            ceilParts = new Transform[4];
            string[] names = { "CeilL", "CeilR", "CeilB", "CeilF" };
            for (int i = 0; i < 4; i++) ceilParts[i] = Prim(names[i], Vector3.zero, Vector3.one, rot, new Color(0.9f, 0.89f, 0.86f)).transform;
            if (sky)
            {
                sky.MaskChanged -= FitEditorCeiling; sky.MaskChanged += FitEditorCeiling;
                var lc = new Vector3(tableCenter.x, ceilingHeight, tableCenter.z) + forward * (enemyDistance * 0.5f + 0.2f);
                FitEditorCeiling(lc, new Vector2(1.2f, (enemyDistance + 1.2f) * 0.5f));
            }
        }

        // ── 编辑器替身天花板（6 × 7.5 m，中心在桌子前方 1.5 m）
        Transform[] ceilParts;
        const float RoomW = 6f, RoomL = 7.5f;
        Vector3 RoomCenter => new Vector3(tableCenter.x, 0, tableCenter.z) + forward * 1.5f;

        /// <summary>替身天花板让出天空开口（开口中心 c、横 / 纵半宽 h）。洞比开口每边大 3 cm：切边藏在盒沿内壁后面</summary>
        void FitEditorCeiling(Vector3 c, Vector2 h)
        {
            if (ceilParts == null || ceilParts[0] == null) return;
            var r = Vector3.Cross(Vector3.up, forward);
            // 盒沿在时洞口外扩 3 cm 藏在内壁后面；结局盒沿收起后不留余量（否则天空和替身天花板之间露一道黑缝）
            float m = sky && sky.RimVisible ? LidRim.WallT + 0.005f : 0f;
            var rc = RoomCenter;
            // 开口在房间坐标里的范围
            var d = new Vector3(c.x, 0, c.z) - rc;
            float x0 = Vector3.Dot(d, r) - h.x - m, x1 = Vector3.Dot(d, r) + h.x + m;
            float z0 = Vector3.Dot(d, forward) - h.y - m, z1 = Vector3.Dot(d, forward) + h.y + m;
            float W = RoomW / 2, L = RoomL / 2;
            x0 = Mathf.Clamp(x0, -W, W); x1 = Mathf.Clamp(x1, -W, W); z0 = Mathf.Clamp(z0, -L, L); z1 = Mathf.Clamp(z1, -L, L);
            float y = ceilingHeight + 0.03f;
            void Put(Transform t, float ax0, float ax1, float az0, float az1)
            {
                bool on = ax1 - ax0 > 0.01f && az1 - az0 > 0.01f;
                t.gameObject.SetActive(on);
                if (!on) return;
                t.position = rc + r * ((ax0 + ax1) / 2) + forward * ((az0 + az1) / 2) + Vector3.up * y;
                t.localScale = new Vector3(ax1 - ax0, 0.06f, az1 - az0);
            }
            Put(ceilParts[0], -W, x0, -L, L);       // 左
            Put(ceilParts[1], x1, W, -L, L);        // 右
            Put(ceilParts[2], x0, x1, z1, L);       // 远
            Put(ceilParts[3], x0, x1, -L, z0);      // 近
        }

        /// <summary>
        /// 整片天花板能开成天空的范围（结局用）：中心（天花板高度）+ 横 / 纵半宽（沿 forward 坐标系）。
        /// 必须不越过真实墙：否则抬头看墙时，墙的上半截会被画成天空。依次用：房间扫描里的天花板 → 系统安全区 → 保守默认值。
        /// 不会比天窗本身小。
        /// </summary>
        public void CeilingSkyRect(out Vector3 center, out Vector2 half)
        {
            var r = Vector3.Cross(Vector3.up, forward);
            var origin = new Vector3(tableCenter.x, 0, tableCenter.z);
            float lidC = enemyDistance * 0.5f + 0.2f, lidH = (enemyDistance + 1.2f) * 0.5f;
            float x0 = -1.2f, x1 = 1.2f, z0 = lidC - lidH, z1 = lidC + lidH;   // 天窗本身
            string src = "默认";
            var room = RoomTable.I;
            if (room && room.TryCeilingBox(new Vector3(tableCenter.x, ceilingHeight, tableCenter.z) + forward * lidC, out var cb))
            {
                // 天花板盒子在 forward 坐标系里的内接矩形：轴对得上（±12°）就直接用，歪着就按短边保守取
                float ang = Mathf.Abs(Mathf.DeltaAngle(0, Vector3.SignedAngle(cb.axisA, r, Vector3.up))) % 90f;
                bool aligned = ang < 12f || ang > 78f;
                var d = cb.center - origin; d.y = 0;
                float cx = Vector3.Dot(d, r), cz = Vector3.Dot(d, forward);
                float hx, hz;
                if (aligned)
                {
                    bool aIsX = Mathf.Abs(Vector3.Dot(cb.axisA, r)) > 0.7f;
                    hx = (aIsX ? cb.halfA : cb.halfB); hz = (aIsX ? cb.halfB : cb.halfA);
                }
                else hx = hz = Mathf.Min(cb.halfA, cb.halfB) * 0.7f;
                x0 = Mathf.Min(x0, cx - hx + 0.15f); x1 = Mathf.Max(x1, cx + hx - 0.15f);
                z0 = Mathf.Min(z0, cz - hz + 0.15f); z1 = Mathf.Max(z1, cz + hz - 0.15f);
                src = "房间扫描天花板";
            }
            else
            {
                var poly = testBoundary ?? ReadBoundary();
                if (poly != null && poly.Length >= 3)
                {
                    // 安全区一般在墙里面：取它在 forward 坐标系里的外框（不往外放）
                    float bx0 = float.MaxValue, bx1 = float.MinValue, bz0 = float.MaxValue, bz1 = float.MinValue;
                    var r2 = new Vector2(r.x, r.z); var f2 = new Vector2(forward.x, forward.z); var o2 = new Vector2(origin.x, origin.z);
                    foreach (var p in poly) { var q = p - o2; float x = Vector2.Dot(q, r2), z = Vector2.Dot(q, f2); bx0 = Mathf.Min(bx0, x); bx1 = Mathf.Max(bx1, x); bz0 = Mathf.Min(bz0, z); bz1 = Mathf.Max(bz1, z); }
                    x0 = Mathf.Min(x0, bx0); x1 = Mathf.Max(x1, bx1); z0 = Mathf.Min(z0, bz0); z1 = Mathf.Max(z1, bz1);
                    src = "安全区";
                }
                else
                {
                    // 读不到房间（编辑器 / 没设边界）：左右各 2.4 m，往后到玩家身后 1.4 m，往前到天窗尽头
                    x0 = -2.4f; x1 = 2.4f; z0 = -1.4f;
                }
            }
            // 上限：左右各 3.5 m、纵向总长 9 m（再大的房间也够了）
            x0 = Mathf.Max(x0, -3.5f); x1 = Mathf.Min(x1, 3.5f); z0 = Mathf.Max(z0, z1 - 9f);
            center = origin + r * ((x0 + x1) / 2) + forward * ((z0 + z1) / 2) + Vector3.up * ceilingHeight;
            half = new Vector2((x1 - x0) / 2, (z1 - z0) / 2);
            Debug.Log($"[木垒] 结局天空范围（{src}）：横 {x0:0.0}～{x1:0.0} m 纵 {z0:0.0}～{z1:0.0} m");
        }

        GameObject Prim(string name, Vector3 pos, Vector3 size, Quaternion rot, Color c)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(env.transform, false);
            g.transform.SetPositionAndRotation(pos, rot);
            g.transform.localScale = size;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = c };
            m.SetFloat("_Smoothness", 0.1f);
            g.GetComponent<Renderer>().sharedMaterial = m;
            return g;
        }

        /// <summary>标题页：上一局的两座城收掉</summary>
        public void ClearCastles()
        {
            if (mine) mine.Cleanup();
            if (enemy) enemy.Cleanup();
            mine = enemy = null;
        }

        /// <summary>只重搭敌城（教学练完、开打前：练习时打坏的城重新一块块搭起来）。位置 / 朝向和开局一样，投石机不动</summary>
        public Castle RebuildEnemy(LevelDef level, float duration = 1.8f)
        {
            if (enemy) enemy.Cleanup();
            var rot = Quaternion.LookRotation(forward, Vector3.up);
            var ep = new Vector3(tableCenter.x, 0, tableCenter.z) + forward * enemyDistance;
            enemy = Castle.Build(level.enemy, Side.Enemy, ep, rot, T.enemyUnit, true, transform);
            if (!Castle.InstantBuild) enemy.StartCoroutine(enemy.Assemble(duration));
            enemy.SetKingHP(level.kingHP);
            return enemy;
        }

        public void BuildCastles(LevelDef level)
        {
            if (mine) mine.Cleanup();
            if (enemy) enemy.Cleanup();
            var rot = Quaternion.LookRotation(forward, Vector3.up);
            // 玩家城：正面（城门）朝向玩家
            // 自己的城用低模：一块才 5 cm，在画面里很小，LOD0 的细节看不出来（省掉约 1/3 的三角面）
            mine = Castle.Build(Levels.PlayerCastle(), Side.Player, tableCenter + forward * 0.02f, rot, T.playerUnit, true, transform);
            // 敌城：在对面地上，正面朝玩家
            var ep = new Vector3(tableCenter.x, 0, tableCenter.z) + forward * enemyDistance;
            enemy = Castle.Build(level.enemy, Side.Enemy, ep, rot, T.enemyUnit, true, transform);
            // 开局两座城都从下往上一块块落下来搭好（有过程）；测试可以关掉
            if (!Castle.InstantBuild) { enemy.StartCoroutine(enemy.Assemble(2.0f)); mine.StartCoroutine(mine.Assemble(1.2f)); }
            mine.SetKingHP(3);              // 自己的帅旗 3 点耐久（不会被一发打掉）
            enemy.SetKingHP(level.kingHP);
            // 投石机放在敌城右前方的城外：按布局里最靠右那块积木的外沿再留 2.9 格（1 米宽的城时就是原来的 7.2 格）。
            // 11-26 巡检：原来固定 7.2 格，第 8～10 关城变宽后投石机被城墙挡住、第十关插进了石墙。
            float edge = 0f;
            foreach (var it in level.enemy.items)
            {
                var def = BlockLibrary.Def(it.piece);
                float half = def != null ? Mathf.Max(def.size.x, def.size.z) * 0.5f : 1f;   // 转过角度的块按长边算，宁可多留
                edge = Mathf.Max(edge, it.pos.x + half);
            }
            catapultSpot.SetPositionAndRotation(enemy.ToWorld(new Vector3(Mathf.Max(7.2f, edge + 2.9f), 0, -2.0f)), Quaternion.LookRotation(forward, Vector3.up));   // 投石机正面是本地 -Z → 朝玩家
        }
    }
}

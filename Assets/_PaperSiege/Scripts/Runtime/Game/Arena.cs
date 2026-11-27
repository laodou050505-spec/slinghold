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
            // 看得见的桌子：读到真实桌子就不画；没有就立一张虚拟木桌（编辑器、头显同一张，搭建有过程）
            var vtPos = tableCenter;
            bool keep = vtable && !usingRealTable && (vtable.transform.position - vtPos).sqrMagnitude < 0.0004f
                        && Quaternion.Angle(vtable.transform.rotation, rot) < 2f;
            if (!keep)
            {
                if (vtable) vtable.Dismantle();
                vtable = usingRealTable ? null : VirtualTable.Build(transform, vtPos, rot, tableSize);
            }
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
            // 占位天花板（盒盖那片之外的部分）：四块拼成一个框，中间留出盒盖的位置
            var cc = new Vector3(tableCenter.x, ceilingHeight + 0.03f, tableCenter.z) + forward * 1.5f;
            var r = Vector3.Cross(Vector3.up, forward);
            // 中间留出盒盖的洞，洞比开口每边大 3 cm：替身天花板的切边藏在盒沿内壁后面（11-27：原来切边和内壁共面，闪成一道灰条像穿模）
            const float m = LidRim.WallT + 0.005f;
            Prim("CeilL", cc - r * (2.1f + m / 2), new Vector3(1.8f - m, 0.06f, 7.5f), rot, new Color(0.9f, 0.89f, 0.86f));
            Prim("CeilR", cc + r * (2.1f + m / 2), new Vector3(1.8f - m, 0.06f, 7.5f), rot, new Color(0.9f, 0.89f, 0.86f));
            float lidL = enemyDistance + 1.2f + 2 * m, lidC = enemyDistance * 0.5f + 0.2f - 1.5f;
            float back = (7.5f / 2) - (lidC + lidL / 2), front = (7.5f / 2) + (lidC - lidL / 2);
            if (back > 0.01f) Prim("CeilB", cc + forward * (7.5f / 2 - back / 2), new Vector3(2.4f + 2 * m, 0.06f, back), rot, new Color(0.9f, 0.89f, 0.86f));
            if (front > 0.01f) Prim("CeilF", cc - forward * (7.5f / 2 - front / 2), new Vector3(2.4f + 2 * m, 0.06f, front), rot, new Color(0.9f, 0.89f, 0.86f));
        }

        void Prim(string name, Vector3 pos, Vector3 size, Quaternion rot, Color c)
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
        }

        /// <summary>标题页：上一局的两座城收掉</summary>
        public void ClearCastles()
        {
            if (mine) mine.Cleanup();
            if (enemy) enemy.Cleanup();
            mine = enemy = null;
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

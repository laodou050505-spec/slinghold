// 运行模式下的测试小工具（cs.py 调用）：切关、瞄准开火、任意机位截图、统计积木位移。
using System.Linq;
using UnityEngine;

namespace PaperSiege.EditorTools
{
    public static class PSTests
    {
        static Director D => Director.I;

        public static string Level(int i) { D.StartLevel(i); return $"第 {i + 1} 关：敌城 {D.arena.enemy.blocks.Count} 块"; }

        /// <summary>测试时：模拟手收起来（不自动挡弹）、投石机停火</summary>
        public static string Quiet(bool stopEnemy = true)
        {
            SimHands.scripted = true; SimHands.Hide(0); SimHands.Hide(1);
            if (stopEnemy) D.catapult.Stop();
            return "quiet";
        }

        /// <summary>从桌面上方（模拟弹弓位置）打敌城第 idx 块（-1 = 帅旗）</summary>
        public static string Shoot(Ammo a, int idx, float speed = 5.5f, bool high = false, float err = 0f)
        {
            var e = D.arena.enemy;
            var b = idx < 0 ? e.king : e.blocks[Mathf.Clamp(idx, 0, e.blocks.Count - 1)];
            var from = D.arena.tableCenter + Vector3.up * 0.35f + D.arena.forward * 0.05f;
            var tgt = b.WorldBounds.center + Random.insideUnitSphere * err;
            D.DebugShoot(a, from, tgt, speed, high);
            return $"打 {b.name}";
        }

        /// <summary>
        /// 按游戏时间跑一串测试步骤（不受命令行往返延迟影响）。steps：“秒数:动作”，动作 = shoot/弹种/块号[/high] 或 snap/名字 或 log。
        /// 例：Scenario("L5火", 4, "0:shoot/Fire/21", "3:snap/火3秒", "9:snap/火9秒", "9:log", "10:shoot/Ice/21", "12:snap/冰后")
        /// </summary>
        public static string Scenario(string dir, int level, params string[] steps)
        {
            Level(level); Quiet();
            D.StartCoroutine(Run(dir, steps));
            return "scenario started";
        }

        static System.Collections.IEnumerator Run(string dir, string[] steps)
        {
            float t0 = Time.time;
            foreach (var st in steps)
            {
                var parts = st.Split(':');
                float at = float.Parse(parts[0]);
                while (Time.time - t0 < at) yield return null;
                var a = parts[1].Split('/');
                switch (a[0])
                {
                    case "shoot": Shoot((Ammo)System.Enum.Parse(typeof(Ammo), a[1]), int.Parse(a[2]), 5.5f, a.Length > 3 && a[3] == "high"); break;
                    case "snap": SnapEnemy(dir, a[1]); break;
                    case "log": Debug.Log($"[场景 {Time.time - t0:0.0}s] " + State()); break;
                }
            }
            Debug.Log("[场景] 完成");
        }

        // ── 手势全流程（模拟手按游戏时间执行）
        public static string Gestures(string dir)
        {
            Level(0); Quiet();
            D.StartCoroutine(GestureRun(dir));
            return "gestures started";
        }

        static Vector3 HeadPt(float fwd, float right, float up)
        {
            var h = D.head.transform;
            var f = Vector3.ProjectOnPlane(h.forward, Vector3.up).normalized; var r = Vector3.Cross(Vector3.up, f);
            return h.position + f * fwd + r * right + Vector3.up * up;
        }

        static System.Collections.IEnumerator Move(int side, Vector3 a, Vector3 b, float t, bool pinch, bool fist = false)
        {
            for (float k = 0; k < 1f; k += Time.deltaTime / t) { SimHands.Pose(side, Vector3.Lerp(a, b, Mathf.SmoothStep(0, 1, k)), pinch, fist); yield return null; }
            SimHands.Pose(side, b, pinch, fist);
        }

        static void Log(string s) => Debug.Log("[手势] " + s);

        static System.Collections.IEnumerator GestureRun(string dir)
        {
            var sling = D.sling;
            // 1) 架弓：左手偏左下捏住
            var bow = HeadPt(0.38f, -0.12f, -0.22f);
            SimHands.Pose(0, bow, true);
            yield return new WaitForSeconds(0.4f);
            Log($"架弓 state={sling.state}");
            // 2) 右手到皮兜捏住
            var pr = sling.PouchRest;
            yield return Move(1, HeadPt(0.3f, 0.15f, -0.25f), pr, 0.3f, false);
            SimHands.Pose(1, pr, true);
            yield return null; yield return null;
            Log($"抓皮兜 state={sling.state}");
            // 3) 往右肩方向斜着拉，瞄敌方帅旗（按弹道反解出拉到哪里）
            var fc = sling.ForkCenter;
            var target = D.arena.enemy.king.WorldBounds.center;
            float pull = 0.26f;
            float speed = Mathf.Lerp(GameTuning.I.slingSpeedMin, GameTuning.I.slingSpeedMax, Mathf.InverseLerp(InputTuning.I.pullMin, InputTuning.I.pullMax, pull));
            Ballistics.Solve(fc, target, speed, out var lo, out var hi);
            var pouchAt = fc - lo.normalized * pull;
            yield return Move(1, pr, pouchAt, 0.6f, true);
            yield return new WaitForSeconds(0.2f);
            Log($"拉弓 state={sling.state} pull01={sling.pull01:0.00} 预测速度={sling.LaunchVelocity().magnitude:0.00}");
            SnapPlayer(dir, "1_拉弓预瞄");
            Snap(dir, "1b_拉弓侧面", HeadPt(0.45f, 0.35f, -0.05f), fc, 50);
            // 4) 松手发射
            int shots = D.stats.shots;
            SimHands.Pose(1, pouchAt, false);
            yield return null;
            Log($"松手 射={D.stats.shots - shots} 弹余={D.ammo.Total}");
            yield return new WaitForSeconds(2.5f);
            Log("发射后 " + State());
            // 5) 接：投石机开火，右手张开迎上去，被吸住后握拳
            SimHands.Hide(0); SimHands.Hide(1);   // 收弓（左手放下，免得敌弹先被左手吸住）
            Level(1);                               // 换一关重新开局（上一发可能已经打赢了）
            yield return new WaitForSeconds(0.5f);
            int caught = D.stats.caught, blocked = D.stats.blocked;
            for (int round = 0; round < 2; round++)
            {
                bool wantCatch = round == 0;
                Projectile p = null;
                float wait = 0;
                while (p == null && wait < 20f) { p = Projectile.Live.Find(x => x && x.from == Side.Enemy && x.state == Projectile.State.Flying); wait += Time.deltaTime; yield return null; }
                if (p == null) { Log("等不到敌弹"); break; }
                // 手放在弹道上（离自己的城 25 cm 处），张开
                var path = new System.Collections.Generic.List<Vector3>();
                Ballistics.Predict(p.transform.position, p.rb.linearVelocity, p.radius, path);
                var mine = D.arena.mine.transform.position;
                Vector3 meet = path[path.Count - 1];
                foreach (var q in path) if (Vector3.Distance(q, mine) < 0.3f) { meet = q; break; }
                SimHands.Pose(1, meet, false, false, (D.head.transform.position - meet).normalized * -1f);
                float t = 0;
                while (p && p.state == Projectile.State.Flying && t < 4f) { t += Time.deltaTime; yield return null; }
                if (wantCatch) { yield return new WaitForSeconds(0.2f); SimHands.Pose(1, meet, false, true); SnapPlayer(dir, "2_握拳接住"); }
                yield return new WaitForSeconds(1.0f);
                SimHands.Hide(1);
                Log($"第{round + 1}发 {(wantCatch ? "握拳" : "张手")}：接 +{D.stats.caught - caught} 挡 +{D.stats.blocked - blocked} 弹余={D.ammo.Total}");
                caught = D.stats.caught; blocked = D.stats.blocked;
            }
            D.catapult.Stop();
            // 6) 修城：拿掉自己城的一块，从补给盘捏一块补上
            var mineC = D.arena.mine;
            int slot = mineC.blocks.FindIndex(b => b.name == "W_Window" && !b.isKing);
            mineC.blocks[slot].Kill(true);
            yield return new WaitForSeconds(0.6f);
            var tray = GameObject.Find("Patch").transform.position + Vector3.up * 0.017f;
            yield return Move(1, HeadPt(0.3f, 0.2f, -0.3f), tray, 0.3f, false);
            SimHands.Pose(1, tray, true); yield return null; yield return null;
            var to = mineC.SlotCenter(slot);
            yield return Move(1, tray, to + Vector3.up * 0.02f, 0.6f, true);
            Log($"修城：空位={mineC.SlotEmpty(slot)} 手里有块={D.supply.Holding(1)}");
            SimHands.Pose(1, to + Vector3.up * 0.02f, false);
            yield return new WaitForSeconds(0.6f);
            Log($"修城后：补上={D.stats.repaired} 空位={mineC.SlotEmpty(slot)}");
            SimHands.Hide(1);
            // 7) 灭火：点着自己城的一块，往下拍两次
            var burn = mineC.blocks.Find(b => b.Wood && !b.Dead && !b.isKing && b.name == "W_Cube");
            burn.Ignite();
            yield return new WaitForSeconds(1f);
            var top = burn.WorldBounds.center + Vector3.up * (burn.WorldBounds.extents.y + 0.06f);
            for (int i = 0; i < 2; i++)
            {
                yield return Move(1, top + Vector3.up * 0.08f, top + Vector3.up * 0.08f, 0.25f, false);
                yield return Move(1, top + Vector3.up * 0.08f, top - Vector3.up * 0.03f, 0.07f, false);
                yield return new WaitForSeconds(0.25f);
            }
            Log($"灭火：还在烧={burn.burning} 灭火数={D.stats.extinguished}");
            SimHands.Hide(1); SimHands.Hide(0);
            Log("完成 " + State());
        }

        // ── 松手瞬间：慢放逐帧截图 + 量弹到弓架模型的最近距离（不能穿模）
        public static string ReleaseCheck(string dir)
        {
            Level(0); Quiet();
            D.StartCoroutine(ReleaseRun(dir));
            return "release check started";
        }

        static float PouchClearance(Vector3 p) => MeshClearance(GameObject.Find("Pouch"), p);
        static float ForkClearance(Vector3 p) => MeshClearance(GameObject.Find("SlingFork"), p);

        static float MeshClearance(GameObject fork, Vector3 p)
        {
            float best = float.MaxValue;
            foreach (var mf in fork.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.name == "LoadedBall") continue;   // 兜里那颗弹不算皮兜
                var m = mf.sharedMesh; var tr = mf.transform;
                foreach (var v in m.vertices) best = Mathf.Min(best, Vector3.Distance(tr.TransformPoint(v), p));
            }
            return best;
        }

        static System.Collections.IEnumerator ReleaseRun(string dir)
        {
            var sling = D.sling;
            var bow = HeadPt(0.42f, -0.09f, -0.16f);
            SimHands.Pose(0, bow, true);
            yield return new WaitForSeconds(0.4f);
            var pr = sling.PouchRest;
            SimHands.Pose(1, pr, true);
            yield return null; yield return null;
            // 静止时（刚抓住皮兜、还没拉）皮兜里的弹离弓架多远
            float r = GameTuning.I.playerBallRadius;
            Transform LoadedBall() { var g = GameObject.Find("LoadedBall"); return g ? g.transform : null; }
            var lb = LoadedBall();
            Debug.Log($"[松手检查] 静止时弹心到皮兜最近 {(lb ? PouchClearance(lb.position) * 1000 : -1):0.0}mm，到弓架 {(lb ? ForkClearance(lb.position) * 1000 : -1):0.0}mm（弹半径 {r * 1000:0.0}mm）");
            var fc = sling.ForkCenter;
            var target = D.arena.enemy.blocks[0].WorldBounds.center;
            Ballistics.Solve(fc, target, 4.4f, out var lo, out _);
            var to = fc - lo.normalized * 0.26f;
            yield return Move(1, pr, to, 0.6f, true);
            yield return new WaitForSeconds(0.3f);
            lb = LoadedBall();
            Debug.Log($"[松手检查] 拉满时弹心到皮兜最近 {(lb ? PouchClearance(lb.position) * 1000 : -1):0.0}mm");
            var side = HeadPt(0.46f, 0.32f, -0.08f);
            Snap(dir, "0_拉满", side, sling.ForkCenter, 45);
            // 松手，1/10 速度
            Time.timeScale = 0.1f;
            SimHands.Pose(1, to, false);
            yield return null;
            Projectile ball = Projectile.Live.Find(x => x && x.from == Side.Player);
            float minClear = float.MaxValue, minPouch = float.MaxValue;
            var trace = new System.Text.StringBuilder();
            var fcNow = sling.ForkCenter; var aim = (fcNow - to).normalized;
            int shot = 0; float lastSnapD = 99f;
            for (int f = 0; f < 600 && ball; f++)
            {
                var bp = ball.transform.position;
                float along = Vector3.Dot(bp - fcNow, aim);          // <0：还在叉口后面；>0：已经飞过叉口
                minClear = Mathf.Min(minClear, ForkClearance(bp));
                float pc = PouchClearance(bp);
                minPouch = Mathf.Min(minPouch, pc);
                trace.Append($"{along * 100:0.0}cm/{pc * 1000:0.0}mm/{(GameObject.Find("Pouch").transform.position - bp).magnitude * 1000:0}  ");
                // 每前进约 6 cm 截一张（接近 → 穿过 → 飞出）
                if (Mathf.Abs(along - lastSnapD) > 0.06f || f == 0) { Snap(dir, $"{++shot}_松手_距叉口{along * 100:0}cm", side, fcNow, 45); lastSnapD = along; }
                if (along > 0.12f) break;
                yield return null;
            }
            Time.timeScale = 1f;
            Debug.Log("[松手逐帧] 距叉口/到皮兜最近/皮兜原点到弹心(mm)： " + trace);
            Debug.Log($"[松手检查] 弹半径 {r * 1000:0.0}mm，飞出时弹心到弓架最近 {minClear * 1000:0.0}mm、到皮兜最近 {minPouch * 1000:0.0}mm → {(minClear > r && minPouch > r ? "不穿模" : "穿模！")}");
            SimHands.Hide(0); SimHands.Hide(1);
        }

        public static string State()
        {
            var a = D.arena;
            string S(Castle c) { int moved = c.blocks.Count(b => Vector3.Distance(b.transform.position, b.homePos) > 0.005f); int dyn = c.blocks.Count(b => b.Dynamic); int dead = c.blocks.Count(b => b.Dead); return $"块{c.blocks.Count} 动{moved} 醒{dyn} 毁{dead} 烧{c.Burning.Count} 完整{c.Integrity:0.00} 旗倒{c.IsKingDown}"; }
            return $"phase={D.phase} 剩{D.timeLeft:0}s 弹{D.ammo.Total}  我方[{S(a.mine)}]  敌方[{S(a.enemy)}]  在飞{Projectile.Live.Count} 射{D.stats.shots} 挡{D.stats.blocked} 接{D.stats.caught}";
        }

        /// <summary>从 pos 看 look 截图（临时挪主相机，截完放回）</summary>
        public static string Snap(string dir, string name, Vector3 pos, Vector3 look, float fov = 60)
        {
            var c = D.head;
            var p0 = c.transform.position; var r0 = c.transform.rotation; var f0 = c.fieldOfView;
            c.transform.position = pos; c.transform.LookAt(look); c.fieldOfView = fov;
            c.name = name;
            var path = PSArt.Shot(name, dir);
            c.name = "XR Camera";
            c.transform.position = p0; c.transform.rotation = r0; c.fieldOfView = f0;
            return path;
        }

        public static string SnapPlayer(string dir, string name) => Snap(dir, name, D.head.transform.position, D.head.transform.position + D.head.transform.forward, D.head.fieldOfView);

        public static string SnapEnemy(string dir, string name)
        {
            var e = D.arena.enemy.transform.position;
            return Snap(dir, name, D.arena.tableCenter + Vector3.up * 0.5f + D.arena.forward * 1.2f, e + Vector3.up * 0.2f, 40);
        }
    }
}

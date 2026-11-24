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
            foreach (var dp in Object.FindObjectsByType<DesktopPlay>()) Object.Destroy(dp);   // 自动测试接管模拟手
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

        /// <summary>桌面试玩的拖动换算：模拟“按下 → 往下（/左）拖 → 松开”，看发射方向和力度</summary>
        public static string DesktopDrag(float dx, float dy)
        {
            var dp = Object.FindAnyObjectByType<DesktopPlay>();
            if (!dp) return "没有 DesktopPlay";
            D.StartCoroutine(DragRun(dp, new Vector2(dx, dy)));
            return "drag started";
        }

        static System.Collections.IEnumerator DragRun(DesktopPlay dp, Vector2 dd)
        {
            var t = D.head.transform;
            SimHands.scripted = true;
            SimHands.Pose(0, t.position + t.forward * 0.40f - t.right * 0.10f - t.up * 0.10f, true);
            yield return new WaitForSeconds(0.3f);
            SimHands.Pose(1, D.sling.PouchRest, true);
            yield return null; yield return null;
            for (float k = 0; k <= 1f; k += Time.deltaTime / 0.5f) { SimHands.Pose(1, dp.HandFromDrag(dd * k), true); yield return null; }
            for (int i = 0; i < 10; i++) { SimHands.Pose(1, dp.HandFromDrag(dd), true); yield return null; }
            var v = D.sling.LaunchVelocity();
            float pitch = Mathf.Asin(Mathf.Clamp(v.normalized.y, -1, 1)) * Mathf.Rad2Deg;
            float yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(t.forward, Vector3.up), Vector3.ProjectOnPlane(v, Vector3.up), Vector3.up);
            int shots = D.stats.shots;
            SimHands.Pose(1, dp.HandFromDrag(dd), false);
            yield return null;
            Debug.Log($"[拖动] 拖({dd.x:0.00},{dd.y:0.00}) → 拉满 {D.sling.pull01:0.00}? 速度 {v.magnitude:0.0} m/s 仰角 {pitch:0}° 偏航 {yaw:0}°（正=右） 射出 {D.stats.shots - shots}");
            SimHands.Hide(1);
        }

        /// <summary>每关：搭好静置 3 秒，量最大位移，拍敌城</summary>
        public static string AllLevels(string dir) { Quiet(); D.StartCoroutine(AllRun(dir)); return "all levels started"; }

        static System.Collections.IEnumerator AllRun(string dir)
        {
            for (int i = 0; i < Levels.Count; i++)
            {
                Level(i); Quiet();
                yield return new WaitForSeconds(3f);
                float maxD = 0; foreach (var b in D.arena.enemy.blocks) maxD = Mathf.Max(maxD, Vector3.Distance(b.transform.position, b.homePos));
                var e = D.arena.enemy.transform.position;
                Snap(dir, $"L{i + 1:00}", D.arena.tableCenter + Vector3.up * 0.45f + D.arena.forward * 1.3f, e + Vector3.up * 0.22f, 42);
                Debug.Log($"[逐关] 第{i + 1}关 {D.arena.enemy.blocks.Count}块 静置3秒最大位移 {maxD * 1000:0.0}mm 帅旗耐久 {D.arena.enemy.kingHP}");
            }
            Debug.Log("[逐关] 完成");
        }

        /// <summary>连打帅旗：每隔 2 秒打一发，记录耐久和是否倒下</summary>
        public static string HitKing(int level, int shots, Ammo a = Ammo.Stone) { Level(level); Quiet(); D.StartCoroutine(HitKingRun(shots, a)); return "hit king started"; }

        static System.Collections.IEnumerator HitKingRun(int shots, Ammo a)
        {
            yield return new WaitForSeconds(0.5f);
            for (int i = 0; i < shots; i++)
            {
                var k = D.arena.enemy.king;
                var from = D.arena.tableCenter + Vector3.up * 0.35f + D.arena.forward * 0.05f;
                // 瞄旗杆露出墙头的那一截（高抛砸下去）
                var tgt = k.transform.position + Vector3.up * 2.3f * D.arena.enemy.unit;
                D.DebugShoot(a, from, tgt, 6.2f, true);
                yield return new WaitForSeconds(2.2f);
                Debug.Log($"[打旗] 第{i + 1}发 耐久 {D.arena.enemy.kingHP}/{D.arena.enemy.kingMaxHP} 旗倒 {D.arena.enemy.IsKingDown} phase {D.phase}");
                if (i == 1) SnapEnemy("1124_打旗", $"第{i + 1}发后");
            }
            yield return new WaitForSeconds(1f);
            SnapEnemy("1124_打旗", "最后");
        }

        /// <summary>自动走一遍教学：模拟手按提示做，每一步截图</summary>
        public static string TutorialWalk(string dir)
        {
            foreach (var dp in Object.FindObjectsByType<DesktopPlay>()) Object.Destroy(dp);
            SimHands.scripted = true; SimHands.Hide(0); SimHands.Hide(1);
            D.head.transform.rotation = Quaternion.Euler(14, 0, 0);
            D.StartLevel(0);
            D.StartCoroutine(TutRun(dir));   // 里面等一帧再找教学对象（旧的那份要到帧末才销毁）
            return "tutorial walk started";
        }

        static System.Collections.IEnumerator TutRun(string dir)
        {
            yield return null;
            Tutorial tut = null;
            foreach (var x in Object.FindObjectsByType<Tutorial>()) if (x && !x.Equals(null)) tut = x;
            void L(string s) => Debug.Log($"[教学] {s}  step={tut.step}");
            var t = D.head.transform;
            Vector3 bow() => t.position + t.forward * 0.40f - t.right * 0.10f - t.up * 0.10f;
            yield return new WaitForSeconds(2.0f);
            SnapPlayer(dir, "1_举弓"); L("开始");
            SimHands.Pose(0, bow(), true);
            yield return new WaitForSeconds(1.2f);
            SnapPlayer(dir, "2_抓皮兜"); L("举弓后");
            SimHands.Pose(1, D.sling.PouchRest, true);
            yield return new WaitForSeconds(1.4f);
            SnapPlayer(dir, "3_往后拉"); L("抓皮兜后");
            var target = D.arena.enemy.blocks[2].WorldBounds.center;
            for (float k = 0; k < 1f; k += Time.deltaTime / 0.6f)
            {
                var fc = D.sling.ForkCenter;
                var dirv = Ballistics.Solve(fc, target, 4.6f, out var lo, out _) ? lo.normalized : t.forward;
                SimHands.Pose(1, Vector3.Lerp(D.sling.PouchRest, fc - dirv * 0.27f, k), true);
                yield return null;
            }
            yield return new WaitForSeconds(0.8f);
            SnapPlayer(dir, "4_松手"); L("拉开后");
            var hold = GameObject.Find("Pouch").transform.position;
            SimHands.Pose(1, hold, false);
            yield return null; SimHands.Hide(1);
            SimHands.Pose(0, bow(), false); yield return null; SimHands.Hide(0);   // 放下弓，免得左手挡弹
            // 挡、接：右手追着敌弹（张手 / 握拳）
            for (int phase = 5; phase <= 6; phase++)
            {
                float wait = 0;
                while (tut.step < phase && wait < 15f) { wait += Time.deltaTime; yield return null; }
                Projectile p = null;
                wait = 0;
                while (p == null && wait < 10f) { p = Projectile.Live.Find(x => x && x.from == Side.Enemy && x.state == Projectile.State.Flying); wait += Time.deltaTime; yield return null; }
                float apex = 0f, ft = 0f; bool snapped = false;
                while (p && p.state == Projectile.State.Flying)
                {
                    ft += Time.deltaTime;
                    if (!snapped && ft > 0.7f)
                    {
                        snapped = true;
                        SnapPlayer(dir, phase == 5 ? "5_挡" : "6_接"); L(phase == 5 ? "敌弹飞来（挡）" : "敌弹飞来（接）");
                        var gh = GameObject.Find("GhostHand_R");
                        L($"示范右手 active={(gh ? gh.activeInHierarchy : false)} pos={(gh ? gh.GetComponentInChildren<SkinnedMeshRenderer>().bounds.center.ToString("F2") : "-")} 弹={p.transform.position.ToString("F2")}");
                    }
                    apex = Mathf.Max(apex, p.transform.position.y);
                    // 手在城的正上方等着（像真人一样迎着弹道），弹飞近了再追过去
                    var c = D.arena.mine.transform.position + Vector3.up * 0.18f;
                    var hp = Vector3.Distance(p.transform.position, c) < 0.6f ? p.transform.position + (t.position - p.transform.position).normalized * 0.05f : c;
                    SimHands.Pose(1, hp, false, phase == 6, (t.position - hp).normalized * -1f);
                    yield return null;
                }
                yield return new WaitForSeconds(0.8f);
                SimHands.Hide(1);
                L($"第{phase}步结果 挡{D.stats.blocked} 接{D.stats.caught} 敌弹最高 {apex:0.00}m（从被看到起）");
            }
            // 修城
            { float w = 0; while (tut.step < 7 && w < 15f) { w += Time.deltaTime; yield return null; } }
            yield return new WaitForSeconds(4.5f);
            SnapPlayer(dir, "7_修城"); L("缺口出现后");
            var piece = GameObject.Find("Patch").transform.position + Vector3.up * 0.017f;
            SimHands.Pose(1, piece, true); yield return null; yield return null;
            int slot = D.arena.mine.layout.items.FindIndex(x => x.piece == "W_Battlement");
            var to = D.arena.mine.SlotCenter(slot) + Vector3.up * 0.02f;
            for (float k = 0; k < 1f; k += Time.deltaTime / 0.6f) { SimHands.Pose(1, Vector3.Lerp(piece, to, k), true); yield return null; }
            SimHands.Pose(1, to, false); yield return null; SimHands.Hide(1);
            yield return new WaitForSeconds(3f);
            L($"修城后 补={D.stats.repaired}");
            { float w = 0; while (tut.step < 8 && w < 10f) { w += Time.deltaTime; yield return null; } }
            yield return new WaitForSeconds(1.0f);
            SnapPlayer(dir, "8_开打"); L($"开打 敌方帅旗耐久={D.arena.enemy.kingHP} 计时暂停={D.timerPaused} 剩{D.timeLeft:0}s");
        }

        /// <summary>新弹演示台 + 着火提示：各关拍演示台近景</summary>
        public static string TipsCheck(string dir) { Quiet(); D.StartCoroutine(TipsRun(dir)); return "tips check started"; }

        static System.Collections.IEnumerator TipsRun(string dir)
        {
            foreach (var i in new[] { 2, 4, 6, 7 })
            {
                Level(i); Quiet();
                yield return new WaitForSeconds(2.4f);
                var st = GameObject.Find("AmmoDemo");
                if (!st) { Debug.Log($"[提示] 第{i + 1}关 没有演示台"); continue; }
                var p = st.transform.position;
                var cam = p - D.arena.forward * 0.45f + Vector3.up * 0.28f - Vector3.Cross(Vector3.up, D.arena.forward) * 0.1f;
                Snap(dir, $"L{i + 1}_演示_a", cam, p + Vector3.up * 0.12f, 50);
                yield return new WaitForSeconds(1.4f);
                Snap(dir, $"L{i + 1}_演示_b", cam, p + Vector3.up * 0.12f, 50);
                Debug.Log($"[提示] 第{i + 1}关 演示台 {p.ToString("F2")}");
            }
            Level(3); Quiet();
            yield return new WaitForSeconds(1f);
            var b = D.arena.mine.blocks.Find(x => x.Wood && !x.isKing && x.name == "W_Cube");
            b.Ignite();
            yield return new WaitForSeconds(1.2f);
            SnapPlayer(dir, "着火提示");
            Debug.Log("[提示] 完成");
        }

        /// <summary>天空：开场逐帧（盒盖掀开）+ 每个时段一张（往上看天）</summary>
        public static string SkyCheck(string dir) { Quiet(); D.StartCoroutine(SkyRun(dir)); return "sky check started"; }

        static System.Collections.IEnumerator SkyRun(string dir)
        {
            var t = D.head.transform;
            t.rotation = Quaternion.Euler(-12, 0, 0);   // 微微抬头：看得到天花板和远处的城
            Level(1); Quiet();
            for (int f = 0; f < 9; f++)
            {
                Snap(dir, $"开场_{f:0}_{f * 0.7f:0.0}s", t.position, t.position + t.forward, 80);
                yield return new WaitForSeconds(0.7f);
            }
            foreach (var i in new[] { 0, 2, 4, 6, 8, 9 })
            {
                Level(i); Quiet();
                yield return new WaitForSeconds(5.8f);
                Snap(dir, $"时段_L{i + 1}", t.position, t.position + t.forward, 80);
                Debug.Log($"[天空] 第{i + 1}关 时段 {D.level.timeOfDay} 盒盖开 {D.sky.Open:0.00}");
            }
            // 最后 30 秒：太阳下沉
            Level(4); Quiet();
            yield return new WaitForSeconds(5.5f);
            Snap(dir, "下沉_前", t.position, t.position + t.forward, 80);
            D.timeLeft = 2f;
            yield return new WaitForSeconds(1.0f);
            D.timeLeft = 30f;
            for (float x = 0; x < 6f; x += Time.deltaTime) { D.timeLeft = Mathf.Max(1f, 30f - x * 5f); yield return null; }
            Snap(dir, "下沉_后", t.position, t.position + t.forward, 80);
            // 赢：烟花
            D.End(true);
            yield return new WaitForSeconds(1.2f);
            Snap(dir, "赢_烟花", t.position, t.position + t.forward, 80);
            Level(4); Quiet();
            yield return new WaitForSeconds(5.5f);
            D.End(false);
            yield return new WaitForSeconds(2.0f);
            Snap(dir, "输_合上", t.position, t.position + t.forward, 80);
            t.rotation = Quaternion.Euler(14, 0, 0);
            Debug.Log("[天空] 完成");
        }

        /// <summary>模拟头显：藏掉占位房间、相机清屏全透明，截一张带 alpha 的图，统计“天空”占了画面哪里</summary>
        public static string PassthroughCheck(string dir)
        {
            var cam = D.head;
            var hidden = new System.Collections.Generic.List<GameObject>();
            foreach (var r in Object.FindObjectsByType<Renderer>())
            {
                var n = r.name;
                if (n.StartsWith("Room") || n.StartsWith("Ceil") || n == "TableTopLook" || n == "Leg") { r.gameObject.SetActive(false); hidden.Add(r.gameObject); }
            }
            var bg = cam.backgroundColor; var cf = cam.clearFlags;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);
            var t = cam.transform; var r0 = t.rotation;
            t.rotation = Quaternion.Euler(-35, 0, 0);
            int w = 800, h = 450;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = null;
            int opaque = 0, top = 0, bottom = 0, sides = 0;
            var px = tex.GetPixels();
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                if (px[y * w + x].a < 0.5f) continue;
                opaque++;
                if (x < w * 0.15f || x > w * 0.85f) sides++;
                if (y > h * 0.85f) top++;
            }
            // 透明部分画成品红，方便肉眼看
            for (int i = 0; i < px.Length; i++) if (px[i].a < 0.5f) px[i] = new Color(1, 0, 1, 1);
            tex.SetPixels(px); tex.Apply();
            var outDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Docs/进度截图", dir);
            System.IO.Directory.CreateDirectory(outDir);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(outDir, "透视模拟_品红为真实房间.png"), tex.EncodeToPNG());
            t.rotation = r0; cam.clearFlags = cf; cam.backgroundColor = bg;
            foreach (var g in hidden) g.SetActive(true);
            rt.Release();
            return $"不透明 {opaque * 100f / px.Length:0}%，左右两侧 15% 区域里不透明 {sides * 100f / (px.Length * 0.3f):0}%";
        }

        /// <summary>整局流程：标题 → 捏“开始” → 打赢 → 结算（截图）→ 捏“下一关” → 进下一关</summary>
        public static string FlowCheck(string dir) { foreach (var dp in Object.FindObjectsByType<DesktopPlay>()) Object.Destroy(dp); D.StartCoroutine(FlowRun(dir)); return "flow started"; }

        static System.Collections.IEnumerator PressButton(string zh)
        {
            WoodButton b = null;
            foreach (var x in WoodButton.All) if (x && x.Key == zh) b = x;
            if (!b) { Debug.Log("[流程] 找不到按钮 " + zh); yield break; }
            var p = b.ClosestPoint(b.transform.position - D.arena.forward * 0.1f);
            SimHands.scripted = true;
            SimHands.Pose(1, p - D.arena.forward * 0.02f, false); yield return null; yield return null;
            SimHands.Pose(1, p, true); yield return null; yield return null;
            SimHands.Pose(1, p, false); yield return new WaitForSeconds(0.4f);
            SimHands.Hide(1);
        }

        static System.Collections.IEnumerator FlowRun(string dir)
        {
            SimHands.scripted = true; SimHands.Hide(0); SimHands.Hide(1);
            Progress.Unlocked = 2;
            D.GoTitle();
            yield return new WaitForSeconds(1.0f);
            Debug.Log($"[流程] 标题 phase={D.phase} 按钮={WoodButton.All.Count}");
            yield return PressButton("继续");
            yield return new WaitForSeconds(1.0f);
            Debug.Log($"[流程] 捏“继续”后 phase={D.phase} 关={D.levelIndex + 1}");
            Quiet();
            yield return new WaitForSeconds(5f);
            // 直接让敌方帅旗耐久打光（模拟打赢）
            D.arena.enemy.DamageKing(99, D.arena.enemy.transform.position - D.arena.forward);
            yield return new WaitForSeconds(5.5f);
            Debug.Log($"[流程] 赢后 phase={D.phase} 结算板={(GameObject.Find("ResultBoard") != null)} 星={Progress.Stars(D.levelIndex)} 解锁到={Progress.Unlocked + 1}");
            var rb = GameObject.Find("ResultBoard");
            if (rb) SnapPlayerLook(dir, "结算_胜利", rb.transform.position);
            yield return PressButton("下一关");
            yield return new WaitForSeconds(1.0f);
            Debug.Log($"[流程] 捏“下一关”后 phase={D.phase} 关={D.levelIndex + 1}");
            Quiet();
            yield return new WaitForSeconds(5f);
            D.arena.mine.DamageKing(99, D.arena.mine.transform.position + D.arena.forward);
            yield return new WaitForSeconds(5.0f);
            rb = GameObject.Find("ResultBoard");
            if (rb) SnapPlayerLook(dir, "结算_失守", rb.transform.position);
            Debug.Log($"[流程] 输后 phase={D.phase} 结算板={(rb != null)}");
            yield return PressButton("回标题");
            yield return new WaitForSeconds(1.0f);
            Debug.Log($"[流程] 捏“回标题”后 phase={D.phase}");
            Debug.Log("[流程] 完成");
        }

        /// <summary>从玩家眼睛的位置看向某处（模拟低头看），视场角和头显接近</summary>
        public static string SnapPlayerLook(string dir, string name, Vector3 at) => Snap(dir, name, D.head.transform.position, at, 80);

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

// 运行模式下的测试小工具（cs.py 调用）：切关、瞄准开火、任意机位截图、统计积木位移。
using System.Linq;
using UnityEngine;

namespace PaperSiege.EditorTools
{
    public static class PSTests
    {
        static Director D => Director.I;

        public static string Level(int i) { Castle.InstantBuild = true; D.StartLevel(i); Castle.InstantBuild = false; return $"第 {i + 1} 关：敌城 {D.arena.enemy.blocks.Count} 块"; }

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
            D.catapult.Begin(D.level, D.arena.mine, D.ammo);   // Level() 里的 Quiet() 会停火，这里要敌人开火
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
                float t = 0, minD = 9f;
                while (p && p.state == Projectile.State.Flying && t < 4f) { t += Time.deltaTime; minD = Mathf.Min(minD, Vector3.Distance(p.transform.position, HandInput.Instance.hands[1].palm)); yield return null; }
                Log($"  诊断：弹离手最近 {minD * 100:0.0}cm、弹状态 {(p ? p.state.ToString() : "已销毁")}、phase={D.phase}、手 tracked={HandInput.Instance.hands[1].tracked} pinch={HandInput.Instance.hands[1].pinch}、迎弹点离城 {Vector3.Distance(meet, mine) * 100:0}cm");
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

        /// <summary>
        /// 边缘手势（11-27 用户：交互要仔细考虑手眼识别效果、好不好操作，别出现难识别 / 难操作）：
        /// 模拟真机上常见的“不标准”操作，看还认不认：松手瞬间跟丢 0.5 秒、跟丢 1.5 秒（应取消）、捏在两格弹中间、
        /// 拍火偏开 6 cm、按暂停木签偏 3.5 cm、看偏 3° 捏“继续”、手离按钮 8 cm 隔空按。
        /// </summary>
        public static string EdgeGestures() { Level(1); Quiet(); D.StartCoroutine(EdgeRun()); return "edge started"; }

        static System.Collections.IEnumerator EdgeRun()
        {
            void E(string m) => Debug.Log("[边缘] " + m);
            var sling = D.sling;
            yield return new WaitForSeconds(2.5f);
            D.catapult.Stop();
            var bow = HeadPt(0.38f, -0.12f, -0.22f);
            SimHands.Pose(0, bow, true); yield return new WaitForSeconds(0.3f);
            // 1) 拉开后右手跟丢 0.5 秒，重新出现时已张开 → 应发射
            for (int round = 0; round < 2; round++)
            {
                float lost = round == 0 ? 0.5f : 1.5f;
                int shots0 = D.stats.shots;
                var pr = sling.PouchRest;
                SimHands.Pose(1, pr, false); yield return null;
                SimHands.Pose(1, pr, true); yield return null; yield return null;
                var back = sling.ForkCenter + (D.head.transform.position - sling.ForkCenter).normalized * 0.2f;
                yield return Move(1, pr, back, 0.4f, true);
                float p01 = sling.pull01;
                SimHands.Hide(1);
                yield return new WaitForSeconds(lost);
                SimHands.Pose(1, back, false); yield return null; yield return null;
                E($"跟丢 {lost:0.0}s 后张手出现：拉力 {p01:0.00} 射出={D.stats.shots > shots0}（期望 {(round == 0 ? "射出" : "不射、取消")}） state={sling.state}");
                SimHands.Hide(1);
                yield return new WaitForSeconds(2.5f);   // 等皮兜回弹、冷却过去
                SimHands.Pose(0, bow, true); yield return new WaitForSeconds(0.3f);
            }
            // 2) 换弹：捏在石丸格和火丸格中间、略偏火丸 → 应选火丸
            D.ammo.Add(Ammo.Fire, 2);
            var box = Object.FindAnyObjectByType<AmmoBox>();
            if (box)
            {
                var mid = Vector3.Lerp(box.SlotPos(Ammo.Stone), box.SlotPos(Ammo.Fire), 0.56f) + Vector3.up * 0.012f;
                SimHands.Pose(1, mid + Vector3.up * 0.03f, false); yield return null;
                SimHands.Pose(1, mid, true); yield return null; yield return null;
                SimHands.Pose(1, mid, false); yield return null;
                E($"捏在两格中间（偏火丸）：选中 {D.ammo.selected}（期望 Fire）");
                SimHands.Hide(1);
            }
            // 3) 拍火：掌心偏开 6 cm 往下拍两下 → 应灭
            var mine = D.arena.mine;
            var burn = mine.blocks.Find(b => b && b.Wood && !b.Dead && !b.isKing);
            burn.Ignite();
            yield return new WaitForSeconds(0.6f);
            var r = Vector3.Cross(Vector3.up, D.arena.forward);
            var top = burn.WorldBounds.center + Vector3.up * (burn.WorldBounds.extents.y + 0.05f) + r * 0.06f;
            for (int i = 0; i < 2; i++)
            {
                yield return Move(1, top + Vector3.up * 0.1f, top + Vector3.up * 0.1f, 0.2f, false);
                yield return Move(1, top + Vector3.up * 0.1f, top - Vector3.up * 0.02f, 0.08f, false);
                yield return new WaitForSeconds(0.2f);
            }
            E($"拍火偏 6 cm：还在烧={burn.burning}（期望 False）");
            SimHands.Hide(1); SimHands.Hide(0);
            yield return new WaitForSeconds(1.5f);
            // 4) 暂停木签：手偏 3.5 cm 捏 → 应暂停
            WoodButton pb = null; foreach (var x in WoodButton.All) if (x && x.Key == "暂停") pb = x;
            if (pb)
            {
                var pp = pb.ClosestPoint(pb.transform.position - D.arena.forward * 0.1f) + r * 0.035f;
                SimHands.Pose(1, pp - D.arena.forward * 0.03f, false); yield return null; yield return null;
                SimHands.Pose(1, pp, true); yield return null; yield return null;
                SimHands.Pose(1, pp, false); yield return new WaitForSecondsRealtime(1.2f);
                SimHands.Hide(1);
                E($"暂停木签偏 3.5 cm 捏：暂停={D.paused}（期望 True）");
            }
            else E("找不到暂停木签");
            yield return new WaitForSecondsRealtime(1.5f);
            // 5) 看偏 3° + 右手在身侧远处捏 → “继续”
            WoodButton cb = null; foreach (var x in WoodButton.All) if (x && x.Key == "继续") cb = x;
            if (cb)
            {
                var eye = D.head.transform.position;
                var dir = Quaternion.AngleAxis(3f, Vector3.up) * (cb.transform.position - eye).normalized;
                HandInput.SimGaze = new Ray(eye, dir);
                yield return null; yield return null;
                var side = HeadPt(0.3f, 0.3f, -0.35f);
                SimHands.Pose(1, side, false); yield return null; yield return null;
                SimHands.Pose(1, side, true); yield return null; yield return null;
                SimHands.Pose(1, side, false);
                yield return new WaitForSecondsRealtime(1.6f);
                HandInput.SimGaze = null; SimHands.Hide(1);
                E($"看偏 3° 捏“继续”：暂停={D.paused}（期望 False）");
            }
            else E("找不到“继续”");
            E("完成");
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
            yield return new WaitForSeconds(2.5f);
            L("松手 2.5 秒后 " + State() + $" 敌旗倒因={D.arena.enemy.kingDownReason} 挨打{D.arena.enemy.kingHits}次 耐久{D.arena.enemy.kingHP}");
            var hold = GameObject.Find("Pouch").transform.position;
            SimHands.Pose(1, hold, false);
            yield return null; SimHands.Hide(1);
            // 换弹：右手去弹盒里捏火丸那一格（左手还举着弓）
            { float w = 0; while (tut.step < 5 && w < 10f) { w += Time.deltaTime; yield return null; } }
            yield return new WaitForSeconds(2.2f);
            SnapPlayer(dir, "5a_换弹"); L("换弹提示");
            var fslot = D.sling.Box.SlotPos(Ammo.Fire);
            SimHands.Pose(1, fslot + Vector3.up * 0.03f, false); yield return null; yield return null;
            SimHands.Pose(1, fslot, true); yield return null; yield return null;
            SimHands.Pose(1, fslot, false); yield return new WaitForSeconds(0.3f); SimHands.Hide(1);
            L($"换弹后 选中={D.ammo.selected}");
            SimHands.Pose(0, bow(), false); yield return null; SimHands.Hide(0);   // 放下弓，免得左手挡弹
            // 挡、接：右手追着敌弹（张手 / 握拳）
            for (int phase = 6; phase <= 7; phase++)
            {
                float wait = 0;
                while (tut.step < phase && wait < 15f) { wait += Time.deltaTime; yield return null; }
                Projectile p = null;
                wait = 0;
                while (p == null && wait < 10f) { p = Projectile.Live.Find(x => x && x.from == Side.Enemy && x.state == Projectile.State.Flying); wait += Time.deltaTime; yield return null; }
                float apex = 0f, ft = 0f, minC = 9f, minH = 9f; bool snapped = false; Vector3 lastP = default;
                while (p && p.state == Projectile.State.Flying)
                {
                    ft += Time.deltaTime;
                    if (!snapped && ft > 0.7f)
                    {
                        snapped = true;
                        SnapPlayer(dir, phase == 6 ? "6_挡" : "7_接"); L(phase == 6 ? "敌弹飞来（挡）" : "敌弹飞来（接）");
                        var gh = GameObject.Find("GhostHand_R");
                        L($"示范右手 active={(gh ? gh.activeInHierarchy : false)} pos={(gh ? gh.GetComponentInChildren<SkinnedMeshRenderer>().bounds.center.ToString("F2") : "-")} 弹={p.transform.position.ToString("F2")}");
                    }
                    apex = Mathf.Max(apex, p.transform.position.y);
                    // 手在城的正上方等着（像真人一样迎着弹道），弹飞近了再追过去
                    var c = D.arena.mine.transform.position + Vector3.up * 0.18f;
                    minC = Mathf.Min(minC, Vector3.Distance(p.transform.position, c)); minH = Mathf.Min(minH, Vector3.Distance(p.transform.position, HandInput.Instance.hands[1].palm)); lastP = p.transform.position;
                    var hp = Vector3.Distance(p.transform.position, c) < 0.6f ? p.transform.position + (t.position - p.transform.position).normalized * 0.05f : c;
                    SimHands.Pose(1, hp, false, phase == 7, (t.position - hp).normalized * -1f);
                    yield return null;
                }
                L($"  诊断：弹离城上方点最近 {minC * 100:0}cm、离手最近 {minH * 100:0}cm、最后位置 {lastP:F2}、弹状态 {(p ? p.state.ToString() : "已销毁")}、phase={D.phase}、手 tracked={HandInput.Instance.hands[1].tracked} pinch={HandInput.Instance.hands[1].pinch}");
                yield return new WaitForSeconds(0.8f);
                SimHands.Hide(1);
                L($"第{phase}步结果 挡{D.stats.blocked} 接{D.stats.caught} 敌弹最高 {apex:0.00}m（从被看到起）");
            }
            // 修城
            { float w = 0; while (tut.step < 8 && w < 15f) { w += Time.deltaTime; yield return null; } }
            yield return new WaitForSeconds(4.5f);
            SnapPlayer(dir, "8_修城"); L("缺口出现后");
            var piece = GameObject.Find("Patch").transform.position + Vector3.up * 0.017f;
            SimHands.Pose(1, piece, true); yield return null; yield return null;
            int slot = D.arena.mine.layout.items.FindIndex(x => x.piece == "W_Battlement");
            var to = D.arena.mine.SlotCenter(slot) + Vector3.up * 0.02f;
            for (float k = 0; k < 1f; k += Time.deltaTime / 0.6f) { SimHands.Pose(1, Vector3.Lerp(piece, to, k), true); yield return null; }
            SimHands.Pose(1, to, false); yield return null; SimHands.Hide(1);
            yield return new WaitForSeconds(3f);
            L($"修城后 补={D.stats.repaired}");
            // 灭火：找到着火的块，往下拍两下
            { float w = 0; while (tut.step < 9 && w < 10f) { w += Time.deltaTime; yield return null; } }
            yield return new WaitForSeconds(2.0f);
            SnapPlayer(dir, "9_灭火"); L("着火提示");
            var fb = D.arena.mine.blocks.Find(b => b && b.burning);
            if (fb)
            {
                var top = fb.WorldBounds.center + Vector3.up * (fb.WorldBounds.extents.y + 0.06f);
                for (int i = 0; i < 2; i++)
                {
                    for (float k = 0; k < 1f; k += Time.deltaTime / 0.25f) { SimHands.Pose(1, top + Vector3.up * 0.08f, false); yield return null; }
                    for (float k = 0; k < 1f; k += Time.deltaTime / 0.07f) { SimHands.Pose(1, Vector3.Lerp(top + Vector3.up * 0.08f, top - Vector3.up * 0.03f, k), false); yield return null; }
                    yield return new WaitForSeconds(0.25f);
                }
                SimHands.Hide(1);
            }
            L($"灭火后 还在烧={(fb ? fb.burning : false)} 灭火数={D.stats.extinguished}");
            yield return new WaitForSeconds(1.6f);
            SnapPlayer(dir, "9b_学会啦"); L("学会啦");
            { float w = 0; while (tut.step < 10 && w < 10f) { w += Time.deltaTime; yield return null; } }
            yield return new WaitForSeconds(1.0f);
            SnapPlayer(dir, "10_开打"); L($"开打 敌方帅旗耐久={D.arena.enemy.kingHP} 计时暂停={D.timerPaused} 剩{D.timeLeft:0}s");
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
                if (n.StartsWith("Room") || n.StartsWith("Ceil")) { r.gameObject.SetActive(false); hidden.Add(r.gameObject); }
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
            bool cleared0 = Progress.Cleared; Progress.Cleared = false;   // 通关后主按钮是“从头再来”，这里测“继续游戏”
            D.GoTitle();
            yield return new WaitForSeconds(2.4f);   // 封面出现过程约 2 秒，期间木签不响应
            Debug.Log($"[流程] 标题 phase={D.phase} 按钮={WoodButton.All.Count}");
            yield return PressButton(Progress.Unlocked == 0 ? "开始游戏" : "继续游戏");
            float w0 = Time.time;   // 没有真实桌子时先搭虚拟桌（约 1.3 秒）再开局
            while (D.phase != Director.Phase.Battle && Time.time - w0 < 3f) yield return null;
            yield return new WaitForSeconds(0.5f);
            Debug.Log($"[流程] 捏“继续”后 phase={D.phase} 关={D.levelIndex + 1}（按下到开局 {Time.time - w0 - 0.5f:0.0}s）");
            if (D.phase != Director.Phase.Battle) { Debug.Log("[流程] 失败：没进入对战"); Debug.Log("[流程] 完成"); yield break; }
            Quiet();
            yield return new WaitForSeconds(5f);
            // 直接让敌方帅旗耐久打光（模拟打赢）
            D.arena.enemy.DamageKing(99, D.arena.enemy.transform.position - D.arena.forward);
            yield return new WaitForSeconds(7f);
            Debug.Log($"[流程] 赢后 phase={D.phase} 结算板={(GameObject.Find("ResultBoard") != null)} 星={Progress.Stars(D.levelIndex)} 解锁到={Progress.Unlocked + 1}");
            var rb = GameObject.Find("ResultBoard");
            if (rb) { var rr = rb.transform.Find("Result"); SnapPlayerLook(dir, "结算_胜利", rr ? rr.position + Vector3.down * 0.17f : rb.transform.position); if (rr) { Snap(dir, "结算_胜利_近看", D.head.transform.position, rr.position + Vector3.down * 0.17f, 38); Snap(dir, "结算_胜利_头显像素", D.head.transform.position, rr.position + Vector3.down * 0.17f, 50.5f); } }
            yield return PressButton("下一关");
            yield return new WaitForSeconds(1.0f);
            Debug.Log($"[流程] 捏“下一关”后 phase={D.phase} 关={D.levelIndex + 1}");
            Quiet();
            yield return new WaitForSeconds(5f);
            D.arena.mine.DamageKing(99, D.arena.mine.transform.position + D.arena.forward);
            yield return new WaitForSeconds(6.5f);
            rb = GameObject.Find("ResultBoard");
            if (rb) { var rr = rb.transform.Find("Result"); SnapPlayerLook(dir, "结算_失守", rr ? rr.position + Vector3.down * 0.17f : rb.transform.position); if (rr) { Snap(dir, "结算_失守_近看", D.head.transform.position, rr.position + Vector3.down * 0.17f, 38); Snap(dir, "结算_失守_头显像素", D.head.transform.position, rr.position + Vector3.down * 0.17f, 50.5f); } }
            Debug.Log($"[流程] 输后 phase={D.phase} 结算板={(rb != null)}");
            yield return PressButton("回标题");
            yield return new WaitForSeconds(1.0f);
            Debug.Log($"[流程] 捏“回标题”后 phase={D.phase}");
            Progress.Cleared = cleared0;
            Debug.Log("[流程] 完成");
        }

        /// <summary>
        /// 结局走查：第十关打倒王城帅旗 → 按时间点连拍（塌城 / 满天烟花 / 天亮 / 终章横木，含头显像素密度）→ 捏“回标题”→ 拍封面，
        /// 查主按钮是不是“从头再来”。跑完把存档里的通关标记 / 解锁进度还原。
        /// </summary>
        public static string EndingCheck(string dir) { foreach (var dp in Object.FindObjectsByType<DesktopPlay>()) Object.Destroy(dp); D.StartCoroutine(EndingRun(dir)); return "ending started"; }

        static System.Collections.IEnumerator EndingRun(string dir)
        {
            bool cleared0 = Progress.Cleared; int unlocked0 = Progress.Unlocked;
            SimHands.scripted = true; SimHands.Hide(0); SimHands.Hide(1);
            Level(Levels.Count - 1); Quiet();
            var t = D.head.transform;
            t.rotation = Quaternion.LookRotation(D.arena.forward) * Quaternion.Euler(4, 0, 0);
            yield return new WaitForSeconds(3f);
            SnapPlayer(dir, "E0_开打前");
            D.arena.enemy.DamageKing(99, D.arena.enemy.transform.position - D.arena.forward);
            Debug.Log($"[结局] 打倒王城 phase={D.phase} ending={D.ending}");
            float t0 = Time.time;
            // 抬头看天花板：从玩家位置往前上方 55°、广角（头显视场约 100°）
            var up = t.position + (D.arena.forward * 0.55f + Vector3.up).normalized * 3f;
            foreach (var (at, name, sky) in new[] { (1.8f, "E1_晃", false), (3.8f, "E2_一层层垮", false), (6.6f, "E3_开口扩开中_抬头", true), (9.8f, "E4_满天烟花_抬头", true),
                                                    (11.5f, "E5_散去中", false), (16.5f, "E6_天亮光柱_抬头", true), (18.0f, "E7_地上空了_光柱", false) })
            {
                while (Time.time - t0 < at) yield return null;
                if (sky) { Snap(dir, name, t.position, up, 100f); SnapRoomless(dir, name + "_头显模拟", t.position, up, 100f); }
                else SnapPlayer(dir, name);
                Debug.Log($"[结局] {at:0.0}s {name} dawn={D.sky.dawn:0.00} 开口扩开={D.sky.Expanded} 敌城完整={D.arena.enemy.Integrity:0.00}");
            }
            while (Time.time - t0 < 23.5f) yield return null;   // 终章横木约 20 s 落下、1 秒多垂到位才能按
            var rb = GameObject.Find("ResultBoard");
            Debug.Log($"[结局] 终章横木={(rb != null)} 通关={Progress.Cleared} 总星={Progress.TotalStars} 按钮={string.Join("/", WoodButton.All.Where(b => b).Select(b => b.Key))}");
            if (rb)
            {
                var rr = rb.transform.Find("Result");
                var at2 = rr ? rr.position + Vector3.down * 0.12f : rb.transform.position;
                SnapPlayerLook(dir, "E7_终章", at2);
                Snap(dir, "E7_终章_头显像素", t.position, at2, 50.5f);
            }
            yield return PressButton("回标题");
            yield return new WaitForSeconds(3f);
            Debug.Log($"[结局] 回标题后 phase={D.phase} 封面按钮={string.Join("/", WoodButton.All.Where(b => b).Select(b => b.Key))} dawn={D.sky.dawn:0.00} ending={D.ending}");
            t.rotation = Quaternion.LookRotation(D.arena.forward) * Quaternion.Euler(10, 0, 0);
            SnapPlayer(dir, "E8_封面");
            Progress.Cleared = cleared0; Progress.Unlocked = unlocked0;
            Debug.Log("[结局] 完成");
        }

        /// <summary>暂停走查：开第 3 关 → 捏左边的“暂停”木签 → 时间冻住、在飞的弹不动 → 拍暂停板 → 捏“继续”→ 恢复；再暂停 → “回标题”</summary>
        public static string PauseCheck(string dir) { foreach (var dp in Object.FindObjectsByType<DesktopPlay>()) Object.Destroy(dp); D.StartCoroutine(PauseRun(dir)); return "pause started"; }

        static System.Collections.IEnumerator PressRT(string zh)
        {
            WoodButton b = null;
            foreach (var x in WoodButton.All) if (x && x.Key == zh && x.interactable) b = x;
            if (!b) { Debug.Log("[暂停] 找不到可按的按钮 " + zh); yield break; }
            var p = b.ClosestPoint(b.transform.position - D.arena.forward * 0.1f);
            SimHands.scripted = true;
            SimHands.Pose(1, p - D.arena.forward * 0.02f, false); yield return null; yield return null;
            SimHands.Pose(1, p, true); yield return null; yield return null;
            SimHands.Pose(1, p, false); yield return new WaitForSecondsRealtime(0.4f);
            SimHands.Hide(1);
        }

        static System.Collections.IEnumerator PauseRun(string dir)
        {
            SimHands.scripted = true; SimHands.Hide(0); SimHands.Hide(1);
            Level(2);
            var t = D.head.transform;
            t.rotation = Quaternion.LookRotation(D.arena.forward) * Quaternion.Euler(14, 0, 0);
            yield return new WaitForSecondsRealtime(4f);
            SnapPlayer(dir, "P1_对局中_左边暂停木签");
            Shoot(Ammo.Stone, 3, 5.5f, false);
            yield return new WaitForSecondsRealtime(0.35f);
            var pr = Projectile.Live.Find(x => x);
            yield return PressRT("暂停");
            yield return new WaitForSecondsRealtime(0.3f);
            var p0 = pr ? pr.transform.position : Vector3.zero; float left0 = D.timeLeft;
            yield return new WaitForSecondsRealtime(2.5f);
            Debug.Log($"[暂停] 按下后 paused={D.paused} timeScale={Time.timeScale} 弹移动={(pr ? Vector3.Distance(p0, pr.transform.position) : -1):0.0000}m 计时变化={left0 - D.timeLeft:0.00}s 暂停板={(GameObject.Find("PauseBoard") != null)}");
            var pb = GameObject.Find("PauseBoard/Root");
            if (pb)
            {
                var at = pb.transform.position + Vector3.down * 0.09f;
                SnapPlayerLook(dir, "P2_暂停板", at);
                Snap(dir, "P2_暂停板_头显像素", t.position, at, 50.5f);
                // 每块手势卡近拍一个完整循环（6 帧）：逐张核对动作和道具
                foreach (var kind in new[] { "Raise", "Draw", "Swap", "Block", "Catch", "Repair", "PatOut" })
                {
                    var g = GameObject.Find("Gesture_" + kind);
                    if (!g) continue;
                    var card = g.transform.Find("Swing/Card");
                    var c = card.position + card.up * -0.04f;
                    var from = c - card.forward * 0.32f;
                    for (int k = 0; k < 6; k++) { Snap(dir, $"P3_{kind}_{k}", from, c, 15f); yield return new WaitForSecondsRealtime(kind == "Draw" ? 0.47f : 0.37f); }
                }
            }
            yield return PressRT("继续");
            yield return new WaitForSecondsRealtime(0.5f);
            Debug.Log($"[暂停] 继续 0.5 秒后 paused={D.paused} timeScale={Time.timeScale:0.00}");
            yield return new WaitForSecondsRealtime(1.5f);
            Debug.Log($"[暂停] 继续 2 秒后 paused={D.paused} timeScale={Time.timeScale:0.00} 暂停板={(GameObject.Find("PauseBoard") != null)} phase={D.phase}");
            yield return PressRT("暂停");
            yield return new WaitForSecondsRealtime(1.8f);
            yield return PressRT("回标题");
            yield return new WaitForSecondsRealtime(1.5f);
            Debug.Log($"[暂停] 回标题后 phase={D.phase} paused={D.paused} timeScale={Time.timeScale} 暂停板={(GameObject.Find("PauseBoard") != null)}");
            Debug.Log("[暂停] 完成");
        }

        /// <summary>
        /// 真实房间合成用：藏掉编辑器的占位房间（墙 / 地板 / 天花板 / 桌子），相机清成全透明，按头显视场截带透明通道的 PNG。
        /// 头显上透视画面就是背景，所以这张图叠到房间照片上 ≈ 头显里看到的样子（天空只在盒盖开口里出现）。
        /// 依次：清晨（第 1 关）/ 上午（第 4 关）/ 午后（第 6 关）/ 黄昏（第 7 关）/ 入夜（第 9 关）/ 烟火夜（第 10 关）/ 结局日出。
        /// </summary>
        public static string RealRoom(string dir, float pitch = 8f, float vfov = 68f) { foreach (var dp in Object.FindObjectsByType<DesktopPlay>()) Object.Destroy(dp); D.StartCoroutine(RealRun(dir, pitch, vfov)); return "realroom started"; }

        static string SnapAlpha(string dir, string name, float pitch, float vfov)
        {
            var hidden = new System.Collections.Generic.List<GameObject>();
            foreach (var r in Object.FindObjectsByType<Renderer>())
            {
                var n = r.name;
                if (n.StartsWith("Room") || n.StartsWith("Ceil")) { r.gameObject.SetActive(false); hidden.Add(r.gameObject); }
            }
            var cam = D.head;
            var bg = cam.backgroundColor; var cf = cam.clearFlags; var fov = cam.fieldOfView; var r0 = cam.transform.rotation;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.fieldOfView = vfov;
            cam.transform.rotation = Quaternion.LookRotation(D.arena.forward) * Quaternion.Euler(pitch, 0, 0);
            int w = 1920, h = 1080;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = null;
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, name + ".png");
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex); rt.Release();
            cam.clearFlags = cf; cam.backgroundColor = bg; cam.fieldOfView = fov; cam.transform.rotation = r0;
            foreach (var g in hidden) if (g) g.SetActive(true);
            return path;
        }

        static System.Collections.IEnumerator RealRun(string dir, float pitch, float vfov)
        {
            SimHands.scripted = true; SimHands.Hide(0); SimHands.Hide(1);
            var shots = new (int lv, string name)[] { (0, "R1_清晨_第1关"), (3, "R2_上午_第4关"), (5, "R3_午后_第6关"), (6, "R4_黄昏_第7关"), (8, "R5_入夜_第9关"), (9, "R6_烟火夜_第10关") };
            foreach (var (lv, name) in shots)
            {
                D.StartLevel(lv); Quiet();
                foreach (var c in D.GetComponents<Tutorial>()) Object.Destroy(c);
                D.arena.enemy.kingLocked = false; D.arena.mine.kingLocked = false;
                yield return new WaitForSeconds(lv == 9 ? 6.5f : 5f);   // 城搭好、盒盖掀开；第十关等烟花放起来
                Debug.Log("[真实房间] " + SnapAlpha(dir, name, pitch, vfov));
            }
            // 结局：打倒王城 → 等天亮
            D.arena.enemy.DamageKing(99, D.arena.enemy.transform.position - D.arena.forward);
            yield return new WaitForSeconds(3.0f);
            Debug.Log("[真实房间] " + SnapAlpha(dir, "R7_结局_烟花", pitch, vfov));
            yield return new WaitForSeconds(10.0f);
            Debug.Log("[真实房间] " + SnapAlpha(dir, "R8_结局_天亮", pitch, vfov));
            Progress.Cleared = false;
            Debug.Log("[真实房间] 完成");
        }

        /// <summary>11-27 用户反馈逐项核对：新弹提示木牌（第 3 关火丸）、封面弹道线、天窗转角近景、结局光柱</summary>
        public static string Check1127(string dir) { foreach (var dp in Object.FindObjectsByType<DesktopPlay>()) Object.Destroy(dp); D.StartCoroutine(Check1127Run(dir)); return "check started"; }

        static System.Collections.IEnumerator Check1127Run(string dir)
        {
            SimHands.scripted = true; SimHands.Hide(0); SimHands.Hide(1);
            var t = D.head.transform;
            // 封面弹道线
            D.GoTitle();
            yield return new WaitForSeconds(3.5f);
            var cv = GameObject.Find("CoverMenu/Cover");
            t.rotation = Quaternion.LookRotation(D.arena.forward) * Quaternion.Euler(4, -12, 0);
            SnapPlayer(dir, "A1_封面_头显视场");
            Snap(dir, "A2_封面弹道_头显像素", t.position, cv.transform.position + Vector3.up * 0.6f - Vector3.Cross(Vector3.up, D.arena.forward) * 0.55f, 50.5f);
            // 新弹提示（第 3 关火丸）
            Castle.InstantBuild = true; D.StartLevel(2); Castle.InstantBuild = false;
            t.rotation = Quaternion.LookRotation(D.arena.forward) * Quaternion.Euler(14, 0, 0);
            yield return new WaitForSeconds(3.0f);
            var ht = GameObject.Find("HintTag");
            if (ht)
            {
                var tag = ht.transform.Find("Hang/Tag") ?? ht.transform;
                Snap(dir, "B1_新弹木牌_头显像素", t.position, tag.position + Vector3.down * 0.06f, 50.5f);
                SnapPlayer(dir, "B2_新弹木牌_全景");
            }
            // 天窗转角（左前角，抬头看）
            yield return new WaitForSeconds(3.0f);
            var lidC = D.sky.LidPoint(0, 0); var half = D.sky.LidHalfSize;
            var corner = D.sky.LidPoint(-half.x, half.y * 0.2f);
            Snap(dir, "C1_天窗边", t.position, corner, 30f);
            Snap(dir, "C2_天窗转角", t.position, D.sky.LidPoint(-half.x, -half.y * 0.75f), 22f);
            Snap(dir, "C3_天窗抬头", t.position, lidC, 90f);
            Debug.Log("[核对] 完成");
        }

        /// <summary>11-27 版式核对：提示木牌（一行 / 两行 / 长句）正面近拍 + 菜单木签正面近拍</summary>
        public static string TagLayout(string dir) { foreach (var dp in Object.FindObjectsByType<DesktopPlay>()) Object.Destroy(dp); D.StartCoroutine(TagLayoutRun(dir)); return "tags started"; }

        static System.Collections.IEnumerator TagLayoutRun(string dir)
        {
            D.GoTitle();
            yield return new WaitForSeconds(3.5f);
            var t = D.head.transform;
            var tags = Object.FindObjectsByType<HangTag>();
            if (tags.Length > 0)
            {
                var tg = tags[0].transform.Find("Swing/Tag/Look");
                Snap(dir, "T1_菜单木签_正面", tg.position - tg.forward * 0.25f + tg.up * -0.022f, tg.position + tg.up * -0.022f, 30f);
            }
            var ht = HintTag.Build(t);
            ht.pin = t.position + D.arena.forward * 0.5f;
            foreach (var (txt, name) in new[] { ("学会啦！", "H1_一行"), ("右手捏弹盒\n换上火丸", "H2_两行"), ("新弹：分裂丸\n空中一分为三", "H3_长句") })
            {
                ht.Show(txt);
                yield return new WaitForSeconds(1.2f);
                var tag = ht.transform.Find("Hang/Tag");
                var c = tag.TransformPoint(new Vector3(0, -0.014f, 0));
                Snap(dir, name, c - tag.forward * 0.45f, c, 16f);
            }
            Object.Destroy(ht.gameObject);
            Debug.Log("[版式] 完成");
        }

        /// <summary>
        /// 机关逐关验证（11-27）：每个机关关先截全景、再按设计的打法打（瞄准机关本身），记录机关是否触发、帅旗是否倒、城还剩多少；
        /// 打法：5 草垛 = 火丸打最左草垛；6 火药桶 = 火丸打左桶；8 水桶 = 冰丸打桶 + 石丸打小堡；9 冰池 = 冰丸打池；
        /// 11 连环 = 炸丸打第一只桶；12 拱桥 = 炸丸打拱心石；14 三钟 = 分裂丸打中间那口钟 + 石丸打帅旗；15 吊秤 = 分裂丸打右秤盘
        /// </summary>
        public static string MechCheck(string dir) { foreach (var dp in Object.FindObjectsByType<DesktopPlay>()) Object.Destroy(dp); D.StartCoroutine(MechRun(dir)); return "mech started"; }

        static int Find(string piece, int nth = 0)
        {
            var e = D.arena.enemy; int k = 0;
            for (int i = 0; i < e.blocks.Count; i++) if (e.blocks[i] && e.blocks[i].def.id == piece) { if (k == nth) return i; k++; }
            return -1;
        }

        public static int MechBlown;
        static string MechState()
        {
            var e = D.arena.enemy;
            int hay = 0, hayBurnt = 0, keg = 0, kegGone = 0, frozen = 0;
            foreach (var b in e.blocks) { if (!b) continue; if (b.def.id == "M_Hay") { hay++; if (b.burning || b.Dead) hayBurnt++; } if (b.def.id == "M_Keg") { keg++; if (b.Dead) kegGone++; } if (b.Frozen) frozen++; }
            int blown = Mech.BlownCount;
            return $"草垛 {hayBurnt}/{hay} 着  火药桶 {kegGone}/{keg} 没了（真炸 {blown} 次）  冻住 {frozen} 块  帅旗耐久 {e.kingHP}/{e.kingMaxHP} 倒={e.IsKingDown}  完整 {e.Integrity:0.00}  phase={D.phase}";
        }

        static System.Collections.IEnumerator Fire(Ammo a, int idx, float wait, bool high = false)
        {
            if (idx < 0 && idx != -1) yield break;
            Shoot(a, idx, 5.5f, high);
            yield return new WaitForSeconds(wait);
        }

        /// <summary>分裂丸要在飞过最高点后才分开：从更远、更高的地方打，分开后三颗正好落到目标附近</summary>
        static System.Collections.IEnumerator FireAt(Ammo a, Vector3 tgt, float wait)
        {
            var from = D.arena.tableCenter + Vector3.up * 0.35f + D.arena.forward * 0.05f;
            D.DebugShoot(a, from, tgt, 5.5f, a == Ammo.Split);
            yield return new WaitForSeconds(wait);
        }

        static System.Collections.IEnumerator MechRun(string dir)
        {
            SimHands.scripted = true; SimHands.Hide(0); SimHands.Hide(1);
            var t = D.head.transform;
            foreach (int lv in new[] { 4, 5, 7, 8, 10, 11, 13, 14 })
            {
                Level(lv); Quiet();
                foreach (var c in D.GetComponents<LevelTips>()) Object.Destroy(c);
                D.arena.enemy.kingLocked = false;
                t.rotation = Quaternion.LookRotation(D.arena.forward) * Quaternion.Euler(12, 0, 0);
                yield return new WaitForSeconds(2.0f);
                var ov = Levels.Overlaps(Levels.Get(lv).enemy);
                Debug.Log($"[机关] 第{lv + 1}关 {D.level.title} 块数 {D.arena.enemy.blocks.Count} 重叠 {ov.Count}{(ov.Count > 0 ? " " + ov[0] : "")}");
                SnapEnemy(dir, $"M{lv + 1:00}_a_城");
                switch (lv)
                {
                    case 4: yield return Fire(Ammo.Fire, Find("M_Hay", 0), 9f); break;
                    case 5: yield return Fire(Ammo.Fire, Find("M_Keg", 0), 4f); yield return Fire(Ammo.Fire, Find("M_Keg", 1), 4f); break;
                    case 7: yield return Fire(Ammo.Ice, Find("M_Bucket"), 1.5f); yield return Fire(Ammo.Stone, -1, 2f, true); yield return Fire(Ammo.Stone, Find("S_Cube", 3), 3f); break;
                    case 8: { var pond = D.arena.enemy.blocks[Find("M_Trough")]; yield return FireAt(Ammo.Ice, pond.WorldBounds.center - D.arena.forward * pond.WorldBounds.extents.z * 0.75f, 6f); break; }
                    case 10: yield return Fire(Ammo.Bomb, Find("M_Keg", 0), 5f); break;
                    case 11: yield return Fire(Ammo.Bomb, Find("M_Keystone"), 5f); break;
                    case 13: { var bells = D.arena.enemy.blocks.FindAll(b => b && b.def.id == "M_Bell"); var mid = bells.OrderBy(b => Mathf.Abs(b.transform.localPosition.x)).First();
                               yield return FireAt(Ammo.Split, mid.WorldBounds.center, 4f); yield return Fire(Ammo.Stone, -1, 4f, true); break; }
                    case 14: { var beam = D.arena.enemy.blocks[Find("M_ScaleBeam")].transform; var pan = beam.TransformPoint(new Vector3(2.0f, -1.3f, 0) * BlockLibrary.G);
                               // 分裂丸两发 + 石丸一发，都打 B 盘（压够 3 发重就翻）
                               yield return FireAt(Ammo.Split, pan, 2.5f); yield return FireAt(Ammo.Split, pan, 2.5f); yield return FireAt(Ammo.Stone, pan, 4f); break; }
                }
                SnapEnemy(dir, $"M{lv + 1:00}_b_触发后");
                Debug.Log($"[机关] 第{lv + 1}关 结果：{MechState()}");
            }
            Debug.Log("[机关] 完成");
        }

        /// <summary>吊秤单测（按游戏时间）：单发最大角度、回弹；隔 1.6 秒两发不翻；0.25 秒三发掀翻帅旗</summary>
        public static string ScaleCheck() { D.StartCoroutine(ScaleRun()); return "scale started"; }
        static System.Collections.IEnumerator ScaleRun()
        {
            Level(14); Quiet();
            foreach (var c in D.GetComponents<LevelTips>()) Object.Destroy(c);
            yield return new WaitForSeconds(2f);
            var e = D.arena.enemy; var m = e.blocks.Find(b => b && b.def.id == "M_ScaleBeam").mech;
            m.DebugKick(1); yield return new WaitForSeconds(2.5f);
            Debug.Log($"[秤测] 1 发重：停在 {m.ScaleAngle:0.0}° 翻={m.ScaleLaunched}");
            m.DebugKick(1); yield return new WaitForSeconds(2.5f);
            Debug.Log($"[秤测] 2 发重：停在 {m.ScaleAngle:0.0}° 翻={m.ScaleLaunched}");
            m.DebugKick(1); yield return new WaitForSeconds(4f);
            Debug.Log($"[秤测] 3 发重：翻={m.ScaleLaunched} 帅旗倒={e.IsKingDown} phase={D.phase}");
            Debug.Log("[秤测] 完成");
        }

        /// <summary>量分裂丸三颗的落点散开宽度（打第 14 关钟架中间，低弧 / 高弧各一次）</summary>
        public static string SplitSpread() { D.StartCoroutine(SplitRun()); return "split started"; }
        static System.Collections.IEnumerator SplitRun()
        {
            Level(13); Quiet();
            foreach (var c in D.GetComponents<LevelTips>()) Object.Destroy(c);
            yield return new WaitForSeconds(2f);
            var e = D.arena.enemy; var bell = e.blocks.Find(b => b && b.def.id == "M_Bell" && Mathf.Abs(b.transform.localPosition.x) < 0.01f);
            var tgt = bell.WorldBounds.center;
            var pts = new System.Collections.Generic.List<Vector3>();
            System.Action<Ammo, Vector3, Block, Side> h = (a, p, b, s) => { if (s == Side.Player) pts.Add(p); };
            Projectile.Impacted += h;
            foreach (bool high in new[] { false, true })
            {
                pts.Clear();
                var from = D.arena.tableCenter + Vector3.up * 0.35f + D.arena.forward * 0.05f;
                D.DebugShoot(Ammo.Split, from, tgt, 5.5f, high);
                yield return new WaitForSeconds(4f);
                var r = Vector3.Cross(Vector3.up, D.arena.forward);
                var xs = pts.Select(p => Vector3.Dot(p - tgt, r) / e.unit).OrderBy(x => x).ToArray();
                Debug.Log($"[分裂] {(high ? "高弧" : "低弧")} 落点 {pts.Count} 颗，相对中间那口钟横向（格）：{string.Join(", ", xs.Select(x => x.ToString("0.0")))}");
            }
            Projectile.Impacted -= h;
            Debug.Log("[分裂] 完成");
        }

        /// <summary>冰池单测：冰丸打池子后每 0.5 秒记录楼脚 / 帅旗的位置、是否醒着</summary>
        public static string PondCheck() { D.StartCoroutine(PondRun()); return "pond started"; }
        static System.Collections.IEnumerator PondRun()
        {
            Level(8); Quiet();
            foreach (var c in D.GetComponents<LevelTips>()) Object.Destroy(c);
            yield return new WaitForSeconds(2f);
            var e = D.arena.enemy;
            var pond = e.blocks.Find(b => b && b.def.id == "M_Trough");
            var legs = e.blocks.FindAll(b => b && b.def.id == "W_Pillar" && b.homePos.y - e.transform.position.y < 1f * e.unit);
            var front = pond.WorldBounds.center - D.arena.forward * pond.WorldBounds.extents.z * 0.75f;   // 楼前面露出来的那片水面
            D.DebugShoot(Ammo.Ice, D.arena.tableCenter + Vector3.up * 0.35f, front, 5.5f, false);
            for (float t = 0; t < 6f; t += 0.5f)
            {
                yield return new WaitForSeconds(0.5f);
                Debug.Log($"[冰池] {t + 0.5f:0.0}s 池冻={pond.Frozen} 楼脚 {string.Join(" / ", legs.Select(l => $"位移 {l.Displacement:0.00} 醒={l.Dynamic}"))} 帅旗 位移 {e.king.Displacement:0.00} 醒={e.king.Dynamic} 倒={e.IsKingDown}");
            }
            Debug.Log("[冰池] 完成");
        }

        /// <summary>18 关全景：每关玩家视角一张（城搭好、盒盖掀开后）</summary>
        public static string LevelShots(string dir) { foreach (var dp in Object.FindObjectsByType<DesktopPlay>()) Object.Destroy(dp); D.StartCoroutine(LevelShotsRun(dir)); return "shots started"; }
        static System.Collections.IEnumerator LevelShotsRun(string dir)
        {
            SimHands.scripted = true; SimHands.Hide(0); SimHands.Hide(1);
            D.head.transform.rotation = Quaternion.LookRotation(D.arena.forward) * Quaternion.Euler(10, 0, 0);
            for (int i = 0; i < Levels.Count; i++)
            {
                Level(i); Quiet();
                foreach (var c in D.GetComponents<Tutorial>()) Object.Destroy(c);
                foreach (var c in D.GetComponents<LevelTips>()) Object.Destroy(c);
                yield return new WaitForSeconds(4.5f);
                SnapPlayer(dir, $"L{i + 1:00}");
                SnapEnemy(dir, $"L{i + 1:00}_敌城");
            }
            Debug.Log("[全景] 完成");
        }

        /// <summary>从玩家眼睛的位置看向某处（模拟低头看），视场角和头显接近</summary>
        public static string SnapPlayerLook(string dir, string name, Vector3 at) => Snap(dir, name, D.head.transform.position, at, 80);

        /// <summary>把合成的音效和音乐导出成 wav（Docs/音频试听/），并统计峰值 / 均方根 / 开头结尾是否突变</summary>
        public static string ExportAudio()
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Docs/音频试听");
            System.IO.Directory.CreateDirectory(dir);
            var list = new System.Collections.Generic.List<(string, AudioClip)>
            {
                ("木块被打", Synth.WoodHit(1)), ("石块被打", Synth.StoneHit(1)), ("积木倒下", Synth.Clatter()), ("炸丸", Synth.Boom()), ("冰丸", Synth.IceCrack()),
                ("着火", Synth.Fire()), ("灭火", Synth.Hiss()), ("松手发射", Synth.Snap()), ("拉弓吱呀", Synth.Creak()), ("敌人蓄力", Synth.Windup()), ("投石机抛出", Synth.Throw()),
                ("接住叮", Synth.Ding()), ("挡开", Synth.Block()), ("放积木按按钮", Synth.Place()), ("烟花", Synth.Firework()),
            };
            for (int t = 0; t < 6; t++) list.Add(($"音乐_时段{t}", Music.Track(t)));
            var sb = new System.Text.StringBuilder();
            foreach (var (name, c) in list)
            {
                var d = new float[c.samples]; c.GetData(d, 0);
                float peak = 0, rms = 0; int clip = 0;
                foreach (var v in d) { peak = Mathf.Max(peak, Mathf.Abs(v)); rms += v * v; if (Mathf.Abs(v) > 0.999f) clip++; }
                rms = Mathf.Sqrt(rms / d.Length);
                float jumpStart = Mathf.Abs(d[0]), jumpEnd = Mathf.Abs(d[d.Length - 1]);
                WriteWav(System.IO.Path.Combine(dir, name + ".wav"), d, c.frequency);
                sb.AppendLine($"{name}: {c.length:0.00}s 峰值 {peak:0.00} 均方根 {rms:0.000} 削波 {clip} 首样 {jumpStart:0.000} 尾样 {jumpEnd:0.000}");
            }
            return sb.ToString();
        }

        static void WriteWav(string path, float[] d, int rate)
        {
            using var fs = new System.IO.FileStream(path, System.IO.FileMode.Create);
            using var w = new System.IO.BinaryWriter(fs);
            int bytes = d.Length * 2;
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            w.Write(System.Text.Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
            foreach (var v in d) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * 32767), -32768, 32767));
        }

        /// <summary>难度模拟：每关 × 两档 × n 局，加速 timeScale 倍跑真实物理。结果写到日志 [难度] 和 Docs/难度模拟.md</summary>
        public static string Difficulty(int n = 6, float speed = 4f, int from = 0, int to = 9)
        {
            foreach (var dp in Object.FindObjectsByType<DesktopPlay>()) Object.Destroy(dp);
            SimHands.scripted = true; SimHands.Hide(0); SimHands.Hide(1);
            D.StartCoroutine(DiffRun(n, speed, from, to));
            return "difficulty started";
        }

        static System.Collections.IEnumerator DiffRun(int n, float speed, int from, int to)
        {
            var bot = DifficultyBot.Attach(D);
            var lines = new System.Collections.Generic.List<string> { "| 关 | 新手胜率 | 新手输因（倒旗/超时） | 熟练胜率 | 熟练输因（倒旗/超时） | 熟练平均用时 | 我方剩余完整度（新手/熟练） |", "|---|---|---|---|---|---|---|" };
            Time.timeScale = speed;
            for (int lv = from; lv <= to; lv++)
            {
                string a = "", ar = "", am = "", b = "", br = "", bm = "", tb = "";
                yield return bot.Run(lv, DifficultyBot.Novice, n, 100 + lv, r => { Debug.Log("[难度] " + r); });
                a = $"{bot.wins}/{bot.games}"; ar = $"{bot.lostFlag}/{bot.lostTime}"; am = $"{System.Linq.Enumerable.Average(bot.mineLeft):0.00}";
                yield return bot.Run(lv, DifficultyBot.Skilled, n, 200 + lv, r => { Debug.Log("[难度] " + r); });
                b = $"{bot.wins}/{bot.games}"; br = $"{bot.lostFlag}/{bot.lostTime}"; bm = $"{System.Linq.Enumerable.Average(bot.mineLeft):0.00}";
                tb = bot.times.Count > 0 ? $"{System.Linq.Enumerable.Average(bot.times):0}s" : "-";
                lines.Add($"| {lv + 1} | {a} | {ar} | {b} | {br} | {tb} | {am} / {bm} |");
            }
            Time.timeScale = 1f;
            // 只跑了部分关卡时写到另一个文件，别把完整的 10 关表覆盖掉
            bool full = from == 0 && to == Levels.Count - 1;
            var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), full ? "Docs/难度模拟.md" : "Docs/难度模拟_部分关卡.md");
            System.IO.File.WriteAllText(path, "# 难度模拟（机器人 vs AI，真实物理）\n\n" + string.Join("\n", lines) + "\n");
            Debug.Log("[难度] 完成");
            D.GoTitle();
        }

        /// <summary>敌弹威力：投石机朝我方城打 n 发、一发都不挡，每发后统计</summary>
        static bool onlyStone;
        public static string EnemyPower(int level, int n, bool stone = false) { onlyStone = stone; foreach (var dp in Object.FindObjectsByType<DesktopPlay>()) Object.Destroy(dp); D.StartCoroutine(PowRun(level, n)); return "power started"; }

        static System.Collections.IEnumerator PowRun(int level, int n)
        {
            Level(level); Quiet();
            var tips = D.GetComponent<LevelTips>(); if (tips) Object.Destroy(tips);
            yield return new WaitForSeconds(1f);
            var mine = D.arena.mine;
            for (int i = 0; i < n; i++)
            {
                var alive = mine.Alive.Where(b => !b.isKing && b.InPlace).OrderByDescending(b => b.WorldBounds.max.y).ToList();
                var tgt = alive.Count > 0 ? alive[i % Mathf.Min(4, alive.Count)].WorldBounds.center : mine.king.WorldBounds.center;   // 正对高处的块（不加误差）
                D.catapult.FireAt(tgt, 1.9f, onlyStone ? Ammo.Stone : D.level.enemyAmmo[i % D.level.enemyAmmo.Length]);
                yield return new WaitForSeconds(4.5f);
                int moved = mine.blocks.Count(b => !b.isKing && !b.InPlace);
                Debug.Log($"[敌弹] 第{i + 1}发（{D.level.enemyAmmo[i % D.level.enemyAmmo.Length]}）后：我方离位 {moved}/{mine.blocks.Count - 1} 块 完整 {mine.Integrity:0.00} 帅旗 {mine.kingHP}/{mine.kingMaxHP} 着火 {mine.Burning.Count}");
            }
            Debug.Log("[敌弹] 完成");
        }

        /// <summary>
        /// 性能：在最大的那一关量帧时间、绘制批次、三角面、材质数。
        /// 编辑器里的绝对数字不能当真机结果（编辑器有额外开销），但批次 / 面数 / 材质数这些和真机一致，用来看有没有失控。
        /// </summary>
        public static string Perf(string dir) { foreach (var dp in Object.FindObjectsByType<DesktopPlay>()) Object.Destroy(dp); D.StartCoroutine(PerfRun(dir)); return "perf started"; }

        static System.Collections.IEnumerator PerfRun(string dir)
        {
            foreach (int lv in new[] { 9, 4 })
            {
                Level(lv); Quiet();
                yield return new WaitForSeconds(3f);
                // 帧时间：连续 120 帧取中位数（避开第一帧的编译 / 加载尖峰）
                var frames = new System.Collections.Generic.List<float>();
                for (int i = 0; i < 120; i++) { yield return null; frames.Add(Time.unscaledDeltaTime * 1000f); }
                frames.Sort();
                float mid = frames[frames.Count / 2], p95 = frames[(int)(frames.Count * 0.95f)];
                // 统计场景里的渲染物
                int renderers = 0, tris = 0;
                var mats = new System.Collections.Generic.HashSet<Material>();
                foreach (var r in Object.FindObjectsByType<MeshRenderer>())
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                    renderers++;
                    foreach (var m in r.sharedMaterials) if (m) mats.Add(m);
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf && mf.sharedMesh) tris += mf.sharedMesh.triangles.Length / 3;
                }
                int blocks = D.arena.mine.blocks.Count + D.arena.enemy.blocks.Count;
                Debug.Log($"[性能] 第{lv + 1}关：积木 {blocks} 块、渲染物 {renderers} 个、三角面 {tris / 1000f:0.0}k、材质 {mats.Count} 种、帧时间中位 {mid:0.0}ms / 95 分位 {p95:0.0}ms（编辑器，仅作相对参考）");
                SnapPlayerLook(dir, $"L{lv + 1}_性能", D.arena.enemy.transform.position + Vector3.up * 0.2f);
            }
            // 一次大爆炸（炸丸 + 倒塌 + 粒子）时的帧时间
            Level(9); Quiet();
            yield return new WaitForSeconds(2f);
            var e = D.arena.enemy;
            var tgt = e.blocks.Find(b => b.name == "S_Wall" && b.InPlace) ?? e.blocks[0];
            D.DebugShoot(Ammo.Bomb, D.arena.tableCenter + Vector3.up * 0.35f, tgt.WorldBounds.center, 6f);
            float worst = 0;
            for (int i = 0; i < 180; i++) { yield return null; worst = Mathf.Max(worst, Time.unscaledDeltaTime * 1000f); }
            Debug.Log($"[性能] 第10关炸丸命中后 3 秒内最差帧 {worst:0.0}ms（编辑器）");
            Debug.Log("[性能] 完成");
        }

        /// <summary>弹道核对：从投石机勺子朝我方城发一发，逐帧记录真实位置，和预测弹道比</summary>
        public static string TrajCheck() { Level(1); Quiet(); SimHands.Hide(0); SimHands.Hide(1); D.StartCoroutine(TrajRun()); return "traj started"; }

        static System.Collections.IEnumerator TrajRun()
        {
            yield return new WaitForSeconds(0.5f);
            var from = D.catapult.CupPosition;
            var aim = D.arena.mine.transform.position + Vector3.up * 0.1f;
            var v = Ballistics.VelocityFor(from, aim, 1.35f);
            var pred = new System.Collections.Generic.List<Vector3>();
            var land = Ballistics.Predict(from, v, GameTuning.I.enemyBallRadius, pred, 4f);
            var p = Projectile.Spawn(Ammo.Stone, Side.Enemy, from, v);
            Debug.Log($"[弹道] 出手 {from:F3} 速度 {v:F2} 目标 {aim:F3} 预测落点 {land:F3} G={Ballistics.G:F2} 刚体重力={p.rb.useGravity} 阻尼={p.rb.linearDamping} 质量={p.rb.mass:0.000} timeScale={Time.timeScale}");
            float t = 0; int k = 0;
            while (p && p.state == Projectile.State.Flying && t < 3f)
            {
                yield return new WaitForFixedUpdate();
                t += Time.fixedDeltaTime;
                if (k++ % 10 == 0)
                {
                    var exact = from + v * t + 0.5f * Ballistics.G * t * t;
                    Debug.Log($"[弹道] t={t:0.00} 实际 {p.rb.position:F3} 理论 {exact:F3} 差 {(p.rb.position - exact).magnitude * 100:0.0}cm 速度 {p.rb.linearVelocity:F2}");
                }
            }
            Debug.Log($"[弹道] 结束 t={t:0.00} 状态 {(p ? p.state.ToString() : "已销毁")} 位置 {(p ? p.transform.position.ToString("F3") : "-")}");
        }

        /// <summary>11-25 补的几项：开局搭城逐帧、小房间敌城距离、单手模式、跟丢提示、失焦暂停</summary>
        public static string Extras(string dir) { foreach (var dp in Object.FindObjectsByType<DesktopPlay>()) Object.Destroy(dp); SimHands.scripted = true; SimHands.Hide(0); SimHands.Hide(1); D.StartCoroutine(ExtrasRun(dir)); return "extras started"; }

        static void X(string s) => Debug.Log("[补全] " + s);

        static System.Collections.IEnumerator ExtrasRun(string dir)
        {
            var h = D.head.transform;
            // 1) 开局搭城：第 10 关，逐帧看敌城和自己的城
            D.StartLevel(9); Quiet();
            var e = D.arena.enemy.transform.position;
            for (int f = 0; f < 6; f++)
            {
                Snap(dir, $"1_搭城_敌_{f * 0.4f:0.0}s", D.arena.tableCenter + Vector3.up * 0.45f + D.arena.forward * 1.3f, e + Vector3.up * 0.22f, 42);
                if (f == 1 || f == 3) SnapPlayerLook(dir, $"1_搭城_我_{f * 0.4f:0.0}s", D.arena.mine.transform.position);
                yield return new WaitForSeconds(0.4f);
            }
            X($"搭城：敌方 {D.arena.enemy.blocks.Count} 块，2.6 秒后 搭建中={D.arena.enemy.Assembling} 完整={D.arena.enemy.Integrity:0.00} 不在原位={D.arena.enemy.blocks.Count(b => !b.InPlace)} 未显示={D.arena.enemy.blocks.Count(b => !b.gameObject.activeSelf)}");
            yield return new WaitForSeconds(3f);
            float maxD = 0; foreach (var b in D.arena.enemy.blocks) maxD = Mathf.Max(maxD, Vector3.Distance(b.transform.position, b.homePos));
            X($"搭好 3 秒后敌城最大位移 {maxD * 1000:0.0}mm 旗倒={D.arena.enemy.IsKingDown}");

            // 2) 小房间：模拟一个 3.0 × 2.6 m 的安全区（玩家站在靠后的位置）
            var hp = new Vector2(h.position.x, h.position.z); var fw = new Vector2(D.arena.forward.x, D.arena.forward.z).normalized; var rt = new Vector2(fw.y, -fw.x);
            Arena.testBoundary = new[] { hp - fw * 0.5f - rt * 1.5f, hp - fw * 0.5f + rt * 1.5f, hp + fw * 3.0f + rt * 1.5f, hp + fw * 3.0f - rt * 1.5f };
            D.arena.Layout();   // 场地只在第一次开局摆一次，测试里手动重摆
            D.StartLevel(4); Quiet();
            yield return new WaitForSeconds(2.6f);
            X($"小房间：敌城距离 {D.arena.enemyDistance:0.00} m（默认 {GameTuning.I.enemyDistance}）");
            SnapPlayer(dir, "2_小房间_玩家视角");
            Arena.testBoundary = null;
            D.arena.Layout();
            D.StartLevel(4); Quiet();
            X($"恢复：敌城距离 {D.arena.enemyDistance:0.00} m");

            // 3) 单手模式：不用左手，右手抓皮兜拉开松手；左手迎敌弹握拳接
            Progress.OneHand = true;
            Castle.InstantBuild = true; D.StartLevel(1); Castle.InstantBuild = false; Quiet();
            yield return new WaitForSeconds(0.5f);
            var sl = D.sling;
            X($"单手：state={sl.state} 弓架 {sl.ForkCenter:F2}");
            SnapPlayer(dir, "3_单手_立柱");
            Snap(dir, "3_单手_侧面", D.arena.tableCenter - D.arena.forward * 0.5f + Vector3.up * 0.25f + Vector3.Cross(Vector3.up, D.arena.forward) * 0.45f, sl.ForkCenter - Vector3.up * 0.12f, 50);
            var pr = sl.PouchRest;
            SimHands.Pose(1, pr + Vector3.up * 0.05f, false); yield return null;
            SimHands.Pose(1, pr, true); yield return null; yield return null;
            var target = D.arena.enemy.king.WorldBounds.center;
            float speed = Mathf.Lerp(GameTuning.I.slingSpeedMin, GameTuning.I.slingSpeedMax, Mathf.InverseLerp(InputTuning.I.pullMin, InputTuning.I.pullMax, 0.26f));
            Ballistics.Solve(sl.ForkCenter, target, speed, out var lo, out _);
            var pouchAt = sl.ForkCenter - lo.normalized * 0.26f;
            for (float k = 0; k < 1f; k += Time.deltaTime / 0.5f) { SimHands.Pose(1, Vector3.Lerp(pr, pouchAt, k), true); yield return null; }
            SimHands.Pose(1, pouchAt, true); yield return new WaitForSeconds(0.2f);
            SnapPlayer(dir, "3_单手_拉弓");
            int shots = D.stats.shots;
            SimHands.Pose(1, pouchAt, false); yield return null; SimHands.Hide(1);
            X($"单手：拉开 {sl.pull01:0.00} 松手后射 +{D.stats.shots - shots}");
            yield return new WaitForSeconds(2.5f);
            X($"单手：发射后 {State()}");
            // 左手接敌弹（上一发可能已经打赢，换一关重开）
            Castle.InstantBuild = true; D.StartLevel(2); Castle.InstantBuild = false; Quiet();
            yield return new WaitForSeconds(0.5f);
            D.catapult.Begin(D.level, D.arena.mine, D.ammo);
            Projectile p = null; float w = 0;
            while (p == null && w < 20f) { p = Projectile.Live.Find(x => x && x.from == Side.Enemy && x.state == Projectile.State.Flying); w += Time.deltaTime; yield return null; }
            int caught = D.stats.caught;
            if (p)
            {
                var path = new System.Collections.Generic.List<Vector3>();
                Ballistics.Predict(p.transform.position, p.rb.linearVelocity, p.radius, path);
                var mine = D.arena.mine.transform.position; Vector3 meet = path[path.Count - 1];
                foreach (var q in path) if (Vector3.Distance(q, mine) < 0.3f) { meet = q; break; }
                SimHands.Pose(0, meet, false, false, (D.head.transform.position - meet).normalized * -1f);
                float t = 0; while (p && p.state == Projectile.State.Flying && t < 4f) { t += Time.deltaTime; yield return null; }
                yield return new WaitForSeconds(0.2f); SimHands.Pose(0, meet, false, true);
                yield return new WaitForSeconds(1f);
            }
            SimHands.Hide(0); D.catapult.Stop();
            X($"单手：左手握拳接 +{D.stats.caught - caught} phase={D.phase}");
            Progress.OneHand = false;

            // 4) 跟丢提示：两手都放下 3 秒
            HandLostHint.testInEditor = true;
            D.StartLevel(1); Quiet();
            SimHands.Hide(0); SimHands.Hide(1);
            yield return new WaitForSeconds(3.4f);
            var hint = D.GetComponent<HandLostHint>();
            X($"跟丢 3.4 秒：提示={hint.Showing}");
            SnapPlayer(dir, "4_跟丢提示");
            SimHands.Pose(1, HeadPt(0.35f, 0.1f, -0.2f), false);
            yield return new WaitForSeconds(0.3f);
            X($"手回来：提示={hint.Showing}");
            SimHands.Hide(1);
            HandLostHint.testInEditor = false;

            // 5) 失焦暂停
            FocusPause.testInEditor = true;
            var fp = D.GetComponent<FocusPause>();
            float left0 = D.timeLeft;
            fp.Handle(true);
            yield return new WaitForSecondsRealtime(1.0f);
            X($"失焦：timeScale={Time.timeScale} 暂停={fp.Paused} 1 秒里倒计时走了 {left0 - D.timeLeft:0.00}s 声音暂停={AudioListener.pause}");
            fp.Handle(false);
            yield return new WaitForSecondsRealtime(0.8f);
            X($"回来 0.8 秒：timeScale={Time.timeScale:0.00}");
            yield return new WaitForSecondsRealtime(1.0f);
            X($"回来 1.8 秒：timeScale={Time.timeScale:0.00} 暂停={fp.Paused}");
            FocusPause.testInEditor = false;
            X("完成");
        }

        /// <summary>封面自查：飘着的积木 / 石弹 / 纸页两两之间的空隙（按包围球 + 漂移幅度算），以及有没有挡到布旗正面</summary>
        public static string CoverCheck()
        {
            var c = Object.FindAnyObjectByType<CoverMenu>();
            if (!c) return "没有封面";
            var items = new System.Collections.Generic.List<(string n, Vector3 p, float r)>();
            foreach (var dr in c.GetComponentsInChildren<Drift>())
            {
                var rs = dr.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                items.Add((dr.name, b.center, b.extents.magnitude * 0.8f + 0.015f));
            }
            var sb = new System.Text.StringBuilder(); int bad = 0;
            for (int i = 0; i < items.Count; i++)
                for (int j = i + 1; j < items.Count; j++)
                {
                    float gap = Vector3.Distance(items[i].p, items[j].p) - items[i].r - items[j].r;
                    if (gap < 0) { bad++; sb.AppendLine($"  太近 {items[i].n} × {items[j].n} 差 {gap * 100:0.0} cm"); }
                }
            // 挡布旗：从眼睛看布旗上的点，射线有没有先打到飘着的东西
            var banner = c.GetComponentInChildren<BannerCloth>();
            int blocked = 0, total = 0;
            var eye = D.head.transform.position;
            var mf = banner.GetComponent<MeshFilter>().sharedMesh;
            foreach (var v in mf.vertices)
            {
                var w = banner.transform.TransformPoint(v); total++;
                foreach (var it in items)
                {
                    var dir = (w - eye); float L = dir.magnitude; dir /= L;
                    float tt = Vector3.Dot(it.p - eye, dir);
                    if (tt > 0 && tt < L && (eye + dir * tt - it.p).magnitude < it.r * 0.8f) { blocked++; break; }
                }
            }
            return $"飘浮物 {items.Count} 个，太近 {bad} 对；布旗顶点被挡 {blocked}/{total}\n" + sb;
        }

        /// <summary>封面全流程：悬停 → 设置 → 换语言（布旗卷起换印花）→ 返回 → 选关 → 选第 2 关（封面收起、开局搭城）；每步截图</summary>
        public static string CoverFlow(string dir) { foreach (var dp in Object.FindObjectsByType<DesktopPlay>()) Object.Destroy(dp); D.StartCoroutine(CoverFlowRun(dir)); return "cover flow started"; }

        static System.Collections.IEnumerator PinchAt(Vector3 p)
        {
            SimHands.scripted = true;
            SimHands.Pose(1, p + Vector3.up * 0.03f, false); yield return null; yield return null;
            SimHands.Pose(1, p, false); yield return new WaitForSeconds(0.15f);
            SimHands.Pose(1, p, true); yield return null; yield return null;
            SimHands.Pose(1, p, false); yield return null;
        }

        static System.Collections.IEnumerator CoverFlowRun(string dir)
        {
            void C(string s) => Debug.Log("[封面流程] " + s);
            Loc.English = false; Progress.SaveLanguage();
            D.GoTitle();
            var h = D.head.transform;
            System.Func<string> keys = () => { var c = Object.FindAnyObjectByType<CoverMenu>(); return c ? string.Join(" / ", c.Tags.Select(t => t.Key ?? "-")) : "无封面"; };
            // 出现过程逐帧
            for (int f = 0; f < 6; f++) { Snap(dir, $"0_出现_{f * 0.35f:0.00}s", h.position, h.position + D.arena.forward, 80); yield return new WaitForSeconds(0.35f); }
            yield return new WaitForSeconds(0.6f);
            var cover = Object.FindAnyObjectByType<CoverMenu>();
            C($"出现完 appear={cover.Appear:0.00} 木签：{keys()}  封面上的虚拟桌={Object.FindObjectsByType<VirtualTable>().Length}");
            // 署名（布旗下沿的 TRIPO 图标 + 一行字）：按头显像素密度（视场 50.5° ≈ 头显 1:1）对准拍
            var ban = GameObject.Find("CoverMenu/Cover/Banner");
            if (ban)
            {
                var br = ban.GetComponent<Renderer>(); var bb = br ? br.bounds : new Bounds(ban.transform.position, Vector3.one * 0.1f);
                var credit = new Vector3(bb.center.x, bb.min.y + bb.size.y * 0.2f, bb.center.z);
                Snap(dir, "0b_署名_头显像素", h.position, credit, 50.5f);
            }
            // 悬停：手停在“开始 / 继续”木签前 4 cm
            var t0 = cover.Tags[0].button.transform.position;
            SimHands.scripted = true; SimHands.Pose(1, t0 - D.arena.forward * 0.04f, false);
            yield return new WaitForSeconds(0.5f);
            C($"悬停：Hovered={(WoodButton.Hovered ? WoodButton.Hovered.Key : "无")}");
            Snap(dir, "1_悬停", h.position, t0, 45);
            Snap(dir, "1b_悬停近看", t0 - D.arena.forward * 0.22f + Vector3.up * 0.03f, t0, 40);
            SimHands.Hide(1);
            // 设置
            yield return PinchAt(cover.Tags[2].button.transform.position);
            yield return new WaitForSeconds(1.0f);
            C($"捏“设置”后：{keys()}");
            Snap(dir, "2_设置页", h.position, h.position + D.arena.forward, 80);
            // 切换语言
            yield return PinchAt(cover.Tags[0].button.transform.position);
            yield return new WaitForSeconds(0.45f);
            Snap(dir, "3_换语言_中途", h.position, h.position + D.arena.forward, 80);
            yield return new WaitForSeconds(1.2f);
            C($"换语言后 English={Loc.English}：{keys()}");
            Snap(dir, "4_英文", h.position, h.position + D.arena.forward, 80);
            // 声音
            yield return PinchAt(cover.Tags[2].button.transform.position);
            yield return new WaitForSeconds(0.8f);
            C($"捏声音后 music={Progress.Music} sfx={Progress.Sfx}：{keys()}");
            yield return PinchAt(cover.Tags[2].button.transform.position); yield return new WaitForSeconds(0.8f);
            yield return PinchAt(cover.Tags[2].button.transform.position); yield return new WaitForSeconds(0.8f);
            C($"再捏两次 music={Progress.Music} sfx={Progress.Sfx}");
            // 切回中文、返回
            yield return PinchAt(cover.Tags[0].button.transform.position); yield return new WaitForSeconds(1.5f);
            yield return PinchAt(cover.Tags[3].button.transform.position); yield return new WaitForSeconds(1.0f);
            C($"返回后 English={Loc.English}：{keys()}");
            // 选关
            Progress.Unlocked = Mathf.Max(Progress.Unlocked, 3);
            yield return PinchAt(cover.Tags[1].button.transform.position); yield return new WaitForSeconds(1.2f);
            C($"选关页：{keys()}  积木 {cover.LevelButtons.Count} 块，可按 {cover.LevelButtons.Count(b => b.interactable)} 块");
            Snap(dir, "5_选关", h.position, h.position + D.arena.forward, 80);
            yield return PinchAt(cover.LevelButtons[1].transform.position);
            var down = h.position + (D.arena.forward + Vector3.down * 0.55f).normalized;
            for (int f = 0; f < 7; f++) { yield return new WaitForSeconds(0.3f); Snap(dir, $"6_开局_{f * 0.3f + 0.3f:0.0}s", h.position, down, 80); }
            yield return new WaitForSeconds(2.5f);
            C($"开局后 phase={D.phase} 关={D.levelIndex + 1} 封面还在={(Object.FindAnyObjectByType<CoverMenu>() != null)}");
            Snap(dir, "7_开局后", h.position, h.position + D.arena.forward, 80);
            D.GoTitle();
            C("完成");
        }

        /// <summary>火 / 灭火特效截图：点着我方城两块木头（近看）+ 敌城一块（远看），连拍；再灭火，按 0.1 秒逐帧拍“噗”的过程</summary>
        public static string FireFx(string dir) { Level(3); Quiet(); D.StartCoroutine(FireFxRun(dir)); return "fire fx started"; }
        static System.Collections.IEnumerator FireFxRun(string dir)
        {
            yield return new WaitForSeconds(0.5f);
            var mine = D.arena.mine; var en = D.arena.enemy;
            var woods = mine.blocks.Where(b => b && b.Wood && !b.Dead && b != mine.king).OrderByDescending(b => b.transform.position.y).Take(2).ToList();
            foreach (var b in woods) b.Ignite();
            var ew = en.blocks.Where(b => b && b.Wood && !b.Dead && b != en.king).OrderByDescending(b => b.transform.position.y).First();
            ew.Ignite();
            var c = woods[0].WorldBounds.center;
            var eye = D.head.transform.position;
            Vector3 near = c + (eye - c).normalized * 0.32f + Vector3.up * 0.04f;
            for (int i = 0; i < 4; i++)
            {
                yield return new WaitForSeconds(i == 0 ? 0.8f : 0.35f);
                Snap(dir, $"火_近_{i}", near, c + Vector3.up * 0.03f, 60);
            }
            SnapPlayer(dir, "火_玩家视角");
            // 倒在桌上的块：从城里拿一块木头横放到城前桌面上点着
            var lying = mine.blocks.Where(x => x && x.Wood && !x.Dead && x != mine.king && !woods.Contains(x) && x.def.id.Contains("Cube")).OrderBy(x => x.transform.position.y).FirstOrDefault()
                        ?? mine.blocks.First(x => x && x.Wood && !x.Dead && x != mine.king && !woods.Contains(x));
            if (lying.rb) lying.rb.isKinematic = true;
            var tp = D.arena.tableCenter + Vector3.ProjectOnPlane(eye - D.arena.tableCenter, Vector3.up).normalized * 0.12f;
            tp.y = mine.transform.position.y + lying.WorldBounds.extents.y * 0.6f;
            lying.transform.SetPositionAndRotation(tp, Quaternion.Euler(0, 35, 90));
            yield return new WaitForFixedUpdate();
            lying.Ignite();
            yield return new WaitForSeconds(1.0f);
            var lc = lying.WorldBounds.center;
            Snap(dir, "火_倒块", lc + (eye - lc).normalized * 0.28f + Vector3.up * 0.05f, lc, 60);
            Snap(dir, "火_倒块_侧", lc + Vector3.Cross(Vector3.up, Vector3.ProjectOnPlane(eye - lc, Vector3.up).normalized) * 0.28f + Vector3.up * 0.06f, lc, 60);
            SnapEnemy(dir, "火_敌城");
            woods[0].Extinguish();
            for (int i = 0; i < 8; i++)
            {
                yield return new WaitForSeconds(0.12f);
                Snap(dir, $"灭_{i:00}", near, c + Vector3.up * 0.03f, 60);
            }
            Debug.Log("[FireFx] 完成");
        }

        /// <summary>面板重叠检查：①教学中推倒我方帅旗 → 应立回、不判负；②教学中途判负 → 教学木牌收起只剩结算；③普通关挂着提示时判负 → 同样</summary>
        public static string OverlapCheck(string dir)
        {
            foreach (var dp in Object.FindObjectsByType<DesktopPlay>()) Object.Destroy(dp);
            SimHands.scripted = true; SimHands.Hide(0); SimHands.Hide(1);
            D.head.transform.rotation = Quaternion.Euler(14, 0, 0);
            D.StartLevel(0);
            D.StartCoroutine(OverlapRun(dir));
            return "overlap started";
        }
        static int ActiveTags() => Object.FindObjectsByType<HintTag>(FindObjectsInactive.Exclude).Length;
        static System.Collections.IEnumerator OverlapRun(string dir)
        {
            yield return new WaitForSeconds(3f);
            var k = D.arena.mine.king;
            k.rb.isKinematic = false;
            k.transform.rotation = Quaternion.Euler(0, 0, 80);
            yield return new WaitForSeconds(0.3f);
            Debug.Log($"[Overlap] ①推倒后 phase={D.phase}");
            yield return new WaitForSeconds(3.5f);
            Debug.Log($"[Overlap] ①3.8 秒后 phase={D.phase} 旗倾斜={Vector3.Angle(k.transform.up, Vector3.up):0}° 木牌={ActiveTags()}");
            SnapPlayer(dir, "1_教学中推倒帅旗后");
            D.End(false);
            yield return new WaitForSeconds(4.5f);
            Debug.Log($"[Overlap] ②教学中判负 phase={D.phase} 挂着的木牌={ActiveTags()}");
            SnapPlayer(dir, "2_教学中判负");
            Castle.InstantBuild = true; D.StartLevel(3); Castle.InstantBuild = false;
            yield return new WaitForSeconds(1f);
            D.catapult.Stop();
            var w = D.arena.mine.blocks.First(b => b && b.Wood && !b.Dead && b != D.arena.mine.king);
            w.Ignite();
            yield return new WaitForSeconds(1.5f);
            Debug.Log($"[Overlap] ③着火提示 木牌={ActiveTags()}");
            D.End(false);
            yield return new WaitForSeconds(4.5f);
            Debug.Log($"[Overlap] ③判负后 挂着的木牌={ActiveTags()}");
            SnapPlayer(dir, "3_着火提示时判负");
            Debug.Log("[Overlap] 完成");
        }

        /// <summary>
        /// 逐关巡检（表现 + 舒适度）：每关①玩家视角（头显视场 ~96°）②敌城近景 ③投石机打 4 发、我方打 3 发后的战况；
        /// 量：敌城 / 己城 / 补给盘相对头部的水平角、俯仰角、距离，敌城在视野里占多大，盒盖开口仰角。
        /// </summary>
        public static string Audit(string dir) { foreach (var dp in Object.FindObjectsByType<DesktopPlay>()) Object.Destroy(dp); D.StartCoroutine(AuditRun(dir)); return "audit started"; }
        static string Ang(Vector3 p)
        {
            var h = D.head.transform.position; var f = D.arena.forward; var v = p - h;
            float yaw = Vector3.SignedAngle(f, Vector3.ProjectOnPlane(v, Vector3.up), Vector3.up);
            float pitch = Mathf.Asin(Mathf.Clamp(v.normalized.y, -1, 1)) * Mathf.Rad2Deg;
            return $"水平{yaw:0}° 俯仰{pitch:0}° 距{v.magnitude:0.00}m";
        }
        static System.Collections.IEnumerator AuditRun(string dir)
        {
            D.head.transform.rotation = Quaternion.LookRotation(D.arena.forward) * Quaternion.Euler(12, 0, 0);
            for (int i = 0; i < Levels.Count; i++)
            {
                Level(i); Quiet();
                if (i == 0) { var tut = D.GetComponent<Tutorial>(); if (tut) Object.Destroy(tut); D.arena.enemy.kingLocked = false; D.arena.mine.kingLocked = false; }
                yield return new WaitForSeconds(2.5f);
                var A = D.arena; var e = A.enemy; var m = A.mine;
                var eb = new Bounds(e.transform.position, Vector3.zero); foreach (var b in e.blocks) if (b) eb.Encapsulate(b.WorldBounds);
                float eAng = 2 * Mathf.Atan2(eb.extents.x, Vector3.Distance(D.head.transform.position, eb.center)) * Mathf.Rad2Deg;
                float eTall = 2 * Mathf.Atan2(eb.extents.y, Vector3.Distance(D.head.transform.position, eb.center)) * Mathf.Rad2Deg;
                var tray = GameObject.Find("SupplyTray");
                Debug.Log($"[巡检] 第{i + 1}关 {D.level.title} 时段{D.level.timeOfDay} 敌城{e.blocks.Count}块 宽{eb.size.x:0.00}m 高{eb.size.y:0.00}m 视角宽{eAng:0.0}° 高{eTall:0.0}° | 敌城 {Ang(eb.center)} | 己城 {Ang(m.transform.position)} | 补给盘 {(tray ? Ang(tray.transform.position) : "无")} | 帅旗 {Ang(e.king.transform.position)} | 天花板 {A.ceilingHeight:0.00}m");
                SnapPlayer(dir, $"L{i + 1:00}_a_玩家视角");
                SnapEnemy(dir, $"L{i + 1:00}_b_敌城");
                // 对轰几发看战况
                for (int k = 0; k < 3; k++) { Shoot(D.level.enemyAmmo[k % D.level.enemyAmmo.Length] == Ammo.Stone ? Ammo.Stone : (Ammo)Mathf.Min(k, (int)Ammo.Split), Random.Range(0, e.blocks.Count), 5.5f, k == 2); yield return new WaitForSeconds(0.6f); }
                D.catapult.Begin(D.level, m, D.ammo);
                yield return new WaitForSeconds(9f);
                D.catapult.Stop();
                SnapPlayer(dir, $"L{i + 1:00}_c_战况");
                Debug.Log($"[巡检] 第{i + 1}关 战后 {State()}");
            }
            Debug.Log("[巡检] 完成");
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

        /// <summary>虚拟桌（11-27）：玩家挪开 0.8 m 重摆场地 → 旧桌拆、新桌搭，侧面逐帧拍；再从玩家视角拍桌面 + 城，最后模拟头显（藏替身房间、透明背景）</summary>
        public static string TableCheck(string dir) { foreach (var dp in Object.FindObjectsByType<DesktopPlay>()) Object.Destroy(dp); D.StartCoroutine(TableRun(dir)); return "table started"; }
        static System.Collections.IEnumerator TableRun(string dir)
        {
            var h = D.head.transform;
            void X(string m) => Debug.Log("[桌] " + m);
            yield return new WaitForSeconds(1.5f);
            var old = Object.FindObjectsByType<VirtualTable>();
            X($"开局虚拟桌 {old.Length} 张 桌面中心 {D.arena.tableCenter:F2} 头 {h.position:F2} 真实桌={D.arena.usingRealTable}");
            var r = Vector3.Cross(Vector3.up, D.arena.forward);
            var c0 = D.arena.tableCenter;
            h.position += r * 0.8f;
            D.arena.Layout();
            var c1 = D.arena.tableCenter;
            var mid = (c0 + c1) * 0.5f; mid.y = 0.45f;
            var eye = mid - D.arena.forward * 1.9f + r * 0.4f + Vector3.up * 0.75f;
            for (int i = 0; i < 14; i++)
            {
                Snap(dir, $"1_过程_{i:00}", eye, mid, 50);
                yield return new WaitForSeconds(0.12f);
            }
            yield return new WaitForSeconds(0.5f);
            Snap(dir, "1_过程_终", eye, mid, 50);
            X($"重摆后虚拟桌 {Object.FindObjectsByType<VirtualTable>().Length} 张 新中心 {c1:F2}");
            h.position -= r * 0.8f;
            D.arena.Layout();
            yield return new WaitForSeconds(2f);
            Castle.InstantBuild = true; D.StartLevel(1); Castle.InstantBuild = false;
            foreach (var c in D.GetComponents<LevelTips>()) Object.Destroy(c);
            D.catapult.Stop();
            yield return new WaitForSeconds(1.5f);
            var tc = D.arena.tableCenter;
            SnapPlayerLook(dir, "2_玩家看桌", tc + D.arena.forward * 0.05f);
            Snap(dir, "3_近看桌角", tc + Quaternion.LookRotation(D.arena.forward) * new Vector3(0.75f, 0.05f, -0.55f), tc + Quaternion.LookRotation(D.arena.forward) * new Vector3(0.3f, -0.35f, -0.2f), 50);
            Snap(dir, "4_侧面全桌", tc + Quaternion.LookRotation(D.arena.forward) * new Vector3(1.6f, -0.2f, 0.2f), tc - Vector3.up * 0.4f, 50);
            // 模拟头显：替身房间藏掉、透明背景
            var hidden = new System.Collections.Generic.List<GameObject>();
            foreach (var rr in Object.FindObjectsByType<Renderer>()) { var n = rr.name; if (n.StartsWith("Room") || n.StartsWith("Ceil")) { rr.gameObject.SetActive(false); hidden.Add(rr.gameObject); } }
            var cam = D.head; var bg = cam.backgroundColor; var cf = cam.clearFlags;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            Snap(dir, "5_头显模拟_侧面", tc + Quaternion.LookRotation(D.arena.forward) * new Vector3(1.6f, -0.2f, 0.2f), tc - Vector3.up * 0.4f, 50);
            cam.backgroundColor = bg; cam.clearFlags = cf;
            foreach (var g in hidden) g.SetActive(true);
            X("完成");
        }

        /// <summary>模拟头显：藏掉编辑器替身房间（墙 / 地 / 天花板）、背景换成中灰当透视，拍一张</summary>
        public static string SnapRoomless(string dir, string name, Vector3 pos, Vector3 look, float fov)
        {
            var hidden = new System.Collections.Generic.List<GameObject>();
            foreach (var rr in Object.FindObjectsByType<Renderer>()) { var n = rr.name; if (n.StartsWith("Room") || n.StartsWith("Ceil")) { rr.gameObject.SetActive(false); hidden.Add(rr.gameObject); } }
            var cam = D.head; var bg = cam.backgroundColor; var cf = cam.clearFlags;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            var p = Snap(dir, name, pos, look, fov);
            cam.backgroundColor = bg; cam.clearFlags = cf;
            foreach (var g in hidden) g.SetActive(true);
            return p;
        }

        public static string SnapPlayer(string dir, string name) => Snap(dir, name, D.head.transform.position, D.head.transform.position + D.head.transform.forward, D.head.fieldOfView);

        public static string SnapEnemy(string dir, string name)
        {
            var e = D.arena.enemy.transform.position;
            return Snap(dir, name, D.arena.tableCenter + Vector3.up * 0.5f + D.arena.forward * 1.2f, e + Vector3.up * 0.2f, 40);
        }
    }
}

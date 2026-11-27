// 关卡机关（11-27 用户：加了新弹之后，一种弹配几种机关，关卡加到 18 关）。
// 机关就是城里的一块积木（同样能被打、着火、冻住、碎掉），只是挂了这个组件，对某种弹 / 某种状态有额外反应：
//   火丸：草垛（烧得快、一碰就着，一排会一路烧过去）、火药桶（着火 1.4 秒后炸；被炸到立刻炸 → 连环）
//   冰丸：水桶（周围一圈冻脆，石墙也一碰就碎）、浅水池（结冰鼓起，上面的塔打滑倒下）、投石机（冻住 8 秒停火，见 EnemyCatapult.Freeze）
//   炸丸：火药桶连环、拱心石（炸掉 → 压在拱上的整片塌）
//   分裂丸：三口铜钟（1.5 秒内三口都响 → 挡在帅旗前的盾墙倒下）、吊秤（另一头秤盘短时间挨几下 → 秤杆一翘把帅旗掀飞）
// 每个机关都有过程：预警（引信冒火星、钟嗡嗡响、秤杆晃）→ 发生 → 收尾。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PaperSiege
{
    /// <summary>机关模型（Resources/Mech/mech.fbx + M_Mech 材质）。没有导入时返回空，由 Placeholder 按碰撞盒搭占位</summary>
    public static class MechLib
    {
        static Dictionary<string, Mesh> map;
        static Material mat;
        static void Load()
        {
            map = new();
            foreach (var m in Resources.LoadAll<Mesh>("Mech/mech")) map[m.name] = m;
            mat = Resources.Load<Material>("Mech/M_Mech");
        }
        public static bool Has(string id) { if (map == null) Load(); return map.ContainsKey(id) && mat; }
        public static Mesh Get(string id, bool low = false)
        {
            if (map == null) Load();
            if (low && map.TryGetValue(id + "_LOD1", out var l)) return l;
            return map.TryGetValue(id, out var m) ? m : null;
        }
        public static Material Material { get { if (map == null) Load(); return mat; } }
    }

    public class Mech : MonoBehaviour
    {
        public enum Kind { Hay, Keg, Bucket, Trough, Keystone, Bell, BellFrame, ScalePost, ScaleBeam }
        public Kind kind;
        public Block block;
        /// <summary>脚本驱动、不受物理叫醒（钟、钟架、秤杆、秤柱）</summary>
        public bool Scripted => kind == Kind.Bell || kind == Kind.BellFrame || kind == Kind.ScaleBeam || kind == Kind.ScalePost || kind == Kind.Hay || kind == Kind.Trough;   // 草垛（一捆软草，挨打不滑走）、冰池（地上的槽）也不交给物理
        static readonly List<Mech> all = new();
        float U => block.Unit;
        GameTuning T => GameTuning.I;

        public static Mech Attach(Block b)
        {
            var id = b.def.id;
            Kind k;
            switch (id)
            {
                case "M_Hay": k = Kind.Hay; break;
                case "M_Keg": k = Kind.Keg; break;
                case "M_Bucket": k = Kind.Bucket; break;
                case "M_Trough": k = Kind.Trough; break;
                case "M_Keystone": k = Kind.Keystone; break;
                case "M_Bell": k = Kind.Bell; break;
                case "M_BellFrame": k = Kind.BellFrame; break;
                case "M_ScalePost": k = Kind.ScalePost; break;
                case "M_ScaleBeam": k = Kind.ScaleBeam; break;
                default: return null;
            }
            var m = b.gameObject.AddComponent<Mech>();
            m.kind = k; m.block = b;
            if (k == Kind.Hay) b.burnRate = 3f;
            return m;
        }

        void OnEnable() { all.Add(this); Projectile.Impacted += OnImpact; Projectile.Exploded += OnExplode; }
        void OnDisable() { all.Remove(this); Projectile.Impacted -= OnImpact; Projectile.Exploded -= OnExplode; }

        bool Near(Vector3 p, float cells) => Vector3.Distance(block.WorldBounds.ClosestPoint(p), p) < cells * U;

        // ───────────────────────── 撞击 / 爆炸
        void OnImpact(Ammo a, Vector3 p, Block hit, Side from)
        {
            if (!block || block.Dead || from != Side.Player && kind != Kind.Keg) return;   // 敌弹只会引爆火药桶（不会替玩家触发机关）
            switch (kind)
            {
                case Kind.Bucket:
                    if (a == Ammo.Ice && (hit == block || Near(p, 1.2f))) StartCoroutine(FreezeAround());
                    break;
                case Kind.Trough:
                    // 冰丸落在池子正上方 2 格以内（打到池子本身、池里的水、或池里那座楼的底部）都算：冰在水面上散开
                    if (a == Ammo.Ice && !iced)
                    {
                        var bb = block.WorldBounds;
                        bool over = p.x > bb.min.x - 0.3f * U && p.x < bb.max.x + 0.3f * U && p.z > bb.min.z - 0.3f * U && p.z < bb.max.z + 0.3f * U;
                        // 冰丸的冰冻半径 1.5 格：落在池边 1 格以内（池前的地上）也能把水面冻上
                        if (hit == block || (over && p.y < bb.max.y + 2.0f * U) || Near(p, 1.0f)) StartCoroutine(IceOver());
                    }
                    break;
                case Kind.Bell:
                    if (Debug.isDebugBuild && Application.isEditor) Debug.Log($"[钟] 弹落 {p:F2} 钟心 {block.WorldBounds.center:F2} 距 {Vector3.Distance(block.WorldBounds.ClosestPoint(p), p) / U:0.00} 格 hit={(hit ? hit.def.id : "-")}");
                    // 打中钟，或者弹落在离钟 0.9 格以内（打到钟架、旁边的墙也算：钟被震响）。分裂丸三颗散开约 1 格，正好一颗一口
                    if (hit == block || Vector3.Distance(block.WorldBounds.ClosestPoint(p), p) < 0.9f * U) Ring();
                    break;
                case Kind.ScaleBeam:
                    ScaleHit(p, hit, a == Ammo.Bomb ? 2f : 1f);
                    break;
            }
        }

        void OnExplode(Vector3 p, float r)
        {
            if (!block || block.Dead) return;
            if (kind == Kind.Keg && Near(p, r / U * 0.95f) && !blowing) StartCoroutine(Blow(0.22f));   // 被炸到：立刻跟着炸（连环）
            if (kind == Kind.ScaleBeam) ScaleHit(p, null, 2f, r);
        }

        void Update()
        {
            if (!block) return;
            switch (kind)
            {
                case Kind.Keg: KegUpdate(); break;
                case Kind.Keystone: KeystoneUpdate(); break;
                case Kind.Bell: BellUpdate(); break;
                case Kind.ScaleBeam: ScaleUpdate(); break;
            }
        }

        // ───────────────────────── 火药桶：着火 → 引信冒火星 1.4 秒 → 炸
        float fuse; bool blowing; float sparkT;
        void KegUpdate()
        {
            if (block.Dead || blowing) return;
            if (!block.burning) { fuse = 0; return; }
            fuse += Time.deltaTime;
            if ((sparkT -= Time.deltaTime) <= 0f)
            {
                sparkT = Mathf.Lerp(0.18f, 0.05f, fuse / 1.4f);
                var top = block.WorldBounds.center + Vector3.up * block.WorldBounds.extents.y;
                Fx.Sparkle(top, new Color(1f, 0.75f, 0.3f), U * 0.25f);
            }
            if (fuse > 0.2f && fuse - Time.deltaTime <= 0.2f) Sfx.Fuse(block.WorldBounds.center);
            if (fuse >= 1.4f) StartCoroutine(Blow(0f));
        }

        IEnumerator Blow(float delay)
        {
            if (blowing) yield break;
            blowing = true;
            if (Application.isEditor) Debug.Log($"[火药桶] {name} {(delay > 0 ? "被炸到，连环" : "引信烧完")} 将爆");
            if (delay > 0f) yield return new WaitForSeconds(delay);
            if (!block || block.Dead) yield break;
            var c = block.WorldBounds.center;
            block.Kill(true);
            BlownCount++;
            Projectile.Explode(c, T.bombRadius * U * 1.15f);
            Sfx.Hit(Ammo.Bomb, Mat.Wood, c);
        }

        // ───────────────────────── 水桶：冰丸 → 一圈冰霜往外长 → 范围内全部冻脆
        bool frozenAround;
        IEnumerator FreezeAround()
        {
            if (frozenAround) yield break;
            frozenAround = true;
            var c = block.WorldBounds.center;
            float R = 2.6f * U;
            Sfx.Hit(Ammo.Ice, Mat.Stone, c);
            for (float t = 0; t < 0.6f; t += 0.15f)   // 冰霜分三圈往外长
            {
                float r = Mathf.Lerp(0.8f, 1f, t / 0.6f) * R * (0.4f + t);
                Fx.IceBurst(c, r);
                foreach (var b in Projectile.BlocksNear(c, r)) if (!b.isKing) b.Chill(T.freezeSeconds * 2f);
                yield return new WaitForSeconds(0.15f);
            }
            block.Chill(999f);
        }

        // ───────────────────────── 浅水池：冰丸 → 结冰、往一边鼓起 → 上面的积木失去摩擦，滑倒
        bool iced;
        static PhysicsMaterial icePM;
        IEnumerator IceOver()
        {
            iced = true;
            block.Chill(999f);
            var bb = block.WorldBounds;
            Fx.IceBurst(bb.center, bb.extents.magnitude);
            Sfx.Hit(Ammo.Ice, Mat.Stone, bb.center);
            icePM ??= new PhysicsMaterial("Ice") { dynamicFriction = 0.02f, staticFriction = 0.03f, bounciness = 0f, frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum };
            // 压在池子上的积木：底面在池面附近、水平投影落在池子里
            var on = new List<Block>();
            foreach (var b in block.castle.blocks)
            {
                if (!b || b == block || b.Dead) continue;
                var wb = b.WorldBounds;
                bool over = wb.center.x > bb.min.x && wb.center.x < bb.max.x && wb.center.z > bb.min.z && wb.center.z < bb.max.z;
                if (over && wb.min.y < bb.max.y + 3.5f * U) on.Add(b);
            }
            foreach (var b in on) foreach (var col in b.GetComponents<Collider>()) col.sharedMaterial = icePM;
            if (Application.isEditor) Debug.Log($"[冰池逻辑] 池面顶 {bb.max.y:F3} 上面 {on.Count} 块：{string.Join(",", on.ConvertAll(x => x.def.id + (x.isKing ? "(旗)" : "") + "@" + x.WorldBounds.min.y.ToString("F3")))}");
            // 冰面往一边鼓起 6°（1 秒），上面的块全醒、楼脚被冰挤得往外滑 → 整栋倒下
            var r0 = transform.rotation;
            var axis = Vector3.Cross(Vector3.up, block.castle.transform.forward);
            yield return new WaitForSeconds(0.35f);
            foreach (var b in on) { if (b && !b.isKing) b.Wake(); }
            // 帅旗平时不会被物理叫醒（规则：弹只扣耐久、推不倒）—— 但它脚下的楼滑走了，它得跟着掉下来：楼一离开原位就放开帅旗
            var kingB = block.castle.king;
            if (kingB && on.Contains(kingB)) block.castle.StartCoroutine(ReleaseKingWhenBaseMoves(kingB, on));
            // 冰面往一边鼓起 6°（1.2 秒）：贴着冰面的楼脚像站在斜冰上一样一直往下坡滑（持续加速度、不许停稳冻结），上面整栋跟着倒
            var legs = on.FindAll(x => x && !x.isKing && x.WorldBounds.min.y < bb.max.y + 0.3f * U);
            if (Application.isEditor) Debug.Log($"[冰池逻辑] 楼脚 {legs.Count} 块");
            for (float t = 0; t < 1.2f; t += Time.deltaTime)
            {
                var q = Quaternion.AngleAxis(6f * Mathf.SmoothStep(0, 1, Mathf.Clamp01(t)), axis) * r0;
                block.rb.MoveRotation(q); transform.rotation = q;
                var downhill = Vector3.Cross(axis, Vector3.up).normalized;   // 鼓起那边的下坡方向
                foreach (var l in legs)
                {
                    if (!l || l.Dead || !l.rb) continue;
                    if (l.rb.isKinematic) l.Wake();
                    l.rb.AddForce(downhill * 1.3f, ForceMode.Acceleration);
                }
                yield return null;
            }
        }

        IEnumerator ReleaseKingWhenBaseMoves(Block kingB, List<Block> on)
        {
            for (float t = 0; t < 6f && kingB && !kingB.Dead; t += Time.deltaTime)
            {
                bool moved = on.Exists(b => b && b != kingB && !b.Dead && b.Displacement > 0.4f);
                if (moved) { kingB.rb.isKinematic = false; kingB.rb.WakeUp(); yield break; }
                yield return null;
            }
        }

        // ───────────────────────── 拱心石：没了（炸碎 / 挪开）→ 拱上的整片塌、拱碎
        bool collapsed;
        void KeystoneUpdate()
        {
            if (collapsed) return;
            if (block.Dead || block.Displacement > 0.6f) StartCoroutine(Collapse());
        }

        IEnumerator Collapse()
        {
            collapsed = true;
            var c = block.homePos;
            var castle = block.castle;
            Fx.Dust(c, U * 2f, 16);
            yield return new WaitForSeconds(0.15f);
            // 拱：拱心石下面 1.6 格内的石拱碎掉
            foreach (var b in castle.blocks)
                if (b && !b.Dead && b.def.id == "S_Arch" && Vector3.Distance(b.homePos, c) < 1.8f * U) b.Shatter();
            Sfx.Collapse(c);
            // 压在拱上的：水平 2 格内、比拱心石高的全醒，往外塌
            foreach (var b in castle.blocks)
            {
                if (!b || b.Dead || b == block) continue;
                var d = b.homePos - c;
                if (d.y > -0.2f * U && new Vector2(d.x, d.z).magnitude < 2.2f * U)
                {
                    if (b.isKing) { if (b.rb) { b.rb.isKinematic = false; b.rb.WakeUp(); } if (b.rb) b.rb.AddForce((Vector3.ProjectOnPlane(d, Vector3.up).normalized + Vector3.down) * b.rb.mass * 0.6f, ForceMode.Impulse); continue; }
                    b.Wake();
                    if (b.rb) b.rb.AddForce((Vector3.ProjectOnPlane(d, Vector3.up).normalized * 0.4f + Vector3.down * 0.3f) * b.rb.mass, ForceMode.Impulse);
                }
            }
        }

        // ───────────────────────── 铜钟：挨一下 → 晃 + 嗡；1.5 秒内三口都响 → 盾墙倒下
        float ringT = -9f, swing, swingV;
        Quaternion bellRest; bool bellInit;
        void Ring()
        {
            ringT = Time.time;
            swingV += 220f;
            block.Hit(1f);
            Sfx.Bell(block.WorldBounds.center, 0.8f + 0.2f * (BellIndex() % 3));
            Fx.Sparkle(block.WorldBounds.center, new Color(1f, 0.85f, 0.5f), U * 0.6f);
            // 三口都在 1.5 秒内响过 → 开门
            var bells = all.FindAll(m => m && m.kind == Kind.Bell && m.block && m.block.castle == block.castle);
            if (Application.isEditor) Debug.Log($"[钟] 响 {name}@{transform.localPosition.x:F2}，同城 {bells.Count} 口，1.5 秒内响过 {bells.FindAll(m => Time.time - m.ringT < 1.5f).Count} 口");
            if (bells.Count >= 3 && bells.TrueForAll(m => Time.time - m.ringT < 1.5f)) block.castle.StartCoroutine(OpenGate(block.castle));
        }
        int BellIndex() => Mathf.RoundToInt(transform.position.x * 37f) & 3;

        void BellUpdate()
        {
            if (!bellInit) { bellRest = transform.localRotation; bellInit = true; }
            // 阻尼摆：被敲一下晃 2～3 下停住
            swingV += -swing * 60f * Time.deltaTime - swingV * 2.2f * Time.deltaTime;
            swing += swingV * Time.deltaTime;
            transform.localRotation = bellRest * Quaternion.Euler(swing, 0, 0);
            bool lit = Time.time - ringT < 1.5f;
            block.Hit(lit ? 0.35f + 0.25f * Mathf.Sin(Time.time * 12f) : 0f);   // 响着的钟闪着暖光：看得出“这口已经响了，还差几口”
        }

        static IEnumerator OpenGate(Castle castle)
        {
            if (gateOpenedFor.Contains(castle)) yield break;
            gateOpenedFor.Add(castle);
            if (Application.isEditor) Debug.Log($"[钟] 三口齐响 → 开门，盾墙 {castle.blocks.FindAll(b => b && !b.Dead && b.def.id == "S_Shield").Count} 块");
            Sfx.Bell(castle.transform.position, 0.6f);
            yield return new WaitForSeconds(0.4f);
            // 盾墙：先抖（预警）→ 往后倒下
            var shields = castle.blocks.FindAll(b => b && !b.Dead && b.def.id == "S_Shield");
            for (float t = 0; t < 0.6f; t += Time.deltaTime)
            {
                foreach (var s in shields) if (s && s.rb && s.rb.isKinematic) s.transform.position = s.homePos + Random.insideUnitSphere * 0.012f * castle.unit;
                yield return null;
            }
            foreach (var s in shields)
            {
                if (!s) continue;
                s.transform.position = s.homePos;
                s.Wake();
                // 往前倒（朝玩家这边）：往后推会靠在帅旗的小堡上倒不下去
                if (s.rb) { s.rb.AddForceAtPosition(-castle.transform.forward * s.rb.mass * 1.6f, s.WorldBounds.center + Vector3.up * s.WorldBounds.extents.y, ForceMode.Impulse); }
                Fx.Dust(s.WorldBounds.center, castle.unit * 1.5f, 12);
            }
            Sfx.Collapse(castle.transform.position);
        }
        static readonly HashSet<Castle> gateOpenedFor = new();
        public static void ResetLevel() { gateOpenedFor.Clear(); BlownCount = 0; }
        /// <summary>这一关火药桶真正爆了几次（测试用）</summary>
        public static int BlownCount;

        // ───────────────────────── 吊秤（11-27 改）：帅旗站在 A 盘（-X）上；打进 B 盘（+X）的弹留在盘里、越压越沉（每发约 8°）；
        //   压到 3 发重 → 秤杆翻过去，A 盘把帅旗掀飞。分裂丸一发能进一两颗，炸丸算两发重。看得见盘里越堆越多（有过程）。
        float angle, angVel, weight; bool launched; Quaternion beamRest; bool beamInit;
        readonly List<Transform> panBalls = new();
        bool creaked;
        public float ScaleAngle => angle;
        public bool ScaleLaunched => launched;
        /// <summary>测试用：直接往 B 盘里加 n 发重</summary>
        public void DebugKick(int n) { AddWeight(n, PanWorld(+1)); }
        Block KingOnPan => block.castle ? block.castle.king : null;
        const float PanX = 2.0f, PanY = -1.3f;
        Vector3 PanWorld(float side) => transform.TransformPoint(new Vector3(side * PanX, PanY, 0) * BlockLibrary.G);

        void ScaleHit(Vector3 p, Block hit, float w, float radius = 0f)
        {
            if (launched) return;
            var pb = PanWorld(+1);
            float d = Vector3.Distance(p, pb) / U;
            float lx = transform.InverseTransformPoint(p).x / BlockLibrary.G;   // 落点在秤杆上的位置（格，+ = B 那头）
            if (Debug.isDebugBuild && Application.isEditor) Debug.Log($"[秤] 弹落 {p:F2} 离 B 盘 {d:0.00} 格 秤杆位置 {lx:0.0} 格 hit={(hit ? hit.def.id : "-")}");
            if (radius > 0f) { if (d * U < radius) AddWeight(w, p); return; }   // 炸丸的冲击：压一下（不留弹）
            if (d < 1.6f || (hit == block && lx > 0.8f)) AddWeight(w, p);   // 11-27 放宽（1.3 → 1.6 格）：秤盘是个圆盘，擦着盘沿落下也算，别让玩家觉得“明明打中了”
        }

        void AddWeight(float w, Vector3 p)
        {
            weight += w;
            angVel += 30f * w;                          // 压下去时先往下一沉、晃两下再停在新位置
            block.Hit(0.8f);
            Sfx.Hit(Ammo.Stone, Mat.Wood, PanWorld(+1));
            // 盘里留一颗弹（看得见越堆越多）
            int n = Mathf.Min(6, Mathf.CeilToInt(weight));
            while (panBalls.Count < n)
            {
                var ball = BallLook.Build(transform, Ammo.Stone, 0.11f * BlockLibrary.G);
                int k = panBalls.Count;
                float a = k * 2.4f, r = k == 0 ? 0f : 0.22f;
                ball.localPosition = new Vector3(PanX + Mathf.Cos(a) * r, PanY + 0.1f + (k >= 4 ? 0.15f : 0f), Mathf.Sin(a) * r) * BlockLibrary.G;
                panBalls.Add(ball);
            }
        }

        void ScaleUpdate()
        {
            if (!beamInit) { beamRest = transform.rotation; beamInit = true; }
            if (launched) return;
            // 弹簧阻尼，平衡位置 = 盘里的重量 × 8°
            float target = weight * 8f;
            angVel += (-(angle - target) * 4f - angVel * 2.4f) * Time.deltaTime;
            angle += angVel * Time.deltaTime;
            angle = Mathf.Clamp(angle, -8f, 35f);
            if (angle > 12f && !creaked) { creaked = true; Sfx.Tremble(transform.position); }   // 翘过 12°：吱嘎一声（再压一下就要翻）
            var axis = block.castle.transform.forward;
            var rot = Quaternion.AngleAxis(-angle, axis) * beamRest;   // B 盘（+X）往下沉 → A 盘往上翘
            block.rb.MoveRotation(rot);
            transform.rotation = rot;
            var k = KingOnPan;
            if (k && !k.Dead && k.rb && k.rb.isKinematic)
            {
                var pa = PanWorld(-1) + Vector3.up * 0.06f * U;
                k.rb.MovePosition(pa); k.transform.position = pa;
            }
            if (weight >= 3f && angle > 20f) Launch();
        }

        void Launch()
        {
            launched = true;
            var k = KingOnPan;
            Sfx.Collapse(transform.position);
            if (!k || k.Dead) return;
            k.Wake();
            if (k.rb)
            {
                var out_ = -Vector3.Cross(Vector3.up, block.castle.transform.forward);   // 往 A 盘外侧（-X）甩
                k.rb.AddForce((Vector3.up * 1.4f + out_ * 0.8f) * k.rb.mass * 1.6f, ForceMode.Impulse);
                k.rb.AddTorque(block.castle.transform.forward * k.rb.mass * 0.05f, ForceMode.Impulse);
            }
            Fx.Dust(PanWorld(-1), U * 1.2f, 10);
        }
    }
}

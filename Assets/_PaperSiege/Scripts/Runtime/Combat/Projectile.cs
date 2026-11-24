// 弹：飞行（自定义重力）、分裂、命中效果（石 / 火 / 冰 / 炸）、被手吸住（挡 / 接）。
using System.Collections.Generic;
using UnityEngine;

namespace PaperSiege
{
    public static class Ballistics
    {
        public static Vector3 G => Physics.gravity * GameTuning.I.shotGravity;

        /// <summary>固定飞行时间 t，从 a 打到 b 需要的初速度</summary>
        public static Vector3 VelocityFor(Vector3 a, Vector3 b, float t) => (b - a) / t - 0.5f * G * t;

        /// <summary>给定出膛速度大小，打到 b 的两个解（低抛 / 高抛）。没解返回 false</summary>
        public static bool Solve(Vector3 a, Vector3 b, float speed, out Vector3 low, out Vector3 high)
        {
            low = high = Vector3.zero;
            float g = -G.y;
            var d = b - a;
            var flat = new Vector3(d.x, 0, d.z);
            float x = flat.magnitude, y = d.y, v2 = speed * speed;
            float disc = v2 * v2 - g * (g * x * x + 2 * y * v2);
            if (disc < 0 || x < 1e-4f) return false;
            float r = Mathf.Sqrt(disc);
            var dir = flat / x;
            float aLow = Mathf.Atan((v2 - r) / (g * x)), aHigh = Mathf.Atan((v2 + r) / (g * x));
            low = (dir * Mathf.Cos(aLow) + Vector3.up * Mathf.Sin(aLow)) * speed;
            high = (dir * Mathf.Cos(aHigh) + Vector3.up * Mathf.Sin(aHigh)) * speed;
            return true;
        }

        /// <summary>沿弹道前向模拟，碰到实体（积木 / 桌面 / 地面）就停。返回落点；points 收集路径点</summary>
        public static Vector3 Predict(Vector3 p, Vector3 v, float radius, List<Vector3> points, float maxT = 3f, float dt = 0.02f)
        {
            points?.Clear();
            points?.Add(p);
            for (float t = 0; t < maxT; t += dt)
            {
                var nv = v + G * dt;
                var np = p + (v + nv) * 0.5f * dt;
                var seg = np - p;
                if (Physics.SphereCast(p, radius * 0.8f, seg.normalized, out var hit, seg.magnitude, Layers.SolidMask, QueryTriggerInteraction.Ignore))
                {
                    points?.Add(hit.point);
                    return hit.point;
                }
                p = np; v = nv;
                points?.Add(p);
                if (p.y < -0.5f) break;
            }
            return p;
        }
    }

    public class Projectile : MonoBehaviour
    {
        public enum State { Flying, Held, Spent }
        public static readonly List<Projectile> Live = new();

        public Ammo type;
        public Side from;
        public State state = State.Flying;
        public Rigidbody rb;
        public float radius;
        public Transform visual;
        bool didSplit;
        float spentT;
        GameTuning T => GameTuning.I;

        public bool Catchable => from == Side.Enemy && state == State.Flying;
        static Material trailMat;
        static Material TrailMat
        {
            get
            {
                if (trailMat) return trailMat;
                trailMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                trailMat.SetFloat("_Surface", 1); trailMat.SetOverrideTag("RenderType", "Transparent");
                trailMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); trailMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                trailMat.SetInt("_ZWrite", 0); trailMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); trailMat.renderQueue = 3000;
                return trailMat;
            }
        }

        public static Projectile Spawn(Ammo type, Side from, Vector3 pos, Vector3 vel)
        {
            var T = GameTuning.I;
            float r = from == Side.Player ? T.playerBallRadius : T.enemyBallRadius;
            var go = new GameObject($"Shot_{type}_{from}");
            go.layer = Layers.Shot;
            go.transform.position = pos;
            var p = go.AddComponent<Projectile>();
            p.type = type; p.from = from; p.radius = r;
            var col = go.AddComponent<SphereCollider>(); col.radius = r;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = from == Side.Player ? T.playerBallMass : T.enemyBallMass;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearVelocity = vel;
            rb.angularVelocity = Random.insideUnitSphere * 6f;
            p.rb = rb;
            p.visual = BallLook.Build(go.transform, type, r);
            // 淡淡的拖尾：看得清弹道
            var tr = go.AddComponent<TrailRenderer>();
            tr.time = 0.22f; tr.minVertexDistance = 0.01f;
            tr.widthCurve = AnimationCurve.Linear(0, r * 1.3f, 1, 0);
            tr.material = TrailMat;
            var tc = BallLook.Tint(type);
            tr.colorGradient = new Gradient { colorKeys = new[] { new GradientColorKey(Color.Lerp(tc, Color.white, 0.5f), 0), new GradientColorKey(tc, 1) }, alphaKeys = new[] { new GradientAlphaKey(0.55f, 0), new GradientAlphaKey(0, 1) } };
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Live.Add(p);
            return p;
        }

        void OnDestroy() => Live.Remove(this);

        void FixedUpdate()
        {
            if (state != State.Flying || rb.isKinematic) return;
            rb.AddForce(Physics.gravity * (T.shotGravity - 1f), ForceMode.Acceleration);   // 自带重力 + 这一项 = 重力 × shotGravity
            if (type == Ammo.Split && !didSplit && rb.linearVelocity.y < 0f) Split();
        }

        void Update()
        {
            if (state == State.Spent)
            {
                spentT += Time.deltaTime;
                if (spentT > 2.2f) transform.localScale = Vector3.one * Mathf.Max(0, 1f - (spentT - 2.2f) / 0.4f);
                if (spentT > 2.6f) Destroy(gameObject);
            }
            if (transform.position.y < -2f) Destroy(gameObject);
        }

        void Split()
        {
            didSplit = true;
            var v = rb.linearVelocity;
            var side = Vector3.Cross(Vector3.up, v).normalized;
            int n = Mathf.Max(2, T.splitCount);
            for (int i = 0; i < n; i++)
            {
                float a = (i - (n - 1) * 0.5f) * T.splitAngle;
                var nv = Quaternion.AngleAxis(a, Vector3.up) * v;
                Spawn(Ammo.Stone, from, transform.position + side * (i - (n - 1) * 0.5f) * radius * 2.5f, nv);
            }
            Fx.Sparkle(transform.position, new Color(1f, 0.9f, 0.6f), radius * 3f);
            Destroy(gameObject);
        }

        void OnCollisionEnter(Collision c)
        {
            if (state != State.Flying) return;
            var p = c.GetContact(0).point;
            var block = c.collider.GetComponent<Block>();
            Impact(p, block, c.relativeVelocity);
        }

        void Impact(Vector3 p, Block block, Vector3 relVel)
        {
            state = State.Spent;
            gameObject.layer = Layers.Shot;
            var castle = block ? block.castle : NearestCastle(p);
            float unit = castle ? castle.unit : BlockLibrary.G;
            // 打中帅旗：只扣耐久（不推倒它）；炸丸在下面 Explode 里扣
            if (block && block.isKing && type != Ammo.Bomb)
            {
                castle.DamageKing(1, transform.position - rb.linearVelocity.normalized);
                if (type == Ammo.Fire) block.Ignite();
                if (type == Ammo.Ice) block.Chill(GameTuning.I.freezeSeconds);
                Sfx.Hit(type, Mat.Wood, p);
                return;
            }
            Sfx.Hit(type, block ? block.def.mat : Mat.Wood, p);
            switch (type)
            {
                case Ammo.Bomb:
                    Explode(p, T.bombRadius * unit);
                    Destroy(gameObject);
                    return;
                case Ammo.Ice:
                    Fx.IceBurst(p, T.iceRadius * unit);
                    foreach (var b in BlocksNear(p, T.iceRadius * unit)) b.Chill(T.freezeSeconds);
                    if (castle) castle.WakeAround(p, 0.6f * unit, block);
                    break;
                case Ammo.Fire:
                    Fx.Sparkle(p, new Color(1f, 0.6f, 0.2f), unit * 0.6f);
                    var target = block && block.Wood ? block : NearestWood(p, 1.3f * unit);
                    if (target) { target.fireGen = 0; target.Ignite(); }
                    if (castle) castle.WakeAround(p, 0.8f * unit, block);
                    break;
                default:
                    Fx.Dust(p, unit);
                    if (castle) castle.WakeAround(p, T.wakeRadius * unit, block);
                    break;
            }
            if (block && !block.isKing && block.rb && !block.rb.isKinematic)
            {
                // 额外推一把（玩具弹太轻，纯物理推不动城）
                block.rb.AddForceAtPosition(-relVel * rb.mass * T.hitImpulse, p, ForceMode.Impulse);
                block.Hit(1f);
            }
        }

        public static void Explode(Vector3 p, float r)
        {
            var T = GameTuning.I;
            Fx.Explosion(p, r);
            var blocks = BlocksNear(p, r);
            var castles = new HashSet<Castle>();
            foreach (var b in blocks) if (b.castle) castles.Add(b.castle);
            foreach (var c in castles) c.WakeAround(p, r);
            foreach (var b in blocks)
            {
                if (b.isKing) { b.castle?.DamageKing(2, p); continue; }   // 炸丸：帅旗 -2（不炸碎、不推飞）
                float d = Vector3.Distance(b.WorldBounds.ClosestPoint(p), p) / r;
                // 冻脆的块：范围内全碎（冰 + 炸 = 碎一大片）；石块和木块：离得近的碎
                if (b.Frozen || (b.def.mat == Mat.Stone && d < 0.55f) || (b.def.mat == Mat.Wood && d < 0.4f && !b.isKing)) { b.Shatter(); continue; }
                if (b.rb && !b.rb.isKinematic)
                {
                    var dir = (b.WorldBounds.center - p).normalized + Vector3.up * 0.4f;
                    b.rb.AddForce(dir.normalized * T.bombForce * (1f - d) * b.rb.mass, ForceMode.Impulse);
                    b.Hit(1f - d);
                }
            }
        }

        static readonly Collider[] buf = new Collider[256];
        public static List<Block> BlocksNear(Vector3 p, float r)
        {
            var list = new List<Block>();
            int n = Physics.OverlapSphereNonAlloc(p, r, buf, Layers.BlockMask);
            for (int i = 0; i < n; i++) { var b = buf[i].GetComponent<Block>(); if (b && !b.Dead && !list.Contains(b)) list.Add(b); }
            return list;
        }

        static Block NearestWood(Vector3 p, float r)
        {
            Block best = null; float bd = float.MaxValue;
            foreach (var b in BlocksNear(p, r))
            {
                if (!b.Wood) continue;
                float d = (b.WorldBounds.ClosestPoint(p) - p).sqrMagnitude;
                if (d < bd) { bd = d; best = b; }
            }
            return best;
        }

        static Castle NearestCastle(Vector3 p)
        {
            Castle best = null; float bd = float.MaxValue;
            foreach (var c in FindObjectsByType<Castle>(FindObjectsSortMode.None))
            {
                float d = (c.transform.position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        // ── 被手吸住（Defense 调用）
        public void Hold()
        {
            state = State.Held;
            rb.isKinematic = true;
            gameObject.layer = Layers.Held;
        }

        public void Deflect(Vector3 vel)
        {
            state = State.Spent;
            rb.isKinematic = false;
            gameObject.layer = Layers.Held;   // 挡开的弹不再碰任何东西（免得掉下来砸到自己的城），飞一小段就淡出
            rb.linearVelocity = vel;
            spentT = 1.4f;
        }
    }
}

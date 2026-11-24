// 一座城：按布局把积木搭起来；负责苏醒连锁、火的蔓延、帅旗灯笼倒下判定、完整度、修城空位。
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PaperSiege
{
    public enum Side { Player, Enemy }

    [Serializable]
    public struct Placement
    {
        public string piece;
        public Paint paint;
        public Vector3 pos;    // 格，相对城的原点（原点在城的地面中心，城的正面朝本地 -Z）
        public float yaw;
        public Placement(string piece, Paint paint, Vector3 pos, float yaw = 0) { this.piece = piece; this.paint = paint; this.pos = pos; this.yaw = yaw; }
    }

    public class CastleLayout
    {
        public string name;
        public readonly List<Placement> items = new();
        public Vector3 king;   // 帅旗灯笼底座位置（格）
        public CastleLayout(string name) { this.name = name; }
        public CastleLayout Add(string piece, Paint paint, float x, float y, float z, float yaw = 0) { items.Add(new Placement(piece, paint, new Vector3(x, y, z), yaw)); return this; }
    }

    public class Castle : MonoBehaviour
    {
        public Side side;
        public float unit = BlockLibrary.G;   // 每格多少米
        public bool lowDetail;                // 远处的城用 LOD1
        public readonly List<Block> blocks = new();
        public Block king;
        public CastleLayout layout;
        public event Action<Castle> KingDown;
        public bool IsKingDown { get; private set; }
        public float totalVolume;
        float fireTick;
        readonly HashSet<Block> burning = new();
        static PhysicsMaterial woodPM, stonePM;

        public static Castle Build(CastleLayout layout, Side side, Vector3 origin, Quaternion facing, float unit, bool lowDetail, Transform parent = null)
        {
            var go = new GameObject($"Castle_{side}_{layout.name}");
            if (parent) go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(origin, facing);
            var c = go.AddComponent<Castle>();
            c.side = side; c.unit = unit; c.lowDetail = lowDetail; c.layout = layout;
            int i = 0;
            foreach (var p in layout.items) c.blocks.Add(c.MakeBlock(p, i++));
            c.king = c.MakeBlock(new Placement("King", Paint.Cream, layout.king), -1);
            c.king.isKing = true;
            c.blocks.Add(c.king);
            c.totalVolume = c.blocks.Where(b => !b.isKing).Sum(b => b.def.Volume);
            return c;
        }

        public Vector3 ToWorld(Vector3 g) => transform.TransformPoint(g * unit);

        Block MakeBlock(Placement p, int slot)
        {
            var def = BlockLibrary.Def(p.piece);
            var lib = BlockLibrary.I;
            var go = new GameObject(p.piece);
            go.layer = Layers.Block;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = p.pos * unit;
            go.transform.localRotation = Quaternion.Euler(0, p.yaw, 0);
            go.transform.localScale = Vector3.one * (unit / BlockLibrary.G);
            var b = go.AddComponent<Block>();
            b.castle = this; b.def = def; b.paint = p.paint; b.slot = slot;
            // 外观
            var pm = lib ? lib.Meshes(p.piece) : null;
            var pole = lib ? lib.Meshes("K_Pole") : null;
            if (p.piece == "King" && pole != null && pole.lod0 && lib.propMaterial)
            {
                Mesh Pick(PieceMeshes m) => lowDetail && m.lod1 ? m.lod1 : m.lod0;
                go.AddComponent<MeshFilter>().sharedMesh = Pick(pole);
                b.mr = go.AddComponent<MeshRenderer>();
                b.mr.sharedMaterial = lib.propMaterial;
                foreach (var (id, at) in new[] { ("K_Flag", PropAnchors.FlagAttach), ("K_Lantern", PropAnchors.LanternHook) })
                {
                    var m = lib.Meshes(id);
                    if (m == null || !m.lod0) continue;
                    var part = new GameObject(id);
                    part.transform.SetParent(go.transform, false);
                    part.transform.localPosition = at;
                    part.AddComponent<MeshFilter>().sharedMesh = id == "K_Flag" ? m.lod0 : Pick(m);
                    part.AddComponent<MeshRenderer>().sharedMaterial = lib.propMaterial;
                    if (id == "K_Flag") part.AddComponent<FlagSway>();
                }
            }
            else if (pm != null && (pm.lod0 || pm.lod1))
            {
                go.AddComponent<MeshFilter>().sharedMesh = lowDetail && pm.lod1 ? pm.lod1 : (pm.lod0 ? pm.lod0 : pm.lod1);
                b.mr = go.AddComponent<MeshRenderer>();
                b.mr.sharedMaterial = lib.Paint(def.mat == Mat.Stone ? Paint.Stone : p.paint);
            }
            else b.mr = Placeholder.Build(go.transform, p.piece, def, p.paint);
            // 碰撞体（标称尺寸，模型空间单位 = 1 格 0.05 m）
            woodPM ??= new PhysicsMaterial("Wood") { dynamicFriction = 0.6f, staticFriction = 0.75f, bounciness = 0.04f, frictionCombine = PhysicsMaterialCombine.Average, bounceCombine = PhysicsMaterialCombine.Minimum };
            stonePM ??= new PhysicsMaterial("Stone") { dynamicFriction = 0.65f, staticFriction = 0.8f, bounciness = 0.02f, frictionCombine = PhysicsMaterialCombine.Average, bounceCombine = PhysicsMaterialCombine.Minimum };
            foreach (var bx in def.boxes)
            {
                var col = go.AddComponent<BoxCollider>();
                col.center = bx.c * BlockLibrary.G;
                col.size = bx.s * BlockLibrary.G;
                col.sharedMaterial = def.mat == Mat.Stone ? stonePM : woodPM;
            }
            // 刚体：开局冻结
            var rb = go.AddComponent<Rigidbody>();
            float density = def.mat == Mat.Stone ? 1500f : 600f;   // kg/m³（石块按玩具石膏块的手感，不按真石头）
            rb.mass = Mathf.Max(0.02f, def.Volume * Mathf.Pow(unit, 3) * density);
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.maxDepenetrationVelocity = 0.5f;
            b.rb = rb;
            b.homePos = go.transform.position;
            b.homeRot = go.transform.rotation;
            return b;
        }

        // ── 苏醒：命中点附近的块 + 压在它们上面的一串块
        public void WakeAround(Vector3 p, float radius, Block hit = null)
        {
            var seen = new HashSet<Block>();
            if (hit && hit.castle == this) WakeChain(hit, seen);
            foreach (var c in Physics.OverlapSphere(p, radius, Layers.BlockMask))
            {
                var b = c.GetComponent<Block>();
                if (b && b.castle == this) WakeChain(b, seen);
            }
        }

        public void OnBlockWoke(Block b) => WakeAbove(b, new HashSet<Block> { b });

        void WakeChain(Block b, HashSet<Block> seen)
        {
            if (!seen.Add(b) || b.Dead) return;
            b.Wake();
            WakeAbove(b, seen);
        }

        /// <summary>叫醒压在 b 顶上的块（它一动，上面的就可能没了支撑）</summary>
        void WakeAbove(Block b, HashSet<Block> seen)
        {
            var bb = b.WorldBounds;
            var center = new Vector3(bb.center.x, bb.max.y + 0.12f * unit, bb.center.z);
            var half = new Vector3(bb.extents.x * 0.95f, 0.14f * unit, bb.extents.z * 0.95f);
            foreach (var c in Physics.OverlapBox(center, half, Quaternion.identity, Layers.BlockMask))
            {
                var o = c.GetComponent<Block>();
                if (o && o != b && o.castle == this && !o.Dead) WakeChain(o, seen);
            }
        }

        // ── 火
        public void OnIgnite(Block b) { burning.Add(b); Fx.AttachFire(b); Sfx.Fire(b.transform.position); }
        public void OnExtinguish(Block b) { burning.Remove(b); Fx.Steam(b.WorldBounds.center, unit); Fx.DetachFire(b); }

        public void OnBlockDead(Block b)
        {
            burning.Remove(b);
            Fx.DetachFire(b);
            if (b.isKing) DeclareKingDown();
        }

        public IReadOnlyCollection<Block> Burning => burning;
        public float BurnSeconds = 9f;   // 一块木头从点着到烧透
        public float SpreadChance = 0.28f;
        public int MaxFireGen = 3;   // 从第一块往外最多传几代（一发火丸大约烧 6～8 块，最多 15 块）   // 每 0.6 秒、每个相接的木块被点着的概率（这块烧到 30% 以后才往外蔓延）

        void Update()
        {
            if (king && !IsKingDown)
            {
                // 倒地：倾斜超过 60°，或者落到了地面（底下的楼塌了但旗还立着、还在楼上，不算）
                float tilt = Vector3.Angle(king.transform.up, Vector3.up);
                float above = (king.transform.position.y - transform.position.y) / unit;
                if (tilt > 60f || (above < 0.6f && king.homePos.y - transform.position.y > 0.6f * unit)) DeclareKingDown();
            }
            if (burning.Count == 0) return;
            fireTick += Time.deltaTime;
            foreach (var b in burning) b.burn = Mathf.Min(1f, b.burn + Time.deltaTime / BurnSeconds);
            if (fireTick < 0.6f) return;
            fireTick = 0;
            foreach (var b in burning.ToArray())
            {
                if (b.Dead) continue;
                // 烧到一半：变轻、变脆，塌下来
                if (b.burn > 0.5f && b.rb.mass > 0.02f && b.rb.isKinematic) { b.rb.mass *= 0.6f; b.Wake(); }
                if (b.burn >= 1f) { b.Ash(); continue; }
                // 蔓延到相接的木块（自己烧到 30% 以后才往外传）
                if (b.burn < 0.3f) continue;
                var bb = b.WorldBounds;
                foreach (var c in Physics.OverlapBox(bb.center, bb.extents + Vector3.one * 0.12f * unit, Quaternion.identity, Layers.BlockMask))
                {
                    var o = c.GetComponent<Block>();
                    if (b.spreadLeft <= 0 || b.fireGen >= MaxFireGen) break;
                    if (o && o != b && !o.burning && o.Wood && o.burn < 1f && !o.doused && UnityEngine.Random.value < SpreadChance)
                    {
                        o.fireGen = b.fireGen + 1;
                        o.Ignite();
                        if (o.burning) b.spreadLeft--;
                    }
                }
            }
        }

        void DeclareKingDown()
        {
            if (IsKingDown) return;
            IsKingDown = true;
            KingDown?.Invoke(this);
        }

        /// <summary>完整度：还在原位的块（按体积）÷ 全部</summary>
        public float Integrity
        {
            get
            {
                if (totalVolume <= 0) return 0;
                float v = 0;
                foreach (var b in blocks) if (!b.isKing && b.InPlace) v += b.def.Volume;
                return v / totalVolume;
            }
        }

        public IEnumerable<Block> Alive => blocks.Where(b => !b.Dead);

        // ── 修城：空位 = 原来那块没了 / 被打离原位，而且那个位置现在空着、下面有支撑
        public bool SlotEmpty(int slot)
        {
            if (slot < 0 || slot >= layout.items.Count) return false;
            var b = blocks[slot];
            if (!b.Dead && b.InPlace) return false;
            var p = layout.items[slot];
            var def = BlockLibrary.Def(p.piece);
            var rot = transform.rotation * Quaternion.Euler(0, p.yaw, 0);
            var center = ToWorld(p.pos) + rot * (Vector3.up * def.size.y * 0.5f * unit);
            var half = new Vector3(def.size.x, def.size.y, def.size.z) * (0.4f * unit);
            foreach (var c in Physics.OverlapBox(center, half, rot, Layers.BlockMask))
            {
                var o = c.GetComponent<Block>();
                if (o && !o.Dead) return false;
            }
            if (p.pos.y < 0.05f) return true;   // 地面那一层
            var below = ToWorld(p.pos) - Vector3.up * 0.1f * unit;
            return Physics.CheckBox(below, new Vector3(def.size.x * 0.3f, 0.08f, def.size.z * 0.3f) * unit, rot, Layers.BlockMask | Layers.EnvMask);
        }

        public Vector3 SlotCenter(int slot)
        {
            var p = layout.items[slot];
            var def = BlockLibrary.Def(p.piece);
            return ToWorld(p.pos) + transform.rotation * (Vector3.up * def.size.y * 0.5f * unit);
        }

        /// <summary>把一块补上的积木放进空位：替换原块（原块如果还在就移走）</summary>
        public Block Refill(int slot)
        {
            var old = blocks[slot];
            if (old) { if (!old.Dead) old.Kill(true); }
            var nb = MakeBlock(layout.items[slot], slot);
            blocks[slot] = nb;
            return nb;
        }

        public void Cleanup()
        {
            foreach (var b in blocks) if (b) Fx.DetachFire(b);
            Destroy(gameObject);
        }
    }
}

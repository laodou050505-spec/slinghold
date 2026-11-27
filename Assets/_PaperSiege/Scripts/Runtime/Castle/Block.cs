// 一块积木：冻结 / 苏醒、燃烧、冰冻、碎裂。
// 冻结 = kinematic（城搭好后静置绝不会塌、不会抖）；被打到或被别的块撞到才苏醒成动态刚体，停稳后重新冻结。
// 状态变化都有过程：着火先冒烟变焦、烧透后缩成灰；冰冻是一层霜慢慢长上去、化掉时慢慢退；碎裂有碎片。
using System.Collections.Generic;
using UnityEngine;

namespace PaperSiege
{
    public static class Layers
    {
        public const int Block = 8, Shot = 9, Held = 10, Env = 11;
        public const int BlockMask = 1 << Block, EnvMask = 1 << Env, SolidMask = (1 << Block) | (1 << Env);
    }

    public class Block : MonoBehaviour
    {
        public Castle castle;
        public PieceDef def;
        public Paint paint;
        public Rigidbody rb;
        public MeshRenderer mr;
        public int slot;               // 在城布局里的序号（修城时按它找空位）
        public bool isKing;
        public Vector3 homePos;        // 搭好时的世界位置 / 朝向
        public Quaternion homeRot;
        public bool Dynamic => rb && !rb.isKinematic;
        public bool Dead { get; private set; }

        // 元素状态
        public bool burning;
        public float burn;             // 0～1，烧焦程度；到 1 化成灰
        public float frost;            // 0～1，冰冻程度（显示用，跟着 frozenT 走）
        public float frozenT;          // 剩余冰冻秒数
        public int fireGen;            // 火从第几代传过来（火丸直接点着的是 0）
        public int spreadLeft;         // 还能再点着几块
        public bool doused;            // 被冰浇过：不会再着
        public float burnRate = 1f;    // 燃烧速度倍数（草垛 3 倍）
        public Mech mech;              // 关卡机关（没有就是普通积木）
        float calm, flash, emberPhase;
        MaterialPropertyBlock mpb;
        bool hasStateLook;

        public float Unit => castle ? castle.unit : BlockLibrary.G;
        public bool Wood => def != null && def.mat == Mat.Wood;
        public bool Frozen => frozenT > 0f;

        public Bounds WorldBounds
        {
            get
            {
                var cs = GetComponents<BoxCollider>();
                if (cs.Length == 0) return mr ? mr.bounds : new Bounds(transform.position, Vector3.one * Unit);   // 演示用样品块没有碰撞体
                var b = cs[0].bounds;
                for (int i = 1; i < cs.Length; i++) b.Encapsulate(cs[i].bounds);
                return b;
            }
        }

        /// <summary>离开原位多远（格）</summary>
        public float Displacement => Vector3.Distance(transform.position, homePos) / Unit;
        /// <summary>还算“在原位”（完整度统计用）</summary>
        public bool InPlace => !Dead && Displacement < 0.6f && Quaternion.Angle(transform.rotation, homeRot) < 25f;

        public void Freeze()
        {
            if (!rb) return;
            if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            rb.isKinematic = true;
            calm = 0;
        }

        public void Wake()
        {
            if (Dead || !rb || !rb.isKinematic) return;
            if (mech && mech.Scripted) return;   // 钟、钟架、秤杆、秤柱由机关脚本驱动，不交给物理
            rb.isKinematic = false;
            rb.WakeUp();
            calm = 0;
            castle?.OnBlockWoke(this);
        }

        void FixedUpdate()
        {
            if (Dead || !rb || rb.isKinematic) return;
            // 停稳 0.6 秒后重新冻结（省物理、防微抖）
            if (rb.linearVelocity.sqrMagnitude < 0.0004f && rb.angularVelocity.sqrMagnitude < 0.04f) calm += Time.fixedDeltaTime;
            else calm = 0;
            if (calm > 0.6f) Freeze();
            // 掉出场地太远：当作已经没了（不再参与计算，但模型留着，过一会儿淡出）
            if (transform.position.y < -1f) Kill(false);
        }

        void OnCollisionEnter(Collision c)
        {
            if (Dead) return;
            var other = c.rigidbody;
            float v = c.relativeVelocity.magnitude;
            // 冻住的块被撞一下就碎
            if (Frozen && v > 0.6f && !(mech && mech.Scripted)) { Shatter(); return; }   // 冰池、钟、秤这些机关件冻住了也不会被碰碎（冰池结冰就是它的效果）
            // 帅旗：只有掉下来的积木砸到它（够快）才会被撞醒；弹打它只扣耐久（Projectile 处理）
            if (isKing)
            {
                if (rb.isKinematic && other && !other.isKinematic && other.GetComponent<Block>() && v > 0.6f) Wake();
                return;
            }
            // 被动态的块 / 弹撞到：自己苏醒（kinematic 块不会自己动，必须叫醒）
            if (rb.isKinematic && other && !other.isKinematic && v > 0.25f)
            {
                var ob = other.GetComponent<Block>();
                if (ob == null || ob.castle == castle) castle?.WakeAround(transform.position, 0.6f * Unit, this);
            }
        }

        // ── 火
        public void Ignite()
        {
            if (Dead || !Wood || burning || burn >= 1f || doused) return;
            if (Frozen) { frozenT = 0; return; }   // 冻着的块点不着，只会把冰化掉
            burning = true;
            spreadLeft = Random.Range(1, 4);
            castle?.OnIgnite(this);
        }

        public void Extinguish()
        {
            if (!burning) return;
            burning = false;
            castle?.OnExtinguish(this);
        }

        // ── 冰
        public void Chill(float seconds)
        {
            if (Dead) return;
            if (burning) { Extinguish(); doused = true; return; }   // 冰浇在火上：灭火（不结冰），湿了不会再着
            frozenT = Mathf.Max(frozenT, seconds);
        }

        public void Hit(float strength)
        {
            flash = Mathf.Max(flash, Mathf.Clamp01(strength));
        }

        /// <summary>冻脆的块被撞碎 / 被炸碎的石块：碎片四散，块消失</summary>
        public void Shatter()
        {
            if (Dead) return;
            Fx.Shards(WorldBounds, Frozen ? Fx.IceColor : (def.mat == Mat.Stone ? Fx.StoneColor : Palette.Of(paint)), Unit);
            Kill(true);
        }

        /// <summary>烧透：缩成一小撮灰（有过程，由 Castle 驱动 burn 到 1 后调用）</summary>
        public void Ash()
        {
            if (Dead) return;
            Fx.Ash(WorldBounds, Unit);
            Kill(true);
        }

        public void Kill(bool hide)
        {
            if (Dead) return;
            Dead = true;
            burning = false;
            castle?.OnBlockDead(this);
            if (hide)
            {
                foreach (var c in GetComponents<Collider>()) c.enabled = false;
                if (rb) rb.isKinematic = true;
                StartCoroutine(ShrinkAway(0.35f));
            }
        }

        System.Collections.IEnumerator ShrinkAway(float t)
        {
            var s0 = transform.localScale;
            for (float k = 0; k < t; k += Time.deltaTime)
            {
                transform.localScale = s0 * (1f - Mathf.SmoothStep(0, 1, k / t));
                yield return null;
            }
            gameObject.SetActive(false);
        }

        void Update()
        {
            if (Dead) return;
            float dt = Time.deltaTime;
            if (frozenT > 0f) frozenT -= dt;
            float targetFrost = frozenT > 0f ? 1f : 0f;
            frost = Mathf.MoveTowards(frost, targetFrost, dt * (targetFrost > frost ? 2.5f : 0.5f));   // 结冰 0.4 秒长满，化冰 2 秒退掉
            flash = Mathf.MoveTowards(flash, 0f, dt * 5f);
            if (burning) emberPhase += dt;
            bool need = burn > 0.001f || frost > 0.001f || flash > 0.001f || burning;
            if (!need && !hasStateLook) return;
            mpb ??= new MaterialPropertyBlock();
            mr.GetPropertyBlock(mpb);
            mpb.SetFloat("_Char", burn);
            mpb.SetFloat("_Ember", burning ? 0.55f + 0.45f * Mathf.PerlinNoise(emberPhase * 3f, slot) : burn * 0.15f);
            mpb.SetFloat("_Frost", frost);
            mpb.SetFloat("_Flash", flash);
            mr.SetPropertyBlock(mpb);
            hasStateLook = need;
        }
    }
}

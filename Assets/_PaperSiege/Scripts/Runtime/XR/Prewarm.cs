// 开机预热（11-27 用户：一定要保证不卡顿）。
// 头显（Vulkan / GLES）第一次画某种“着色器 + 关键字 + 渲染状态”组合时，驱动要现编译管线，会卡几十毫秒；
// 在头显里就是画面突然顿一下（第一次着火、第一朵烟花、第一次出光柱……），很容易晕。
// 做法：开机时、封面出现之前，把游戏里会出现的每一种东西真实地在相机正前方各画一帧：
//   ① Resources 里的所有材质（积木 8 种漆、封面、机关、桌子、着色器保底材质）× 带阴影 / 不带阴影；
//   ② 运行时会生成的每一种特效（火、蒸汽、尘、碎块、灰、爆炸、冰爆、闪光、浮尘、五种烟花、小火苗）；
//   ③ 一颗每种弹（带拖尾）。
// 这些东西放在相机前 1.2 米、缩到 1 毫米大小（画了但看不见），透视下玩家什么也看不到；全部在 0.4 秒左右的黑场里完成，
// 黑场本来就是开机启动画面之后、封面出现之前那一下。编辑器里也跑（同一套流程，便于测试），只是没有卡顿可省。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PaperSiege
{
    public static class Prewarm
    {
        public static bool Done { get; private set; }
        public static float Seconds { get; private set; }

        public static IEnumerator Run(Camera cam)
        {
            if (Done || !cam) yield break;
            float t0 = Time.realtimeSinceStartup;
            var root = new GameObject("Prewarm").transform;
            var ct = cam.transform;
            // 相机正前方：只要在视锥里、不被剔除就行；缩得极小，像素上看不见
            root.position = ct.position + ct.forward * 1.2f;
            root.rotation = ct.rotation;
            var made = new List<Object>();

            // ① 所有材质：每种挂在一个小方块上，一半投阴影、一半不投（阴影 pass 也要编译）
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var cubeMesh = cube.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(cube);
            var mats = new HashSet<Material>();
            foreach (var m in Resources.LoadAll<Material>("")) if (m && m.shader) mats.Add(m);
            var lib = BlockLibrary.I;
            if (lib) foreach (Paint p in System.Enum.GetValues(typeof(Paint))) { try { var m = lib.Paint(p); if (m) mats.Add(m); } catch { } }
            int i = 0;
            // 分批：每帧最多新加 8 种材质（一帧里编译太多管线，这一帧本身就会卡很久 —— 虽然是黑场，系统也会判定应用无响应）
            foreach (var m in mats)
            {
                if (i > 0 && i % 8 == 0) yield return null;   // 每帧 8 种（头显上每帧不能超时太多：超了系统会拿上一帧补，画面像冻住）
                var g = new GameObject("pw_" + m.name);
                g.transform.SetParent(root, false);
                g.transform.localPosition = new Vector3((i % 10) * 0.0012f, (i / 10) * 0.0012f, 0);
                g.transform.localScale = Vector3.one * 0.001f;
                g.AddComponent<MeshFilter>().sharedMesh = cubeMesh;
                var r = g.AddComponent<MeshRenderer>();
                r.sharedMaterial = m;
                r.shadowCastingMode = i % 2 == 0 ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                made.Add(g);
                i++;
            }

            float tMats = Time.realtimeSinceStartup - t0;
            yield return null; yield return null;
            // ② 特效：在 root 位置各放一次（特效本身按世界坐标生成，事后统一删掉）
            var p0 = root.position;
            const float u = 0.001f;
            Fx.Dust(p0, u, 2);
            Fx.Steam(p0, u);
            Fx.Shards(new Bounds(p0, Vector3.one * u), Color.gray, u);
            Fx.Ash(new Bounds(p0, Vector3.one * u), u);
            Fx.Explosion(p0, u);
            Fx.IceBurst(p0, u);
            Fx.Sparkle(p0, Color.white, u);
            var mf = Fx.MiniFlame(root, Vector3.zero, u);
            var motes = Fx.Motes(p0, Vector3.one * u, Color.white);
            int fwBefore = Fx.FireworksAlive;
            foreach (Fx.FwKind k in System.Enum.GetValues(typeof(Fx.FwKind)))
                Fx.Firework(p0, Color.white, k, 0.0003f, 0.01f, false);
            // ③ 每种弹（带拖尾）：生成后立刻冻住在相机前
            var balls = new List<Projectile>();
            foreach (Ammo a in System.Enum.GetValues(typeof(Ammo)))
            {
                try
                {
                    var pr = Projectile.Spawn(a, Side.Enemy, p0 + ct.right * 0.002f, Vector3.zero);
                    if (pr) { pr.transform.localScale = Vector3.one * 0.01f; if (pr.TryGetComponent<Rigidbody>(out var rb)) { rb.linearVelocity = Vector3.zero; rb.isKinematic = true; rb.detectCollisions = false; } balls.Add(pr); }
                }
                catch { }
            }
            // 积木烧着的火（火苗序列帧 / 火星 / 烟团三种材质）
            Fx.PrewarmFire(root);

            // 让粒子真的发出来、画几帧（烟花有 0.01 秒升空 + 炸开）
            for (int f = 0; f < 6; f++) yield return null;
            // 粒子挪到相机前（特效里有的按自己的位置偏移生成）
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>())
                if (ps && (ps.transform.position - p0).sqrMagnitude < 4f) ps.transform.position = p0;
            for (int f = 0; f < 4; f++) yield return null;

            // 清理
            foreach (var pr in balls) if (pr) Object.Destroy(pr.gameObject);
            if (mf) Object.Destroy(mf.gameObject);
            if (motes) Object.Destroy(motes.gameObject);
            foreach (var go in Object.FindObjectsByType<Transform>())
                if (go && go.parent == null && go.name.StartsWith("Fx_") && (go.position - p0).sqrMagnitude < 9f) Object.Destroy(go.gameObject);
            Object.Destroy(root.gameObject);
            Done = true;
            Seconds = Time.realtimeSinceStartup - t0;
            Debug.Log($"[木垒][预热] 材质 {mats.Count} 种（{tMats:0.00}s）、特效 {9 + System.Enum.GetValues(typeof(Fx.FwKind)).Length} 种、弹 {balls.Count} 种，共 {Seconds:0.00}s，最长一帧 {HeadsetPerf.PrewarmWorst * 1000f:0}ms");
        }
    }
}

// 特效（代码生成的粒子）：火、蒸汽、碎片、灰、爆炸、冰雾、尘、接住的闪光。
// 火 / 灭火已换手绘贴图（11-26），其余仍是软圆点。所有尺寸按积木格（unit）缩放，敌城放大时特效跟着大。
using System.Collections.Generic;
using UnityEngine;

namespace PaperSiege
{
    public static class Fx
    {
        public static readonly Color IceColor = new(0.78f, 0.9f, 1f);
        public static readonly Color StoneColor = new(0.62f, 0.63f, 0.64f);
        static Material softMat, meshMat;
        static Texture2D soft;
        static Mesh shardMesh;
        static readonly Dictionary<Block, ParticleSystem> fires = new();

        static Material skyMat;
        /// <summary>天上的粒子（烟花）：只在天花板开口里看得到（PaperSiege/SkyParticle），圆形柔点、叠加发光</summary>
        static Material SkyMat
        {
            get
            {
                if (skyMat) return skyMat;
                var sh = Shader.Find("PaperSiege/SkyParticle");
                if (!sh) return Soft;
                _ = Soft;   // 确保圆形柔点贴图已生成
                skyMat = new Material(sh);
                skyMat.SetTexture("_BaseMap", soft);
                return skyMat;
            }
        }

        static Material Soft
        {
            get
            {
                if (softMat) return softMat;
                soft = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 32f;
                        float a = Mathf.Clamp01(1f - d); a = a * a * (3 - 2 * a);
                        soft.SetPixel(x, y, new Color(1, 1, 1, a));
                    }
                soft.Apply();
                var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                softMat = new Material(sh);
                softMat.SetTexture("_BaseMap", soft);
                softMat.SetFloat("_Surface", 1);   // 透明
                softMat.SetFloat("_Blend", 0);
                softMat.SetOverrideTag("RenderType", "Transparent");
                softMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                softMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                softMat.SetInt("_ZWrite", 0);
                softMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                softMat.renderQueue = 3000;
                return softMat;
            }
        }

        static Material MeshMat
        {
            get
            {
                if (meshMat) return meshMat;
                meshMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Simple Lit"));
                return meshMat;
            }
        }

        static ParticleSystem Make(string name, Vector3 pos, bool mesh = false)
        {
            var go = new GameObject("Fx_" + name);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var r = go.GetComponent<ParticleSystemRenderer>();
            if (mesh)
            {
                if (!shardMesh) { var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube); shardMesh = tmp.GetComponent<MeshFilter>().sharedMesh; Object.Destroy(tmp); }
                r.renderMode = ParticleSystemRenderMode.Mesh;
                r.mesh = shardMesh;
                r.sharedMaterial = MeshMat;
            }
            else r.sharedMaterial = Soft;
            var main = ps.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 0;
            return ps;
        }

        static void Burst(ParticleSystem ps, int n, float life)
        {
            var em = ps.emission; em.SetBursts(new[] { new ParticleSystem.Burst(0, (short)n) });
            ps.Play();
            Object.Destroy(ps.gameObject, life + 0.5f);
        }

        // ── 火：附着在块上，跟着块走。三层：手绘火苗序列帧（底部钉在块顶，跳动）+ 往上蹦的火星 + 偶尔一小团卡通棕烟。
        //    贴图 Resources/Fx/fx_flame.png（4×2 帧）、fx_puff.png（2×2 种烟团），由 Tools/cover/fx_textures.py 生成。
        static Material flameMat, puffMat;
        static Material SheetMat(string res, ref Material cache)
        {
            if (cache) return cache;
            var tex = Resources.Load<Texture2D>(res);
            cache = new Material(Soft);
            if (tex) { tex.wrapMode = TextureWrapMode.Clamp; cache.SetTexture("_BaseMap", tex); }
            return cache;
        }
        static Material FlameMat => SheetMat("Fx/fx_flame", ref flameMat);
        static Material PuffMat => SheetMat("Fx/fx_puff", ref puffMat);

        static ParticleSystem Child(Transform parent, string name, Material mat, int fudge)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.sortingFudge = fudge;
            var m = ps.main; m.playOnAwake = false; m.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 0;
            var sh = ps.shape; sh.rotation = new Vector3(-90, 0, 0);   // 发射方向朝上
            return ps;
        }

        static void Sheet(ParticleSystem ps, int x, int y, bool animate)
        {
            var ts = ps.textureSheetAnimation; ts.enabled = true;
            ts.numTilesX = x; ts.numTilesY = y;
            if (animate)
            {
                ts.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0, 1, 1));
                ts.cycleCount = 2;
                ts.startFrame = new ParticleSystem.MinMaxCurve(0, x * y - 1);
            }
            else ts.frameOverTime = new ParticleSystem.MinMaxCurve(0f, 0.999f);   // 每颗随机一种形状、不变
        }

        public static void AttachFire(Block b)
        {
            if (fires.ContainsKey(b)) return;
            float u = b.Unit;
            var root = new GameObject("Fx_Fire").transform;
            root.position = b.WorldBounds.center;
            root.SetParent(b.transform, true);

            // 火苗：轴心在底边，底部贴着烧着的那块木面往上长，边跳边缩（发射点由 FireSurface 从模型表面挑）
            var fl = Child(root, "Flames", FlameMat, -20);
            fl.GetComponent<ParticleSystemRenderer>().pivot = new Vector3(0, 0.45f, 0);
            var m = fl.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.8f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.75f * u, 1.25f * u);
            m.startRotation = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
            m.maxParticles = 24;
            var sh = fl.shape; sh.enabled = false;
            Sheet(fl, 4, 2, true);
            var sz = fl.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, 0.35f), new Keyframe(0.22f, 1f), new Keyframe(0.7f, 0.85f), new Keyframe(1, 0f)));
            fl.Play();

            // 火星：几颗小亮点往上蹦、左右飘
            var sp = Child(root, "Embers", Soft, -25);
            m = sp.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.1f * u, 0.18f * u);
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.55f, 0.2f));
            m.maxParticles = 10;
            sh = sp.shape; sh.enabled = false;
            var no = sp.noise; no.enabled = true; no.strength = 0.8f * u; no.frequency = 2f;
            Fade(sp);
            sp.Play();

            // 烟：火苗上方冒出一小团棕灰卡通烟，慢慢变大、往上飘、淡掉
            var sm = Child(root, "Smoke", PuffMat, 20);
            m = sm.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 1.7f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.6f * u, 0.9f * u);
            m.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            m.startColor = new Color(0.46f, 0.4f, 0.36f, 0.8f);
            m.maxParticles = 8;
            sh = sm.shape; sh.enabled = false;
            Sheet(sm, 2, 2, false);
            sz = sm.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 0.6f, 1, 1.9f));
            var rot = sm.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            Fade(sm);
            sm.Play();

            var fs = root.gameObject.AddComponent<FireSurface>();
            fs.Init(b, fl, sp, sm);
            fires[b] = fl;
        }

        /// <summary>灭火 / 烧完：火苗在约 0.3 秒内缩下去（不是瞬间消失），火星和烟停发、飘完自己没</summary>
        public static void DetachFire(Block b)
        {
            if (!fires.TryGetValue(b, out var ps)) return;
            fires.Remove(b);
            if (!ps) return;
            var root = ps.transform.parent;
            root.SetParent(null, true);
            var fsurf = root.GetComponent<FireSurface>(); if (fsurf) fsurf.enabled = false;   // 不再发新火苗
            root.gameObject.AddComponent<FxShrink>().target = ps;
            Object.Destroy(root.gameObject, 1.8f);
        }

        /// <summary>灭火的“噗”：一圈白色卡通蒸汽团贴着块往四周喷开、减速，接着中间几团往上冒，变大淡掉</summary>
        public static void Steam(Vector3 p, float unit)
        {
            var root = new GameObject("Fx_Steam").transform;
            root.position = p;
            var ring = Child(root, "Ring", PuffMat, 0);
            var m = ring.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.0f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(2.2f * unit, 3.2f * unit);
            m.startSize = new ParticleSystem.MinMaxCurve(0.7f * unit, 1.0f * unit);
            m.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            m.startColor = new Color(0.97f, 0.97f, 0.95f, 0.95f);
            var sh = ring.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.35f * unit; sh.rotation = new Vector3(90, 0, 0);
            sh.radiusMode = ParticleSystemShapeMultiModeValue.Loop; sh.radiusSpread = 0;
            var lim = ring.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 0.2f * unit; lim.dampen = 0.12f;
            var vel = ring.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = vel.z = new ParticleSystem.MinMaxCurve(0); vel.y = new ParticleSystem.MinMaxCurve(0.5f * unit, 0.9f * unit);
            Sheet(ring, 2, 2, false);
            var sz = ring.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, 0.5f), new Keyframe(0.2f, 1f), new Keyframe(1, 1.5f)));
            PuffFade(ring);
            var em = ring.emission; em.SetBursts(new[] { new ParticleSystem.Burst(0, 8) });
            ring.Play();

            var col = Child(root, "Column", PuffMat, 0);
            m = col.main;
            m.startDelay = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 1.4f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(1.2f * unit, 2.0f * unit);
            m.startSize = new ParticleSystem.MinMaxCurve(0.9f * unit, 1.3f * unit);
            m.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            m.startColor = new Color(0.95f, 0.95f, 0.94f, 0.9f);
            sh = col.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 18; sh.radius = 0.3f * unit;
            Sheet(col, 2, 2, false);
            sz = col.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 0.6f, 1, 1.8f));
            var rot = col.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
            PuffFade(col);
            em = col.emission; em.SetBursts(new[] { new ParticleSystem.Burst(0, 4) });
            col.Play();
            Object.Destroy(root.gameObject, 2f);
        }

        /// <summary>烟团淡出：前 60% 保持实心（卡通烟是“一团”，不是半透明雾），后面淡掉</summary>
        static void PuffFade(ParticleSystem ps)
        {
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.06f), new GradientAlphaKey(1, 0.6f), new GradientAlphaKey(0, 1) });
            col.color = g;
        }

        public static void Dust(Vector3 p, float unit, int n = 10)
        {
            var ps = Make("Dust", p);
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.4f * unit, 0.9f * unit);
            m.startColor = new Color(0.85f, 0.78f, 0.66f, 0.5f);
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 0.3f * unit;
            Fade(ps);
            Burst(ps, n, 0.9f);
        }

        public static void Shards(Bounds b, Color c, float unit)
        {
            var ps = Make("Shards", b.center, true);
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.3f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.0f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.12f * unit, 0.3f * unit);
            m.startRotation3D = true;
            m.startRotationX = m.startRotationY = m.startRotationZ = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            m.startColor = c;
            m.gravityModifier = 1f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = b.size * 0.8f;
            var col = ps.collision; col.enabled = true; col.type = ParticleSystemCollisionType.World; col.bounce = 0.2f; col.dampen = 0.5f; col.lifetimeLoss = 0.2f;
            Burst(ps, 18, 1.4f);
            Dust(b.center, unit, 6);
        }

        public static void Ash(Bounds b, float unit)
        {
            var ps = Make("Ash", b.center);
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.15f * unit, 0.5f * unit);
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.25f, 0.23f, 0.22f, 0.8f), new Color(1f, 0.45f, 0.15f, 0.9f));
            m.gravityModifier = -0.08f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = b.size;
            Fade(ps);
            Burst(ps, 24, 1.7f);
        }

        public static void Explosion(Vector3 p, float radius)
        {
            var ps = Make("Boom", p);
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(radius * 1.5f, radius * 4f);
            m.startSize = new ParticleSystem.MinMaxCurve(radius * 0.3f, radius * 0.7f);
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.8f, 0.4f, 0.95f), new Color(0.55f, 0.5f, 0.45f, 0.8f));
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = radius * 0.2f;
            Fade(ps);
            Burst(ps, 30, 0.7f);
        }

        public static void IceBurst(Vector3 p, float radius)
        {
            var ps = Make("Ice", p);
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(radius * 0.5f, radius * 1.5f);
            m.startSize = new ParticleSystem.MinMaxCurve(radius * 0.15f, radius * 0.35f);
            m.startColor = new Color(0.85f, 0.95f, 1f, 0.8f);
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = radius * 0.3f;
            Fade(ps);
            Burst(ps, 26, 1f);
        }

        public static void Sparkle(Vector3 p, Color c, float size = 0.03f)
        {
            var ps = Make("Sparkle", p);
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
            m.startSize = new ParticleSystem.MinMaxCurve(size * 0.3f, size);
            m.startColor = c;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = size;
            Fade(ps);
            Burst(ps, 12, 0.6f);
        }

        /// <summary>烟花：先一道细光往上蹿（预警），到顶炸成一圈亮点，慢慢下坠、熄灭</summary>
        public static void Firework(Vector3 p, Color c)
        {
            var rise = Make("FwRise", p - Vector3.up * 1.2f);
            var rm = rise.main;
            rm.startLifetime = 0.4f; rm.startSpeed = 3.2f; rm.startSize = 0.05f; rm.startColor = Color.Lerp(c, Color.white, 0.6f);
            var rs = rise.shape; rs.shapeType = ParticleSystemShapeType.Cone; rs.angle = 2; rs.radius = 0.001f;
            rise.transform.rotation = Quaternion.LookRotation(Vector3.up);
            var tr = rise.trails; tr.enabled = true; tr.lifetime = 0.3f; tr.widthOverTrail = 0.5f;
            rise.GetComponent<ParticleSystemRenderer>().sharedMaterial = SkyMat;
            rise.GetComponent<ParticleSystemRenderer>().trailMaterial = SkyMat;
            Burst(rise, 1, 0.5f);
            var ps = Make("Firework", p);
            var m = ps.main;
            m.startDelay = 0.4f;
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 1.6f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(1.4f, 2.0f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.12f);
            m.startColor = new ParticleSystem.MinMaxGradient(c, Color.Lerp(c, Color.white, 0.5f));
            m.gravityModifier = 0.08f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.02f;
            ps.transform.position = p;   // 升空那道光在下面 0.6 m 处起步，到这里炸开
            var ptr = ps.trails; ptr.enabled = true; ptr.lifetime = 0.25f; ptr.widthOverTrail = 0.4f;
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = SkyMat;
            ps.GetComponent<ParticleSystemRenderer>().trailMaterial = SkyMat;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(c, 0.3f), new GradientColorKey(c * 0.6f, 1) },
                      new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0.9f, 0.6f), new GradientAlphaKey(0, 1) });
            col.color = g;
            Burst(ps, 70, 2.2f);
            Sfx.Firework(p);
            SkyDome.Flash(c);
        }

        /// <summary>
        /// 小火苗（手势卡上的“灭火”示意）：和积木着火同一张手绘火苗序列帧，一直循环；用真实时间（暂停时也在动）。
        /// size = 火苗高度（米）。返回粒子系统，调 Stop / Play 让它缩下去 / 再着起来。
        /// </summary>
        public static ParticleSystem MiniFlame(Transform parent, Vector3 localPos, float size)
        {
            var fl = Child(parent, "MiniFlame", FlameMat, -20);
            fl.transform.localPosition = localPos;
            fl.GetComponent<ParticleSystemRenderer>().pivot = new Vector3(0, 0.45f, 0);
            var m = fl.main;
            m.simulationSpace = ParticleSystemSimulationSpace.Local;
            m.useUnscaledTime = true;
            m.loop = true;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.75f);
            m.startSize = new ParticleSystem.MinMaxCurve(size * 0.8f, size * 1.15f);
            m.startRotation = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
            m.startSpeed = 0f;
            m.maxParticles = 8;
            var em = fl.emission; em.rateOverTime = 7f;
            var sh = fl.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(size * 0.6f, 0.001f, 0.001f);
            Sheet(fl, 4, 2, true);
            var sz = fl.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, 0.35f), new Keyframe(0.22f, 1f), new Keyframe(0.7f, 0.85f), new Keyframe(1, 0f)));
            fl.Play();
            return fl;
        }

        /// <summary>浮尘：一块空间（中心 center、大小 size）里慢慢飘的小亮点，持续发射（发射率由调用方控制）</summary>
        public static ParticleSystem Motes(Vector3 center, Vector3 size, Color c)
        {
            var ps = Make("Motes", center);
            var m = ps.main;
            m.loop = true; m.duration = 5f;
            m.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
            m.startSpeed = 0.01f;
            m.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.012f);
            m.startColor = c;
            m.maxParticles = 150;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = size;
            var em = ps.emission; em.rateOverTime = 0;
            var no = ps.noise; no.enabled = true; no.strength = 0.03f; no.frequency = 0.4f;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.y = new ParticleSystem.MinMaxCurve(-0.01f, 0.015f);
            Fade(ps);
            ps.Play();
            return ps;
        }

        static void Fade(ParticleSystem ps)
        {
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.12f), new GradientAlphaKey(0, 1) });
            col.color = g;
        }
    }

    /// <summary>把一组粒子在 duration 秒内等比缩到没（灭火时火苗“缩下去”）</summary>
    public class FxShrink : MonoBehaviour
    {
        public ParticleSystem target;
        public float duration = 0.3f;
        float t;
        ParticleSystem.Particle[] buf;
        void Update()
        {
            if (!target) return;
            float dt = Time.deltaTime;
            t += dt;
            buf ??= new ParticleSystem.Particle[target.main.maxParticles];
            int n = target.GetParticles(buf);
            float k = Mathf.Clamp01(1f - dt / Mathf.Max(0.01f, duration - t + dt));
            for (int i = 0; i < n; i++) buf[i].startSize *= k;
            target.SetParticles(buf, n);
            if (t >= duration) target.Clear();
        }
    }

    /// <summary>
    /// 火苗贴着积木真实表面长：按面积在模型三角面上撒采样点（局部坐标 + 法线，按网格缓存），
    /// 每次发射挑“此刻朝上、上方没被别的块压着”的点 —— 尖顶塔就沿屋面烧，倒在桌上的块就从朝上的那面烧。
    /// </summary>
    public class FireSurface : MonoBehaviour
    {
        struct Pt { public Vector3 p, n; }
        static readonly Dictionary<Mesh, Pt[]> cache = new();
        Block b; ParticleSystem flames, embers, smoke;
        Pt[] pts;
        readonly List<int> ok = new();
        float acc0, acc1, acc2, recheck;
        const float FlameRate = 11f, EmberRate = 4f, SmokeRate = 2.2f;

        public void Init(Block blk, ParticleSystem f, ParticleSystem e, ParticleSystem s)
        {
            b = blk; flames = f; embers = e; smoke = s;
            var mf = blk.mr ? blk.mr.GetComponent<MeshFilter>() : blk.GetComponentInChildren<MeshFilter>();
            var mesh = mf ? mf.sharedMesh : null;
            if (mesh && mesh.isReadable && !cache.TryGetValue(mesh, out pts)) cache[mesh] = pts = Sample(mesh, 96);
            if (mf) meshTf = mf.transform;
        }
        Transform meshTf;

        static Pt[] Sample(Mesh mesh, int n)
        {
            var v = mesh.vertices; var t = mesh.triangles;
            int tc = t.Length / 3;
            var cum = new float[tc]; float tot = 0;
            for (int i = 0; i < tc; i++) { tot += Vector3.Cross(v[t[i * 3 + 1]] - v[t[i * 3]], v[t[i * 3 + 2]] - v[t[i * 3]]).magnitude * 0.5f; cum[i] = tot; }
            var r = new System.Random(mesh.vertexCount);
            var res = new Pt[n];
            for (int k = 0; k < n; k++)
            {
                float x = (float)r.NextDouble() * tot;
                int lo = System.Array.BinarySearch(cum, x); if (lo < 0) lo = ~lo; lo = Mathf.Min(lo, tc - 1);
                Vector3 a = v[t[lo * 3]], bb = v[t[lo * 3 + 1]], c = v[t[lo * 3 + 2]];
                float s1 = (float)r.NextDouble(), s2 = (float)r.NextDouble();
                if (s1 + s2 > 1) { s1 = 1 - s1; s2 = 1 - s2; }
                res[k] = new Pt { p = a + (bb - a) * s1 + (c - a) * s2, n = Vector3.Cross(bb - a, c - a).normalized };
            }
            return res;
        }

        /// <summary>重新挑能烧的点：此刻法线朝上（y>0.3），且正上方一格内没有别的积木</summary>
        void Refresh()
        {
            ok.Clear();
            if (pts == null || !meshTf) return;
            float u = b.Unit;
            for (int i = 0; i < pts.Length; i++)
            {
                var n = meshTf.TransformDirection(pts[i].n);
                if (n.y < 0.3f) continue;
                var p = meshTf.TransformPoint(pts[i].p) + n * 0.03f * u;
                if (Physics.Raycast(p, Vector3.up, out var hit, 1.0f * u, Layers.BlockMask, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(b.transform)) continue;
                ok.Add(i);
            }
        }

        bool Pick(out Vector3 pos, out Vector3 nrm)
        {
            if (ok.Count > 0)
            {
                var pt = pts[ok[Random.Range(0, ok.Count)]];
                nrm = meshTf.TransformDirection(pt.n);
                pos = meshTf.TransformPoint(pt.p);
                return true;
            }
            var bb = b.WorldBounds;   // 兜底：包围盒顶面
            pos = new Vector3(bb.center.x + Random.Range(-0.3f, 0.3f) * bb.size.x, bb.max.y, bb.center.z + Random.Range(-0.3f, 0.3f) * bb.size.z);
            nrm = Vector3.up;
            return true;
        }

        void Update()
        {
            if (!b) { enabled = false; return; }
            float dt = Time.deltaTime, u = b.Unit;
            recheck -= dt;
            if (recheck <= 0) { Refresh(); recheck = b.Dynamic ? 0.1f : 0.6f; }   // 块在动时勤查朝向
            acc0 += FlameRate * dt; acc1 += EmberRate * dt; acc2 += SmokeRate * dt;
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            while (acc0 >= 1f)
            {
                acc0 -= 1f; Pick(out var p, out var n);
                ep.position = p + n * 0.06f * u;                     // 底部压在木面上（稍外推，免得被面吃掉）
                ep.velocity = Vector3.up * Random.Range(0.15f, 0.35f) * u;
                flames.Emit(ep, 1);
            }
            while (acc1 >= 1f)
            {
                acc1 -= 1f; Pick(out var p, out var n);
                ep.position = p + n * 0.1f * u;
                ep.velocity = (Vector3.up + Random.insideUnitSphere * 0.3f) * Random.Range(1.2f, 2.4f) * u;
                embers.Emit(ep, 1);
            }
            while (acc2 >= 1f)
            {
                acc2 -= 1f; Pick(out var p, out var n);
                ep.position = p + Vector3.up * 1.1f * u;               // 从火苗尖上方冒
                ep.velocity = Vector3.up * Random.Range(0.6f, 1.0f) * u;
                smoke.Emit(ep, 1);
            }
        }
    }
}

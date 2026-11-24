// 特效（代码生成的粒子）：火、蒸汽、碎片、灰、爆炸、冰雾、尘、接住的闪光。
// 先做能用的版本（D2），D4 统一按手绘风重做贴图和形状。所有尺寸按积木格（unit）缩放，敌城放大时特效跟着大。
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

        // ── 火：附着在块上，跟着块走
        public static void AttachFire(Block b)
        {
            if (fires.ContainsKey(b)) return;
            var ps = Make("Fire", b.WorldBounds.center);
            ps.transform.SetParent(b.transform, true);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f * b.Unit / 0.05f * 0.3f, 0.8f * b.Unit / 0.05f * 0.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.7f * b.Unit, 1.3f * b.Unit);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.5f, 0.12f, 1f), new Color(1f, 0.85f, 0.35f, 1f));
            main.gravityModifier = -0.15f;
            main.maxParticles = 60;
            var em = ps.emission; em.rateOverTime = 34;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box;
            var bb = b.WorldBounds; sh.scale = new Vector3(bb.size.x, bb.size.y * 0.6f, bb.size.z) * 0.8f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.4f), 0), new GradientColorKey(new Color(1f, 0.4f, 0.1f), 0.5f), new GradientColorKey(new Color(0.3f, 0.25f, 0.22f), 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.9f, 0.15f), new GradientAlphaKey(0.5f, 0.6f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.EaseInOut(0, 1, 1, 0.3f));
            ps.Play();
            fires[b] = ps;
        }

        public static void DetachFire(Block b)
        {
            if (!fires.TryGetValue(b, out var ps)) return;
            fires.Remove(b);
            if (!ps) return;
            ps.transform.SetParent(null, true);
            var em = ps.emission; em.rateOverTime = 0;
            Object.Destroy(ps.gameObject, 1f);
        }

        public static void Steam(Vector3 p, float unit)
        {
            var ps = Make("Steam", p);
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.8f * unit, 1.6f * unit);
            m.startColor = new Color(0.95f, 0.95f, 0.95f, 0.55f);
            m.gravityModifier = -0.05f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.4f * unit;
            Fade(ps);
            Burst(ps, 14, 1.2f);
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
            rise.GetComponent<ParticleSystemRenderer>().trailMaterial = Soft;
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
            ps.GetComponent<ParticleSystemRenderer>().trailMaterial = Soft;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(c, 0.3f), new GradientColorKey(c * 0.6f, 1) },
                      new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0.9f, 0.6f), new GradientAlphaKey(0, 1) });
            col.color = g;
            Burst(ps, 70, 2.2f);
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
}

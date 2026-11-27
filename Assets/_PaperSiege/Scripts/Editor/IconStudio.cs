// 应用图标的几个方案（11-27 用户：图标重做，多出几版挑）。全部用游戏里的真实模型实拍，背景透明，背景渐变在 Tools/icon_compose.py 里合成。
// 要在运行模式下调用（城、封面、弹弓都是运行时搭的）：IconStudio.Run(输出目录)。
using System.Collections;
using System.Collections.Generic;
using System.IO;
using PaperSiege;
using UnityEngine;

namespace PaperSiege.EditorTools
{
    public static class IconStudio
    {
        static Director D => Director.I;

        /// <summary>只保留 keep 里这些物体（及子物体）的渲染，透明背景拍一张 1024² PNG</summary>
        public static string Shot(string dir, string name, Vector3 camPos, Vector3 look, float fov, IList<Transform> keep, float roll = 0f)
        {
            var hidden = new List<Renderer>();
            foreach (var r in Object.FindObjectsByType<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                bool ok = false;
                foreach (var k in keep) if (k && (r.transform == k || r.transform.IsChildOf(k))) { ok = true; break; }
                if (!ok) { r.enabled = false; hidden.Add(r); }
            }
            var cam = new GameObject("IconCam").AddComponent<Camera>();
            cam.transform.position = camPos;
            cam.transform.rotation = Quaternion.LookRotation(look - camPos, Vector3.up) * Quaternion.Euler(0, 0, roll);
            cam.fieldOfView = fov; cam.nearClipPlane = 0.02f; cam.farClipPlane = 30f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);
            const int S = 1024;
            var rt = new RenderTexture(S, S, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
            RenderTexture.active = rt;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false); tex.ReadPixels(new Rect(0, 0, S, S), 0, 0); tex.Apply();
            RenderTexture.active = null;
            foreach (var r in hidden) if (r) r.enabled = true;
            Object.DestroyImmediate(cam.gameObject); rt.Release();
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            return path;
        }

        /// <summary>按 focus 这几个物体的包围盒自动取景：从 viewDir 方向看过去，主体占画面 fill（0～1）</summary>
        public static string Fit(string dir, string name, IList<Transform> keep, IList<Bounds> focus, Vector3 viewDir, float fov, float fill = 0.82f, Vector3 shift = default, float roll = 0f)
        {
            var b = focus[0]; foreach (var x in focus) b.Encapsulate(x);
            float rad = b.extents.magnitude;
            float dist = rad / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) / fill;
            var c = b.center + shift;
            return Shot(dir, name, c - viewDir.normalized * dist, c, fov, keep, roll);
        }

        static Bounds RB(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>();
            var b = new Bounds(t.position, Vector3.zero); bool first = true;
            foreach (var r in rs) { if (r is ParticleSystemRenderer || r is LineRenderer) continue; if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
            return b;
        }

        public static string Run(string dir) { foreach (var dp in Object.FindObjectsByType<DesktopPlay>()) Object.Destroy(dp); D.StartCoroutine(RunCo(dir)); return "icons started"; }

        static Transform T(string path) { var g = GameObject.Find(path); return g ? g.transform : null; }

        static IEnumerator RunCo(string dir)
        {
            SimHands.scripted = true; SimHands.Hide(0); SimHands.Hide(1);
            // ── V1 帅旗灯笼：只取布旗上半截（“木垒”两个字）+ 灯笼
            D.GoTitle();
            yield return new WaitForSeconds(3.5f);
            var cover = T("CoverMenu/Cover");
            if (cover)
            {
                var keep = new List<Transform>();
                foreach (Transform ch in cover) if (!ch.name.StartsWith("Hang_") && ch.name != "TagArm" && ch.name != "Debris") keep.Add(ch);
                var bb = RB(cover.Find("Banner"));
                var top = new Bounds(new Vector3(bb.center.x, bb.max.y - bb.size.y * 0.3f, bb.center.z), new Vector3(bb.size.x, bb.size.y * 0.6f, bb.size.z));
                var lb = RB(cover.Find("LanternPivot"));
                var view = new Vector3(-0.18f, -0.08f, 1f);
                Debug.Log("[图标] " + Fit(dir, "V1_帅旗灯笼", keep, new[] { top, lb }, view, 30f, 0.9f));
                // 带几块飘着的积木
                var keep2 = new List<Transform>(keep); var deb = cover.Find("Debris"); if (deb) keep2.Add(deb);
                Debug.Log("[图标] " + Fit(dir, "V1c_帅旗灯笼_积木", keep2, new[] { top, lb }, view, 30f, 0.72f));
            }
            // ── V2 我方城堡（修正版）、V4 中央主楼 + 帅旗特写
            Castle.InstantBuild = true; D.StartLevel(3); Castle.InstantBuild = false;
            foreach (var c in D.GetComponents<LevelTips>()) Object.Destroy(c);
            D.catapult.Stop();
            yield return new WaitForSeconds(2.5f);
            var mine = D.arena.mine.transform;
            var f = D.arena.forward; var r = Vector3.Cross(Vector3.up, f);
            var view2 = f - r * 0.28f - Vector3.up * 0.06f;
            Debug.Log("[图标] " + Fit(dir, "V2_我方城堡", new[] { mine }, new[] { RB(mine) }, view2, 25f, 0.9f));
            var king = D.arena.mine.king.transform;
            var kb = RB(king);
            // 主楼特写：帅旗 + 灯笼 + 两边尖顶（取城上半截）
            var mb = RB(mine);
            var upper = new Bounds(new Vector3(mb.center.x, (kb.max.y + mb.center.y) * 0.5f + 0.02f, mb.center.z), new Vector3(mb.size.x * 0.55f, kb.max.y - mb.center.y, mb.size.z));
            Debug.Log("[图标] " + Fit(dir, "V4_主楼帅旗", new[] { mine }, new[] { upper }, view2 + Vector3.up * 0.1f, 22f, 0.95f));
            // ── V3 拉满的弹弓（越过右肩看弓架和远处的城）
            var t = D.head.transform;
            t.rotation = Quaternion.LookRotation(f) * Quaternion.Euler(10, 0, 0);
            var bow = t.position + t.forward * 0.40f - t.right * 0.10f - t.up * 0.10f;
            SimHands.Pose(0, bow, true);
            yield return new WaitForSeconds(0.6f);
            SimHands.Pose(1, D.sling.PouchRest, true);
            yield return new WaitForSeconds(0.4f);
            var target = D.arena.enemy.king.WorldBounds.center;
            for (float k = 0; k < 1f; k += Time.deltaTime / 0.5f)
            {
                var fc = D.sling.ForkCenter;
                var dv = Ballistics.Solve(fc, target, 4.6f, out var lo, out _) ? lo.normalized : t.forward;
                SimHands.Pose(1, Vector3.Lerp(D.sling.PouchRest, fc - dv * 0.22f, k), true);
                yield return null;
            }
            yield return new WaitForSeconds(0.3f);
            var keepS = new List<Transform> { D.arena.enemy.transform, D.catapult.transform };
            foreach (var n in new[] { "SlingFork", "Pouch", "BandL", "BandR" }) { var x = T(n); if (x) keepS.Add(x); }
            var forkT = T("SlingFork"); var pouchT = T("Pouch");
            Debug.Log($"[图标] 弓架={(forkT != null)} 皮兜={(pouchT != null)} 拉力={D.sling.pull01:0.00}");
            if (forkT && pouchT)
            {
                var fb = RB(forkT); fb.Encapsulate(RB(pouchT));
                var shoulder = pouchT.position - f * 0.16f + r * 0.1f + Vector3.up * 0.07f;   // 右肩后上方
                var aim = Vector3.Lerp(fb.center, target, 0.06f);
                Debug.Log("[图标] " + Shot(dir, "V3_拉满弹弓", shoulder, aim, 46f, keepS, -6f));
                Debug.Log("[图标] " + Shot(dir, "V3b_拉满弹弓_远城", shoulder - f * 0.1f, Vector3.Lerp(fb.center, target, 0.25f), 36f, keepS, -6f));
            }
            SimHands.Pose(1, D.sling.PouchRest, false); yield return null; SimHands.Hide(1); SimHands.Pose(0, bow, false); yield return null; SimHands.Hide(0);
            // ── V5 两城对轰：火丸在半空，从侧上方看两座城和弹道
            yield return new WaitForSeconds(0.5f);
            PSTests.Shoot(Ammo.Fire, 3, 5.0f, true);
            yield return new WaitForSeconds(0.5f);
            var keepD = new List<Transform> { D.arena.enemy.transform, D.arena.mine.transform, D.catapult.transform };
            var foc = new List<Bounds> { RB(D.arena.enemy.transform), RB(D.arena.mine.transform) };
            foreach (var p in Projectile.Live) if (p) { keepD.Add(p.transform); foc.Add(new Bounds(p.transform.position, Vector3.one * 0.05f)); }
            Debug.Log("[图标] " + Fit(dir, "V5_两城对轰", keepD, foc, f * 0.55f - r * 1f - Vector3.up * 0.42f, 30f, 0.95f));
            Debug.Log("[图标] 完成");
        }
    }
}

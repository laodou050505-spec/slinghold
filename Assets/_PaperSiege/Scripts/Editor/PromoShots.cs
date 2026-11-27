// Vibe Lab 宣传图（海报 / 详情截图）截图助手（11-27）。照《将军沙盘》的做法：
//   游戏画面一律引擎实拍：同一机位黑底、白底各拍一张（Python 里差分取透明度，叠加发光的烟花 / 光柱也保得住）；
//   再拍一张“白模房间”（编辑器替身墙地 + 桌子，游戏内容全藏），交给 banana_pro 转成写实房间照片当 MR 透视背景。
// 合成：Tools/promo/compose.py。生成模型只画房间，不碰游戏内容（标题字、积木、城都是引擎原像素）。
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PaperSiege;
using UnityEngine;

namespace PaperSiege.EditorTools
{
    public static class PromoShots
    {
        public const int W = 2560, H = 1440;
        static Director D => Director.I;
        public static string Dir = "/tmp/ps_promo";

        static bool IsRoom(Transform t) => t.name.StartsWith("Room") || t.name.StartsWith("Ceil");
        static bool IsTable(Transform t) => t.GetComponentInParent<VirtualTable>() != null;

        static void Render(string path, Vector3 pos, Vector3 look, float fov, Color bg)
        {
            var cam = D.head;
            var p0 = cam.transform.position; var r0 = cam.transform.rotation; var f0 = cam.fieldOfView;
            var cf = cam.clearFlags; var bc = cam.backgroundColor;
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
            cam.fieldOfView = fov;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = bg;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            RenderTexture.active = null;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex); rt.Release();
            cam.transform.SetPositionAndRotation(p0, r0); cam.fieldOfView = f0; cam.clearFlags = cf; cam.backgroundColor = bc;
        }

        /// <summary>游戏层：藏掉替身房间和虚拟桌（真实房间里桌子是照片里的），黑底 / 白底各一张</summary>
        public static string Pair(string name, Vector3 pos, Vector3 look, float fov)
        {
            var hide = new List<GameObject>();
            foreach (var r in Object.FindObjectsByType<Renderer>())
                if (r.enabled && r.gameObject.activeInHierarchy && (IsRoom(r.transform) || IsTable(r.transform))) { r.enabled = false; hide.Add(r.gameObject); }
            try
            {
                Render($"{Dir}/{name}_k.png", pos, look, fov, Color.black);
                Render($"{Dir}/{name}_w.png", pos, look, fov, Color.white);
            }
            finally { foreach (var g in hide) if (g) g.GetComponent<Renderer>().enabled = true; }
            return $"{name} 游戏层（藏 {hide.Count} 件）";
        }

        /// <summary>白模房间：只留替身墙 / 地 / 天花板 + 桌子（统一浅灰），天花板开口处补上（结局除外：那时天花板整片是天空）</summary>
        public static string Room(string name, Vector3 pos, Vector3 look, float fov, bool fillCeiling = true)
        {
            var off = new List<Renderer>();
            var swapped = new Dictionary<Renderer, Material[]>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); mat.SetColor("_BaseColor", new Color(0.82f, 0.82f, 0.8f)); mat.SetFloat("_Smoothness", 0.1f);
            var wood = new Material(mat); wood.SetColor("_BaseColor", new Color(0.62f, 0.5f, 0.38f));
            var made = new List<GameObject>();
            foreach (var r in Object.FindObjectsByType<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                bool room = IsRoom(r.transform), table = IsTable(r.transform);
                if (room || table) { swapped[r] = r.sharedMaterials; r.sharedMaterial = table ? wood : mat; }
                else { r.enabled = false; off.Add(r); }
            }
            try
            {
                if (fillCeiling)
                {
                    // 天花板开口补一块（照片里那块就是普通天花板，游戏层的盒盖 / 天空会盖在上面）
                    var c = GameObject.CreatePrimitive(PrimitiveType.Cube); c.name = "__promo_ceil"; made.Add(c);
                    var f = D.arena.forward;
                    c.transform.SetPositionAndRotation(new Vector3(D.arena.tableCenter.x, D.arena.ceilingHeight + 0.03f, D.arena.tableCenter.z) + f * 1.5f, Quaternion.LookRotation(f));
                    c.transform.localScale = new Vector3(6f, 0.06f, 7.5f);
                    c.GetComponent<Renderer>().sharedMaterial = mat;
                }
                Render($"{Dir}/{name}_room.png", pos, look, fov, new Color(0.9f, 0.9f, 0.92f));
            }
            finally
            {
                foreach (var r in off) if (r) r.enabled = true;
                foreach (var kv in swapped) if (kv.Key) kv.Key.sharedMaterials = kv.Value;
                foreach (var g in made) Object.DestroyImmediate(g);
            }
            return $"{name} 白模房间";
        }

        /// <summary>一个机位：游戏层 + 白模房间</summary>
        public static string Both(string name, Vector3 pos, Vector3 look, float fov, bool fillCeiling = true)
            => Pair(name, pos, look, fov) + "；" + Room(name, pos, look, fov, fillCeiling);

        // ───────── 各机位（玩家站位：头 = D.head 当前位置）
        public static Vector3 HeadPos => D.head.transform.position;
        public static Vector3 F => D.arena.forward;
        public static Vector3 R => Vector3.Cross(Vector3.up, D.arena.forward);

        /// <summary>连拍：每 interval 秒拍一对游戏层（name_00、name_01……），同时存一张低清预览拼图用</summary>
        public static IEnumerator Burst(string name, Vector3 pos, Vector3 look, float fov, int count, float interval)
        {
            for (int i = 0; i < count; i++)
            {
                Pair($"{name}_{i:00}", pos, look, fov);
                float t0 = Time.time; while (Time.time - t0 < interval) yield return null;
            }
            Debug.Log($"[宣传] 连拍完 {name} ×{count}");
        }

        // ───────── 整套（海报 + 5 张详情截图）：每个机位游戏层 + 白模房间；有动作的连拍几对，事后挑
        public static string All() { foreach (var dp in Object.FindObjectsByType<DesktopPlay>()) Object.Destroy(dp); D.StartCoroutine(AllRun()); return "promo started"; }

        static IEnumerator AllRun()
        {
            SimHands.scripted = true; SimHands.Hide(0); SimHands.Hide(1);
            Progress.MarkSeen("fire"); Progress.MarkSeen("empty");
            var h = HeadPos;
            // ① 海报：封面（标题页）
            D.GoTitle();
            yield return new WaitForSeconds(4.5f);
            var cv = GameObject.Find("CoverMenu/Cover").transform;
            var cc = cv.position + Vector3.up * 0.62f;
            var pp = h - F * 0.5f - R * 0.22f + Vector3.up * 0.06f;
            Both("poster", pp, cc + R * 0.04f, 60f);
            // ② 第 5 关草垛：白天、天窗开着；全景 + 近景（同一时刻）
            Castle.InstantBuild = true; D.StartLevel(4); Castle.InstantBuild = false; PSTests.Quiet();
            foreach (var c in D.GetComponents<LevelTips>()) Object.Destroy(c);
            yield return new WaitForSeconds(5.5f);
            var e = D.arena.enemy;
            var hay = e.blocks.Where(b => b && b.mech && b.mech.kind == Mech.Kind.Hay).OrderBy(b => b.transform.position.x).ToList();
            var from = D.arena.tableCenter + Vector3.up * 0.35f;
            D.DebugShoot(Ammo.Fire, from, hay[0].WorldBounds.center, 5.5f, false);
            yield return new WaitForSeconds(2.6f);
            D.DebugShoot(Ammo.Fire, from + R * 0.05f, e.king.WorldBounds.center + Vector3.up * 0.05f, 5.3f, true);
            var wideLook = D.arena.tableCenter + F * 1.9f + Vector3.up * 0.55f;
            var closeLook = e.transform.position + Vector3.up * 0.22f;
            for (int i = 0; i < 5; i++)
            {
                Pair($"s_wide_{i}", h, wideLook, 64f);
                Pair($"s_close_{i}", h, closeLook, 22f);
                yield return new WaitForSeconds(0.25f);
            }
            Room("s_wide", h, wideLook, 64f); Room("s_close", h, closeLook, 22f);
            // ③ 第 14 关三钟：入夜，月亮星星，敌城的钟
            Castle.InstantBuild = true; D.StartLevel(13); Castle.InstantBuild = false; PSTests.Quiet();
            foreach (var c in D.GetComponents<LevelTips>()) Object.Destroy(c);
            yield return new WaitForSeconds(6f);
            var nightLook = D.arena.tableCenter + F * 1.6f + Vector3.up * 1.05f;
            Both("s_night", h, nightLook, 66f);
            // ④ 教学：第一步“左手捏住 举起弹弓”（示范手 + 手势卡）
            Progress.ClearSeen();
            D.StartLevel(0);
            yield return new WaitForSeconds(6.5f);
            var tutPos = h - F * 0.22f + R * 0.08f;
            var tutLook = D.arena.tableCenter + F * 0.85f + Vector3.up * 0.12f;
            for (int i = 0; i < 4; i++) { Pair($"s_tut_{i}", tutPos, tutLook, 58f); yield return new WaitForSeconds(0.35f); }
            Room("s_tut", tutPos, tutLook, 58f);
            Progress.MarkSeen("fire"); Progress.MarkSeen("empty");
            foreach (var c in D.GetComponents<Tutorial>()) Object.Destroy(c);
            // ⑤ 结局：烟花节，抬头看整片天花板
            Castle.InstantBuild = true; D.StartLevel(Levels.Count - 1); Castle.InstantBuild = false; PSTests.Quiet();
            foreach (var c in D.GetComponents<LevelTips>()) Object.Destroy(c);
            yield return new WaitForSeconds(4f);
            Room("s_end", h, h + (F * 0.75f + Vector3.up).normalized * 3f, 80f, false);
            D.arena.enemy.DamageKing(99, D.arena.enemy.transform.position - F);
            yield return new WaitForSeconds(8.5f);
            var up = h + (F * 0.75f + Vector3.up).normalized * 3f;
            for (int i = 0; i < 8; i++) { Pair($"s_end_{i}", h, up, 80f); yield return new WaitForSeconds(0.45f); }
            Progress.Cleared = false;
            Debug.Log("[宣传] 全部完成");
        }

        /// <summary>预览（带替身房间的普通截图，挑机位用）</summary>
        public static string Preview(string name, Vector3 pos, Vector3 look, float fov) { Render($"{Dir}/preview_{name}.png", pos, look, fov, new Color(0.2f, 0.2f, 0.22f)); return name; }
    }
}

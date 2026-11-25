// 补给盘：开局 3 块，每 20 秒补 1 块，最多 5 块。从盘里捏起一块，拿到自己城的缺口附近松开 → 飞过去补上（变成那个位置原来的零件）；
// 不在缺口附近松手 → 飞回盘里。
using System.Collections.Generic;
using UnityEngine;

namespace PaperSiege
{
    [DefaultExecutionOrder(0)]
    public class SupplyTray : MonoBehaviour
    {
        Director d;
        HandInput input;
        Transform tray;
        readonly List<Transform> pieces = new();
        readonly Transform[] held = new Transform[2];
        readonly bool[] claimed = new bool[2];
        float refill;
        const float SnapReach = 0.08f;
        InputTuning IT => InputTuning.I;
        GameTuning T => GameTuning.I;

        public bool Holding(int s) => held[s] != null;
        public bool ClaimedThisFrame(int s) => claimed[s];
        public int Count => pieces.Count;

        /// <summary>难度模拟器用：直接从盘里拿走一块（不走手势）</summary>
        public bool TakeOne()
        {
            if (pieces.Count == 0) return false;
            var p = pieces[pieces.Count - 1]; pieces.RemoveAt(pieces.Count - 1);
            if (p) Destroy(p.gameObject);
            return true;
        }

        public void Setup(Director director)
        {
            d = director; input = HandInput.Instance;
            if (!tray) BuildTray();
            tray.gameObject.SetActive(true);
            foreach (var p in pieces) if (p) Destroy(p.gameObject);
            pieces.Clear();
            for (int s = 0; s < 2; s++) { if (held[s]) Destroy(held[s].gameObject); held[s] = null; }
            var a = d.arena;
            var right = Vector3.Cross(Vector3.up, a.forward);
            tray.SetPositionAndRotation(a.tableCenter + right * 0.3f - a.forward * 0.16f, Quaternion.LookRotation(a.forward));
            for (int i = 0; i < T.supplyStart; i++) AddPiece();
            refill = T.supplyEvery;
        }

        /// <summary>标题页：补给盘和盘里的积木都收起来（不和桌上的按钮挤在一起）</summary>
        public void HideForTitle()
        {
            if (tray) tray.gameObject.SetActive(false);
            foreach (var p in pieces) if (p) p.gameObject.SetActive(false);
        }

        void BuildTray()
        {
            tray = new GameObject("SupplyTray").transform;
            var lib = BlockLibrary.I;
            var tm = lib ? lib.Meshes("T_Tray") : null;
            if (tm != null && tm.lod0 && lib.propMaterial)
            {
                var g = new GameObject("Look"); g.transform.SetParent(tray, false);
                g.AddComponent<MeshFilter>().sharedMesh = tm.lod0; g.AddComponent<MeshRenderer>().sharedMaterial = lib.propMaterial;
            }
            else
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(g.GetComponent<Collider>());
                g.transform.SetParent(tray, false); g.transform.localScale = new Vector3(0.15f, 0.012f, 0.1f); g.transform.localPosition = new Vector3(0, 0.006f, 0);
                g.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.62f, 0.45f, 0.28f) };
            }
        }

        Vector3 SlotPos(int i) => tray.TransformPoint(new Vector3(((i % 3) - 1) * 0.045f, 0.005f, (i / 3 - 0.5f) * 0.045f));   // 盘底 0.005 m

        void AddPiece()
        {
            if (pieces.Count >= T.supplyMax) return;
            var g = new GameObject("Patch");
            var lib = BlockLibrary.I;
            var pm = lib ? lib.Meshes("W_Cube") : null;
            if (pm != null && pm.lod0)
            {
                g.AddComponent<MeshFilter>().sharedMesh = pm.lod0;
                g.AddComponent<MeshRenderer>().sharedMaterial = lib.Paint(Paint.Cream);
                g.transform.localScale = Vector3.one * (0.035f / BlockLibrary.G);
            }
            else Placeholder.Build(g.transform, "W_Cube", BlockLibrary.Def("W_Cube"), Paint.Cream);
            pieces.Add(g.transform);
            g.transform.position = SlotPos(pieces.Count - 1) + Vector3.up * 0.06f;
            StartCoroutine(Drop(g.transform, SlotPos(pieces.Count - 1)));
        }

        System.Collections.IEnumerator Drop(Transform t, Vector3 to)
        {
            // 新补给从上方轻轻落进盘里（有过程）
            var a = t.position;
            for (float k = 0; k < 1f && t; k += Time.deltaTime / 0.4f) { t.position = Vector3.Lerp(a, to, Mathf.SmoothStep(0, 1, k)); yield return null; }
            if (t) t.position = to;
        }

        void Update()
        {
            claimed[0] = claimed[1] = false;
            if (!d || d.phase != Director.Phase.Battle || !tray) return;
            input ??= HandInput.Instance;
            refill -= Time.deltaTime;
            if (refill <= 0f) { refill = T.supplyEvery; AddPiece(); }
            for (int s = 0; s < 2; s++)
            {
                var h = input.hands[s];
                if (held[s])
                {
                    if (h.tracked && h.pinch) { held[s].position = h.pinchPoint; held[s].rotation = Quaternion.LookRotation(d.arena.forward); continue; }
                    Release(s);
                    continue;
                }
                if (!input.pinchDown[s]) continue;
                if (s == 1 && d.sling && d.sling.Drawing) continue;
                // 捏到盘里的块
                Transform best = null; float bd = IT.grabRadius + 0.02f;
                foreach (var p in pieces) { float dd = Vector3.Distance(p.position + Vector3.up * 0.017f, h.pinchPoint); if (dd < bd) { bd = dd; best = p; } }
                if (best) { held[s] = best; pieces.Remove(best); claimed[s] = true; }
            }
        }

        void Release(int s)
        {
            var t = held[s]; held[s] = null;
            var mine = d.arena.mine;
            int slot = -1; float bd = SnapReach;
            for (int i = 0; i < mine.layout.items.Count; i++)
            {
                if (!mine.SlotEmpty(i)) continue;
                float dd = Vector3.Distance(mine.SlotCenter(i), t.position);
                if (dd < bd) { bd = dd; slot = i; }
            }
            if (slot >= 0) StartCoroutine(SnapInto(t, mine, slot));
            else { pieces.Add(t); StartCoroutine(Drop(t, SlotPos(pieces.Count - 1))); }
        }

        System.Collections.IEnumerator SnapInto(Transform t, Castle c, int slot)
        {
            var a = t.position; var to = c.SlotCenter(slot);
            for (float k = 0; k < 1f; k += Time.deltaTime / 0.25f) { t.position = Vector3.Lerp(a, to, Mathf.SmoothStep(0, 1, k)); yield return null; }
            Destroy(t.gameObject);
            if (c.SlotEmpty(slot))
            {
                var nb = c.Refill(slot);
                Fx.Dust(nb.WorldBounds.center, c.unit, 6);
                nb.Hit(0.8f);
                d.stats.repaired++;
                Sfx.Place(to);
            }
            else { AddPiece(); }   // 位置在飞的途中被占了：还回盘里
        }
    }
}

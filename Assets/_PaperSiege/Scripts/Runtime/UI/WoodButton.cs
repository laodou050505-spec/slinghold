// 木头按钮（实物界面）：一块积木，正面写字。捏它（捏合点离它 6 cm 内按下）= 按下：积木往下沉 4 mm 再弹回来，然后触发。
// 悬停高亮：有眼动时只跟眼睛（同一时刻只有一个亮）；没有眼动时，手靠近（10 cm）的那个亮。亮 = 轻轻抬起 + 漆色提亮。
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace PaperSiege
{
    public class WoodButton : MonoBehaviour
    {
        public static readonly List<WoodButton> All = new();
        public static WoodButton Hovered { get; private set; }
        public Action onPress;
        public bool interactable = true;
        public float pressRadius = 0.06f;
        TextMeshPro label;
        Transform look;
        MeshRenderer mr;
        float lift, pressT = -1;
        Vector3 lookRest;
        MaterialPropertyBlock mpb;
        public Vector3 size;

        public static WoodButton Build(string piece, Paint paint, Vector3 size, string text, Transform parent, float fontMax = 0.5f)
        {
            var go = new GameObject("Btn_" + text.Replace("\n", " "));
            go.layer = 12;
            go.transform.SetParent(parent, false);
            var b = go.AddComponent<WoodButton>();
            b.size = size;
            var lib = BlockLibrary.I;
            var pm = lib ? lib.Meshes(piece) : null;
            var def = BlockLibrary.Def(piece);
            var lk = new GameObject("Look");
            lk.transform.SetParent(go.transform, false);
            if (pm != null && pm.lod0)
            {
                lk.AddComponent<MeshFilter>().sharedMesh = pm.lod0;
                b.mr = lk.AddComponent<MeshRenderer>();
                b.mr.sharedMaterial = lib.Paint(def != null && def.mat == Mat.Stone ? Paint.Stone : paint);
                var ds = def != null ? def.size * BlockLibrary.G : Vector3.one * BlockLibrary.G;
                lk.transform.localScale = new Vector3(size.x / ds.x, size.y / ds.y, size.z / ds.z);
                lk.transform.localPosition = Vector3.down * size.y * 0.5f;   // 积木原点在底面，按钮原点在中心
            }
            b.look = lk.transform;
            b.lookRest = lk.transform.localPosition;
            var col = go.AddComponent<BoxCollider>(); col.size = size;
            // 字：写在正面（本地 -Z，朝玩家）
            if (!string.IsNullOrEmpty(text))
            {
                var tg = new GameObject("Text");
                tg.transform.SetParent(lk.transform.parent, false);
                tg.transform.localPosition = new Vector3(0, 0, -size.z * 0.5f - 0.0012f);
                b.label = tg.AddComponent<TextMeshPro>();
                if (Fonts.Hand) b.label.font = Fonts.Hand;
                b.label.rectTransform.sizeDelta = new Vector2(size.x * 0.86f, size.y * 0.8f);
                b.label.enableAutoSizing = true; b.label.fontSizeMin = 0.02f; b.label.fontSizeMax = fontMax;
                b.label.alignment = TextAlignmentOptions.Center;
                b.label.textWrappingMode = TextWrappingModes.NoWrap;
                b.label.color = new Color(0.2f, 0.13f, 0.08f);
                b.SetText(text);
            }
            return b;
        }

        public void SetText(string zh) { if (label) label.text = Loc.T(zh); keyText = zh; }
        string keyText;
        /// <summary>当前显示文字的中文原文（测试按它找按钮）</summary>
        public string Key => keyText;
        public void Relocalize() { if (label && keyText != null) label.text = Loc.T(keyText); }
        public void SetInk(Color c) { if (label) label.color = c; }

        void OnEnable() => All.Add(this);
        void OnDisable() { All.Remove(this); if (Hovered == this) Hovered = null; }

        /// <summary>每帧由第一个按钮统一算一次：谁亮、谁被按</summary>
        static int frame = -1;
        static void Tick()
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            var input = HandInput.Instance;
            Hovered = null;
            if (!input) return;
            float best = float.MaxValue;
            if (input.EyeTracked)
            {
                var g = input.Gaze;
                foreach (var b in All)
                {
                    if (!b.interactable) continue;
                    float ang = Vector3.Angle(g.direction, b.transform.position - g.origin);
                    if (ang < InputTuning.I.gazeSnap + 2f && ang < best) { best = ang; Hovered = b; }
                }
            }
            else
            {
                foreach (var b in All)
                {
                    if (!b.interactable) continue;
                    for (int s = 0; s < 2; s++)
                    {
                        if (!input.hands[s].tracked) continue;
                        float dd = Vector3.Distance(b.ClosestPoint(input.hands[s].pinchPoint), input.hands[s].pinchPoint);
                        if (dd < 0.1f && dd < best) { best = dd; Hovered = b; }
                    }
                }
            }
            // 按下：捏合的那一帧，捏合点离哪个按钮最近（6 cm 内）
            for (int s = 0; s < 2; s++)
            {
                if (!input.pinchDown[s]) continue;
                WoodButton hit = null; float hd = float.MaxValue;
                foreach (var b in All)
                {
                    if (!b.interactable) continue;
                    float dd = Vector3.Distance(b.ClosestPoint(input.hands[s].pinchPoint), input.hands[s].pinchPoint);
                    if (dd < b.pressRadius && dd < hd) { hd = dd; hit = b; }
                }
                if (hit) hit.Press();
            }
        }

        public Vector3 ClosestPoint(Vector3 p)
        {
            var lp = transform.InverseTransformPoint(p);
            var h = size * 0.5f;
            lp = new Vector3(Mathf.Clamp(lp.x, -h.x, h.x), Mathf.Clamp(lp.y, -h.y, h.y), Mathf.Clamp(lp.z, -h.z, h.z));
            return transform.TransformPoint(lp);
        }

        public void Press()
        {
            if (!interactable || pressT >= 0f) return;
            pressT = 0f;
            Sfx.Place(transform.position);
        }

        void Update()
        {
            Tick();
            float dt = Time.deltaTime;
            bool hov = Hovered == this && interactable;
            lift = Mathf.MoveTowards(lift, hov ? 1f : 0f, dt * 6f);
            float sink = 0f;
            if (pressT >= 0f)
            {
                pressT += dt;
                sink = pressT < 0.08f ? pressT / 0.08f : Mathf.Max(0f, 1f - (pressT - 0.08f) / 0.12f);
                if (pressT >= 0.2f) { pressT = -1f; onPress?.Invoke(); }
            }
            if (look) look.localPosition = lookRest + Vector3.up * (lift * 0.006f - sink * 0.004f);
            if (label) label.transform.localPosition = new Vector3(0, lift * 0.006f - sink * 0.004f, -size.z * 0.5f - 0.0012f);
            if (mr)
            {
                mpb ??= new MaterialPropertyBlock();
                mr.GetPropertyBlock(mpb);
                mpb.SetFloat("_Flash", lift * 0.12f + (interactable ? 0f : 0f));
                mr.SetPropertyBlock(mpb);
            }
        }
    }
}

// 程序合成音效（我们自己做的，没有版权问题；见 Audio/CREDITS.md）。开局一次性合成成 AudioClip 缓存起来。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PaperSiege
{
    public static class Synth
    {
        public const int Rate = 44100;
        static readonly Dictionary<string, AudioClip> cache = new();
        static System.Random rng = new(1124);

        static float N() => (float)(rng.NextDouble() * 2 - 1);

        public static AudioClip Clip(string name, float seconds, Func<float, float> f)
        {
            if (cache.TryGetValue(name, out var c)) return c;
            int n = Mathf.CeilToInt(seconds * Rate);
            var d = new float[n];
            float peak = 1e-4f;
            for (int i = 0; i < n; i++) { d[i] = f(i / (float)Rate); peak = Mathf.Max(peak, Mathf.Abs(d[i])); }
            float g = 0.9f / peak;
            for (int i = 0; i < n; i++) d[i] *= g;
            // 头尾各 3 ms 淡入淡出，防止爆音
            int fade = Rate * 3 / 1000;
            for (int i = 0; i < fade && i < n; i++) { float k = i / (float)fade; d[i] *= k; d[n - 1 - i] *= k; }
            c = AudioClip.Create(name, n, 1, Rate, false);
            c.SetData(d, 0);
            cache[name] = c;
            return c;
        }

        static float Env(float t, float a, float decay) => t < a ? t / a : Mathf.Exp(-(t - a) / decay);

        // 一阶低通（给噪声“去刺”）
        sealed class LP { float y; readonly float k; public LP(float cutoff) { k = 1 - Mathf.Exp(-2 * Mathf.PI * cutoff / Rate); } public float F(float x) => y += (x - y) * k; }
        sealed class HP { float y, x0; readonly float k; public HP(float cutoff) { k = Mathf.Exp(-2 * Mathf.PI * cutoff / Rate); } public float F(float x) { y = k * (y + x - x0); x0 = x; return y; } }

        // ── 打击
        public static AudioClip WoodHit(int v)
        {
            float f0 = 180 + v * 37, f1 = 520 + v * 61;
            var lp = new LP(2400);
            return Clip("wood" + v, 0.35f, t =>
                (Mathf.Sin(2 * Mathf.PI * f0 * t * (1 - 0.15f * t)) * Env(t, 0.002f, 0.06f)
                 + 0.5f * Mathf.Sin(2 * Mathf.PI * f1 * t) * Env(t, 0.001f, 0.025f)
                 + 0.6f * lp.F(N()) * Env(t, 0.0005f, 0.012f)));
        }

        public static AudioClip StoneHit(int v)
        {
            var hp = new HP(900); var lp = new LP(5000);
            float f0 = 900 + v * 130;
            return Clip("stone" + v, 0.25f, t =>
                lp.F(hp.F(N())) * Env(t, 0.0005f, 0.03f) + 0.35f * Mathf.Sin(2 * Mathf.PI * f0 * t) * Env(t, 0.001f, 0.02f)
                + 0.4f * Mathf.Sin(2 * Mathf.PI * 140 * t) * Env(t, 0.002f, 0.05f));
        }

        public static AudioClip Clatter()
        {
            // 积木倒下：一串随机间隔的小“哒”
            var times = new List<float>(); float tt = 0;
            for (int i = 0; i < 9; i++) { times.Add(tt); tt += 0.03f + (float)rng.NextDouble() * 0.09f; }
            var lp = new LP(3000);
            return Clip("clatter", tt + 0.2f, t =>
            {
                float s = 0;
                for (int i = 0; i < times.Count; i++) { float dt = t - times[i]; if (dt < 0 || dt > 0.08f) continue; float f = 300 + i * 53 % 220; s += (Mathf.Sin(2 * Mathf.PI * f * dt) + 0.5f * N()) * Mathf.Exp(-dt / 0.02f) * (1 - i * 0.07f); }
                return lp.F(s);
            });
        }

        public static AudioClip Boom()
        {
            var lp = new LP(600); var lp2 = new LP(180);
            return Clip("boom", 1.0f, t => lp.F(N()) * Env(t, 0.003f, 0.18f) + 1.4f * lp2.F(N()) * Env(t, 0.005f, 0.35f) + 0.8f * Mathf.Sin(2 * Mathf.PI * (60 - 20 * t) * t) * Env(t, 0.005f, 0.25f));
        }

        public static AudioClip IceCrack()
        {
            var hp = new HP(2500);
            return Clip("ice", 0.5f, t =>
            {
                float s = hp.F(N()) * Env(t, 0.0005f, 0.05f);
                for (int k = 0; k < 4; k++) s += 0.25f * Mathf.Sin(2 * Mathf.PI * (2200 + k * 640) * t) * Env(t, 0.001f, 0.08f + k * 0.03f);
                return s;
            });
        }

        public static AudioClip Fire()
        {
            // 点着：“呼”一声（低通噪声起伏）
            var lp = new LP(700);
            return Clip("fire", 1.0f, t => lp.F(N()) * Env(t, 0.08f, 0.35f) * (0.7f + 0.3f * Mathf.Sin(2 * Mathf.PI * 7 * t)));
        }

        public static AudioClip Hiss()
        {
            var hp = new HP(1800); var lp = new LP(7000);
            return Clip("hiss", 0.7f, t => lp.F(hp.F(N())) * Env(t, 0.02f, 0.22f));
        }

        // ── 弹弓
        public static AudioClip Snap()
        {
            // 松手：皮筋“啪”+ 一点点嗡
            var hp = new HP(400);
            return Clip("snap", 0.35f, t => hp.F(N()) * Env(t, 0.0005f, 0.015f) + 0.6f * Mathf.Sin(2 * Mathf.PI * 160 * t) * Env(t, 0.001f, 0.09f) * Mathf.Sin(2 * Mathf.PI * 9 * t));
        }

        public static AudioClip Creak()
        {
            // 拉弓：皮筋绷紧的“吱——”（窄带噪声 + 一点点颤动的音调），柔和不刺耳；整段 0.6 s 里频率回到起点，循环无缝
            var bp1 = new LP(1400); var hp = new HP(500);
            return Clip("creak", 0.6f, t =>
            {
                float wob = Mathf.Sin(2 * Mathf.PI * (5f / 0.6f) * t);   // 0.6 s 里正好 5 个周期
                float tone = Mathf.Sin(2 * Mathf.PI * (330 + 18 * wob) * t) * 0.35f;
                float rough = hp.F(bp1.F(N())) * 0.8f;
                return (tone + rough) * (0.75f + 0.25f * wob);
            });
        }

        public static AudioClip Windup()
        {
            // 投石机绞盘：一下下“咯”
            var lp = new LP(2200);
            return Clip("windup", 1.2f, t =>
            {
                float s = 0;
                for (int i = 0; i < 6; i++) { float dt = t - i * 0.19f; if (dt < 0 || dt > 0.06f) continue; s += (Mathf.Sin(2 * Mathf.PI * (380 + i * 25) * dt) + 0.7f * N()) * Mathf.Exp(-dt / 0.012f); }
                return lp.F(s);
            });
        }

        public static AudioClip Throw()
        {
            var lp = new LP(500);
            return Clip("throw", 0.5f, t => lp.F(N()) * Env(t, 0.04f, 0.12f) + 0.8f * Mathf.Sin(2 * Mathf.PI * 95 * t) * Env(t, 0.002f, 0.07f));
        }

        // ── 防守
        public static AudioClip Ding()
        {
            // 接住：清脆的“叮”（两个泛音，像木琴高音）
            return Clip("ding", 1.0f, t => Mathf.Sin(2 * Mathf.PI * 1568 * t) * Env(t, 0.001f, 0.25f) + 0.45f * Mathf.Sin(2 * Mathf.PI * 3136 * t) * Env(t, 0.001f, 0.12f) + 0.2f * Mathf.Sin(2 * Mathf.PI * 4704 * t) * Env(t, 0.001f, 0.06f));
        }

        public static AudioClip Block()
        {
            // 挡开：“噗”
            var lp = new LP(900);
            return Clip("block", 0.25f, t => lp.F(N()) * Env(t, 0.001f, 0.04f) + 0.6f * Mathf.Sin(2 * Mathf.PI * 220 * t) * Env(t, 0.001f, 0.05f));
        }

        public static AudioClip Place()
        {
            // 放下积木 / 按按钮：短促的“嗒”
            return Clip("place", 0.18f, t => Mathf.Sin(2 * Mathf.PI * 620 * t) * Env(t, 0.001f, 0.025f) + 0.5f * Mathf.Sin(2 * Mathf.PI * 260 * t) * Env(t, 0.001f, 0.04f));
        }

        public static AudioClip Firework()
        {
            var lp = new LP(1500); var hp = new HP(1500);
            return Clip("firework", 1.6f, t => lp.F(N()) * Env(t, 0.003f, 0.15f) + 0.35f * hp.F(N()) * Env(t, 0.05f, 0.5f) * (0.5f + 0.5f * Mathf.Sin(2 * Mathf.PI * 23 * t)));
        }
    }
}

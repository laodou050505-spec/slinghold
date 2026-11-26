// 背景音乐（程序合成，见 Audio/CREDITS.md）：轻快的木头打击乐（低鼓 + 木鱼 + 沙锤）+ 木琴旋律。
// 每个时段一首（同一套生成器，换速度 / 调式 / 种子）：白天五声大调、轻快；黄昏放慢；夜里小调、更稀。开局合成成一段 16 小节的循环。
// 标题页音量低一点；战斗最后 30 秒鼓点加密（紧张）；赢 / 输时淡出。
using System.Collections.Generic;
using UnityEngine;

namespace PaperSiege
{
    public class Music : MonoBehaviour
    {
        static readonly Dictionary<int, AudioClip> clips = new();
        AudioSource a, b;         // 两个声道交叉淡入淡出（换曲时不硬切）
        int current = -1;
        float baseVol = 0.32f;
        Director d;

        public static Music Build(Director d)
        {
            var m = new GameObject("Music").AddComponent<Music>();
            m.d = d;
            m.a = m.gameObject.AddComponent<AudioSource>(); m.b = m.gameObject.AddComponent<AudioSource>();
            foreach (var s in new[] { m.a, m.b }) { s.loop = true; s.spatialBlend = 0; s.volume = 0; s.playOnAwake = false; }
            return m;
        }

        // 时段 → (BPM, 是否小调, 种子)
        static (float bpm, bool minor, int seed) Style(int tod) => tod switch
        {
            0 => (104, false, 11), 1 => (116, false, 23), 2 => (112, false, 37),
            3 => (96, false, 41), 4 => (88, true, 53), _ => (100, true, 67),
        };

        public static AudioClip Track(int tod)
        {
            if (clips.TryGetValue(tod, out var c)) return c;
            var (bpm, minor, seed) = Style(tod);
            var rng = new System.Random(seed);
            const int R = Synth.Rate;
            float beat = 60f / bpm;
            int bars = 16, steps = bars * 8;   // 八分音符一步
            float step = beat / 2;
            int n = Mathf.CeilToInt(steps * step * R);
            var d = new float[n];
            // 音阶（以 D 为主音）：大调五声 / 小调五声
            int[] scale = minor ? new[] { 0, 3, 5, 7, 10 } : new[] { 0, 2, 4, 7, 9 };
            float root = 293.66f;   // D4
            float Freq(int deg) { int oct = Mathf.FloorToInt(deg / 5f); int i = ((deg % 5) + 5) % 5; return root * Mathf.Pow(2, (scale[i] + 12 * oct) / 12f); }
            void Add(float t0, float dur, System.Func<float, float> f, float vol)
            {
                int i0 = Mathf.Max(0, (int)(t0 * R)), i1 = (int)((t0 + dur) * R);
                for (int i = i0; i < i1; i++) d[i % n] += f((i - i0) / (float)R) * vol;   // 超出末尾的余音绕回开头（无缝循环）
            }
            // 和声进行（每 2 小节一个）：I - V - vi - IV（五声里用 0 / 3 / 4 / 2 级代替）
            int[] prog = { 0, 3, 4, 2, 0, 3, 2, 3 };
            // 打击乐
            for (int s = 0; s < steps; s++)
            {
                float t = s * step;
                int inBar = s % 8;
                bool night = tod >= 4;
                if (inBar == 0 || inBar == 4 || (!night && inBar == 6 && rng.NextDouble() < 0.5)) Add(t, 0.3f, x => Mathf.Sin(2 * Mathf.PI * (110 - 60 * x) * x) * Mathf.Exp(-x / 0.09f), 0.55f);          // 低鼓
                if (inBar == 2 || inBar == 6) Add(t, 0.15f, x => (Mathf.Sin(2 * Mathf.PI * 820 * x) + 0.5f * Mathf.Sin(2 * Mathf.PI * 1240 * x)) * Mathf.Exp(-x / 0.03f), 0.22f);        // 木鱼
                if (!night || s % 2 == 0) { var r2 = new System.Random(s * 7 + seed); Add(t, 0.06f, x => (float)(r2.NextDouble() * 2 - 1) * Mathf.Exp(-x / 0.015f), 0.05f); }                     // 沙锤
            }
            // 木琴旋律：每小节 4～6 个音，沿和声的音阶级进为主、偶尔跳进
            int deg0 = 5;
            for (int bar = 0; bar < bars; bar++)
            {
                int chord = prog[(bar / 2) % prog.Length];
                for (int k = 0; k < 8; k++)
                {
                    bool play = k == 0 || (rng.NextDouble() < (tod >= 4 ? 0.35 : 0.55));
                    if (!play) continue;
                    int mv = rng.NextDouble() < 0.75 ? (rng.Next(3) - 1) : (rng.Next(5) - 2) * 2;
                    deg0 = Mathf.Clamp(deg0 + mv, 3, 10);
                    if (k == 0) deg0 = chord + 5 + (rng.Next(2) * 2);
                    float f = Freq(deg0);
                    float t = (bar * 8 + k) * step;
                    Add(t, 0.6f, x => (Mathf.Sin(2 * Mathf.PI * f * x) + 0.3f * Mathf.Sin(2 * Mathf.PI * f * 4 * x) * Mathf.Exp(-x / 0.05f)) * Mathf.Exp(-x / 0.22f), 0.18f);
                }
                // 低音：小节第一拍、第三拍
                float bf = Freq(chord) / 2;
                Add(bar * 8 * step, beat * 1.8f, x => Mathf.Sin(2 * Mathf.PI * bf * x) * Mathf.Exp(-x / 0.4f), 0.16f);
                Add((bar * 8 + 4) * step, beat * 1.8f, x => Mathf.Sin(2 * Mathf.PI * bf * 1.5f * x) * Mathf.Exp(-x / 0.4f), 0.1f);
            }
            float peak = 1e-4f; foreach (var v in d) peak = Mathf.Max(peak, Mathf.Abs(v));
            for (int i = 0; i < n; i++) d[i] = d[i] / peak * 0.8f;
            // 循环接缝：把超出末尾的余音（最后一拍的鼓和木琴还没响完）卷回开头叠加 —— 循环播放时它们本来就该接着开头响；
            // 再在最后 30 ms 淡到和第一个样本相同（循环点的波形连续，不会“咔”）
            // （余音已经因为 Add 在 n 处截断而丢失，这里改为：生成时就把 i ≥ n 的部分绕回 i - n）
            int x0 = R * 3 / 100;
            float first = d[0];
            for (int i = 0; i < x0; i++) { float k = i / (float)(x0 - 1); d[n - x0 + i] = Mathf.Lerp(d[n - x0 + i], first, k * k); }
            c = AudioClip.Create("music_" + tod, n, 1, R, false);
            c.SetData(d, 0);
            clips[tod] = c;
            return c;
        }

        void Update()
        {
            if (!d) return;
            int tod = d.level != null ? d.level.timeOfDay : 0;
            if (d.phase == Director.Phase.Title) tod = 0;
            if (d.ending && d.sky && d.sky.dawn > 0.15f) tod = 0;   // 结局：天一亮就换回清晨那首
            if (tod != current)
            {
                current = tod;
                var t = a; a = b; b = t;   // a = 新的
                a.clip = Track(tod); a.time = 0; a.Play();
            }
            float target = Progress.Music ? baseVol : 0f;
            if (d.phase == Director.Phase.Title) target *= 0.7f;
            if (d.phase == Director.Phase.Won || d.phase == Director.Phase.Lost) target *= d.ending ? 0.8f : 0.35f;
            a.volume = Mathf.MoveTowards(a.volume, target, Time.deltaTime * 0.3f);
            b.volume = Mathf.MoveTowards(b.volume, 0f, Time.deltaTime * 0.3f);
            if (b.volume <= 0f && b.isPlaying) b.Stop();
            // 最后 30 秒：音乐稍微加快一点点（不改调）
            a.pitch = d.phase == Director.Phase.Battle && !d.timerPaused && d.timeLeft < 30f ? 1.06f : 1f;
        }
    }
}

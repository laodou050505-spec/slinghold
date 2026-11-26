// 音效播放：3D 空间声（敌城的声音从敌城那边来），一个小对象池；设置里能关。
// 11-26 整套换成离线合成的 wav（Tools/audio/make_audio.py → Resources/Audio/Sfx/<名字>_<n>.wav），
// 每种 2～4 个变体随机轮换（连续两次不重复）；缺文件时退回旧的运行时合成（Synth.cs），不会没声。
using System.Collections.Generic;
using UnityEngine;

namespace PaperSiege
{
    public static class Sfx
    {
        static AudioSource[] pool;
        static int next;
        static readonly Dictionary<string, AudioClip[]> bank = new();
        static readonly Dictionary<string, int> lastPick = new();

        // 拉弓：每多拉开一截就“咯吱”一下（11-26 用户：原来的持续“吱——”很难听）
        static float stretchMark = -1f;
        const float StretchStep = 0.14f;

        static void Init()
        {
            if (pool != null && pool[0]) return;
            var root = new GameObject("Sfx");
            Object.DontDestroyOnLoad(root);
            pool = new AudioSource[20];
            for (int i = 0; i < pool.Length; i++)
            {
                var s = root.AddComponent<AudioSource>();
                s.playOnAwake = false; s.spatialBlend = 1f; s.rolloffMode = AudioRolloffMode.Logarithmic;
                s.minDistance = 0.6f; s.maxDistance = 12f; s.dopplerLevel = 0;
                pool[i] = s;
            }
        }

        /// <summary>按名字取一个变体（Resources/Audio/Sfx/名字_1.wav、名字_2.wav…），和上一次不同</summary>
        static AudioClip Pick(string key, System.Func<AudioClip> fallback)
        {
            if (!bank.TryGetValue(key, out var arr))
            {
                var list = new List<AudioClip>();
                for (int i = 1; i <= 8; i++)
                {
                    var c = Resources.Load<AudioClip>($"Audio/Sfx/{key}_{i}");
                    if (!c) break;
                    list.Add(c);
                }
                bank[key] = arr = list.ToArray();
            }
            if (arr.Length == 0) return fallback?.Invoke();
            int k = Random.Range(0, arr.Length);
            if (arr.Length > 1 && lastPick.TryGetValue(key, out var last) && k == last) k = (k + 1) % arr.Length;
            lastPick[key] = k;
            return arr[k];
        }

        static void Play(AudioClip c, Vector3 p, float vol = 1f, float pitch = 1f, float spread = 0.04f)
        {
            if (!Progress.Sfx || !c) return;
            Init();
            var s = pool[next++ % pool.Length];
            s.transform.position = p;
            s.pitch = pitch * Random.Range(1f - spread, 1f + spread);
            s.PlayOneShot(c, vol);
        }

        public static void Hit(Ammo a, Mat m, Vector3 p)
        {
            switch (a)
            {
                case Ammo.Bomb: Play(Pick("bomb", Synth.Boom), p, 1f); Play(Pick("clatter", Synth.Clatter), p, 0.6f); return;
                case Ammo.Ice: Play(Pick("ice", Synth.IceCrack), p, 0.8f); break;
                case Ammo.Fire: Play(Pick("fire", Synth.Fire), p, 0.6f); break;
            }
            Play(m == Mat.Stone ? Pick("stone_hit", () => Synth.StoneHit(0)) : Pick("wood_hit", () => Synth.WoodHit(0)), p, 0.9f);
            Play(Pick("clatter", Synth.Clatter), p, 0.4f, Random.Range(0.92f, 1.1f));
        }

        /// <summary>整座城塌下来（结局）：一段约 3 秒的“断裂 + 隆隆 + 积木哗啦啦滚落”</summary>
        public static void Collapse(Vector3 p)
        {
            Play(Pick("collapse", Synth.Clatter), p, 1f, 1f, 0f);
            Play(Pick("clatter", Synth.Clatter), p + Vector3.right * 0.3f, 0.5f, 0.85f);
        }

        /// <summary>松手：力度越大越响、略低一点（更“饱”）</summary>
        public static void Launch(Vector3 p, float power) { StopCreak(); Play(Pick("sling_release", Synth.Snap), p, 0.55f + power * 0.45f, 1.06f - power * 0.12f); }
        /// <summary>右手捏住皮兜</summary>
        public static void Grab(Vector3 p) => Play(Pick("sling_grab", null), p, 0.6f);
        public static void Throw(Vector3 p) => Play(Pick("throw", Synth.Throw), p, 0.9f);
        public static void Catch(Vector3 p) => Play(Pick("catch", Synth.Ding), p, 0.85f, 1f, 0f);   // 带音高的奖励音不随机变调（不然会跑调）
        public static void Block(Vector3 p) => Play(Pick("block", Synth.Block), p, 0.8f);
        public static void Windup(Vector3 p) => Play(Pick("windup", Synth.Windup), p, 0.9f);
        public static void Fire(Vector3 p) => Play(Pick("fire", Synth.Fire), p, 0.5f);
        public static void Extinguish(Vector3 p) => Play(Pick("extinguish", Synth.Hiss), p, 0.75f);
        public static void Place(Vector3 p) => Play(Pick("place", Synth.Place), p, 0.75f);
        /// <summary>按木签（菜单 / 结算 / 暂停的按钮）</summary>
        public static void Button(Vector3 p) => Play(Pick("button", Synth.Place), p, 0.8f, 1f, 0.02f);
        /// <summary>结算时铜星钉上</summary>
        public static void Star(Vector3 p) => Play(Pick("star", Synth.Ding), p, 0.6f, 1f, 0f);
        public static void Firework(Vector3 p) => Play(Pick("firework", Synth.Firework), p, 0.85f, Random.Range(0.9f, 1.1f));

        /// <summary>拉弓时每帧调：tension 0～1。只在“又拉开了一截”时咯吱一下，停着不动就没声</summary>
        public static void Creak(Vector3 p, float tension)
        {
            if (!Progress.Sfx) return;
            if (stretchMark < 0f) stretchMark = tension;
            if (tension > stretchMark + StretchStep)
            {
                stretchMark = tension;
                Play(Pick("sling_stretch", null), p, 0.25f + tension * 0.35f, 0.94f + tension * 0.18f);
            }
            else if (tension < stretchMark - 0.25f) stretchMark = tension;   // 往回松了一截：重新起算
        }

        public static void StopCreak() => stretchMark = -1f;
    }
}

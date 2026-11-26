// 音效播放：3D 空间声（敌城的声音从敌城那边来），一个小对象池；设置里能关。
using UnityEngine;

namespace PaperSiege
{
    public static class Sfx
    {
        static AudioSource[] pool;
        static int next;
        static AudioSource creak;

        static void Init()
        {
            if (pool != null && pool[0]) return;
            var root = new GameObject("Sfx");
            Object.DontDestroyOnLoad(root);
            pool = new AudioSource[16];
            for (int i = 0; i < pool.Length; i++)
            {
                var s = root.AddComponent<AudioSource>();
                s.playOnAwake = false; s.spatialBlend = 1f; s.rolloffMode = AudioRolloffMode.Logarithmic;
                s.minDistance = 0.6f; s.maxDistance = 12f; s.dopplerLevel = 0;
                pool[i] = s;
            }
            var cg = new GameObject("Creak"); Object.DontDestroyOnLoad(cg);
            creak = cg.AddComponent<AudioSource>();
            creak.clip = Synth.Creak(); creak.loop = true; creak.spatialBlend = 1f; creak.minDistance = 0.5f; creak.volume = 0; creak.playOnAwake = false;
        }

        static void Play(AudioClip c, Vector3 p, float vol = 1f, float pitch = 1f)
        {
            if (!Progress.Sfx || !c) return;
            Init();
            var s = pool[next++ % pool.Length];
            s.transform.position = p;
            s.pitch = pitch * Random.Range(0.94f, 1.06f);
            s.PlayOneShot(c, vol);
        }

        public static void Hit(Ammo a, Mat m, Vector3 p)
        {
            switch (a)
            {
                case Ammo.Bomb: Play(Synth.Boom(), p, 1f); Play(Synth.Clatter(), p, 0.7f); return;
                case Ammo.Ice: Play(Synth.IceCrack(), p, 0.8f); break;
                case Ammo.Fire: Play(Synth.Fire(), p, 0.6f); break;
            }
            Play(m == Mat.Stone ? Synth.StoneHit(Random.Range(0, 3)) : Synth.WoodHit(Random.Range(0, 4)), p, 0.9f);
            Play(Synth.Clatter(), p, 0.45f, Random.Range(0.9f, 1.15f));
        }

        /// <summary>整座城塌下来（结局）：一声闷响 + 几串音高不同的积木散落声叠在一起</summary>
        public static void Collapse(Vector3 p)
        {
            Play(Synth.Boom(), p, 0.75f, 0.8f);
            for (int i = 0; i < 4; i++) Play(Synth.Clatter(), p + Random.insideUnitSphere * 0.15f, 0.8f - i * 0.12f, 0.85f + i * 0.08f);
        }

        public static void Launch(Vector3 p, float power) => Play(Synth.Snap(), p, 0.5f + power * 0.5f, 1f + power * 0.15f);
        public static void Throw(Vector3 p) => Play(Synth.Throw(), p, 0.9f);
        public static void Catch(Vector3 p) => Play(Synth.Ding(), p, 0.8f);
        public static void Block(Vector3 p) => Play(Synth.Block(), p, 0.8f);
        public static void Windup(Vector3 p) => Play(Synth.Windup(), p, 1f);
        public static void Fire(Vector3 p) => Play(Synth.Fire(), p, 0.5f);
        public static void Extinguish(Vector3 p) => Play(Synth.Hiss(), p, 0.7f);
        public static void Place(Vector3 p) => Play(Synth.Place(), p, 0.7f);
        public static void Firework(Vector3 p) => Play(Synth.Firework(), p, 0.8f, Random.Range(0.85f, 1.15f));

        /// <summary>拉弓时每帧调：tension 0～1，0 = 停</summary>
        public static void Creak(Vector3 p, float tension)
        {
            Init();
            if (!Progress.Sfx) tension = 0;
            creak.transform.position = p;
            float target = tension > 0.02f ? 0.15f + tension * 0.35f : 0f;
            creak.volume = Mathf.MoveTowards(creak.volume, target, Time.deltaTime * 3f);
            creak.pitch = 0.85f + tension * 0.6f;
            if (creak.volume > 0.001f && !creak.isPlaying) creak.Play();
            if (creak.volume <= 0.001f && creak.isPlaying) creak.Stop();
        }

        public static void StopCreak() { if (creak) { creak.volume = 0; creak.Stop(); } }
    }
}

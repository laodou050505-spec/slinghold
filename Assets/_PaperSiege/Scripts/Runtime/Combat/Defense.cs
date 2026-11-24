// 防守：挡 / 接 / 灭火（方案第 4 节）。
// 吸住窗口：敌弹进入某只手掌心约 20 cm 内（这只手没在捏东西、没在拉弓），被手“吸住”减速、悬停在手边约 0.6 秒；
// 这段时间里握拳 = 接住（弹飞进弹盒，+1 发）；一直张着 = 挡开（弹飞走淡出）。已经握着拳迎上去也算接。
// 灭火：手掌往下拍（速度 ≥ 1.2 m/s）拍到自己城上着火的块；同一块拍两下灭掉。
using System.Collections.Generic;
using UnityEngine;

namespace PaperSiege
{
    [DefaultExecutionOrder(20)]
    public class Defense : MonoBehaviour
    {
        class Held { public Projectile p; public int side; public float t; public Vector3 from; }
        Director d;
        HandInput input;
        readonly Held[] held = new Held[2];
        readonly bool[] patArmed = { true, true };
        readonly Dictionary<Block, (int n, float t)> pats = new();
        InputTuning IT => InputTuning.I;

        public void Setup(Director director) { d = director; input = HandInput.Instance; held[0] = held[1] = null; pats.Clear(); }

        bool HandFree(int s)
        {
            var h = input.hands[s];
            if (!h.tracked || h.pinch) return false;
            if (d.supply && d.supply.Holding(s)) return false;
            if (s == 0 && d.sling && d.sling.Holding) return false;
            if (s == 1 && d.sling && d.sling.Drawing) return false;
            return held[s] == null;
        }

        void Update()
        {
            if (!d || d.phase != Director.Phase.Battle) return;
            input ??= HandInput.Instance;
            if (!input) return;
            // 吸住
            foreach (var p in Projectile.Live)
            {
                if (!p || !p.Catchable) continue;
                for (int s = 0; s < 2; s++)
                {
                    if (!HandFree(s)) continue;
                    var h = input.hands[s];
                    if (Vector3.Distance(p.transform.position, h.palm) < IT.catchRadius)
                    {
                        p.Hold();
                        held[s] = new Held { p = p, side = s, from = p.transform.position };
                        break;
                    }
                }
            }
            for (int s = 0; s < 2; s++) UpdateHeld(s);
            Pats();
        }

        void UpdateHeld(int s)
        {
            var c = held[s];
            if (c == null) return;
            if (!c.p) { held[s] = null; return; }
            var h = input.hands[s];
            c.t += Time.deltaTime;
            if (!h.tracked) { Deflect(s, Vector3.up); return; }
            // 悬停在掌心前方一点，轻轻转
            var target = h.palm + h.palmN.normalized * 0.05f;
            float k = Mathf.SmoothStep(0, 1, Mathf.Clamp01(c.t / 0.15f));
            c.p.transform.position = Vector3.Lerp(c.from, target, k);
            c.p.transform.Rotate(Vector3.up, 200f * Time.deltaTime, Space.World);
            if (h.fist) { Catch(s); return; }
            if (c.t > IT.catchHover) Deflect(s, h.palmN);
        }

        void Catch(int s)
        {
            var c = held[s]; held[s] = null;
            var p = c.p;
            Fx.Sparkle(p.transform.position, new Color(1f, 0.92f, 0.6f), 0.03f);
            Sfx.Catch(p.transform.position);
            d.ammo.Add(p.type);
            d.stats.caught++;
            // 飞进弹盒（有过程）；没架弓时弹盒不在，就直接缩没
            var box = d.sling ? d.sling.Box : null;
            StartCoroutine(FlyInto(p, box && box.isActiveAndEnabled ? box.SlotPos(p.type) : (Vector3?)null));
        }

        System.Collections.IEnumerator FlyInto(Projectile p, Vector3? to)
        {
            p.state = Projectile.State.Spent;
            var a = p.transform.position;
            for (float t = 0; t < 0.35f && p; t += Time.deltaTime)
            {
                float k = t / 0.35f;
                if (to.HasValue) p.transform.position = Vector3.Lerp(a, to.Value, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.05f;
                p.transform.localScale = Vector3.one * (1f - k * 0.6f);
                yield return null;
            }
            if (p) Destroy(p.gameObject);
        }

        void Deflect(int s, Vector3 palmN)
        {
            var c = held[s]; held[s] = null;
            if (!c.p) return;
            var away = Vector3.ProjectOnPlane(palmN, Vector3.up);
            if (away.sqrMagnitude < 0.01f) away = Vector3.ProjectOnPlane(c.p.transform.position - (d.head ? d.head.transform.position : Vector3.zero), Vector3.up);
            c.p.Deflect(away.normalized * 1.2f + Vector3.up * 1.4f);
            Fx.Dust(c.p.transform.position, 0.02f, 6);
            Sfx.Block(c.p.transform.position);
            d.stats.blocked++;
        }

        void Pats()
        {
            var mine = d.arena.mine;
            if (!mine) return;
            for (int s = 0; s < 2; s++)
            {
                var h = input.hands[s];
                if (!h.tracked) continue;
                float sp = h.vel.magnitude;
                if (sp < 0.5f) patArmed[s] = true;
                if (!patArmed[s] || sp < IT.patSpeed || h.vel.y > -0.5f * sp) continue;
                foreach (var b in mine.Burning)
                {
                    if (b.Dead) continue;
                    if (Vector3.Distance(b.WorldBounds.ClosestPoint(h.palm), h.palm) > IT.patRadius) continue;
                    patArmed[s] = false;
                    pats.TryGetValue(b, out var rec);
                    int n = Time.time - rec.t < 1.5f ? rec.n + 1 : 1;
                    pats[b] = (n, Time.time);
                    Fx.Dust(h.palm, 0.03f, 5);
                    if (n >= 2) { b.Extinguish(); d.stats.extinguished++; Sfx.Extinguish(b.transform.position); pats.Remove(b); }
                    break;
                }
            }
        }
    }
}

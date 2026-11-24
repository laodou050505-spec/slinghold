// 玩家的弹药（全部限量；接住的敌弹归自己）。弹盒实物显示由 AmmoBox 读这里。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PaperSiege
{
    public class AmmoStore
    {
        public const int MaxPerType = 5;   // 弹盒每格 5 个钉孔
        readonly int[] n = new int[5];
        public event Action<Ammo, int> Changed;
        public Ammo selected = Ammo.Stone;

        public void Reset(Dictionary<Ammo, int> start)
        {
            Array.Clear(n, 0, n.Length);
            foreach (var kv in start) n[(int)kv.Key] = Mathf.Min(MaxPerType, kv.Value);
            selected = Ammo.Stone;
            for (int i = 0; i < 5; i++) Changed?.Invoke((Ammo)i, n[i]);
        }

        public int Count(Ammo a) => n[(int)a];
        public int Total { get { int t = 0; foreach (var x in n) t += x; return t; } }

        public void Add(Ammo a, int k = 1)
        {
            n[(int)a] = Mathf.Min(MaxPerType, n[(int)a] + k);
            Changed?.Invoke(a, n[(int)a]);
        }

        public bool Take(Ammo a)
        {
            if (n[(int)a] <= 0) return false;
            n[(int)a]--;
            Changed?.Invoke(a, n[(int)a]);
            return true;
        }

        /// <summary>当前选中的弹没了：自动换成还有的（石丸优先）</summary>
        public Ammo AutoSelect()
        {
            if (n[(int)selected] > 0) return selected;
            for (int i = 0; i < 5; i++) if (n[i] > 0) { selected = (Ammo)i; break; }
            return selected;
        }
    }
}

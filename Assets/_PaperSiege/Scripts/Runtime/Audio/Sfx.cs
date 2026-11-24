// 音效入口（D5 换成程序合成 / 可商用素材，见 Audio/CREDITS.md）。现在先留接口，玩法代码只调这里。
using UnityEngine;

namespace PaperSiege
{
    public static class Sfx
    {
        public static void Hit(Ammo a, Mat m, Vector3 p) { }
        public static void Launch(Vector3 p, float power) { }
        public static void Catch(Vector3 p) { }
        public static void Block(Vector3 p) { }
        public static void Creak(Vector3 p, float tension) { }
        public static void Windup(Vector3 p) { }
        public static void Fire(Vector3 p) { }
        public static void Extinguish(Vector3 p) { }
        public static void Place(Vector3 p) { }
    }
}

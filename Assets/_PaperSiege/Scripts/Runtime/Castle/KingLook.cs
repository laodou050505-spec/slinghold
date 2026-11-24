// 帅旗灯笼的亮度跟着耐久走：满耐久最亮，每挨一下暗一档（挨打那一下先闪一下）
using UnityEngine;

namespace PaperSiege
{
    public static class KingLook
    {
        public static void SetGlow(Block king, float k)
        {
            foreach (var r in king.GetComponentsInChildren<MeshRenderer>())
            {
                if (r.name != "K_Lantern") continue;
                var mpb = new MaterialPropertyBlock();
                r.GetPropertyBlock(mpb);
                mpb.SetFloat("_EmissionStrength", Mathf.Lerp(0.15f, 1.4f, Mathf.Clamp01(k)));
                r.SetPropertyBlock(mpb);
            }
        }
    }
}

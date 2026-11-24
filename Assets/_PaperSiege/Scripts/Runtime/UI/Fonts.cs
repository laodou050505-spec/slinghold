// 字体：龙藏体（毛笔手写，OFL，见 Resources/Fonts/README.md）运行时生成 TextMeshPro 动态字体（用到哪个字烘哪个字）。
using TMPro;
using UnityEngine;

namespace PaperSiege
{
    public static class Fonts
    {
        static TMP_FontAsset hand;
        public static TMP_FontAsset Hand
        {
            get
            {
                if (hand) return hand;
                var f = Resources.Load<Font>("Fonts/LongCang-Regular");
                if (!f) return null;
                hand = TMP_FontAsset.CreateFontAsset(f, 72, 8, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                hand.name = "LongCang (dynamic)";
                return hand;
            }
        }
    }
}

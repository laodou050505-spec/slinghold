// 字体：龙藏体（毛笔手写，OFL，见 Resources/Fonts/README.md）运行时生成 TextMeshPro 动态字体（用到哪个字烘哪个字）。
using TMPro;
using UnityEngine;

namespace PaperSiege
{
    public static class Fonts
    {
        static TMP_FontAsset brand, sans;

        static TMP_FontAsset Make(string path, string name)
        {
            var f = Resources.Load<Font>(path);
            if (!f) return null;
            var fa = TMP_FontAsset.CreateFontAsset(f, 72, 8, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            fa.name = name;
            return fa;
        }

        /// <summary>思源黑体（署名中文、手写体缺字时兜底）</summary>
        public static TMP_FontAsset Sans => sans ? sans : (sans = Make("Fonts/NotoSansSC-Medium", "NotoSansSC (dynamic)"));

        /// <summary>Outfit（Tripo 品牌字体，署名英文），缺字回退思源黑体</summary>
        public static TMP_FontAsset Brand
        {
            get
            {
                if (brand) return brand;
                brand = Make("Fonts/Outfit", "Outfit (dynamic)");
                if (brand && Sans) brand.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { Sans };
                return brand;
            }
        }

        static TMP_FontAsset type;
        /// <summary>Courier Prime（封面木签、打字机字），中文回退思源黑体</summary>
        public static TMP_FontAsset Type
        {
            get
            {
                if (type) return type;
                type = Make("Fonts/CourierPrime-Bold", "CourierPrime (dynamic)");
                if (type && Sans) type.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { Sans };
                return type;
            }
        }

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
                if (Sans) hand.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { Sans };
                return hand;
            }
        }
    }
}

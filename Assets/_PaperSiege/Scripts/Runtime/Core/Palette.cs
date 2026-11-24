// 积木漆色（取自概念图 A：旧木头玩具的低饱和做旧色）。材质按这些颜色各建一份，所有同色积木共用（SRP Batcher 合批）。
using UnityEngine;

namespace PaperSiege
{
    public enum Paint { Teal, Ochre, Brick, Cream, Blue, Sage, Bare, Stone }

    public static class Palette
    {
        public static Color Of(Paint p) => p switch
        {
            Paint.Teal => Hex("6d958e"),
            Paint.Ochre => Hex("c09a55"),
            Paint.Brick => Hex("a05a46"),
            Paint.Cream => Hex("dcd1b7"),
            Paint.Blue => Hex("6b8198"),
            Paint.Sage => Hex("8d9773"),
            Paint.Bare => Hex("c49b6c"),     // 不上漆：漆面覆盖 0，看到的是裸木
            Paint.Stone => Hex("6e7c45"),    // 石块的“漆色”是青苔色
            _ => Color.white,
        };

        public static Color Hex(string h)
        {
            ColorUtility.TryParseHtmlString("#" + h, out var c);
            return c;
        }

        public static readonly Paint[] WoodPaints = { Paint.Teal, Paint.Ochre, Paint.Brick, Paint.Cream, Paint.Blue, Paint.Sage };
    }
}

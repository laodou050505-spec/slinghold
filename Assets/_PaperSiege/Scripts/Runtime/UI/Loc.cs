// 中英文本表（通用规范第 8 条）：以中文原文为键，取对应英文。默认跟随系统语言，标题页可切换。
// 代码里所有给玩家看的字都写成 Loc.T("中文")，不许直接写死英文。
using System.Collections.Generic;
using UnityEngine;

namespace PaperSiege
{
    public static class Loc
    {
        static bool? english;
        public static bool English
        {
            get => english ??= !(Application.systemLanguage == SystemLanguage.Chinese || Application.systemLanguage == SystemLanguage.ChineseSimplified || Application.systemLanguage == SystemLanguage.ChineseTraditional);
            set => english = value;
        }

        static readonly Dictionary<string, string> en = new()
        {
            // 教学（第 1 关边玩边学）
            { "左手捏住\n举起弹弓", "Pinch with\nyour left hand" },
            { "右手\n捏住皮兜", "Right hand:\npinch the pouch" },
            { "往右肩\n斜着拉开", "Pull back to\nyour right shoulder" },
            { "瞄准对面\n松手！", "Aim…\nand let go!" },
            { "敌弹来了\n张手去挡", "Incoming!\nOpen hand to block" },
            { "再来一发\n握拳接住", "Another one —\nfist to catch" },
            { "接住的弹\n归你了", "Caught shots\nare yours" },
            { "城墙破了\n拿积木补上", "Wall's broken —\npatch it from the tray" },
            { "补好了！", "Patched!" },
            { "打倒对面\n的帅旗！", "Knock down\ntheir banner!" },
            { "再试一次", "Try again" },
            { "新弹：火丸\n木头一点就着", "New: Fire shot\nwood catches & spreads" },
            { "新弹：冰丸\n冻脆一碰就碎", "New: Ice shot\nfrozen blocks shatter" },
            { "新弹：炸丸\n石墙也能炸开", "New: Bomb\nblasts through stone" },
            { "新弹：分裂丸\n空中一分为三", "New: Split shot\nbreaks into three" },
            { "着火了\n拍两下灭火", "On fire!\nslap it out twice" },
            { "没弹了\n接住敌弹来用", "Out of shots —\ncatch theirs!" },
            // 关卡名
            { "第一关 · 矮墙", "1 · The Low Wall" },
            { "第二关 · 小城", "2 · Little Keep" },
            { "第三关 · 木楼", "3 · Timber Tower" },
            { "第四关 · 着火", "4 · Kindling" },
            { "第五关 · 高塔", "5 · High Towers" },
            { "第六关 · 双层", "6 · Double Walls" },
            { "第七关 · 石墙", "7 · Stone Walls" },
            { "第八关 · 盾墙", "8 · Shield Wall" },
            { "第九关 · 要塞", "9 · Fortress" },
            { "第十关 · 王城", "10 · Royal Citadel" },
        };

        public static string T(string zh)
        {
            if (!English) return zh;
            return en.TryGetValue(zh, out var e) ? e : zh;
        }
    }
}

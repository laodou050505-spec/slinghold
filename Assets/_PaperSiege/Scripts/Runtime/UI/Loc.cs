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
            // 标题 / 结算
            { "开始", "Start" }, { "继续", "Continue" },
            { "开始游戏", "PLAY" }, { "继续游戏", "CONTINUE" }, { "选择关卡", "LEVELS" }, { "设置", "SETTINGS" }, { "退出游戏", "QUIT" }, { "返回", "BACK" },
            { "切换语言\nLanguage", "切换语言\nLanguage" },
            { "声音：开", "SOUND: ON" }, { "声音：只留音效", "SOUND: SFX ONLY" }, { "声音：关", "SOUND: OFF" },
            { "双手弓", "BOW: TWO HANDS" }, { "单手弓", "BOW: ONE HAND" },
            { "把手抬到眼前", "Raise your hands\ninto view" },
            { "右手捏住皮兜\n往后拉", "Right hand: pinch\nthe pouch, pull back" },
            { "音乐", "Music" }, { "音乐 ×", "Music off" }, { "音效", "SFX" }, { "音效 ×", "SFX off" },
            { "胜利！", "Victory!" }, { "失守……", "Your banner fell…" }, { "时间到……", "Out of time…" },
            { "射 {0}　挡 {1}　接 {2}　修 {3}", "Shots {0}   Blocks {1}   Catches {2}   Repairs {3}" },
            { "赢　·　接住 3 发　·　帅旗没挨打", "Win  ·  Catch 3  ·  Banner untouched" },
            { "下一关", "NEXT" }, { "重来", "RETRY" }, { "回标题", "TITLE" }, { "全城尽破", "Every fort has fallen" }, { "暂停", "PAUSE" }, { "举弓", "Raise" }, { "拉放", "Pull·Let go" }, { "换弹", "Swap" }, { "挡", "Block" }, { "接", "Catch" }, { "补城", "Repair" }, { "灭火", "Pat out" }, { "从头再来", "PLAY AGAIN" },
            { "射 {0}　挡 {1}", "Shots {0}   Blocks {1}" }, { "接 {0}　修 {1}", "Catches {0}   Repairs {1}" },
            { "打下对方帅旗", "Banner taken" }, { "接住 3 发", "3 catches" }, { "帅旗没挨打", "Banner untouched" },
            { "夺旗", "Win" }, { "接三发", "3 catches" }, { "旗无伤", "Unhurt" }, { "无伤", "Unhurt" }, { "射{0} 挡{1} 修{2}", "Shot {0} · Block {1} · Fix {2}" }, { "接{0}/3", "Catch {0}/3" },
            // 关卡名
            { "第一关 · 矮墙", "1 · The Low Wall" },
            { "第二关 · 小城", "2 · Little Keep" },
            { "第三关 · 木楼", "3 · Timber Hall" },
            { "第四关 · 着火", "4 · Kindling" },
            { "第五关 · 草垛", "5 · Haystacks" },
            { "第六关 · 火药桶", "6 · Powder Kegs" },
            { "第七关 · 冰丸", "7 · Ice Shot" },
            { "第八关 · 水桶", "8 · The Bucket" },
            { "第九关 · 冰池", "9 · Frozen Pond" },
            { "第十关 · 炸丸", "10 · Bomb Shot" },
            { "第十一关 · 连环", "11 · Chain Reaction" },
            { "第十二关 · 拱桥", "12 · The Arch Bridge" },
            { "第十三关 · 分裂", "13 · Split Shot" },
            { "第十四关 · 三钟", "14 · Three Bells" },
            { "第十五关 · 吊秤", "15 · The Scale" },
            { "第十六关 · 要塞", "16 · Fortress" },
            { "第十七关 · 混战", "17 · Melee" },
            { "第十八关 · 王城", "18 · Royal Citadel" },
            { "草垛：一点就着\n一排烧过去", "Haystack: catches fast,\nburns down the row" },
            { "火药桶：着火就炸\n用火丸点它", "Powder keg: blows\nwhen it catches fire" },
            { "水桶：冰丸打它\n周围冻脆一碰就碎", "Bucket: ice it, and\nall around turns brittle" },
            { "冰池：冻住它\n上面的楼会滑倒", "Pond: freeze it and\nthe tower slides over" },
            { "火药桶挨着火药桶\n炸一个响一串", "Kegs in a row:\nblow one, blow all" },
            { "拱心石：炸掉它\n整座桥都会塌", "Keystone: blast it and\nthe bridge comes down" },
            { "分裂丸高抛三口钟\n一起响盾墙就倒", "Lob a split shot:\nring all three bells" },
            { "往另一头秤盘里打\n压翻它掀飞帅旗", "Load the far pan\nto flip the flag off" },
            { "冰丸也能冻住投石机", "Ice shot can freeze\nthe catapult too" },
        };

        public static string T(string zh)
        {
            if (!English) return zh;
            return en.TryGetValue(zh, out var e) ? e : zh;
        }
    }
}

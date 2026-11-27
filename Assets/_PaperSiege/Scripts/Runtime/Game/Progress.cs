// 进度存档（本机 PlayerPrefs）：解锁到第几关、每关最好几颗星、音乐 / 音效开关、语言。
using UnityEngine;

namespace PaperSiege
{
    public static class Progress
    {
        /// <summary>
        /// 11-27 关卡从 10 关扩到 18 关、编号重排：老存档按“同一座城”搬到新编号（旧 1～2 → 新 1～2；旧 3 木楼 → 新 3；旧 4 着火 → 新 4；
        /// 旧 5 高塔 → 新 7；旧 6 双层 → 无对应（并进新 6）；旧 7 石墙 → 新 10；旧 8 盾墙 → 新 13；旧 9 要塞 → 新 16；旧 10 王城 → 新 18）。只迁一次
        /// </summary>
        public static void Migrate()
        {
            if (PlayerPrefs.GetInt("ps_layout", 10) == Levels.Count) return;
            int[] map = { 0, 1, 2, 3, 6, 5, 9, 12, 15, 17 };
            var stars = new int[10];
            for (int i = 0; i < 10; i++) stars[i] = PlayerPrefs.GetInt("ps_stars_" + i, 0);
            int oldUnlocked = Mathf.Clamp(PlayerPrefs.GetInt("ps_unlocked", 0), 0, 9);
            for (int i = 0; i < Levels.Count; i++) PlayerPrefs.DeleteKey("ps_stars_" + i);
            for (int i = 0; i < 10; i++) if (stars[i] > 0) PlayerPrefs.SetInt("ps_stars_" + map[i], stars[i]);
            PlayerPrefs.SetInt("ps_unlocked", map[oldUnlocked]);
            PlayerPrefs.SetInt("ps_layout", Levels.Count);
            PlayerPrefs.Save();
        }

        public static int Unlocked { get => Mathf.Clamp(PlayerPrefs.GetInt("ps_unlocked", 0), 0, Levels.Count - 1); set { PlayerPrefs.SetInt("ps_unlocked", Mathf.Clamp(value, 0, Levels.Count - 1)); PlayerPrefs.Save(); } }
        public static int Stars(int lv) => PlayerPrefs.GetInt("ps_stars_" + lv, 0);
        /// <summary>十关全部打过（看过结局）：封面主按钮变“从头再来”，选关全开</summary>
        public static bool Cleared { get => PlayerPrefs.GetInt("ps_cleared", 0) == 1; set { PlayerPrefs.SetInt("ps_cleared", value ? 1 : 0); PlayerPrefs.Save(); } }
        public static int TotalStars { get { int n = 0; for (int i = 0; i < Levels.Count; i++) n += Stars(i); return n; } }
        public static void SetStars(int lv, int n) { if (n > Stars(lv)) { PlayerPrefs.SetInt("ps_stars_" + lv, n); PlayerPrefs.Save(); } }
        public static bool Music { get => PlayerPrefs.GetInt("ps_music", 1) == 1; set { PlayerPrefs.SetInt("ps_music", value ? 1 : 0); PlayerPrefs.Save(); } }
        /// <summary>单手模式（兜底）：自己城边立一个固定弓架，只用右手拉；左手专门挡 / 接</summary>
        public static bool OneHand { get => PlayerPrefs.GetInt("ps_onehand", 0) == 1; set { PlayerPrefs.SetInt("ps_onehand", value ? 1 : 0); PlayerPrefs.Save(); } }
        public static bool Sfx { get => PlayerPrefs.GetInt("ps_sfx", 1) == 1; set { PlayerPrefs.SetInt("ps_sfx", value ? 1 : 0); PlayerPrefs.Save(); } }

        public static void LoadLanguage()
        {
            int l = PlayerPrefs.GetInt("ps_lang", -1);
            if (l >= 0) Loc.English = l == 1;
        }

        public static void SaveLanguage() { PlayerPrefs.SetInt("ps_lang", Loc.English ? 1 : 0); PlayerPrefs.Save(); }
    }
}

// 进度存档（本机 PlayerPrefs）：解锁到第几关、每关最好几颗星、音乐 / 音效开关、语言。
using UnityEngine;

namespace PaperSiege
{
    public static class Progress
    {
        public static int Unlocked { get => Mathf.Clamp(PlayerPrefs.GetInt("ps_unlocked", 0), 0, Levels.Count - 1); set { PlayerPrefs.SetInt("ps_unlocked", Mathf.Clamp(value, 0, Levels.Count - 1)); PlayerPrefs.Save(); } }
        public static int Stars(int lv) => PlayerPrefs.GetInt("ps_stars_" + lv, 0);
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

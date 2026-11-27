// 编辑器菜单：演示 / 手动试玩
using UnityEditor;
using UnityEditor.SceneManagement;

namespace PaperSiege.EditorTools
{
    public static class PSDemo
    {
        [MenuItem("PaperSiege/演示：拉弓发射")]
        public static void Demo() => Play(true);

        [MenuItem("PaperSiege/桌面试玩（鼠标 + 键盘模拟双手）")]
        public static void Manual() => Play(false);

        [MenuItem("PaperSiege/直接看结局（进度设到最后一关并自动打下王城）")]
        public static void Ending()
        {
            Progress.Unlocked = Levels.Count - 1;
            SessionState.SetBool("PS_Ending", true);
            Play(false);
        }

        [MenuItem("PaperSiege/进度设到最后一关（自己打）")]
        public static void ToLastLevel() { Progress.Unlocked = Levels.Count - 1; Play(false); }

        [MenuItem("PaperSiege/清空存档进度")]
        public static void ResetProgress() { Progress.ClearSeen(); Progress.Unlocked = 0; Progress.Cleared = false; for (int i = 0; i < Levels.Count; i++) UnityEngine.PlayerPrefs.DeleteKey("ps_stars_" + i); UnityEngine.PlayerPrefs.Save(); }

        static void Play(bool demo)
        {
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            EditorSceneManager.OpenScene(ProjectSetup.ScenePath);
            DemoDriver.Requested = demo;
            SessionState.SetBool("PS_Demo", demo);
            EditorApplication.isPlaying = true;
        }

        /// <summary>演示开始后把 Game 窗口放到最前、放大（Shift+空格 还原）</summary>
        public static void ShowGameView()
        {
            var t = System.Type.GetType("UnityEditor.GameView,UnityEditor");
            if (t == null) return;
            var w = UnityEditor.EditorWindow.GetWindow(t);
            w.Focus();
            w.maximized = true;
        }

        [InitializeOnLoadMethod]
        static void Restore()   // 进运行模式会重载域，静态变量要从 SessionState 恢复
        {
            DemoDriver.Requested = SessionState.GetBool("PS_Demo", false);
            EndingPreview.Requested = SessionState.GetBool("PS_Ending", false);
            SessionState.SetBool("PS_Ending", false);   // 只用一次
        }
    }
}

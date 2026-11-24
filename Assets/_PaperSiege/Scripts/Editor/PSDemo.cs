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
        static void Restore() => DemoDriver.Requested = SessionState.GetBool("PS_Demo", false);   // 进运行模式会重载域，静态变量要从 SessionState 恢复
    }
}

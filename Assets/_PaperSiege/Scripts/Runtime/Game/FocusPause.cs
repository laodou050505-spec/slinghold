// 摘下头显 / 打开系统菜单（应用失去焦点）时冻结对局：时间停住、声音暂停。
// 回来后不是一下子开打，而是用 1 秒从静止慢慢恢复到正常速度（有过程，也给玩家一点反应时间）。
// 编辑器里不启用：编辑器窗口一失焦（比如在终端里跑测试）游戏就会被冻住。
using System.Collections;
using UnityEngine;

namespace PaperSiege
{
    public class FocusPause : MonoBehaviour
    {
        public static bool testInEditor;
        public bool Paused { get; private set; }
        Coroutine resume;

        void OnApplicationFocus(bool focus) => Handle(!focus);
        void OnApplicationPause(bool pause) => Handle(pause);

        public void Handle(bool lose)
        {
            if (Application.isEditor && !testInEditor) return;
            var d = Director.I;
            if (lose)
            {
                if (Paused || !d || d.phase != Director.Phase.Battle) return;
                if (resume != null) { StopCoroutine(resume); resume = null; }
                Paused = true;
                Time.timeScale = 0f;
                AudioListener.pause = true;
            }
            else if (Paused && resume == null) resume = StartCoroutine(Resume());
        }

        IEnumerator Resume()
        {
            AudioListener.pause = false;
            if (Director.I && Director.I.paused) { Paused = false; resume = null; yield break; }   // 菜单暂停着：保持冻住，等玩家按“继续”
            yield return new WaitForSecondsRealtime(0.4f);
            for (float t = 0; t < 1f; t += Time.unscaledDeltaTime) { Time.timeScale = Mathf.SmoothStep(0.05f, 1f, t); yield return null; }
            Time.timeScale = 1f;
            Paused = false;
            resume = null;
        }
    }
}

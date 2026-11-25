// 手跟丢提示（通用规范第 4 条）：对战中两只手都跟丢超过 lostPrompt（默认 2.5 秒）→ 视野右前方挂出木牌“把手抬到眼前”；
// 任一只手回来就收牌。只放下一只手休息很正常，不提示。编辑器模拟手默认不提示（测试时把 testInEditor 打开）。
using UnityEngine;

namespace PaperSiege
{
    public class HandLostHint : MonoBehaviour
    {
        public static bool testInEditor;
        Director d;
        HintTag tag;
        bool showing;

        void Start() { d = Director.I; tag = HintTag.Build(d.head.transform); }
        void OnDestroy() { if (tag) Destroy(tag.gameObject); }

        void Update()
        {
            var input = HandInput.Instance;
            if (!d || !input || !tag) return;
            bool active = d.phase == Director.Phase.Battle && (!HandInput.Sim || testInEditor);
            float t = InputTuning.I.lostPrompt;
            bool bothLost = !input.hands[0].tracked && !input.hands[1].tracked && input.hands[0].lostFor > t && input.hands[1].lostFor > t;
            bool want = active && bothLost;
            if (want == showing) return;
            showing = want;
            tag.Show(want ? "把手抬到眼前" : "");
        }

        public bool Showing => showing;
    }
}

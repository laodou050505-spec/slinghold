// 布旗轻轻飘：绕旗杆小幅摆动 + 旗尾上下起伏（不用骨骼，转动挂旗点即可）
using UnityEngine;

namespace PaperSiege
{
    public class FlagSway : MonoBehaviour
    {
        Quaternion rest;
        float seed, shake;
        public void Shake(float k) => shake = Mathf.Max(shake, k);
        void Start() { rest = transform.localRotation; seed = Random.value * 10f; }
        void Update()
        {
            float t = Time.time + seed;
            shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 1.5f);
            float j = shake * 25f * Mathf.Sin(Time.time * 38f);   // 挨打：旗子猛抖一下再慢慢停
            transform.localRotation = rest * Quaternion.Euler(Mathf.Sin(t * 2.3f) * 4f + j * 0.3f, Mathf.Sin(t * 1.3f) * 14f + j, Mathf.Sin(t * 3.1f) * 3f);
        }
    }
}

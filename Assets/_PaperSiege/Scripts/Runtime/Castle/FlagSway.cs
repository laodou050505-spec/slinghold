// 布旗轻轻飘：绕旗杆小幅摆动 + 旗尾上下起伏（不用骨骼，转动挂旗点即可）
using UnityEngine;

namespace PaperSiege
{
    public class FlagSway : MonoBehaviour
    {
        Quaternion rest;
        float seed;
        void Start() { rest = transform.localRotation; seed = Random.value * 10f; }
        void Update()
        {
            float t = Time.time + seed;
            transform.localRotation = rest * Quaternion.Euler(Mathf.Sin(t * 2.3f) * 4f, Mathf.Sin(t * 1.3f) * 14f, Mathf.Sin(t * 3.1f) * 3f);
        }
    }
}

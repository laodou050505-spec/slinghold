// 我方帅旗旗面：换成一面干净的燕尾布旗，中间原样印 TRIPO 图标（只图标、不带字、不垫底、不改色）。
// 敌方帅旗仍是道具贴图里的太阳纹，敌我一眼分得清。
// 网格照原旗面的尺寸（模型空间：挂旗边在原点、朝 -X 展开 0.045 m、高 0.0275 m），前后各一片单面网格，
// 两片的贴图坐标都按“从这一面看是正的”排，所以从哪一面看图标都不是镜像；旗面微微起伏，摆动沿用 FlagSway。
using System.Collections.Generic;
using UnityEngine;

namespace PaperSiege
{
    public static class MineFlag
    {
        const float W = 0.045f, H = 0.0275f, Notch = 0.008f, Gap = 0.00035f;
        static Mesh mesh;
        static Material mat;

        public static void Apply(GameObject part)
        {
            mat ??= Resources.Load<Material>("Cover/M_FlagMine");
            if (!mat) return;
            mesh ??= Build();
            part.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = part.GetComponent<MeshRenderer>();
            if (mr) mr.sharedMaterial = mat;
        }

        static Mesh Build()
        {
            const int nx = 16, ny = 8;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int side = 0; side < 2; side++)          // 0 = 朝 -Z 那面，1 = 朝 +Z 那面
            {
                int b0 = v.Count;
                for (int y = 0; y <= ny; y++)
                    for (int x = 0; x <= nx; x++)
                    {
                        float s = x / (float)nx, t = y / (float)ny;       // s：挂旗边 0 → 旗尾 1
                        float px = -s * W, py = (t - 0.5f) * H;
                        float wave = (Mathf.Sin(s * 7f + t * 2f) * 0.0017f + Mathf.Sin(s * 3.1f) * 0.001f) * Mathf.Min(1f, s / 0.25f);
                        v.Add(new Vector3(px, py, wave + (side == 0 ? -Gap : Gap)));
                        // 两片的三角形各自朝外：side 0 这片从 +Z 一侧看得见（右手边 = 旗尾），side 1 从 -Z 一侧（右手边 = 挂旗边）
                        // → 贴图横坐标按各自看过去的左右排（11-26 截图核对过：反过来两面都是镜像）
                        float u = side == 0 ? s : 1f - s;
                        uv.Add(new Vector2(u, t));
                    }
                for (int y = 0; y < ny; y++)
                    for (int x = 0; x < nx; x++)
                    {
                        float cs = (x + 0.5f) / nx * W, cz = ((y + 0.5f) / ny - 0.5f) * H;
                        if (cs > W - Notch * (1 - Mathf.Abs(cz) / (H / 2))) continue;   // 燕尾缺口
                        int a = b0 + y * (nx + 1) + x, b = a + 1, c = a + nx + 1, e = c + 1;
                        if (side == 0) tri.AddRange(new[] { a, c, b, b, c, e });
                        else tri.AddRange(new[] { a, b, c, b, e, c });
                    }
            }
            var m = new Mesh { name = "MineFlag" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}

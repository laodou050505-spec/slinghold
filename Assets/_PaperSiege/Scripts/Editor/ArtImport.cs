// 美术资源导入设置：
// - 积木模型：不导材质、导入法线、MikkTSpace 切线（和 Blender 烘法线一致），并把“平滑法线”写进 UV3 给描边用
//   （积木有硬边，普通法线在棱上是断开的，直接沿它外扩描边会裂开；按位置把法线平均后外扩就是连续的一圈）。
// - 积木贴图：mask / normal 线性，albedo sRGB；安卓用 ASTC。
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PaperSiege.EditorTools
{
    public class ArtImport : AssetPostprocessor
    {
        const string ArtRoot = "Assets/_PaperSiege/Art/";

        const string CoverRoot = "Assets/_PaperSiege/Resources/Cover/";
        // 11-27 补：关卡机关（Resources/Mech）、虚拟桌（Resources/Table）原来不在这里，法线 / 遮罩图被当成 sRGB 颜色图导入、模型也没写描边用的平滑法线
        static readonly string[] ExtraRoots = { "Assets/_PaperSiege/Resources/Mech/", "Assets/_PaperSiege/Resources/Table/" };
        static bool IsArt(string p) { p = p.Replace('\\', '/'); if (p.StartsWith(ArtRoot) || p.StartsWith(CoverRoot)) return true; foreach (var r in ExtraRoots) if (p.StartsWith(r)) return true; return false; }
        static bool IsCoverPrint(string p) { var f = System.IO.Path.GetFileName(p); return p.Replace('\\', '/').StartsWith(CoverRoot) && (f.StartsWith("banner_") || f.StartsWith("scrap_") || f.StartsWith("mark_") || f.StartsWith("flag_") || f.StartsWith("result_") || f.StartsWith("note_")); }

        void OnPreprocessModel()
        {
            if (!IsArt(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.importAnimation = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.isReadable = true;            // 运行时要读顶点（碰撞体尺寸、碎裂）
            mi.useFileScale = true;
            mi.globalScale = 1f;
        }

        void OnPostprocessModel(GameObject root)
        {
            if (!IsArt(assetPath)) return;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh) WriteSmoothNormals(mf.sharedMesh);
        }

        /// <summary>按顶点位置平均法线（面积加权已由导入的法线体现），写入 UV3</summary>
        public static void WriteSmoothNormals(Mesh m)
        {
            var v = m.vertices; var n = m.normals;
            if (n == null || n.Length != v.Length) return;
            var acc = new Dictionary<Vector3Int, Vector3>();
            Vector3Int Key(Vector3 p) => new(Mathf.RoundToInt(p.x * 1e5f), Mathf.RoundToInt(p.y * 1e5f), Mathf.RoundToInt(p.z * 1e5f));
            for (int i = 0; i < v.Length; i++)
            {
                var k = Key(v[i]);
                acc[k] = acc.TryGetValue(k, out var s) ? s + n[i] : n[i];
            }
            var outN = new List<Vector3>(v.Length);
            for (int i = 0; i < v.Length; i++)
            {
                var s = acc[Key(v[i])];
                outN.Add(s.sqrMagnitude > 1e-8f ? s.normalized : n[i]);
            }
            m.SetUVs(3, outN);
        }

        /// <summary>音频（11-26 离线合成）：音效解压进内存、单声道、预加载（播放零延迟）；音乐 Vorbis 压缩、流式读取</summary>
        void OnPreprocessAudio()
        {
            if (!assetPath.Contains("/Resources/Audio/")) return;
            var ai = (AudioImporter)assetImporter;
            bool music = assetPath.Contains("/Audio/Music/");
            ai.forceToMono = !music;
            ai.loadInBackground = music;
            var st = ai.defaultSampleSettings;
            st.preloadAudioData = true;
            st.loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            st.compressionFormat = music ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.ADPCM;
            st.quality = music ? 0.55f : 1f;
            ai.defaultSampleSettings = st;
        }

        void OnPreprocessTexture()
        {
            var ti = (TextureImporter)assetImporter;
            // 启动画面（MADE WITH TRIPO）和应用图标：和其他游戏（《摘星》真机验证版）一样不压缩、不加安卓覆盖。
            // 11-27 查出：它们在 Art/ 下被套了积木贴图的 ASTC 6×6 压缩，头显里标志和小字边缘会有色块 / 发糊
            var pp = assetPath.Replace('\\', '/');
            if (pp.StartsWith(ArtRoot + "Brand/") || pp.StartsWith(ArtRoot + "Icon/"))
            {
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.ClearPlatformTextureSettings("Android");
                return;
            }
            if (!IsArt(assetPath)) return;
            string f = System.IO.Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
            if (f.EndsWith("_normal")) { ti.textureType = TextureImporterType.NormalMap; ti.sRGBTexture = false; }
            else if (f.EndsWith("_mask")) { ti.textureType = TextureImporterType.Default; ti.sRGBTexture = false; }
            else ti.sRGBTexture = true;
            ti.mipmapEnabled = true;
            ti.anisoLevel = 4;
            if (IsCoverPrint(assetPath)) { ti.alphaIsTransparency = true; ti.wrapMode = f.StartsWith("mark_dot") ? TextureWrapMode.Repeat : TextureWrapMode.Clamp; ti.npotScale = TextureImporterNPOTScale.None; }   // 点线沿线平铺
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            var android = ti.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = 2048;
            android.format = f.EndsWith("_normal") ? TextureImporterFormat.ASTC_5x5 : TextureImporterFormat.ASTC_6x6;
            ti.SetPlatformTextureSettings(android);
        }
    }
}

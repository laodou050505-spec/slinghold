// 一键打包到 PICO（照 Skywright/SkywrightBuild 改）：PICO 设置（透视 + 手势 + 眼动）→ 运行时着色器保底 → 图标 / 启动画面 → APK。
//   菜单 PaperSiege/打包 APK；命令行：cs.py 'PSBuild.BuildApkLater();'（下一帧开始，结果追加到 /tmp/papersiege_build.txt）
//   注意（规范第 3 条）：编辑器必须用 -buildTarget Android 启动，否则 PICO 处理清单的代码不编译，打出来是平面应用。
//   英文系统里的应用名在 Assets/Plugins/Android/AppName.androidlib/res/values-en（其它语言用产品名《木垒》）。
using System;
using System.IO;
using ByteDance.PICO.XR;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace PaperSiege.EditorTools
{
    public static class PSBuild
    {
        public const string ProductNameZh = "木垒", AppId = "com.tripoxr.papersiege";
        const string PicoSettingPath = "Assets/Resources/PXR_ProjectSetting.asset";
        const string ResultFile = "/tmp/papersiege_build.txt";
        public const string IconPath = "Assets/_PaperSiege/Art/Icon/icon_1024.png";
        const string SplashPath = "Assets/_PaperSiege/Art/Brand/made_with_tripo.png";
        const string SplashVrPath = "Assets/_PaperSiege/Art/Brand/made_with_tripo_vr.png";

        public static string Prepare()
        {
            var c = AssetDatabase.LoadAssetAtPath<PXR_ProjectSetting>(PicoSettingPath);
            if (!c)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
                c = ScriptableObject.CreateInstance<PXR_ProjectSetting>(); AssetDatabase.CreateAsset(c, PicoSettingPath);
            }
            c.portalInited = true; c.handTracking = true; c.videoSeeThrough = true; c.eyeTracking = true;
            c.handTrackingSupportType = HandTrackingSupport.ControllersAndHands;
            EditorUtility.SetDirty(c);
            // 运行时 new Material 的着色器 + 关键字变体：Resources/ShaderKeep 里的保底材质保证打进包
            var keep = ShaderKeep.Build();
            var gs = AssetDatabase.LoadAssetAtPath<GraphicsSettings>("ProjectSettings/GraphicsSettings.asset");
            var so = new SerializedObject(gs);
            so.FindProperty("m_InstancingStripping").intValue = 2;   // 运行时 new Material 开了实例化：实例化变体全部保留（MonsterCity 11-09 的坑）
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(gs);
            PlayerSettings.enableFrameTimingStats = true;
            AssetDatabase.SaveAssets();
            return $"hand={c.handTracking} eye={c.eyeTracking} vst={c.videoSeeThrough}\n{keep}\n{ShaderKeep.Check()}";
        }

        public static void ApplyIcon()
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(IconPath);
            if (ti == null) { Debug.LogWarning("[木垒] 没找到图标 " + IconPath + "（先在运行模式下执行 PSBuild.MakeIcon()）"); return; }
            if (ti.textureCompression != TextureImporterCompression.Uncompressed || ti.mipmapEnabled) { ti.textureCompression = TextureImporterCompression.Uncompressed; ti.mipmapEnabled = false; ti.SaveAndReimport(); }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new[] { tex });
            var nt = NamedBuildTarget.Android;
            foreach (var kind in new[] { UnityEditor.Android.AndroidPlatformIconKind.Legacy, UnityEditor.Android.AndroidPlatformIconKind.Round })
            { var icons = PlayerSettings.GetPlatformIcons(nt, kind); foreach (var ic in icons) ic.SetTextures(tex); PlayerSettings.SetPlatformIcons(nt, kind, icons); }
        }

        /// <summary>启动画面：MADE WITH + TRIPO 标志（规范第 8 条）。普通标志列表和 VR Splash Image 两处都要填（头显里只显示后者）</summary>
        public static void ApplySplash()
        {
            var ti = AssetImporter.GetAtPath(SplashPath) as TextureImporter;
            if (ti == null) { Debug.LogWarning("[木垒] 没找到启动画面图 " + SplashPath); return; }
            if (ti.textureType != TextureImporterType.Sprite || ti.mipmapEnabled || ti.textureCompression != TextureImporterCompression.Uncompressed)
            { ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.mipmapEnabled = false; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.alphaIsTransparency = true; ti.SaveAndReimport(); }
            var sp = AssetDatabase.LoadAssetAtPath<Sprite>(SplashPath);
            PlayerSettings.SplashScreen.show = true; PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SplashScreen.logos = new[] { PlayerSettings.SplashScreenLogo.Create(2f, sp) };
            var vi = AssetImporter.GetAtPath(SplashVrPath) as TextureImporter;
            if (vi == null) { Debug.LogWarning("[木垒] 没找到头显启动图 " + SplashVrPath); return; }
            if (vi.textureType != TextureImporterType.Default || vi.mipmapEnabled || vi.textureCompression != TextureImporterCompression.Uncompressed || vi.npotScale != TextureImporterNPOTScale.None)
            { vi.textureType = TextureImporterType.Default; vi.mipmapEnabled = false; vi.textureCompression = TextureImporterCompression.Uncompressed; vi.npotScale = TextureImporterNPOTScale.None; vi.wrapMode = TextureWrapMode.Clamp; vi.sRGBTexture = true; vi.SaveAndReimport(); }
            var vt = AssetDatabase.LoadAssetAtPath<Texture2D>(SplashVrPath);
            var all = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            var so = new SerializedObject(all[0]); var prop = so.FindProperty("m_VirtualRealitySplashScreen");
            if (prop != null && prop.objectReferenceValue != vt) { prop.objectReferenceValue = vt; so.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssets(); }
        }

        /// <summary>
        /// 应用图标：用游戏里自己的城实拍（不用生成图，和游戏画面一致）。要在运行模式下执行（城是运行时搭的）。
        /// 机位：正面略仰拍城门和帅旗，背景是第 4 关午后天空色的渐变，图标四周留安全边距（系统会裁圆角）。
        /// </summary>
        public static string MakeIcon()
        {
            var d = PaperSiege.Director.I;
            if (!d || d.arena == null || !d.arena.mine) return "要在运行模式、城搭好以后执行";
            var cam = new GameObject("IconCam").AddComponent<Camera>();
            var mine = d.arena.mine.transform;
            var f = d.arena.forward;
            var center = mine.position + Vector3.up * 0.16f;
            cam.transform.position = center - f * 1.0f + Vector3.up * 0.02f + Vector3.Cross(Vector3.up, f) * 0.3f;
            cam.transform.LookAt(center);
            cam.fieldOfView = 26f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 3f;   // 远裁剪面只到 3 m：看不到房间墙和敌城
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.93f, 0.80f, 0.60f);
            cam.cullingMask = ~0;
            // 背景：暖色天空渐变卡片（放在城后面）
            var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            UnityEngine.Object.DestroyImmediate(bg.GetComponent<Collider>());
            var gt = new Texture2D(4, 256) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 256; y++) { float k = y / 255f; var c = Color.Lerp(new Color(0.96f, 0.78f, 0.52f), new Color(0.55f, 0.72f, 0.86f), Mathf.SmoothStep(0.15f, 1f, k)); for (int x = 0; x < 4; x++) gt.SetPixel(x, y, c); }
            gt.Apply();
            var bm = new Material(Shader.Find("Universal Render Pipeline/Unlit")); bm.SetTexture("_BaseMap", gt);
            bg.GetComponent<Renderer>().sharedMaterial = bm;
            bg.transform.position = center + f * 0.9f; bg.transform.rotation = Quaternion.LookRotation(f); bg.transform.localScale = new Vector3(3f, 3f, 1);
            // 藏掉房间、桌子（只留城）
            var hidden = new System.Collections.Generic.List<GameObject>();
            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r.transform.IsChildOf(mine) || r.gameObject == bg) continue;
                if (r.gameObject.activeInHierarchy) { r.gameObject.SetActive(false); hidden.Add(r.gameObject); }
            }
            const int S = 1024;
            var rt = new RenderTexture(S, S, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
            RenderTexture.active = rt;
            var tex = new Texture2D(S, S, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, S, S), 0, 0); tex.Apply();
            RenderTexture.active = null;
            foreach (var g in hidden) if (g) g.SetActive(true);
            UnityEngine.Object.DestroyImmediate(bg); UnityEngine.Object.DestroyImmediate(cam.gameObject); rt.Release();
            // 11-27 起正式图标是插画（icon_1024.png，别覆盖）；实拍版只另存一份备查
            var full = Path.Combine(Path.GetDirectoryName(Application.dataPath), Path.Combine(Path.GetDirectoryName(IconPath), "icon_ingame.png"));
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, tex.EncodeToPNG());
            return full;
        }

        [MenuItem("PaperSiege/打包 APK")]
        public static void BuildApkMenu() => Debug.Log(BuildApk());

        public static void BuildApkLater() => EditorApplication.delayCall += () =>
        {
            string msg; try { msg = BuildApk(); } catch (Exception e) { msg = "Failed 异常 " + e.Message; }
            File.AppendAllText(ResultFile, DateTime.Now.ToString("HH:mm:ss ") + msg + "\n");
        };

        public static string BuildApk()
        {
            if (EditorApplication.isPlaying) return "Failed 先退出运行模式";
            AssetDatabase.Refresh();
            ApplyIcon(); ApplySplash();
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android)) return "Failed 切换 Android 平台失败";
                return "Switched 已切到 Android，等编译完成后再执行一次打包";
            }
            var prep = Prepare();
            PlayerSettings.productName = ProductNameZh;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, AppId);
            PlayerSettings.bundleVersion = "0.2." + DateTime.Now.ToString("MMdd");
            PlayerSettings.Android.bundleVersionCode = int.Parse(DateTime.Now.ToString("MMddHH"));
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "../Builds"));
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, $"../Builds/Slinghold_{DateTime.Now:MMdd_HHmm}.apk"));
            var rep = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ProjectSetup.ScenePath }, locationPathName = path, target = BuildTarget.Android, targetGroup = BuildTargetGroup.Android, options = BuildOptions.None });
            var s = rep.summary;
            return $"{s.result} 用时 {s.totalTime.TotalMinutes:0.0} 分钟 APK {(File.Exists(path) ? new FileInfo(path).Length / 1048576f : 0):0.0} MB 错误 {s.totalErrors} → {path}\n准备：{prep}";
        }
    }
}

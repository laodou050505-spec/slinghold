// 一键搭工程：物理层 + 碰撞矩阵 + 物理参数 + 调参资源 + 积木库 + 主场景。
// 命令行：cs.py 'ProjectSetup.All(); result.Log("ok");'
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace PaperSiege.EditorTools
{
    public static class ProjectSetup
    {
        const string Root = "Assets/_PaperSiege";
        public const string ScenePath = Root + "/Scenes/Main.unity";

        [MenuItem("PaperSiege/Setup Project")]
        public static void All()
        {
            LayersAndPhysics();
            Tunings();
            BuildLibrary();
            BuildScene();
            AssetDatabase.SaveAssets();
        }

        public static void LayersAndPhysics()
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tm.FindProperty("layers");
            void Set(int i, string n) => layers.GetArrayElementAtIndex(i).stringValue = n;
            Set(Layers.Block, "PS_Block"); Set(Layers.Shot, "PS_Shot"); Set(Layers.Held, "PS_Held"); Set(Layers.Env, "PS_Env");
            tm.ApplyModifiedProperties();
            for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(Layers.Held, i, true);   // 手里的东西：谁也不碰
            Physics.IgnoreLayerCollision(Layers.Shot, Layers.Shot, true);
            Physics.IgnoreLayerCollision(Layers.Shot, Layers.Block, false);
            Physics.IgnoreLayerCollision(Layers.Shot, Layers.Env, false);
            Physics.IgnoreLayerCollision(Layers.Block, Layers.Block, false);
            Physics.IgnoreLayerCollision(Layers.Block, Layers.Env, false);
            Physics.IgnoreLayerCollision(Layers.Env, Layers.Env, true);
            for (int i = 0; i < 8; i++) { Physics.IgnoreLayerCollision(i, Layers.Block, true); Physics.IgnoreLayerCollision(i, Layers.Shot, true); }   // 默认层的占位外观不参与
            Physics.defaultSolverIterations = 12;
            Physics.defaultSolverVelocityIterations = 4;
            Physics.sleepThreshold = 0.0015f;
            Physics.defaultContactOffset = 0.002f;
            Physics.bounceThreshold = 0.5f;
            Physics.defaultMaxDepenetrationVelocity = 0.5f;
            Time.fixedDeltaTime = 0.01f;   // 100 Hz：小积木叠高更稳
        }

        static void Tunings()
        {
            const string dir = Root + "/Resources";
            Directory.CreateDirectory(dir);
            if (!AssetDatabase.LoadAssetAtPath<InputTuning>(dir + "/InputTuning.asset")) AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<InputTuning>(), dir + "/InputTuning.asset");
            if (!AssetDatabase.LoadAssetAtPath<GameTuning>(dir + "/GameTuning.asset")) AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<GameTuning>(), dir + "/GameTuning.asset");
        }

        /// <summary>积木库：积木 / 道具网格 + 漆色材质 → Resources/BlockLibrary.asset</summary>
        public static void BuildLibrary()
        {
            const string path = Root + "/Resources/BlockLibrary.asset";
            var lib = AssetDatabase.LoadAssetAtPath<BlockLibrary>(path);
            if (!lib) { lib = ScriptableObject.CreateInstance<BlockLibrary>(); AssetDatabase.CreateAsset(lib, path); }
            lib.meshes.Clear();
            var all = PSArt.Meshes();
            foreach (var kv in PSArt.PropMeshes()) all[kv.Key] = kv.Value;
            foreach (var id in all.Keys.Where(k => !k.EndsWith("_LOD1")).OrderBy(k => k))
                lib.meshes.Add(new PieceMeshes { id = id, lod0 = all[id], lod1 = all.TryGetValue(id + "_LOD1", out var l1) ? l1 : null });
            lib.paints = new Material[8];
            foreach (Paint p in System.Enum.GetValues(typeof(Paint)))
                lib.paints[(int)p] = PSArt.Mat(p == Paint.Stone ? "M_Stone" : "M_Wood_" + p);
            lib.propMaterial = PSArt.Mat("M_Props");
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            Debug.Log($"[PaperSiege] 积木库 {lib.meshes.Count} 件");
        }

        [MenuItem("PaperSiege/Build Main Scene")]
        public static void BuildScene()
        {
            Directory.CreateDirectory(Root + "/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.color = new Color(1f, 0.95f, 0.86f); sun.intensity = 1.2f; sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.7f;
            sun.transform.rotation = Quaternion.Euler(48, -30, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.80f, 0.82f, 0.88f);
            RenderSettings.ambientEquatorColor = new Color(0.72f, 0.70f, 0.68f);
            RenderSettings.ambientGroundColor = new Color(0.50f, 0.47f, 0.44f);
            // XR 装置（照《筑天》/ WarTable）
            var origin = new GameObject("XR Origin").transform;
            var camGO = new GameObject("XR Camera");
            camGO.transform.SetParent(origin, false);
            camGO.transform.localPosition = new Vector3(0, 1.45f, -0.05f);
            camGO.transform.localRotation = Quaternion.Euler(28, 0, 0);
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.nearClipPlane = 0.02f; cam.farClipPlane = 30f; cam.fieldOfView = 80f;
            camGO.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;   // 透视下后处理会破坏 alpha
            camGO.AddComponent<AudioListener>();
            camGO.tag = "MainCamera";
            var tpd = camGO.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            tpd.positionInput = new InputActionProperty(new InputAction("HeadPos", InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
            tpd.rotationInput = new InputActionProperty(new InputAction("HeadRot", InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
            var pxr = origin.gameObject.AddComponent<ByteDance.PICO.XR.PXR_Manager>();
            pxr.eyeTracking = true;
            var pc = ByteDance.PICO.XR.PXR_ProjectSetting.GetProjectConfig();
            if (pc != null) { pc.handTracking = true; pc.eyeTracking = true; pc.videoSeeThrough = true; EditorUtility.SetDirty(pc); }
            origin.gameObject.AddComponent<MRRig>().cam = cam;
            // 玩法
            var game = new GameObject("Game");
            var input = game.AddComponent<HandInput>(); input.cam = cam; input.origin = origin;
            game.AddComponent<SimHands>().cam = cam;
            var arena = game.AddComponent<Arena>(); arena.head = cam.transform;
            var dir = game.AddComponent<Director>(); dir.arena = arena; dir.head = cam;
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("[PaperSiege] 主场景已生成 " + ScenePath);
        }
    }
}

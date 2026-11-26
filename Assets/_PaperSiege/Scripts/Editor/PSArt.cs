// 美术工具：从 ArtSource 同步积木、建材质、搭观察场景（lookdev）、从指定相机截图。
// 命令行：~/.unity/papersiege-tools/cs.py 'PSArt.BuildMaterials(); PSArt.BuildLookdev(); PSArt.ShotAll("1124_积木初版");'
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PaperSiege.EditorTools
{
    public static class PSArt
    {
        public const string BlocksDir = "Assets/_PaperSiege/Art/Blocks";
        public const string MatDir = "Assets/_PaperSiege/Art/Materials";
        const float G = 0.05f;

        static string Proj => Path.GetDirectoryName(Application.dataPath);

        /// <summary>把 ArtSource/Models/blocks 下的 FBX 和贴图拷进工程</summary>
        public static void SyncBlocks()
        {
            string src = Path.Combine(Proj, "ArtSource/Models/blocks");
            string dst = Path.Combine(Proj, BlocksDir);
            Directory.CreateDirectory(Path.Combine(dst, "Textures"));
            foreach (var f in Directory.GetFiles(src, "*.fbx")) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (var f in Directory.GetFiles(Path.Combine(src, "tex"), "*.png")) File.Copy(f, Path.Combine(dst, "Textures", Path.GetFileName(f)), true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        }

        /// <summary>把 ArtSource/Models/props 下的道具 FBX 和贴图拷进工程，建 M_Props 材质</summary>
        public static void SyncProps()
        {
            string src = Path.Combine(Proj, "ArtSource/Models/props");
            string dst = Path.Combine(Proj, "Assets/_PaperSiege/Art/Props");
            Directory.CreateDirectory(Path.Combine(dst, "Textures"));
            File.Copy(Path.Combine(src, "props.fbx"), Path.Combine(dst, "props.fbx"), true);
            foreach (var f in Directory.GetFiles(Path.Combine(src, "tex"), "*.png")) File.Copy(f, Path.Combine(dst, "Textures", Path.GetFileName(f)), true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            string path = $"{MatDir}/M_Props.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("PaperSiege/Block")); AssetDatabase.CreateAsset(m, path); }
            Texture T(string n) => AssetDatabase.LoadAssetAtPath<Texture>($"Assets/_PaperSiege/Art/Props/Textures/{n}.png");
            m.SetTexture("_Albedo", T("props_albedo"));
            m.SetTexture("_Mask", T("props_mask"));
            m.SetTexture("_Normal", T("props_normal"));
            m.SetFloat("_PaintCover", 0f);            // 道具的颜色全部烘在 albedo 里
            m.SetFloat("_EmissionStrength", 1.2f);
            m.SetColor("_ShadowTint", new Color(0.72f, 0.66f, 0.76f));
            m.SetFloat("_Ambient", 1.1f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
        }

        public static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{name}.mat");

        /// <summary>封面道具（旗杆、木签横臂、大灯笼、木签）：ArtSource/Models/cover → Resources/Cover，建材质 M_Cover（同一个积木着色器，颜色全烘在 albedo 里）</summary>
        public static void SyncCover()
        {
            string src = Path.Combine(Proj, "ArtSource/Models/cover");
            string dst = Path.Combine(Proj, "Assets/_PaperSiege/Resources/Cover");
            Directory.CreateDirectory(dst);
            File.Copy(Path.Combine(src, "cover.fbx"), Path.Combine(dst, "cover.fbx"), true);
            foreach (var n in new[] { "cover_albedo", "cover_mask", "cover_normal" }) File.Copy(Path.Combine(src, "tex", n + ".png"), Path.Combine(dst, n + ".png"), true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            const string path = "Assets/_PaperSiege/Resources/Cover/M_Cover.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("PaperSiege/Block")); AssetDatabase.CreateAsset(m, path); }
            Texture T(string n) => AssetDatabase.LoadAssetAtPath<Texture>($"Assets/_PaperSiege/Resources/Cover/{n}.png");
            m.SetTexture("_Albedo", T("cover_albedo"));
            m.SetTexture("_Mask", T("cover_mask"));
            m.SetTexture("_Normal", T("cover_normal"));
            m.SetFloat("_PaintCover", 0f);
            m.SetFloat("_EmissionStrength", 1.4f);      // 纸灯笼的纸面（albedo.A）
            m.SetColor("_ShadowTint", new Color(0.72f, 0.66f, 0.76f));
            m.SetFloat("_Ambient", 1.1f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// 封面材质（资源放在 Resources/Cover，打包时着色器变体跟着材质保留）：
        ///   布旗（中 / 英）、三张纸页 = URP Lit 双面 + 透明度裁切 + 不反光；点线 / 叉号 = Unlit 透明（颜色在运行时用实例改）
        /// </summary>
        public static string BuildCoverMaterials()
        {
            const string dir = "Assets/_PaperSiege/Resources/Cover/";
            var log = new List<string>();
            Material Get(string name, string shader)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(dir + name + ".mat");
                if (!m) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, dir + name + ".mat"); }
                m.shader = Shader.Find(shader);
                return m;
            }
            void Cloth(string name, string tex, float smooth)
            {
                var m = Get(name, "Universal Render Pipeline/Lit");
                m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(dir + tex + ".png"));
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Cull", 0f);                 // 双面
                m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.5f); m.EnableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0f);
                m.SetFloat("_SpecularHighlights", 0f); m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                m.SetFloat("_EnvironmentReflections", 0f); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                m.doubleSidedGI = true; m.enableInstancing = true;
                EditorUtility.SetDirty(m); log.Add(name);
            }
            Cloth("M_Banner_zh", "banner_zh", 0.05f);
            Cloth("M_Banner_en", "banner_en", 0.05f);
            foreach (var n in new[] { "M_Banner_zh", "M_Banner_en" })   // 布旗不接影子：盒盖 / 横杆的影子会横着切过标题（URP 看材质开关，不看 renderer.receiveShadows）
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(dir + n + ".mat");
                m.SetFloat("_ReceiveShadows", 0f); m.EnableKeyword("_RECEIVE_SHADOWS_OFF"); EditorUtility.SetDirty(m);
            }
            for (int i = 1; i <= 3; i++) Cloth("M_Scrap_" + i, "scrap_" + i, 0.08f);
            void Mark(string name, string tex)
            {
                var m = Get(name, "Universal Render Pipeline/Unlit");
                m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(dir + tex + ".png"));
                m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetOverrideTag("RenderType", "Transparent");
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = 3000;
                m.SetColor("_BaseColor", new Color(0.66f, 0.29f, 0.23f, 0.9f));   // 砖红
                EditorUtility.SetDirty(m); log.Add(name);
            }
            Mark("M_Dots", "mark_dot");
            Mark("M_Cross", "mark_cross");
            var cord = Get("M_Cord", "Universal Render Pipeline/Unlit");
            cord.SetColor("_BaseColor", new Color(0.52f, 0.42f, 0.3f)); EditorUtility.SetDirty(cord); log.Add("M_Cord");
            AssetDatabase.SaveAssets();
            return string.Join(", ", log);
        }

        public static void BuildMaterials()
        {
            Directory.CreateDirectory(Path.Combine(Proj, MatDir));
            var sh = Shader.Find("PaperSiege/Block");
            Texture T(string n) => AssetDatabase.LoadAssetAtPath<Texture>($"{BlocksDir}/Textures/{n}.png");
            foreach (Paint p in System.Enum.GetValues(typeof(Paint)))
            {
                bool stone = p == Paint.Stone;
                string set = stone ? "stone" : "wood";
                string path = $"{MatDir}/M_{(stone ? "Stone" : "Wood_" + p)}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
                m.shader = sh;
                m.SetTexture("_Albedo", T(set + "_albedo"));
                m.SetTexture("_Mask", T(set + "_mask"));
                m.SetTexture("_Normal", T(set + "_normal"));
                m.SetColor("_PaintColor", Palette.Of(p));
                m.SetFloat("_PaintCover", p == Paint.Bare ? 0f : 1f);
                // 着色器默认值只对新建材质生效，已有材质要显式写
                m.SetColor("_ShadowTint", new Color(0.72f, 0.66f, 0.76f));
                m.SetFloat("_Ambient", 1.1f);
                m.SetFloat("_NormalScale", stone ? 0.8f : 1f);
                // 石块在暖光下会被照成米黄：本色往冷灰偏一点，保持“灰石头”的感觉
                m.SetColor("_BareTint", stone ? new Color(0.86f, 0.93f, 1.04f) : Color.white);
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
            }
            AssetDatabase.SaveAssets();
        }

        public static Dictionary<string, Mesh> Meshes()
        {
            var d = new Dictionary<string, Mesh>();
            foreach (var f in new[] { "blocks_wood", "blocks_stone" })
                foreach (var m in AssetDatabase.LoadAllAssetsAtPath($"{BlocksDir}/{f}.fbx").OfType<Mesh>())
                    d[m.name] = m;
            return d;
        }

        /// <summary>道具组的网格（ArtSource/Models/props → Art/Props/props.fbx），还没有时返回空</summary>
        public static Dictionary<string, Mesh> PropMeshes()
        {
            var d = new Dictionary<string, Mesh>();
            foreach (var m in AssetDatabase.LoadAllAssetsAtPath("Assets/_PaperSiege/Art/Props/props.fbx").OfType<Mesh>()) d[m.name] = m;
            return d;
        }

        /// <summary>示范城用远景低模（敌城在 3 米外）</summary>
        static bool useLod;

        static GameObject Piece(Dictionary<string, Mesh> ms, string name, Paint paint, Transform parent, Vector3 posG, float yaw = 0, float scale = 1)
        {
            if (useLod && ms.ContainsKey(name + "_LOD1")) { var go1 = Piece0(ms, name + "_LOD1", paint, parent, posG, yaw, scale); go1.name = name; return go1; }
            return Piece0(ms, name, paint, parent, posG, yaw, scale);
        }

        static GameObject Piece0(Dictionary<string, Mesh> ms, string name, Paint paint, Transform parent, Vector3 posG, float yaw = 0, float scale = 1)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = posG * G;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = Vector3.one * scale;
            go.AddComponent<MeshFilter>().sharedMesh = ms.TryGetValue(name, out var m) ? m : null;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Mat(paint == Paint.Stone ? "M_Stone" : "M_Wood_" + paint);
            r.shadowCastingMode = ShadowCastingMode.On;
            return go;
        }

        /// <summary>按概念图 A 拼一座小城（单位：积木格 G）。正面朝 -Z（对着玩家）。</summary>
        public static GameObject DemoCastle(Dictionary<string, Mesh> ms, Transform parent, string name, int seed)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            var rng = new System.Random(seed);
            Paint P() => Palette.WoodPaints[rng.Next(Palette.WoodPaints.Length)];
            // 前排（z = -1）：城门 + 门楼雉堞；两侧墙（方块 + 窗块）；两座角楼（方柱 + 窗块 + 尖顶）
            Piece(ms, "W_Gate", Paint.Cream, root, new Vector3(0, 0, -1));
            Piece(ms, "W_Battlement", Paint.Teal, root, new Vector3(0, 1.5f, -1));
            foreach (int s in new[] { -1, 1 })
            {
                Piece(ms, "W_Cube", s < 0 ? Paint.Teal : Paint.Ochre, root, new Vector3(1.5f * s, 0, -1));
                Piece(ms, "W_Window", s < 0 ? Paint.Ochre : Paint.Teal, root, new Vector3(1.5f * s, 1, -1));
                Piece(ms, "W_Pillar", s < 0 ? Paint.Cream : Paint.Brick, root, new Vector3(2.5f * s, 0, -1));
                Piece(ms, "W_Window", s < 0 ? Paint.Teal : Paint.Cream, root, new Vector3(2.5f * s, 2, -1));
                Piece(ms, "W_Cone", Paint.Brick, root, new Vector3(2.5f * s, 3, -1), 37 * s);
            }
            // 中间主楼（z = 0.3）：拱门 + 木板 + 方块 + 窗块 + 尖顶
            Piece(ms, "W_Arch", Paint.Blue, root, new Vector3(0, 0, 0.3f));
            Piece(ms, "W_Slab", Paint.Ochre, root, new Vector3(0, 1, 0.3f));
            Piece(ms, "W_Cube", Paint.Brick, root, new Vector3(0, 1.5f, 0.3f));
            Piece(ms, "W_Window", Paint.Cream, root, new Vector3(0, 2.5f, 0.3f));
            Piece(ms, "W_Cone", Paint.Brick, root, new Vector3(0, 3.5f, 0.3f), 12);
            // 后排（z = 1.6）：两根圆柱塔 + 中间带三角屋顶的拱厅
            foreach (int s in new[] { -1, 1 })
            {
                Piece(ms, "W_Column", s < 0 ? Paint.Sage : Paint.Teal, root, new Vector3(2.1f * s, 0, 1.6f));
                Piece(ms, "W_Cone", Paint.Brick, root, new Vector3(2.1f * s, 2, 1.6f), 70 * s);
            }
            Piece(ms, "W_Arch", Paint.Ochre, root, new Vector3(0, 0, 1.6f));
            Piece(ms, "W_Roof", Paint.Brick, root, new Vector3(0, 1, 1.6f));
            Piece(ms, "W_Beam", Paint.Cream, root, new Vector3(0, 0, -2.3f));
            return root.gameObject;
        }

        public static void BuildLookdev()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var ms = Meshes();
            // 灯光：暖色主光（带软阴影）+ 三色环境光
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.94f, 0.84f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            sun.transform.rotation = Quaternion.Euler(42, -35, 0);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.80f, 0.82f, 0.88f);
            RenderSettings.ambientEquatorColor = new Color(0.72f, 0.70f, 0.68f);
            RenderSettings.ambientGroundColor = new Color(0.50f, 0.47f, 0.44f);
            DynamicGI.UpdateEnvironment();   // 改了环境光要刷新球谐，否则着色器里 SampleSH 拿到的是旧的（暗部会发黑）
            // 占位房间（编辑器里代替透视画面）：地板、两面墙、一张桌子
            var room = new GameObject("RoomStandIn").transform;
            Prim(PrimitiveType.Cube, room, "Floor", new Vector3(0, -0.05f, 1.5f), new Vector3(6, 0.1f, 7), new Color(0.55f, 0.43f, 0.32f));
            Prim(PrimitiveType.Cube, room, "WallBack", new Vector3(0, 1.4f, 5f), new Vector3(6, 2.8f, 0.1f), new Color(0.86f, 0.84f, 0.80f));
            Prim(PrimitiveType.Cube, room, "WallLeft", new Vector3(-3f, 1.4f, 1.5f), new Vector3(0.1f, 2.8f, 7), new Color(0.83f, 0.81f, 0.77f));
            Prim(PrimitiveType.Cube, room, "TableTop", new Vector3(0, 0.82f, 0.55f), new Vector3(0.9f, 0.04f, 0.6f), new Color(0.50f, 0.34f, 0.21f));
            // 零件陈列（地上，左侧；每排 6 件，间隔 3 格）
            var lineup = new GameObject("Lineup").transform;
            lineup.position = new Vector3(-1.6f, 0, 1.2f);
            var names = new[] { "W_Cube", "W_Slab", "W_Beam", "W_Pillar", "W_Column", "W_Arch", "W_Window", "W_Battlement", "W_Gate", "W_Roof", "W_Cone",
                                "S_Cube", "S_Slab", "S_Pillar", "S_Arch", "S_Battlement", "S_Wall", "S_Shield" };
            for (int i = 0; i < names.Length; i++)
            {
                var paint = names[i].StartsWith("S_") ? Paint.Stone : Palette.WoodPaints[i % Palette.WoodPaints.Length];
                Piece(ms, names[i], paint, lineup, new Vector3((i % 6) * 3.4f - 8.5f, 0, (i / 6) * 3.6f));
            }
            // 玩家的城（桌上）和敌城（3 米外地上，放大 1.6 倍）
            var mine = DemoCastle(ms, null, "MyCastle", 1);
            mine.transform.position = new Vector3(0, 0.84f, 0.45f);
            useLod = true;
            var enemy = DemoCastle(ms, null, "EnemyCastle", 2);
            enemy.transform.position = new Vector3(0.3f, 0, 3.0f);
            enemy.transform.rotation = Quaternion.Euler(0, 8, 0);
            enemy.transform.localScale = Vector3.one * 1.8f;
            // 敌城前的石墙
            var sw = new GameObject("StoneWall").transform;
            sw.SetParent(enemy.transform, false);
            for (int k = -1; k <= 1; k++) Piece(ms, "S_Wall", Paint.Stone, sw, new Vector3(k * 2, 0, -3.2f));
            Piece(ms, "S_Battlement", Paint.Stone, sw, new Vector3(-2, 1, -3.2f));
            Piece(ms, "S_Battlement", Paint.Stone, sw, new Vector3(2, 1, -3.2f));
            Piece(ms, "S_Shield", Paint.Stone, sw, new Vector3(0, 1, -3.4f));
            useLod = false;
            // 相机
            Cam("Cam_Player", new Vector3(-0.05f, 1.36f, -0.12f), new Vector3(0.25f, 0.72f, 2.4f), 75);
            Cam("Cam_Lineup", new Vector3(-1.6f, 0.62f, 0.55f), new Vector3(-1.6f, 0.02f, 1.38f), 52);
            Cam("Cam_Close", new Vector3(0.16f, 1.02f, 0.18f), new Vector3(0, 0.9f, 0.45f), 40);
            Cam("Cam_Enemy", new Vector3(0.0f, 1.4f, 0.3f), new Vector3(0.3f, 0.4f, 3.0f), 34);
            Directory.CreateDirectory(Path.Combine(Proj, "Assets/_PaperSiege/Scenes"));
            EditorSceneManager.SaveScene(scene, "Assets/_PaperSiege/Scenes/Lookdev.unity");
        }

        static void Prim(PrimitiveType t, Transform parent, string name, Vector3 pos, Vector3 size, Color c)
        {
            var go = GameObject.CreatePrimitive(t);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = size;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = c;
            mat.SetFloat("_Smoothness", 0.15f);
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        static Camera Cam(string name, Vector3 pos, Vector3 look, float fov)
        {
            var c = new GameObject(name).AddComponent<Camera>();
            c.transform.position = pos;
            c.transform.LookAt(look);
            c.fieldOfView = fov;
            c.nearClipPlane = 0.02f;
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = new Color(0.82f, 0.80f, 0.76f);
            c.enabled = false;
            var d = c.GetUniversalAdditionalCameraData();
            d.renderShadows = true;
            d.antialiasing = AntialiasingMode.None;
            return c;
        }

        /// <summary>从场景里名叫 camName 的相机截图到 Docs/进度截图/&lt;dir&gt;/&lt;camName&gt;.png</summary>
        public static string Shot(string camName, string dir, int w = 1600, int h = 900)
        {
            var cam = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(c => c.name == camName);
            if (!cam) return "没有相机 " + camName;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = prev;
            string outDir = Path.Combine(Proj, "Docs/进度截图", dir);
            Directory.CreateDirectory(outDir);
            string p = Path.Combine(outDir, camName + ".png");
            File.WriteAllBytes(p, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
            return p;
        }

        /// <summary>
        /// 逐件检查图：每个零件单独取景，正面 3/4 和背面 3/4 各一张（真实材质），再各一张“背面染红”检查。
        /// 每行放两个零件（8 列），输出 Docs/进度截图/&lt;dir&gt;/sheet_&lt;wood|stone&gt;.png
        /// </summary>
        public static string PieceSheet(string dir, int tile = 300)
        {
            var ms = Meshes();
            var check = new Material(Shader.Find("PaperSiege/BackfaceCheck"));
            var camGo = new GameObject("SheetCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.80f, 0.78f, 0.74f);
            cam.fieldOfView = 28; cam.nearClipPlane = 0.005f; cam.enabled = false;
            var outs = new List<string>();
            foreach (var set in new[] { "W_", "S_" })
            {
                var names = ms.Keys.Where(k => k.StartsWith(set) && !k.EndsWith("_LOD1")).OrderBy(k => k).ToList();
                int cols = 8, rows = (names.Count + 1) / 2;
                var sheet = new Texture2D(cols * tile, rows * tile, TextureFormat.RGB24, false);
                var rt = new RenderTexture(tile, tile, 24) { antiAliasing = 4 };
                for (int i = 0; i < names.Count; i++)
                {
                    var paint = set == "S_" ? Paint.Stone : Palette.WoodPaints[i % Palette.WoodPaints.Length];
                    var go = Piece(ms, names[i], paint, null, Vector3.zero);
                    go.transform.position = new Vector3(100, 0, 0);
                    var r = go.GetComponent<MeshRenderer>();
                    var b = r.bounds;
                    float dist = b.extents.magnitude / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
                    var dirs = new[] { new Vector3(-0.7f, 0.55f, -1f), new Vector3(0.7f, 0.55f, 1f) };
                    for (int v = 0; v < 4; v++)
                    {
                        var d = dirs[v % 2].normalized;
                        cam.transform.position = b.center + d * dist;
                        cam.transform.LookAt(b.center);
                        var keep = r.sharedMaterial;
                        if (v >= 2) r.sharedMaterial = check;
                        cam.targetTexture = rt;
                        cam.Render();
                        r.sharedMaterial = keep;
                        RenderTexture.active = rt;
                        int col = (i % 2) * 4 + v, row = rows - 1 - i / 2;
                        sheet.ReadPixels(new Rect(0, 0, tile, tile), col * tile, row * tile);
                        RenderTexture.active = null;
                    }
                    Object.DestroyImmediate(go);
                }
                sheet.Apply();
                string outDir = Path.Combine(Proj, "Docs/进度截图", dir);
                Directory.CreateDirectory(outDir);
                string p = Path.Combine(outDir, $"sheet_{(set == "W_" ? "wood" : "stone")}.png");
                File.WriteAllBytes(p, sheet.EncodeToPNG());
                outs.Add(p);
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(sheet);
            }
            Object.DestroyImmediate(camGo);
            return string.Join("\n", outs);
        }

        public static string ShotAll(string dir)
        {
            var outs = new List<string>();
            foreach (var n in new[] { "Cam_Player", "Cam_Lineup", "Cam_Close", "Cam_Enemy" }) outs.Add(Shot(n, dir));
            return string.Join("\n", outs);
        }
    }
}

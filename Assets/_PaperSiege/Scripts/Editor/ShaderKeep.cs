// 打包保底：运行时用 Shader.Find 新建的材质（弹、拖尾、特效、示范手、标题页绳子……）在安卓包里可能找不到着色器，
// 或者找到了但对应的关键字变体（透明、自发光）被剥离 → 真机上是粉色或看不见。
// 做法：在 Resources/ShaderKeep/ 放一组“保底材质”，覆盖运行时用到的每一种 着色器 + 关键字 组合；
// Resources 里的东西一定会打进包，材质用到的变体也就保留下来了。
// 新增一种运行时材质写法时，在 Specs 里补一行，再执行：cs.py 'ShaderKeep.Build();'
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PaperSiege.EditorTools
{
    public static class ShaderKeep
    {
        const string Dir = "Assets/_PaperSiege/Resources/ShaderKeep";

        enum Kind { Opaque, Transparent, Emission }

        static readonly (string file, string shader, Kind kind)[] Specs =
        {
            ("Lit_Opaque",              "Universal Render Pipeline/Lit", Kind.Opaque),              // 木托盘、弹盒、投石机占位、结算板、弹……
            ("Lit_Emission",            "Universal Render Pipeline/Lit", Kind.Emission),            // 火丸
            ("Lit_Transparent",         "Universal Render Pipeline/Lit", Kind.Transparent),         // 城墙 TRIPO 图标
            ("Unlit_Opaque",            "Universal Render Pipeline/Unlit", Kind.Opaque),            // 吊绳、弹弓皮筋、提示木牌绳
            ("Unlit_Transparent",       "Universal Render Pipeline/Unlit", Kind.Transparent),       // 示范手、弹道点、标题页字面、模拟手
            ("ParticlesUnlit_Transparent", "Universal Render Pipeline/Particles/Unlit", Kind.Transparent), // 烟、火星、拖尾
            ("ParticlesSimpleLit_Opaque",  "Universal Render Pipeline/Particles/Simple Lit", Kind.Opaque), // 碎木块粒子
            ("Sky",                     "PaperSiege/Sky", Kind.Opaque),                              // 盒盖开口里的天空
        };

        public static string Build()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
            {
                if (!AssetDatabase.IsValidFolder("Assets/_PaperSiege/Resources")) AssetDatabase.CreateFolder("Assets/_PaperSiege", "Resources");
                AssetDatabase.CreateFolder("Assets/_PaperSiege/Resources", "ShaderKeep");
            }
            var log = new List<string>();
            foreach (var (file, shaderName, kind) in Specs)
            {
                var sh = Shader.Find(shaderName);
                if (!sh) { log.Add($"缺着色器 {shaderName}"); continue; }
                string path = $"{Dir}/{file}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
                m.shader = sh;
                // 和运行时代码里的写法保持一致（同样的属性 → 同样的关键字）
                if (kind == Kind.Transparent)
                {
                    if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1);
                    if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0);
                    m.SetOverrideTag("RenderType", "Transparent");
                    if (m.HasProperty("_SrcBlend")) m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                    if (m.HasProperty("_DstBlend")) m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);
                    m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    m.renderQueue = 3000;
                }
                if (kind == Kind.Emission)
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", new Color(1f, 0.45f, 0.1f));
                }
                EditorUtility.SetDirty(m);
                log.Add($"{file} ← {shaderName} [{string.Join(" ", m.shaderKeywords)}]");
            }
            CleanAlwaysIncluded(log);
            AssetDatabase.SaveAssets();
            return string.Join("\n", log);
        }

        /// <summary>“总是包含的着色器”里去掉空引用（从别的工程复制骨架时带过来的坏 GUID）</summary>
        static void CleanAlwaysIncluded(List<string> log)
        {
            var gs = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
            var so = new SerializedObject(gs);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            int removed = 0;
            for (int i = arr.arraySize - 1; i >= 0; i--)
            {
                var el = arr.GetArrayElementAtIndex(i);
                if (el.objectReferenceValue) continue;
                el.objectReferenceValue = null;
                arr.DeleteArrayElementAtIndex(i);
                removed++;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(gs);
            AssetDatabase.SaveAssetIfDirty(gs);   // 项目设置不标脏不会写盘
            log.Add($"总是包含列表：去掉空引用 {removed} 个，剩 {arr.arraySize} 个");
        }

        /// <summary>检查运行时代码里出现的 Shader.Find 名字是不是都有保底材质（打包前跑一遍）</summary>
        public static string Check()
        {
            var have = new HashSet<string>();
            foreach (var s in Specs) have.Add(s.shader);
            var miss = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets/_PaperSiege/Scripts/Runtime" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                var txt = System.IO.File.ReadAllText(p);
                foreach (System.Text.RegularExpressions.Match mt in System.Text.RegularExpressions.Regex.Matches(txt, "Shader\\.Find\\(\"([^\"]+)\"\\)"))
                    if (!have.Contains(mt.Groups[1].Value)) miss.Add($"{System.IO.Path.GetFileName(p)}: {mt.Groups[1].Value}");
            }
            return miss.Count == 0 ? "运行时 Shader.Find 全部有保底材质" : "缺保底：\n" + string.Join("\n", miss);
        }
    }
}

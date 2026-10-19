using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

namespace PushHands.Editor
{
    public static class GreyboxBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/PushHandsGreybox.unity";
        private const string ConfigPath = "Assets/_Project/Configs/GreyboxConfig.asset";
        private static Material blue, orange, skin, dark, ground, line;
        private static TMP_FontAsset uiFont;

        [MenuItem("推手/打开灰盒场景")]
        public static void OpenForHandoff()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
            Selection.activeGameObject = UnityEngine.Object.FindObjectOfType<PrototypeController>().gameObject;
        }

        public static void ImportTextResources()
        {
            if (Resources.Load<TMP_Settings>("TMP Settings") != null)
            { ReimportTextShaders(); EditorJobs.Complete("resources", "TMP 基础资源已存在，已刷新着色器路径"); return; }
            // 当前 TMP 3.0.7 没有新版 ImportResources API，使用该版本自身导入器所用的非交互接口。
            AssetDatabase.importPackageCompleted += TextResourcesImported;
            string package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly).resolvedPath;
            AssetDatabase.ImportPackage(package + "/Package Resources/TMP Essential Resources.unitypackage", false);
        }

        private static void TextResourcesImported(string package)
        {
            if (package != "TMP Essential Resources") return;
            AssetDatabase.importPackageCompleted -= TextResourcesImported;
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Art")) AssetDatabase.CreateFolder("Assets/_Project", "Art");
            string error = AssetDatabase.MoveAsset("Assets/TextMesh Pro", "Assets/_Project/Art/TextMeshPro");
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
            AssetDatabase.SaveAssets(); EditorJobs.Complete("resources", "TMP 基础资源已导入自有资源目录");
            ReimportTextShaders();
        }

        private static void ReimportTextShaders()
        {
            const string folder = "Assets/_Project/Art/TextMeshPro/Shaders";
            if (AssetDatabase.IsValidFolder(folder))
                foreach (string guid in AssetDatabase.FindAssets("t:Shader", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string source = File.ReadAllText(path);
                    string relocated = source;
                    foreach (string include in new[] { "TMPro_Properties.cginc", "TMPro.cginc", "TMPro_Mobile.cginc", "TMPro_Surface.cginc" })
                        relocated = relocated.Replace("\"" + include + "\"", "\"" + folder + "/" + include + "\"");
                    if (relocated != source) File.WriteAllText(path, relocated);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
        }

        [MenuItem("推手/生成或重建灰盒（覆盖原型场景）")]
        public static void Generate()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("请先退出运行模式。");
            if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("当前场景有未保存修改，请先保存后重新生成。");
            foreach (string folder in new[] { "Scenes", "Configs", "Prefabs", "Art" })
                if (!AssetDatabase.IsValidFolder("Assets/_Project/" + folder)) AssetDatabase.CreateFolder("Assets/_Project", folder);
            var config = AssetDatabase.LoadAssetAtPath<PrototypeConfig>(ConfigPath);
            if (config == null) { config = ScriptableObject.CreateInstance<PrototypeConfig>(); AssetDatabase.CreateAsset(config, ConfigPath); }
            uiFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Art/Fonts/PushHandsUI.asset");
            if (uiFont == null)
            {
                var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/_Project/Art/Fonts/PushHandsUI.ttf");
                if (source == null) throw new InvalidOperationException("缺少界面字体，请先运行 Tools/PrepareUIFont.py。");
                uiFont = TMP_FontAsset.CreateFontAsset(source, 48, 5, GlyphRenderMode.SDFAA, 2048, 2048);
                uiFont.name = "PushHandsUI";
                AssetDatabase.CreateAsset(uiFont, "Assets/_Project/Art/Fonts/PushHandsUI.asset");
                AssetDatabase.AddObjectToAsset(uiFont.material, uiFont);
                foreach (var texture in uiFont.atlasTextures) AssetDatabase.AddObjectToAsset(texture, uiFont);
            }
            uiFont.ClearFontAssetData();
            uiFont.TryAddCharacters(File.ReadAllText("Assets/_Project/Art/Fonts/characters.txt"));
            EditorUtility.SetDirty(uiFont);
            blue = Material("Blue", new Color(0.15f, 0.42f, 0.65f));
            orange = Material("Orange", new Color(0.85f, 0.37f, 0.15f));
            skin = Material("Hands", new Color(0.75f, 0.60f, 0.45f));
            dark = Material("Dark", new Color(0.12f, 0.16f, 0.19f));
            ground = Material("Floor", new Color(0.23f, 0.29f, 0.30f));
            line = Material("Marking", new Color(0.62f, 0.76f, 0.69f));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var floor = Part("TrainingFloor", null, PrimitiveType.Cube, Vector3.zero, new Vector3(8, 0.1f, 8), ground);
            floor.localPosition = new Vector3(0, -0.06f, 0);
            for (int i = -4; i <= 4; i++)
            {
                Part("FloorLineX", null, PrimitiveType.Cube, new Vector3(i, 0, 0), new Vector3(0.012f, 0.005f, 8), line);
                Part("FloorLineZ", null, PrimitiveType.Cube, new Vector3(0, 0, i), new Vector3(8, 0.005f, 0.012f), line);
            }
            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.15f; light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(40, -30, 0);
            RenderSettings.ambientLight = new Color(0.48f, 0.52f, 0.57f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
            RenderSettings.reflectionIntensity = 0;
            RenderSettings.fog = true; RenderSettings.fogColor = new Color(0.19f, 0.25f, 0.29f);
            RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogStartDistance = 6; RenderSettings.fogEndDistance = 18;
            var controller = new GameObject("PushHandsPrototype").AddComponent<PrototypeController>();
            controller.Config = config;
            controller.Views = new FighterView[2]; controller.Hud = new TMP_Text[2];
            for (int i = 0; i < 2; i++)
            {
                controller.Views[i] = CreateFighter(i, config);
                controller.Hud[i] = CreateHud(i);
                PrefabUtility.SaveAsPrefabAsset(controller.Views[i].gameObject, $"Assets/_Project/Prefabs/Fighter{i + 1}.prefab");
            }
            Time.fixedDeltaTime = 0.02f;
            PlayerSettings.productName = "推手 · 核心灰盒";
            PlayerSettings.companyName = "PushHandsPrototype";
            PlayerSettings.defaultScreenWidth = 1600; PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.runInBackground = true;
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("灰盒场景生成完成：" + ScenePath);
        }

        private static FighterView CreateFighter(int index, PrototypeConfig config)
        {
            var root = new GameObject("Fighter" + (index + 1));
            root.transform.position = new Vector3(0, 0, index == 0 ? -config.PlayerSpacing * 0.5f : config.PlayerSpacing * 0.5f);
            root.transform.rotation = Quaternion.Euler(0, index == 0 ? 0 : 180, 0);
            var view = root.AddComponent<FighterView>();
            Material shirt = index == 0 ? blue : orange;
            view.Torso = Part("Torso", root.transform, PrimitiveType.Cube, new Vector3(0, 1.26f, 0), new Vector3(0.42f, 0.58f, 0.30f), shirt);
            view.Head = Part("Head", root.transform, PrimitiveType.Sphere, new Vector3(0, 1.67f, 0), new Vector3(0.25f, 0.29f, 0.25f), skin);
            view.Head.gameObject.layer = 8 + index;
            view.Feet = new Transform[2]; view.UpperLegs = new Transform[2]; view.LowerLegs = new Transform[2];
            view.UpperArms = new Transform[2]; view.LowerArms = new Transform[2]; view.Palms = new Transform[2];
            for (int side = 0; side < 2; side++)
            {
                string label = side == 0 ? "Left" : "Right";
                view.Feet[side] = Part(label + "Foot", root.transform, PrimitiveType.Cube, new Vector3(side == 0 ? -0.14f : 0.14f, 0.06f, side == 0 ? 0.06f : -0.06f), new Vector3(0.13f, 0.10f, 0.24f), dark);
                view.UpperLegs[side] = Part(label + "Thigh", root.transform, PrimitiveType.Capsule, new Vector3(0, 0.7f, 0), new Vector3(0.20f, 0.2f, 0.20f), dark);
                view.LowerLegs[side] = Part(label + "Shin", root.transform, PrimitiveType.Capsule, new Vector3(0, 0.3f, 0), new Vector3(0.16f, 0.2f, 0.16f), dark);
                view.UpperArms[side] = Part(label + "UpperArm", root.transform, PrimitiveType.Capsule, new Vector3(0, 1.2f, 0), Vector3.one * 0.1f, shirt);
                view.LowerArms[side] = Part(label + "Forearm", root.transform, PrimitiveType.Capsule, new Vector3(0, 1.1f, 0), Vector3.one * 0.1f, skin);
                view.Palms[side] = Part(label + "Palm", root.transform, PrimitiveType.Cube, new Vector3(side == 0 ? -0.22f : 0.22f, 1.1f, 0.24f), new Vector3(0.105f, 0.14f, 0.055f), skin);
            }
            var cameraObject = new GameObject("FirstPersonCamera"); cameraObject.transform.SetParent(root.transform, false);
            cameraObject.transform.localPosition = new Vector3(0, 1.61f, 0.08f);
            view.ViewCamera = cameraObject.AddComponent<Camera>();
            view.ViewCamera.rect = new Rect(index * 0.5f, 0, 0.5f, 1);
            view.ViewCamera.nearClipPlane = 0.03f; view.ViewCamera.farClipPlane = 50;
            view.ViewCamera.fieldOfView = 78; view.ViewCamera.cullingMask &= ~(1 << (8 + index));
            view.ViewCamera.clearFlags = CameraClearFlags.SolidColor; view.ViewCamera.backgroundColor = RenderSettings.fogColor;
            if (index == 0) cameraObject.AddComponent<AudioListener>();
            // 站位标线在运行包中可见；重心趋势额外由 Gizmos 和 HUD 展示。
            Part("SupportArea", root.transform, PrimitiveType.Cube, new Vector3(0, 0.002f, 0),
                new Vector3(config.SupportHalfSize.x * 2, 0.005f, config.SupportHalfSize.y * 2), line);
            return view;
        }

        private static TMP_Text CreateHud(int index)
        {
            var canvasObject = new GameObject("Player" + (index + 1) + "Hud", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = 0.5f;
            var panel = new GameObject("StatusPanel", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            panel.transform.SetParent(canvasObject.transform, false);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(index * 0.5f + 0.012f, 0.53f); rect.anchorMax = new Vector2(index * 0.5f + 0.488f, 0.98f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var background = panel.GetComponent<UnityEngine.UI.Image>(); background.color = new Color(0.035f, 0.055f, 0.07f, 0.82f); background.raycastTarget = false;
            var textObject = new GameObject("Status", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(panel.transform, false);
            var textRect = textObject.GetComponent<RectTransform>(); textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(15, 10); textRect.offsetMax = new Vector2(-15, -10);
            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.font = uiFont;
            text.text = "推手灰盒 · 等待加入"; text.fontSize = 21; text.color = new Color(0.87f, 0.94f, 0.94f);
            text.raycastTarget = false; text.enableWordWrapping = true; text.overflowMode = TextOverflowModes.Truncate;
            return text;
        }

        private static Material Material(string name, Color color)
        {
            string path = "Assets/_Project/Art/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, path); }
            material.color = color; material.SetFloat("_Glossiness", 0.2f); EditorUtility.SetDirty(material); return material;
        }
        private static Transform Part(string name, Transform parent, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var obj = GameObject.CreatePrimitive(type); obj.name = name; obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position; obj.transform.localScale = scale;
            obj.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>());
            return obj.transform;
        }

        [MenuItem("推手/构建 Windows 灰盒")]
        public static void BuildWindows()
        {
            if (!File.Exists(ScenePath)) throw new InvalidOperationException("请先生成灰盒场景。");
            Directory.CreateDirectory("Builds/Windows");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, locationPathName = "Builds/Windows/PushHandsGreybox.exe",
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development | BuildOptions.CleanBuildCache
            });
            File.WriteAllText("Artifacts/Verification/build-report.txt", $"结果={report.summary.result}\n错误={report.summary.totalErrors}\n警告={report.summary.totalWarnings}\n大小={report.summary.totalSize}");
            foreach (var step in report.steps)
                foreach (var message in step.messages)
                    if (message.type == LogType.Error || message.type == LogType.Warning)
                        File.AppendAllText("Artifacts/Verification/build-report.txt", "\n" + message.type + ": " + message.content);
            if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors > 0)
                throw new InvalidOperationException("Windows 构建未达到零错误，请查看构建报告。");
            Directory.CreateDirectory("Builds/Windows/ThirdPartyNotices");
            File.Copy("Assets/_Project/Art/Fonts/OFL.txt", "Builds/Windows/ThirdPartyNotices/PushHandsUI-OFL.txt", true);
            File.Copy("Assets/_Project/Art/TextMeshPro/Fonts/LiberationSans - OFL.txt", "Builds/Windows/ThirdPartyNotices/LiberationSans-OFL.txt", true);
        }
    }
}

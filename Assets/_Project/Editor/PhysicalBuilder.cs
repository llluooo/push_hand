using System;
using System.IO;
using PushHands.Physical;
using TMPro;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

namespace PushHands.Editor
{
    public static class PhysicalBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/PushHandsPhysical.unity";
        [MenuItem("推手/物理人偶/更新独立低台与重心参数")]
        public static void ConfigureArena()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("请先退出运行模式。");
            foreach (var loaded in LoadedScenes()) if (loaded.isDirty)
                throw new InvalidOperationException("当前场景有未保存的修改，未自动覆盖。");
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var match = UnityEngine.Object.FindObjectOfType<PhysicalMatch>();
            if (match == null || match.Fighters == null || match.Fighters.Length != 2)
                throw new InvalidOperationException("物理场景缺少双人人偶和对局。");
            var config = match.Config;
            // 试玩后收窄旧默认台面；保留另行配置的地图尺寸。
            if ((Mathf.Approximately(config.PlatformSize.x, 0.52f) && Mathf.Approximately(config.PlatformSize.y, 0.46f))
                || (Mathf.Approximately(config.PlatformSize.x, 0.46f) && Mathf.Approximately(config.PlatformSize.y, 0.42f)))
                config.PlatformSize = new Vector2(0.46f, 0.38f);
            // 迁移旧版默认俯角，用户已自行调整的值保持不变。
            if (Mathf.Approximately(config.ViewDownDegrees, 8) || Mathf.Approximately(config.ViewDownDegrees, 16)) config.ViewDownDegrees = 20;
            var ground = GameObject.Find("PhysicalTrainingFloor");
            if (ground == null) throw new InvalidOperationException("缺少原训练地面，未创建重复场地。");
            ground.transform.position = new Vector3(0, -config.PlatformHeight - 0.06f, 0);
            var arena = match.GetComponent<PhysicalArena>();
            if (arena == null) arena = match.gameObject.AddComponent<PhysicalArena>();
            arena.LandingGround = ground.GetComponent<Collider>();
            if (arena.Platforms == null || arena.Platforms.Length != 2) arena.Platforms = new BoxCollider[2];
            arena.SpawnOffsets = new Vector3[2];
            var platformMaterial = Material("Platform", new Color(0.35f, 0.40f, 0.46f));
            for (int i = 0; i < 2; i++)
            {
                if (arena.Platforms[i] == null)
                {
                    var obj = GameObject.CreatePrimitive(PrimitiveType.Cube); obj.name = "Player" + (i + 1) + "Platform";
                    arena.Platforms[i] = obj.GetComponent<BoxCollider>();
                }
                var platform = arena.Platforms[i];
                platform.transform.rotation = Quaternion.Euler(0, i * 180, 0);
                platform.transform.position = new Vector3(0, -config.PlatformHeight * 0.5f - 0.01f, (i == 0 ? -1 : 1) * config.Spacing * 0.5f)
                    + platform.transform.forward * config.PlatformForwardOffset;
                arena.SpawnOffsets[i] = Vector3.back * config.PlatformForwardOffset;
                platform.transform.localScale = new Vector3(config.PlatformSize.x, config.PlatformHeight, config.PlatformSize.y);
                platform.sharedMaterial = arena.LandingGround.sharedMaterial;
                platform.GetComponent<Renderer>().sharedMaterial = platformMaterial;
            }
            Physics.SyncTransforms(); match.Arena = arena;
            for (int i = 0; i < 2; i++)
            {
                match.Fighters[i].Arena = arena; match.Fighters[i].RelocateSpawn(arena.SpawnPosition(i));
                match.Fighters[i].ViewCamera.fieldOfView = config.ViewFieldOfView;
            }
            EditorUtility.SetDirty(config); EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            CompleteArenaInspection(match);
        }
        private static void CompleteArenaInspection(PhysicalMatch match)
        {
            if (match.Arena == null || match.Arena.Platforms.Length != 2) throw new InvalidOperationException("场地引用未保存。");
            foreach (var platform in match.Arena.Platforms)
                if (platform == null || match.Arena.LandingGround == null || platform.bounds.max.y <= match.Arena.LandingGround.bounds.max.y)
                    throw new InvalidOperationException("站台或下方地面的配置无效。");
            foreach (var fighter in match.Fighters)
            {
                if (fighter.Arena != match.Arena || fighter.Config != match.Config) throw new InvalidOperationException("人偶场地或参数引用不一致。");
                foreach (var bone in fighter.Bones)
                    if (bone.Body == null || bone.Joint == null) throw new InvalidOperationException("人偶存在缺失引用。");
            }
            Directory.CreateDirectory("Artifacts/Verification");
            File.WriteAllText("Artifacts/Verification/arena-setup.txt", "独立站台=2\n台面尺寸=" + match.Config.PlatformSize
                + "\n台面前移=" + match.Config.PlatformForwardOffset + "\n出生点1=" + match.Arena.SpawnPosition(0) + "\n出生点2=" + match.Arena.SpawnPosition(1)
                + "\n台高=" + match.Config.PlatformHeight + "\n落地面=" + match.Arena.LandingGround.bounds.max.y
                + "\n视角=" + match.Config.ViewFieldOfView + "\n俯角=" + match.Config.ViewDownDegrees + "\n资源引用检查=通过");
        }
        [MenuItem("推手/物理人偶/更新推动反馈资源")]
        public static void ConfigureFeedback()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("请先退出运行模式。");
            var config = AssetDatabase.LoadAssetAtPath<PhysicalConfig>("Assets/_Project/Configs/PhysicalConfig.asset");
            if (config == null) throw new InvalidOperationException("物理参数资源不存在。");
            // 仅保存新增字段的默认值，已有力量和平衡参数保持原值。
            EditorUtility.SetDirty(config);
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Art/PhysicalFonts/PushHandsUI.asset");
            if (font == null) throw new InvalidOperationException("物理原型字体资源不存在。");
            // 字体子集更新会改变 glyph 索引，按原资源引用重建图集与缺字缓存。
            font.ClearFontAssetData();
            font.TryAddCharacters(File.ReadAllText("Assets/_Project/Art/PhysicalFonts/characters.txt"));
            if (!font.HasCharacters("保护步展示中"))
                throw new InvalidOperationException("请重新生成物理字体子集，结束展示提示存在缺字。");
            EditorUtility.SetDirty(font); AssetDatabase.SaveAssets();
        }
        [MenuItem("推手/物理人偶/生成独立物理原型")]
        public static void Generate()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("请先退出运行模式。");
            foreach (var scene in LoadedScenes()) if (scene.isDirty) throw new InvalidOperationException("请先保存所有已修改场景，再生成独立物理原型。");
            foreach (string folder in new[] { "Scenes", "Configs", "Art", "Art/Physical", "Prefabs", "Prefabs/Physical" }) EnsureFolder("Assets/_Project/" + folder);
            var config = AssetDatabase.LoadAssetAtPath<PhysicalConfig>("Assets/_Project/Configs/PhysicalConfig.asset");
            if (config == null) { config = ScriptableObject.CreateInstance<PhysicalConfig>(); AssetDatabase.CreateAsset(config, "Assets/_Project/Configs/PhysicalConfig.asset"); }
            // 只迁移上一版默认驱动值；保留用户主动调整过的参数。
            if (Mathf.Approximately(config.ArmSpring, 180)) config.ArmSpring = 650;
            if (Mathf.Approximately(config.ArmDamper, 18)) config.ArmDamper = 50;
            if (Mathf.Approximately(config.PushForcePerHand, 140)) config.PushForcePerHand = 55;
            EditorUtility.SetDirty(config);
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Art/PhysicalFonts/PushHandsUI.asset");
            if (font == null)
            {
                var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/_Project/Art/PhysicalFonts/PushHandsUI.ttf");
                if (source == null) throw new InvalidOperationException("请先运行 python Tools/PrepareUIFont.py --physical。");
                font = TMP_FontAsset.CreateFontAsset(source, 48, 5, GlyphRenderMode.SDFAA, 2048, 2048);
                AssetDatabase.CreateAsset(font, "Assets/_Project/Art/PhysicalFonts/PushHandsUI.asset");
                AssetDatabase.AddObjectToAsset(font.material, font);
                foreach (var texture in font.atlasTextures) AssetDatabase.AddObjectToAsset(texture, font);
            }
            font.TryAddCharacters(File.ReadAllText("Assets/_Project/Art/PhysicalFonts/characters.txt")); EditorUtility.SetDirty(font);
            var physicalScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "PhysicalTrainingFloor";
            floor.transform.position = new Vector3(0, -0.06f, 0); floor.transform.localScale = new Vector3(8, 0.1f, 8);
            floor.GetComponent<Renderer>().sharedMaterial = Material("Floor", new Color(0.20f, 0.25f, 0.28f));
            const string frictionPath = "Assets/_Project/Art/Physical/SupportFriction.physicMaterial";
            var friction = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(frictionPath);
            if (friction == null)
            {
                friction = new PhysicMaterial("SupportFriction") { dynamicFriction = 0.75f, staticFriction = 0.9f, bounciness = 0,
                    frictionCombine = PhysicMaterialCombine.Average };
                AssetDatabase.CreateAsset(friction, frictionPath);
            }
            floor.GetComponent<Collider>().sharedMaterial = friction;
            var sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.1f;
            sun.transform.rotation = Quaternion.Euler(45, -25, 0); sun.shadows = LightShadows.Soft;
            RenderSettings.ambientLight = new Color(0.5f, 0.54f, 0.58f);
            var match = new GameObject("PhysicalPushHands").AddComponent<PhysicalMatch>(); match.Config = config;
            match.Recorder = match.gameObject.AddComponent<PhysicalRecorder>();
            match.Fighters = new[] { PhysicalFighter.Create(0, config, Material("PlayerOne", new Color(0.18f, 0.48f, 0.72f))),
                PhysicalFighter.Create(1, config, Material("PlayerTwo", new Color(0.86f, 0.42f, 0.20f))) };
            match.Hud = new TMP_Text[2]; match.CountdownHud = new TMP_Text[2];
            var canvas = new GameObject("PhysicalHud", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = 0.5f;
            for (int i = 0; i < 2; i++)
            {
                foreach (var bone in match.Fighters[i].Bones) bone.Collider.sharedMaterial = friction;
                match.Hud[i] = Label(canvas.transform, "Player" + i + "Status", font, new Vector2(i * 0.5f + 0.012f, 0.70f), new Vector2((i + 1) * 0.5f - 0.012f, 0.98f), 23);
                match.Hud[i].alignment = TextAlignmentOptions.TopLeft;
                match.CountdownHud[i] = Label(canvas.transform, "Player" + i + "Countdown", font, new Vector2(i * 0.5f, 0.38f), new Vector2((i + 1) * 0.5f, 0.62f), 110);
                match.CountdownHud[i].alignment = TextAlignmentOptions.Center;
                match.CountdownHud[i].color = new Color(1, 0.9f, 0.55f);
                PrefabUtility.SaveAsPrefabAsset(match.Fighters[i].gameObject, "Assets/_Project/Prefabs/Physical/Fighter" + i + ".prefab");
            }
            PhysicalFighter.ConfigureInteraction(match.Fighters[0], match.Fighters[1]);
            EditorSceneManager.SaveScene(physicalScene, ScenePath); AssetDatabase.SaveAssets();
            ConfigureArena();
            // 不覆盖原灰盒 Build Settings；构建入口显式指定独立场景。
            Debug.Log("独立物理原型已生成：" + ScenePath);
        }
        [MenuItem("推手/物理人偶/打开物理原型")]
        public static void Open()
        { if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath); }
        public static void Handoff()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("请先退出运行模式。");
            foreach (var scene in LoadedScenes()) if (scene.isDirty) throw new InvalidOperationException("当前场景尚未保存，未自动切换场景。");
            EditorSceneManager.OpenScene(ScenePath);
            var match = UnityEngine.Object.FindObjectOfType<PhysicalMatch>();
            if (match == null) throw new InvalidOperationException("物理场景缺少对局控制器。");
            CompleteArenaInspection(match);
            Selection.activeGameObject = match.gameObject;
            CapturePreview(match);
            Debug.Log("物理试玩已就绪：点击 Play，F1 后按 Enter 可键鼠试玩；双手柄分别按菜单键加入。");
        }
        private static void CapturePreview(PhysicalMatch match)
        {
            Directory.CreateDirectory("Artifacts/Verification");
            var observer = new GameObject("PhysicalInspectionCamera"); observer.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var camera = observer.AddComponent<Camera>(); camera.fieldOfView = 48;
                observer.transform.position = new Vector3(2.1f, 1.8f, -2.4f);
                observer.transform.LookAt(new Vector3(0, 0.95f, 0));
                SaveCamera(camera, "Artifacts/Verification/physical-overview.png");
                for (int i = 0; i < 2; i++)
                { match.Fighters[i].RenderView(0); SaveCamera(match.Fighters[i].ViewCamera, "Artifacts/Verification/physical-view" + i + ".png"); }
            }
            finally { UnityEngine.Object.DestroyImmediate(observer); }
        }
        private static void SaveCamera(Camera camera, string path)
        {
            var target = new RenderTexture(960, 720, 24); var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active; var previousRect = camera.rect; Texture2D texture = null;
            try
            {
                camera.rect = new Rect(0, 0, 1, 1); camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                texture = new Texture2D(960, 720, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 960, 720), 0, 0); texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget; camera.rect = previousRect; RenderTexture.active = previousActive;
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture); target.Release(); UnityEngine.Object.DestroyImmediate(target);
            }
        }
        [MenuItem("推手/物理人偶/构建 Windows 实验包")]
        public static void BuildWindows()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("请先退出运行模式。");
            if (!File.Exists(ScenePath)) throw new InvalidOperationException("请先生成物理原型场景。");
            foreach (var scene in LoadedScenes()) if (scene.isDirty) throw new InvalidOperationException("请先保存场景。");
            Directory.CreateDirectory("Builds/Physical"); Directory.CreateDirectory("Artifacts/Verification");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath },
                locationPathName = "Builds/Physical/PushHandsPhysical.exe", target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development });
            File.WriteAllText("Artifacts/Verification/physical-build.txt", $"{report.summary.result}\nerrors={report.summary.totalErrors}\nwarnings={report.summary.totalWarnings}");
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("物理原型构建失败，请查看构建报告和 Console。");
        }
        private static System.Collections.Generic.IEnumerable<Scene> LoadedScenes()
        { for (int i = 0; i < SceneManager.sceneCount; i++) yield return SceneManager.GetSceneAt(i); }
        private static TMP_Text Label(Transform parent, string name, TMP_FontAsset font, Vector2 min, Vector2 max, int size)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)); obj.transform.SetParent(parent, false);
            var rect = obj.GetComponent<RectTransform>(); rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var text = obj.GetComponent<TextMeshProUGUI>(); text.font = font; text.fontSize = size; text.raycastTarget = false;
            text.color = Color.white; text.text = ""; return text;
        }
        private static void EnsureFolder(string path)
        { if (!AssetDatabase.IsValidFolder(path)) { string parent = Path.GetDirectoryName(path).Replace('\\', '/'); AssetDatabase.CreateFolder(parent, Path.GetFileName(path)); } }
        private static Material Material(string name, Color color)
        {
            string path = "Assets/_Project/Art/Physical/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, path); }
            material.color = color; EditorUtility.SetDirty(material); return material;
        }
    }
}

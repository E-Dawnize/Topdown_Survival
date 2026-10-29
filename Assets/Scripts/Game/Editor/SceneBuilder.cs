using System.IO;
using Game.Config;
using Game.Core;
using Game.Services;
using Game.Views;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.EditorTools
{
    /// <summary>
    /// 场景脚手架。场景里只保留需要目视调、且很少变的东西：
    /// 方向光、地面、边界墙、主相机、AppRoot、HUD 实例。
    /// 玩家、虚拟相机、相机目标点都在运行时创建，改参数不需要重建场景。
    /// </summary>
    public static class SceneBuilder
    {
        const string ScenesDir = "Assets/Scenes";
        const string MaterialsDir = "Assets/Art/Materials";

        const float GroundSize = 160f;
        const float ArenaSize = 40f;
        const float CameraTilt = 50f;
        const float OrthoSize = 7f;
        const float CameraDistance = 30f;

        static Vector3 CameraOffset
        {
            get
            {
                float rad = CameraTilt * Mathf.Deg2Rad;
                return new Vector3(0f, CameraDistance * Mathf.Sin(rad), -CameraDistance * Mathf.Cos(rad));
            }
        }

        [MenuItem("Tools/Game/重建全部场景", priority = 20)]
        public static void RebuildAll()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            PrefabBuilder.EnsureFolder(ScenesDir);
            PrefabBuilder.EnsureFolder(MaterialsDir);

            // 预制体是场景的前置依赖，缺了就先生成
            PrefabBuilder.BuildAll();

            BuildArenaScene(Path.Combine(ScenesDir, "Run.unity"), GroundSize, "Run");
            BuildArenaScene(Path.Combine(ScenesDir, "Test_Arena.unity"), ArenaSize, "Test_Arena");
            BuildMinimalScene(Path.Combine(ScenesDir, "Boot.unity"), "Boot", true);
            BuildMinimalScene(Path.Combine(ScenesDir, "MainMenu.unity"), "MainMenu", false);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ApplyBuildSettings();
            Debug.Log("[SceneBuilder] 场景已重建: Run / Test_Arena / Boot / MainMenu");
        }

        [MenuItem("Tools/Game/打开 Run 场景", priority = 21)]
        public static void OpenRunScene()
        {
            EditorSceneManager.OpenScene(Path.Combine(ScenesDir, "Run.unity"));
        }

        static void ApplyBuildSettings()
        {
            string[] names = { "Boot", "MainMenu", "Run", "Test_Arena" };
            var list = new EditorBuildSettingsScene[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                list[i] = new EditorBuildSettingsScene(Path.Combine(ScenesDir, names[i] + ".unity"), true);
            }

            EditorBuildSettings.scenes = list;
            Debug.Log("[SceneBuilder] Build Settings 已更新");
        }

        static Scene NewScene()
        {
            return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        static void BuildArenaScene(string path, float size, string sceneName)
        {
            Scene scene = NewScene();

            // 装配根：输入、调试层、主循环、相机装配、AppRoot
            var rootGo = new GameObject("AppRoot");
            rootGo.AddComponent<InputService>();
            rootGo.AddComponent<DebugOverlay>();
            rootGo.AddComponent<RunLoop>();
            CameraRig cameraRig = rootGo.AddComponent<CameraRig>();
            AppRoot appRoot = rootGo.AddComponent<AppRoot>();

            // 方向光
            var lightGo = new GameObject("Directional Light");
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.97f, 0.92f);
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.75f;
            lightGo.transform.rotation = Quaternion.Euler(CameraTilt, -30f, 0f);

            // 地面
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, -0.25f, 0f);
            ground.transform.localScale = new Vector3(size, 0.5f, size);
            ground.GetComponent<Renderer>().sharedMaterial =
                GetOrCreateMaterial("MAT_Ground", new Color(0.26f, 0.28f, 0.31f));
            Object.DestroyImmediate(ground.GetComponent<Collider>());

            // 边界挡板
            float half = size * 0.5f;
            CreateWall("Boundary_N", new Vector3(0f, 1f, half + 0.5f), new Vector3(size + 2f, 2f, 1f));
            CreateWall("Boundary_S", new Vector3(0f, 1f, -half - 0.5f), new Vector3(size + 2f, 2f, 1f));
            CreateWall("Boundary_E", new Vector3(half + 0.5f, 1f, 0f), new Vector3(1f, 2f, size + 2f));
            CreateWall("Boundary_W", new Vector3(-half - 0.5f, 1f, 0f), new Vector3(1f, 2f, size + 2f));

            // 主相机：虚拟相机与目标点在运行时由 CameraRig 创建
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            Camera cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = OrthoSize;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 300f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.07f, 0.09f);
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<Cinemachine.CinemachineBrain>();
            camGo.transform.position = CameraOffset;
            camGo.transform.rotation = Quaternion.Euler(CameraTilt, 0f, 0f);

            // HUD 实例
            GameObject hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabBuilder.HudPrefabPath);
            if (hudPrefab != null)
            {
                var hud = (GameObject)PrefabUtility.InstantiatePrefab(hudPrefab);
                hud.transform.SetParent(null);
            }
            else
            {
                Debug.LogWarning("[SceneBuilder] 没找到 HUD 预制体，场景里不会有 HUD");
            }

            // 装配引用
            SetObjectField(cameraRig, "_camera", cam);
            SetFloatField(cameraRig, "_orthoSize", OrthoSize);
            SetFloatField(cameraRig, "_tiltDegrees", CameraTilt);
            SetFloatField(cameraRig, "_groundHalfExtent", size * 0.5f);
            SetFloatField(cameraRig, "_distance", CameraDistance);

            UnitPrefabMap map = AssetDatabase.LoadAssetAtPath<UnitPrefabMap>(PrefabBuilder.MapPath);
            SetObjectField(appRoot, "_prefabMap", map);

            EditorSceneManager.SaveScene(scene, path);
            Debug.Log($"[SceneBuilder] 生成 {sceneName} ({size}×{size} 米)");
        }

        static void CreateWall(string name, Vector3 position, Vector3 scale)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial =
                GetOrCreateMaterial("MAT_Boundary", new Color(0.12f, 0.13f, 0.16f));
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        static void BuildMinimalScene(string path, string sceneName, bool withRoot)
        {
            Scene scene = NewScene();

            if (withRoot)
            {
                var rootGo = new GameObject("AppRoot");
                rootGo.AddComponent<InputService>();
                rootGo.AddComponent<DebugOverlay>();
                AppRoot appRoot = rootGo.AddComponent<AppRoot>();
                UnitPrefabMap map = AssetDatabase.LoadAssetAtPath<UnitPrefabMap>(PrefabBuilder.MapPath);
                SetObjectField(appRoot, "_prefabMap", map);
            }
            else
            {
                new GameObject("MenuRoot");
            }

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            Camera cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = OrthoSize;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.07f, 0.09f);
            camGo.AddComponent<AudioListener>();
            camGo.transform.position = CameraOffset;
            camGo.transform.rotation = Quaternion.Euler(CameraTilt, 0f, 0f);

            EditorSceneManager.SaveScene(scene, path);
            Debug.Log($"[SceneBuilder] 生成 {sceneName}");
        }

        static void SetObjectField(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[SceneBuilder] {target.name} 上没有字段 {field}");
                return;
            }

            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetFloatField(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[SceneBuilder] {target.name} 上没有字段 {field}");
                return;
            }

            property.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Material GetOrCreateMaterial(string name, Color color)
        {
            string path = Path.Combine(MaterialsDir, name + ".mat");
            if (AssetDatabase.LoadAssetAtPath<Material>(path) is Material existing)
            {
                existing.SetColor("_BaseColor", color);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("[SceneBuilder] 找不到 URP/Lit，检查 URP 是否装好");
                shader = Shader.Find("Standard");
            }

            var mat = new Material(shader) { name = name };
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }

            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}

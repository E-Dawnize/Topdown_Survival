using System.IO;
using Cinemachine;
using Game.Core;
using Game.Services;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.EditorTools
{
    /// <summary>
    /// 场景骨架生成器。用代码建场景而不是手写 .unity，避免 YAML 手改出问题。
    /// 参数改了重跑一次即可，不需要在场景里手工调。
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

            Directory.CreateDirectory(ScenesDir);
            Directory.CreateDirectory(MaterialsDir);

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

            // 装配根
            var rootGo = new GameObject("AppRoot");
            rootGo.AddComponent<InputService>();
            rootGo.AddComponent<DebugOverlay>();
            rootGo.AddComponent<AppRoot>();

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

            // 边界：四块暗色挡板，先给个可见的尽头，玩法逻辑里的 clamp 在 I1 加
            float half = size * 0.5f;
            CreateWall("Boundary_N", new Vector3(0f, 1f, half + 0.5f), new Vector3(size + 2f, 2f, 1f));
            CreateWall("Boundary_S", new Vector3(0f, 1f, -half - 0.5f), new Vector3(size + 2f, 2f, 1f));
            CreateWall("Boundary_E", new Vector3(half + 0.5f, 1f, 0f), new Vector3(1f, 2f, size + 2f));
            CreateWall("Boundary_W", new Vector3(-half - 0.5f, 1f, 0f), new Vector3(1f, 2f, size + 2f));

            // 玩家占位
            GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Player";
            player.transform.position = new Vector3(0f, 0.5f, 0f);
            player.GetComponent<Renderer>().sharedMaterial =
                GetOrCreateMaterial("MAT_Player", new Color(0.85f, 0.87f, 0.92f));
            Object.DestroyImmediate(player.GetComponent<Collider>());

            // 相机 + Cinemachine
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
            camGo.AddComponent<CinemachineBrain>();
            camGo.transform.position = CameraOffset;
            camGo.transform.rotation = Quaternion.Euler(CameraTilt, 0f, 0f);

            var vcamGo = new GameObject("vcam_Follow");
            vcamGo.transform.position = CameraOffset;
            vcamGo.transform.rotation = Quaternion.Euler(CameraTilt, 0f, 0f);
            CinemachineVirtualCamera vcam = vcamGo.AddComponent<CinemachineVirtualCamera>();
            vcam.Priority = 10;
            vcam.Follow = player.transform;
            vcam.m_Lens.Orthographic = true;
            vcam.m_Lens.OrthographicSize = OrthoSize;
            vcam.m_Lens.NearClipPlane = 0.3f;
            vcam.m_Lens.FarClipPlane = 300f;

            CinemachineTransposer transposer = vcam.AddCinemachineComponent<CinemachineTransposer>();
            transposer.m_BindingMode = CinemachineTransposer.BindingMode.LockToTargetNoRoll;
            transposer.m_FollowOffset = CameraOffset;
            transposer.m_XDamping = 0.35f;
            transposer.m_YDamping = 0.0f;
            transposer.m_ZDamping = 0.35f;

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
                rootGo.AddComponent<AppRoot>();
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

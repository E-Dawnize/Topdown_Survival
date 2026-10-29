using System.Collections.Generic;
using System.IO;
using Game.Config;
using Game.Views;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.EditorTools
{
    /// <summary>
    /// 一次性脚手架：生成初始预制体与映射表。
    /// 跑完之后预制体归手工维护，这个工具不再参与日常流程。
    /// </summary>
    public static class PrefabBuilder
    {
        public const string UnitsDir = "Assets/Prefabs/Units";
        public const string UiDir = "Assets/Prefabs/UI";
        public const string MapPath = "Assets/Prefabs/UnitPrefabMap.asset";
        public const string HudPrefabPath = UiDir + "/HudCanvas.prefab";
        public const string MaterialsDir = "Assets/Art/Materials";

        static readonly string[] EnemyIds = { "Crawler", "Charger", "Thrower" };

        [MenuItem("Tools/Game/生成预制体", priority = 10)]
        public static void BuildAll()
        {
            EnsureFolder(UnitsDir);
            EnsureFolder(UiDir);
            EnsureFolder(MaterialsDir);

            var entries = new List<UnitPrefabMap.Entry>();

            GameObject player = BuildPlayer();
            entries.Add(new UnitPrefabMap.Entry { Id = "Player", Prefab = player });

            foreach (string id in EnemyIds)
            {
                entries.Add(new UnitPrefabMap.Entry { Id = id, Prefab = BuildEnemy(id) });
            }

            BuildHud();
            WriteMap(entries.ToArray());

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[PrefabBuilder] 生成 {entries.Count} 个单位预制体、HUD 与映射表");
        }

        // ---- 单位 ----

        static GameObject BuildPlayer()
        {
            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            root.name = "Player";
            Object.DestroyImmediate(root.GetComponent<Collider>());
            root.GetComponent<Renderer>().sharedMaterial =
                GetOrCreateMaterial("MAT_Player", new Color(0.85f, 0.87f, 0.92f));

            GameObject front = GameObject.CreatePrimitive(PrimitiveType.Cube);
            front.name = "Front";
            front.transform.SetParent(root.transform, false);
            front.transform.localPosition = new Vector3(0f, 0.15f, 0.42f);
            front.transform.localScale = new Vector3(0.18f, 0.12f, 0.36f);
            front.GetComponent<Renderer>().sharedMaterial =
                GetOrCreateMaterial("MAT_PlayerFront", new Color(0.35f, 0.75f, 1f));
            Object.DestroyImmediate(front.GetComponent<Collider>());

            root.AddComponent<PlayerView>();
            return SavePrefab(root, UnitsDir + "/Player.prefab");
        }

        static GameObject BuildEnemy(string id)
        {
            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = id;
            root.transform.localScale = new Vector3(0.7f, 0.7f, 0.7f);
            root.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            Object.DestroyImmediate(root.GetComponent<Collider>());

            Color color = id == "Crawler" ? new Color(0.85f, 0.45f, 0.35f)
                : id == "Charger" ? new Color(0.9f, 0.35f, 0.45f)
                : new Color(0.75f, 0.5f, 0.85f);

            root.GetComponent<Renderer>().sharedMaterial =
                GetOrCreateMaterial("MAT_Enemy" + id, color);

            return SavePrefab(root, UnitsDir + "/" + id + ".prefab");
        }

        // ---- HUD ----

        static void BuildHud()
        {
            var canvasGo = new GameObject("HudCanvas", typeof(RectTransform));

            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Transform root = canvasGo.transform;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            RectTransform hpBack = CreateRect("HpBack", root, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(24f, -24f), new Vector2(320f, 26f));
            AddImage(hpBack, new Color(0.08f, 0.09f, 0.11f, 0.85f));

            RectTransform hpFillRect = CreateRect("HpFill", hpBack, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(3f, 0f), new Vector2(314f, 20f));
            Image hpFill = AddImage(hpFillRect, new Color(0.35f, 0.8f, 0.45f));
            hpFill.type = Image.Type.Filled;
            hpFill.fillMethod = Image.FillMethod.Horizontal;
            hpFill.fillOrigin = 0;

            RectTransform hpTextRect = CreateRect("HpText", hpBack, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(12f, 0f), new Vector2(290f, 22f));
            Text hpText = AddText(hpTextRect, font, "100 / 100", 15, TextAnchor.MiddleLeft, Color.white);

            RectTransform coinRect = CreateRect("CoinText", root, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(26f, -58f), new Vector2(320f, 26f));
            Text coinText = AddText(coinRect, font, "金币  0", 20, TextAnchor.MiddleLeft,
                new Color(1f, 0.86f, 0.4f));

            RectTransform waveRect = CreateRect("WaveText", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -26f), new Vector2(400f, 32f));
            Text waveText = AddText(waveRect, font, "第 1 / 5 波", 22, TextAnchor.MiddleCenter,
                new Color(0.9f, 0.92f, 0.96f));

            RectTransform dashBack = CreateRect("DashBack", root, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-24f, 24f), new Vector2(220f, 52f));
            AddImage(dashBack, new Color(0.08f, 0.09f, 0.11f, 0.85f));

            RectTransform dashFillRect = CreateRect("DashFill", dashBack, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(3f, 3f), new Vector2(214f, 8f));
            Image dashFill = AddImage(dashFillRect, new Color(0.55f, 0.9f, 1f));
            dashFill.type = Image.Type.Filled;
            dashFill.fillMethod = Image.FillMethod.Horizontal;
            dashFill.fillOrigin = 0;

            RectTransform dashTextRect = CreateRect("DashText", dashBack, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, 6f), new Vector2(210f, 26f));
            Text dashText = AddText(dashTextRect, font, "冲刺就绪", 17, TextAnchor.MiddleCenter,
                new Color(0.55f, 0.9f, 1f));

            RectTransform hintRect = CreateRect("Hint", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 92f), new Vector2(440f, 40f));
            AddImage(hintRect, new Color(0f, 0f, 0f, 0.55f));
            hintRect.gameObject.SetActive(false);

            RectTransform hintTextRect = CreateRect("HintText", hintRect, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420f, 34f));
            Text hintText = AddText(hintTextRect, font, "", 18, TextAnchor.MiddleCenter, Color.white);

            RectTransform keysRect = CreateRect("Keys", root, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(24f, 24f), new Vector2(560f, 26f));
            AddText(keysRect, font, "WASD 移动    空格 冲刺    F2 扣血    F3 加金币    F1 调试信息",
                15, TextAnchor.MiddleLeft, new Color(0.75f, 0.78f, 0.85f, 0.75f));

            HudView view = canvasGo.AddComponent<HudView>();
            var so = new SerializedObject(view);
            so.FindProperty("_hpFill").objectReferenceValue = hpFill;
            so.FindProperty("_hpText").objectReferenceValue = hpText;
            so.FindProperty("_coinText").objectReferenceValue = coinText;
            so.FindProperty("_waveText").objectReferenceValue = waveText;
            so.FindProperty("_dashFill").objectReferenceValue = dashFill;
            so.FindProperty("_dashText").objectReferenceValue = dashText;
            so.FindProperty("_hintBackdrop").objectReferenceValue = hintRect.gameObject;
            so.FindProperty("_hintText").objectReferenceValue = hintText;
            so.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(canvasGo, HudPrefabPath);
            Object.DestroyImmediate(canvasGo);
            Debug.Log($"[PrefabBuilder] 生成 HUD: {HudPrefabPath} ({(prefab != null ? "成功" : "失败")})");
        }

        // ---- 映射表 ----

        static void WriteMap(UnitPrefabMap.Entry[] entries)
        {
            var map = AssetDatabase.LoadAssetAtPath<UnitPrefabMap>(MapPath);
            if (map == null)
            {
                map = ScriptableObject.CreateInstance<UnitPrefabMap>();
                AssetDatabase.CreateAsset(map, MapPath);
            }

            var so = new SerializedObject(map);
            SerializedProperty arr = so.FindProperty("_entries");
            arr.arraySize = entries.Length;
            for (int i = 0; i < entries.Length; i++)
            {
                SerializedProperty e = arr.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("Id").stringValue = entries[i].Id;
                e.FindPropertyRelative("Prefab").objectReferenceValue = entries[i].Prefab;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(map);
        }

        // ---- 工具 ----

        /// <summary>
        /// Assets 下的目录必须走 AssetDatabase 创建。用 System.IO.Directory.CreateDirectory
        /// 建的目录 Unity 不认，随后的 SaveAsPrefabAsset / CreateAsset 会静默失败。
        /// </summary>
        public static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = path.Substring(0, path.LastIndexOf('/'));
            string name = path.Substring(path.LastIndexOf('/') + 1);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, name);
        }

        static GameObject SavePrefab(GameObject root, string path)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
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
            if (shader == null) shader = Shader.Find("Standard");

            var mat = new Material(shader) { name = name };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static RectTransform CreateRect(string name, Transform parent, Vector2 anchor, Vector2 pivot,
            Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        static Image AddImage(RectTransform rect, Color color)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static Text AddText(RectTransform rect, Font font, string content, int size, TextAnchor anchor, Color color)
        {
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.text = content;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
    }
}

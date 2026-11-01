using System;
using Game.Views;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.EditorTools
{
    /// <summary>
    /// 把导入的 CC0 FBX 包成运行时 prefab，并生成 WeaponVisualCatalog。
    /// 这是资产管线，不是运行时逻辑：加武器/换模型时只改这里的 source，不碰 WeaponVisual。
    ///
    /// 菜单：Tools/Weapons/Create Missing Visual Prefabs（默认，不覆盖手调 prefab）
    ///       Tools/Weapons/Force Rebuild Visual Prefabs (overwrites)
    ///
    /// 模型来源与许可见 Assets/Art/Models/Weapons/README.md。
    /// </summary>
    public static class WeaponPrefabBuilder
    {
        const string Root = "Assets/Prefabs/Weapons";
        const string CatalogPath = "Assets/Resources/WeaponVisualCatalog.asset";
        const string MaterialRoot = "Assets/Art/Materials/Weapons";
        const string FlashMaterialPath = MaterialRoot + "/MAT_WeaponFlash.mat";
        const string OrbGlowMaterialPath = MaterialRoot + "/MAT_WeaponOrbGlow.mat";
        
        const string ArrowSource = "Assets/Art/Models/Weapons/Quaternius/Arrow.fbx";
        const string ProjectileArrowPath = "Assets/Prefabs/Projectiles/Projectile_Arrow.prefab";
        const float ProjectileArrowLength = 0.55f;

        enum ModelPivot
        {
            /// <summary>包围盒中心落在 prefab 原点，适合投射物这种需要居中旋转的模型。</summary>
            Center,

            /// <summary>箭尾（最小 Z）落在 prefab 原点，适合搭在弓上的箭。</summary>
            Nock
        }

        enum Grip
        {
            /// <summary>模型最低点落在槽位，适合剑/匕首/锤这类握把在底部的武器。</summary>
            Bottom,

            /// <summary>模型包围盒中心落在槽位，适合弓/弩/灵球这类需要悬在身体一侧的武器。</summary>
            Center
        }

        sealed class Spec
        {
            public string Id;
            public string Source;
            public float TargetSize;
            public WeaponAttackStyle Style;
            public float Duration;
            public Grip Grip;
            public Vector3 FlashLocal;
            public float FlashScale;
            public bool OrbGlow;

            // 这些只是“创建缺失 prefab”时的初始值；prefab 生成后以 prefab 里的序列化值为准。

            /// <summary>双持武器（匕首）在 prefab 里放两份模型，左右各偏一点。</summary>
            public bool Pair;
        }

        static readonly Spec[] Specs =
        {
            new Spec
            {
                Id = "Bow",
                Source = "Assets/Art/Models/Weapons/Quaternius/Bow_Wooden.fbx",
                TargetSize = 1.30f, Style = WeaponAttackStyle.BowDraw, Duration = 0.26f,
                Grip = Grip.Center, FlashLocal = new Vector3(0f, 0.05f, 0.12f), FlashScale = 0.10f
            },
            new Spec
            {
                Id = "Crossbow",
                Source = "Assets/Art/Models/Weapons/KayKit/crossbow_1handed.fbx",
                TargetSize = 1.05f, Style = WeaponAttackStyle.CrossbowRecoil, Duration = 0.34f,
                Grip = Grip.Center, FlashLocal = new Vector3(0f, 0.05f, 0.58f), FlashScale = 0.10f
            },
            new Spec
            {
                Id = "Orb",
                Source = "Assets/Art/Models/Weapons/KayKit/wand.fbx",
                TargetSize = 1.00f, Style = WeaponAttackStyle.OrbPulse, Duration = 0.38f,
                Grip = Grip.Center, FlashLocal = new Vector3(0f, 0.52f, 0f), FlashScale = 0.20f,
                OrbGlow = true
            },
            new Spec
            {
                Id = "Sword",
                Source = "Assets/Art/Models/Weapons/Quaternius/Sword.fbx",
                TargetSize = 1.20f, Style = WeaponAttackStyle.Slash, Duration = 0.32f,
                Grip = Grip.Bottom, FlashLocal = new Vector3(0f, 0.95f, 0f), FlashScale = 0.10f
            },
            new Spec
            {
                Id = "Dagger",
                Source = "Assets/Art/Models/Weapons/Quaternius/Dagger.fbx",
                TargetSize = 0.70f, Style = WeaponAttackStyle.DoubleSlash, Duration = 0.24f,
                Grip = Grip.Bottom, FlashLocal = new Vector3(0f, 0.95f, 0f), FlashScale = 0.09f,
                Pair = true
            },
            new Spec
            {
                Id = "Hammer",
                Source = "Assets/Art/Models/Weapons/Quaternius/Hammer_Small.fbx",
                TargetSize = 1.30f, Style = WeaponAttackStyle.Slam, Duration = 0.46f,
                Grip = Grip.Bottom, FlashLocal = new Vector3(0f, 0.90f, 0f), FlashScale = 0.12f
            }
        };

        /// <summary>
        /// 只补缺失的 prefab：已经存在的 prefab 是美术/场景侧的唯一真源，
        /// 在这里手调的 Model 朝向、NockedArrow、闪光挂点不会被覆盖。
        /// </summary>
        [MenuItem("Tools/Weapons/Create Missing Visual Prefabs", priority = 39)]
        public static void CreateMissing() => Build(force: false);

        /// <summary>
        /// 强制重生成所有 prefab。会覆盖手调的 Model 旋转/搭箭位置，只在明确要重置时用。
        /// </summary>
        [MenuItem("Tools/Weapons/Force Rebuild Visual Prefabs (overwrites)", priority = 40)]
        public static void Rebuild() => Build(force: true);

        static void Build(bool force)
        {
            EnsureFolder(Root);
            EnsureFolder("Assets/Resources");
            EnsureFolder("Assets/Prefabs/Projectiles");
            EnsureFolder(MaterialRoot);

            Material flashMaterial = CreateUnlitMaterial(FlashMaterialPath,
                new Color(1f, 0.86f, 0.38f, 1f));
            Material orbGlowMaterial = CreateUnlitMaterial(OrbGlowMaterialPath,
                new Color(0.22f, 0.95f, 1f, 1f));

            BuildProjectileArrow(force);

            var entries = new WeaponVisualCatalog.Entry[Specs.Length];
            for (int i = 0; i < Specs.Length; i++)
            {
                Spec spec = Specs[i];
                string path = Root + "/Weapon_" + spec.Id + ".prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                // prefab 已存在时 Create Missing 不碰它；只有 Force Rebuild 或缺失才生成。
                if (force || prefab == null)
                    prefab = BuildOne(spec, flashMaterial, orbGlowMaterial, prefab);

                entries[i] = new WeaponVisualCatalog.Entry
                {
                    WeaponId = spec.Id,
                    Prefab = prefab,
                    AttackStyle = spec.Style,
                    AttackDuration = spec.Duration
                };
            }

            WriteCatalog(entries);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[WeaponPrefabBuilder] " + (force ? "强制重建" : "补齐缺失") +
                      " " + Specs.Length + " 个武器 prefab 与 catalog");
        }

        static GameObject BuildOne(Spec spec, Material flashMaterial, Material orbGlowMaterial,
            GameObject existingPrefab)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(spec.Source);
            if (source == null)
            {
                Debug.LogError("[WeaponPrefabBuilder] 找不到模型：" + spec.Source);
                return null;
            }

            GameObject root = new GameObject("Weapon_" + spec.Id);
            WeaponVisual visual = root.AddComponent<WeaponVisual>();

            GameObject model = UnityEngine.Object.Instantiate(source);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);

            // 模型朝向以已有 prefab 的 Model 为真源；Force Rebuild 也把它读回来。
            // prefab 不存在时才保留 FBX 导入器给的根旋转。
            Transform existingModel = existingPrefab != null ? existingPrefab.transform.Find("Model") : null;
            if (existingModel != null) model.transform.localRotation = existingModel.localRotation;
            RemoveColliders(model);

            Bounds bounds = BoundsOf(model);
            float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            float scale = longest > 1e-4f ? spec.TargetSize / longest : 1f;

            model.transform.localScale *= scale;

            // 导入模型的 pivot 各不相同。统一把"握点"落到 prefab 原点：
            // 剑/锤握把在底端，弓/弩/灵球要悬在槽位中心，否则会整把插在角色头顶或脚底。
            float y = spec.Grip == Grip.Bottom ? -bounds.min.y : -bounds.center.y;
            Vector3 gripPosition = new Vector3(
                -bounds.center.x * scale,
                y * scale,
                -bounds.center.z * scale);
            model.transform.localPosition = gripPosition;

            if (spec.Pair)
            {
                Quaternion daggerRotation = model.transform.localRotation;
                model.transform.localPosition = gripPosition + new Vector3(0.08f, 0f, 0f);
                model.transform.localRotation = daggerRotation * Quaternion.Euler(0f, 0f, -12f);

                GameObject second = UnityEngine.Object.Instantiate(source);
                second.name = "Model_L";
                second.transform.SetParent(root.transform, false);
                RemoveColliders(second);
                second.transform.localScale = model.transform.localScale;
                second.transform.localPosition = gripPosition + new Vector3(-0.08f, 0f, 0f);
                second.transform.localRotation = daggerRotation * Quaternion.Euler(0f, 0f, 12f);
            }

            Transform nockedArrow = null;
            GameObject nockedArrowPrefab = null;
            if (spec.Id == "Bow")
            {
                nockedArrowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectileArrowPath);
                if (nockedArrowPrefab == null)
                {
                    Debug.LogError("[WeaponPrefabBuilder] 找不到搭箭 prefab：" + ProjectileArrowPath);
                }
                else
                {
                    // 搭箭就是投射物 prefab 的一个实例：开火时这个实例被交给投射物池，
                    // 而不是再生成一个长得不一样的箭。
                    var nocked = (GameObject)PrefabUtility.InstantiatePrefab(nockedArrowPrefab);
                    nocked.name = "NockedArrow";
                    nocked.transform.SetParent(root.transform, false);
                    nocked.transform.localPosition = new Vector3(0f, 0f, 0.02f);
                    nocked.transform.localRotation = Quaternion.identity;
                    nocked.SetActive(false);
                    nockedArrow = nocked.transform;
                }
            }

            GameObject flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flash.name = "Flash";
            flash.transform.SetParent(root.transform, false);
            flash.transform.localPosition = spec.FlashLocal * spec.TargetSize;
            flash.transform.localScale = Vector3.one * (spec.FlashScale * spec.TargetSize);
            RemoveColliders(flash);
            SetUnlitRenderer(flash, flashMaterial);

            if (spec.OrbGlow)
            {
                GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                glow.name = "OrbGlow";
                glow.transform.SetParent(root.transform, false);
                glow.transform.localPosition = new Vector3(0f, 0.46f * spec.TargetSize, 0f);
                glow.transform.localScale = Vector3.one * (0.30f * spec.TargetSize);
                RemoveColliders(glow);
                SetUnlitRenderer(glow, orbGlowMaterial);
            }

            var serialized = new SerializedObject(visual);
            SerializedProperty flashProperty = serialized.FindProperty("_flash");
            flashProperty.objectReferenceValue = flash.transform;
            SerializedProperty nockedProperty = serialized.FindProperty("_nockedArrow");
            nockedProperty.objectReferenceValue = nockedArrow;
            SerializedProperty nockedPrefabProperty = serialized.FindProperty("_nockedArrowPrefab");
            nockedPrefabProperty.objectReferenceValue = nockedArrowPrefab;

            // 投射物视觉起点：弓用搭箭（弓把），弩/灵球用闪光前端；这些都在 prefab 里可改。
            Transform muzzle = nockedArrow != null ? nockedArrow : flash.transform;
            SerializedProperty muzzleProperty = serialized.FindProperty("_muzzle");
            muzzleProperty.objectReferenceValue = muzzle;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            string path = Root + "/Weapon_" + spec.Id + ".prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);

            Debug.Log("[WeaponPrefabBuilder] " + spec.Id + " bounds=" + bounds.size +
                      " scale=" + scale.ToString("0.000") + " -> " + path);
            return prefab;
        }

        static void BuildProjectileArrow(bool force)
        {
            if (!force && AssetDatabase.LoadAssetAtPath<GameObject>(ProjectileArrowPath) != null) return;

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(ArrowSource);
            if (source == null)
            {
                Debug.LogError("[WeaponPrefabBuilder] 找不到投射物箭模型：" + ArrowSource);
                return;
            }

            GameObject root = new GameObject("Projectile_Arrow");
            root.AddComponent<ProjectileView>();

            GameObject model = BuildModel(source, ProjectileArrowLength, ModelPivot.Nock);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);

            PrefabUtility.SaveAsPrefabAsset(root, ProjectileArrowPath);
            UnityEngine.Object.DestroyImmediate(root);
            Debug.Log("[WeaponPrefabBuilder] 投射物箭 -> " + ProjectileArrowPath);
        }

        static GameObject BuildModel(GameObject source, float targetSize, ModelPivot pivot)
        {
            GameObject model = UnityEngine.Object.Instantiate(source);
            RemoveColliders(model);

            Bounds bounds = BoundsOf(model);
            float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            float scale = longest > 1e-4f ? targetSize / longest : 1f;
            model.transform.localScale *= scale;

            float z = pivot == ModelPivot.Nock ? -bounds.min.z : -bounds.center.z;
            model.transform.localPosition = new Vector3(
                -bounds.center.x * scale,
                -bounds.center.y * scale,
                z * scale);
            return model;
        }

        static void SetUnlitRenderer(GameObject go, Material material)
        {
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        static void WriteCatalog(WeaponVisualCatalog.Entry[] entries)
        {
            WeaponVisualCatalog catalog = AssetDatabase.LoadAssetAtPath<WeaponVisualCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<WeaponVisualCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            var serialized = new SerializedObject(catalog);
            SerializedProperty list = serialized.FindProperty("_entries");
            list.arraySize = entries.Length;

            for (int i = 0; i < entries.Length; i++)
            {
                SerializedProperty item = list.GetArrayElementAtIndex(i);
                item.FindPropertyRelative("WeaponId").stringValue = entries[i].WeaponId;
                item.FindPropertyRelative("Prefab").objectReferenceValue = entries[i].Prefab;
                item.FindPropertyRelative("AttackStyle").enumValueIndex = (int)entries[i].AttackStyle;
                item.FindPropertyRelative("AttackDuration").floatValue = entries[i].AttackDuration;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
        }

        static Material CreateUnlitMaterial(string path, Color color)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Bounds BoundsOf(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one);

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        static void RemoveColliders(GameObject go)
        {
            Collider[] colliders = go.GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
                UnityEngine.Object.DestroyImmediate(colliders[i]);
            }
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}

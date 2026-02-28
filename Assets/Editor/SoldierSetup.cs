using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.IO;

public class SoldierSetup : EditorWindow
{
    [MenuItem("Tools/Setup Soldier")]
    static void SetupSoldier()
    {
        Debug.Log("Setting up soldier from Kevin Iglesias Human Animations...");

        string animBase = "Assets/Kevin Iglesias/Human Animations";
        string maleAnims = animBase + "/Animations/Male";
        string modelsDir = animBase + "/Models";
        string demoDir = animBase + "/Unity Demo Scenes/Human Soldier Animations";

        // Check if Kevin Iglesias assets exist
        if (!Directory.Exists(animBase))
        {
            EditorUtility.DisplayDialog("Missing Assets",
                "Kevin Iglesias Human Animations not found.\n\n" +
                "Expected at: Assets/Kevin Iglesias/Human Animations/",
                "OK");
            return;
        }

        // Try to use existing demo prefab first (already has model + weapon + materials)
        string prefabSource = demoDir + "/Prefabs/SoldierAssaultRifleM.prefab";
        string modelPath = modelsDir + "/HumanM_Model.fbx";

        if (!File.Exists(prefabSource) && !File.Exists(modelPath))
        {
            EditorUtility.DisplayDialog("Missing Model",
                "Could not find SoldierAssaultRifleM prefab or HumanM_Model.fbx",
                "OK");
            return;
        }

        // Find animation clips (idle/walk/fire loop, die does not)
        AnimationClip idleClip = FindClip(maleAnims + "/Idles", "MilitaryIdle01", true);
        AnimationClip walkClip = FindClip(maleAnims + "/Movement/Walk", "Walk01_Forward", true);
        AnimationClip runClip = FindClip(maleAnims + "/Movement/Run", "Run01_Forward", true);
        AnimationClip fireClip = FindClip(maleAnims + "/Combat/AssaultRifle", "Aim01_Shoot01", true);
        AnimationClip aimClip = FindClipExact(maleAnims + "/Combat/AssaultRifle", "AssaultRifle_Aim01", true);
        AnimationClip dieClip1 = FindClip(maleAnims + "/Combat", "Death01", false);
        AnimationClip dieClip2 = FindClip(maleAnims + "/Combat", "Death02", false);
        AnimationClip dieClip3 = FindClip(maleAnims + "/Combat", "Death03", false);
        AnimationClip damageClip = FindClip(maleAnims + "/Combat", "Damage01", false);
        AnimationClip reloadClip = FindClip(maleAnims + "/Combat/AssaultRifle", "Reload01", true);

        AnimationClip dieClip = dieClip1 ?? dieClip2 ?? dieClip3;

        Debug.Log("Idle: " + (idleClip != null ? idleClip.name : "MISSING"));
        Debug.Log("Walk: " + (walkClip != null ? walkClip.name : "MISSING"));
        Debug.Log("Fire: " + (fireClip != null ? fireClip.name : "MISSING"));
        Debug.Log("Die: " + (dieClip != null ? dieClip.name : "MISSING"));
        Debug.Log("Aim: " + (aimClip != null ? aimClip.name : "MISSING"));

        // Create output directory
        if (!Directory.Exists("Assets/Soldier"))
            AssetDatabase.CreateFolder("Assets", "Soldier");

        // Create Animator Controller with our parameters
        string controllerPath = "Assets/Soldier/SoldierController.controller";
        if (File.Exists(controllerPath))
            AssetDatabase.DeleteAsset(controllerPath);

        var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

        controller.AddParameter("Walking", AnimatorControllerParameterType.Bool);
        controller.AddParameter("InRange", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);

        var sm = controller.layers[0].stateMachine;

        // Idle state (default) - use military idle or aim
        var idleState = sm.AddState("Idle", new Vector3(200, 0, 0));
        idleState.motion = aimClip ?? idleClip;
        sm.defaultState = idleState;

        // Walk state
        AnimatorState walkState = null;
        AnimationClip walkAnim = walkClip ?? runClip;
        if (walkAnim != null)
        {
            walkState = sm.AddState("Walk", new Vector3(200, 80, 0));
            walkState.motion = walkAnim;

            var toWalk = idleState.AddTransition(walkState);
            toWalk.AddCondition(AnimatorConditionMode.If, 0, "Walking");
            toWalk.hasExitTime = false;
            toWalk.duration = 0.2f;

            var toIdle = walkState.AddTransition(idleState);
            toIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "Walking");
            toIdle.hasExitTime = false;
            toIdle.duration = 0.2f;
        }

        // Fire state
        AnimatorState fireState = null;
        if (fireClip != null)
        {
            fireState = sm.AddState("Fire", new Vector3(400, 40, 0));
            fireState.motion = fireClip;

            var idleToFire = idleState.AddTransition(fireState);
            idleToFire.AddCondition(AnimatorConditionMode.If, 0, "InRange");
            idleToFire.hasExitTime = false;
            idleToFire.duration = 0.15f;

            if (walkState != null)
            {
                var walkToFire = walkState.AddTransition(fireState);
                walkToFire.AddCondition(AnimatorConditionMode.If, 0, "InRange");
                walkToFire.hasExitTime = false;
                walkToFire.duration = 0.15f;
            }

            var fireToWalk = fireState.AddTransition(walkState ?? idleState);
            fireToWalk.AddCondition(AnimatorConditionMode.IfNot, 0, "InRange");
            fireToWalk.hasExitTime = false;
            fireToWalk.duration = 0.2f;
        }

        // Die state
        if (dieClip != null)
        {
            var dieState = sm.AddState("Die", new Vector3(200, -100, 0));
            dieState.motion = dieClip;

            var anyToDie = sm.AddAnyStateTransition(dieState);
            anyToDie.AddCondition(AnimatorConditionMode.If, 0, "Die");
            anyToDie.hasExitTime = false;
            anyToDie.duration = 0.1f;
        }

        // Create the soldier prefab
        GameObject instance = null;

        // Try using the existing demo prefab (has weapon model + setup)
        if (File.Exists(prefabSource))
        {
            GameObject demoPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabSource);
            if (demoPrefab != null)
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(demoPrefab);
                Debug.Log("Using existing SoldierAssaultRifleM prefab");
            }
        }

        // Fallback: build from the base model
        if (instance == null && File.Exists(modelPath))
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model != null)
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                Debug.Log("Using HumanM_Model base model");
            }
        }

        if (instance == null)
        {
            EditorUtility.DisplayDialog("Error", "Could not load character model", "OK");
            return;
        }

        instance.name = "SoldierEnemy";

        // Unpack the nested prefab completely so material overrides persist
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        Debug.Log("Unpacked prefab instance");

        // Remove any Kevin Iglesias controller scripts that would interfere
        foreach (var mb in instance.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb != null && mb.GetType().Name.Contains("HumanSoldier"))
            {
                Debug.Log($"Removing Kevin Iglesias script: {mb.GetType().Name} from {mb.gameObject.name}");
                Object.DestroyImmediate(mb);
            }
        }

        // Find the Animator wherever it is in the hierarchy (usually on the model child)
        var animator = instance.GetComponentInChildren<Animator>();
        if (animator == null)
        {
            animator = instance.AddComponent<Animator>();
            Debug.Log("Added new Animator to root");
        }
        else
        {
            Debug.Log($"Found existing Animator on '{animator.gameObject.name}', avatar={(animator.avatar != null ? animator.avatar.name : "NONE")}");
        }

        // Remove any duplicate Animators (root vs child)
        var allAnimators = instance.GetComponentsInChildren<Animator>(true);
        if (allAnimators.Length > 1)
        {
            // Keep the one with an avatar, remove the rest
            Animator best = null;
            foreach (var a in allAnimators)
            {
                if (a.avatar != null) { best = a; break; }
            }
            if (best == null) best = allAnimators[0];

            foreach (var a in allAnimators)
            {
                if (a != best)
                {
                    Debug.Log($"Removing duplicate Animator from '{a.gameObject.name}'");
                    Object.DestroyImmediate(a);
                }
            }
            animator = best;
        }

        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        Debug.Log($"Animator setup: controller={controller.name}, avatar={(animator.avatar != null ? animator.avatar.name : "NONE")}, on '{animator.gameObject.name}'");

        // Ensure proper scale (some FBX models import at 0.01)
        if (instance.transform.localScale.x < 0.5f)
        {
            instance.transform.localScale = Vector3.one;
            Debug.Log("Fixed model scale to 1,1,1");
        }

        // Load the original color palette texture (this is how Kevin Iglesias creates clothing)
        // The model UVs map different body parts to different regions of this palette
        // The palette at offset y=0.625 creates the soldier look: green uniform, skin, boots
        // We MUST use the original palette unmodified to preserve clothing appearance
        string palettePath = animBase + "/Textures/HumanAnimations_ColorPalette.png";
        Texture2D originalPalette = AssetDatabase.LoadAssetAtPath<Texture2D>(palettePath);

        if (originalPalette != null)
            Debug.Log($"Loaded color palette: {originalPalette.width}x{originalPalette.height}");
        else
            Debug.LogError("Could not load HumanAnimations_ColorPalette.png!");

        var urpShader = Shader.Find("Universal Render Pipeline/Lit");

        // Generate woodland camo detail texture for overlay
        Texture2D camoDetail = GenerateCamoDetail(256);
        string camoDetailPath = "Assets/Soldier/CamoDetail.png";
        File.WriteAllBytes(camoDetailPath, camoDetail.EncodeToPNG());
        AssetDatabase.ImportAsset(camoDetailPath);
        var camoTexImporter = AssetImporter.GetAtPath(camoDetailPath) as TextureImporter;
        if (camoTexImporter != null)
        {
            camoTexImporter.wrapMode = TextureWrapMode.Repeat;
            camoTexImporter.filterMode = FilterMode.Bilinear;
            camoTexImporter.SaveAndReimport();
        }
        Object.DestroyImmediate(camoDetail);
        camoDetail = AssetDatabase.LoadAssetAtPath<Texture2D>(camoDetailPath);

        // Body material - uses ORIGINAL palette with soldier offset (y=0.625)
        // The palette creates the clothing look (green uniform, skin tone, dark boots)
        // A slight dark green tint darkens everything for a more military look
        // The camo detail map adds woodland pattern on top
        var bodyMat = new Material(urpShader);
        bodyMat.name = "SoldierBody";
        bodyMat.SetTexture("_BaseMap", originalPalette);
        bodyMat.SetColor("_BaseColor", new Color(0.75f, 0.82f, 0.65f)); // slight green tint to darken
        bodyMat.SetTextureScale("_BaseMap", Vector2.one);
        bodyMat.SetTextureOffset("_BaseMap", new Vector2(0f, 0.625f));
        bodyMat.SetFloat("_Smoothness", 0.15f);
        bodyMat.SetFloat("_Metallic", 0f);

        // Camo detail overlay - adds dark/light green woodland pattern
        bodyMat.SetTexture("_DetailAlbedoMap", camoDetail);
        bodyMat.SetTextureScale("_DetailAlbedoMap", new Vector2(5f, 5f));
        bodyMat.SetFloat("_DetailAlbedoMapScale", 1f);
        bodyMat.EnableKeyword("_DETAIL_MULX2");

        string bodyMatPath = "Assets/Soldier/SoldierBody.mat";
        if (File.Exists(bodyMatPath)) AssetDatabase.DeleteAsset(bodyMatPath);
        AssetDatabase.CreateAsset(bodyMat, bodyMatPath);
        bodyMat = AssetDatabase.LoadAssetAtPath<Material>(bodyMatPath);

        // Gun material - black metallic
        var gunMat = new Material(urpShader);
        gunMat.name = "SoldierGunBlack";
        gunMat.SetColor("_BaseColor", new Color(0.04f, 0.04f, 0.05f));
        gunMat.SetFloat("_Smoothness", 0.55f);
        gunMat.SetFloat("_Metallic", 0.8f);
        string gunMatPath = "Assets/Soldier/SoldierGunBlack.mat";
        if (File.Exists(gunMatPath)) AssetDatabase.DeleteAsset(gunMatPath);
        AssetDatabase.CreateAsset(gunMat, gunMatPath);
        gunMat = AssetDatabase.LoadAssetAtPath<Material>(gunMatPath);

        // Log all renderers and their current materials for debugging
        Debug.Log("=== RENDERER DUMP BEFORE MATERIAL ASSIGNMENT ===");
        foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            string matNames = "";
            foreach (var m in renderer.sharedMaterials)
                matNames += (m != null ? m.name : "NULL") + ", ";
            Debug.Log($"  {renderer.gameObject.name} [{renderer.GetType().Name}]: {matNames}");
        }
        Debug.Log("=== END RENDERER DUMP ===");

        // Apply materials to all renderers
        foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = true;
            var mats = renderer.sharedMaterials;
            string objName = renderer.gameObject.name.ToLower();

            for (int i = 0; i < mats.Length; i++)
            {
                string matName = mats[i] != null ? mats[i].name.ToLower() : "";

                // Props material or weapon-named objects get black gun material
                if (matName.Contains("prop") || objName.Contains("rifle") ||
                    objName.Contains("gun") || objName.Contains("weapon") ||
                    objName.Contains("knife") || objName.Contains("pistol"))
                {
                    mats[i] = gunMat;
                }
                else
                {
                    // Body, helmet, clothes - use the palette-based material
                    mats[i] = bodyMat;
                }
            }
            renderer.sharedMaterials = mats;
            Debug.Log($"Material assigned: {renderer.gameObject.name} -> {(mats[0] == gunMat ? "GunBlack" : "Body")}");
        }

        // Enable all child objects
        foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
        {
            if (!child.gameObject.activeSelf)
                child.gameObject.SetActive(true);
        }

        // Add capsule collider for hit detection
        if (instance.GetComponent<Collider>() == null)
        {
            var capsule = instance.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.9f, 0f);
            capsule.radius = 0.3f;
            capsule.height = 1.8f;
        }

        // Save prefab directly to Resources for runtime loading
        if (!Directory.Exists("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        string prefabPath = "Assets/Resources/SoldierEnemy.prefab";
        if (File.Exists(prefabPath))
            AssetDatabase.DeleteAsset(prefabPath);
        PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        DestroyImmediate(instance);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string summary = "Soldier is ready! (Kevin Iglesias)\n\nAnimations:\n";
        summary += "  Idle: " + (idleState.motion != null ? "YES" : "MISSING") + "\n";
        summary += "  Walk: " + (walkAnim != null ? "YES" : "MISSING") + "\n";
        summary += "  Fire: " + (fireClip != null ? "YES" : "MISSING") + "\n";
        summary += "  Die: " + (dieClip != null ? "YES" : "MISSING") + "\n";
        summary += "  Aim: " + (aimClip != null ? "YES" : "MISSING") + "\n";
        summary += "  Damage: " + (damageClip != null ? "YES" : "MISSING") + "\n";
        summary += "\nPress Play to test!";

        EditorUtility.DisplayDialog("Setup Complete", summary, "OK");
    }

    static Texture2D GenerateCamoDetail(int size)
    {
        // Generates a woodland camouflage detail texture
        // Values are centered around 0.5 gray (neutral for detail multiply x2)
        // Dark patches darken the base, light patches lighten it
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGB24, false);

        // Multiple noise layers at different scales for organic blobs
        // Layer 1: large blobs (base camo shapes)
        float s1 = 3.5f; float ox1 = 47.3f, oy1 = 83.1f;
        // Layer 2: medium blobs (secondary shapes)
        float s2 = 7f;   float ox2 = 131.7f, oy2 = 29.4f;
        // Layer 3: fine detail / texture grain
        float s3 = 15f;  float ox3 = 67.9f, oy3 = 199.2f;
        // Layer 4: extra variation for natural look
        float s4 = 5f;   float ox4 = 223.1f, oy4 = 11.8f;

        // Camo detail colors (centered around 0.5 for detail map blending)
        // When multiplied x2: 0.5->1.0(neutral), 0.35->0.7(darken), 0.65->1.3(lighten)
        Color darkGreen  = new Color(0.28f, 0.32f, 0.22f);  // dark forest patches
        Color medGreen   = new Color(0.42f, 0.48f, 0.35f);  // medium olive (near neutral)
        Color lightGreen = new Color(0.55f, 0.60f, 0.45f);  // lighter sage green
        Color brown      = new Color(0.38f, 0.33f, 0.25f);  // earth brown patches
        Color black      = new Color(0.22f, 0.23f, 0.18f);  // dark splotches

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size;
                float v = (float)y / size;

                float n1 = Mathf.PerlinNoise(u * s1 + ox1, v * s1 + oy1);
                float n2 = Mathf.PerlinNoise(u * s2 + ox2, v * s2 + oy2);
                float n3 = Mathf.PerlinNoise(u * s3 + ox3, v * s3 + oy3);
                float n4 = Mathf.PerlinNoise(u * s4 + ox4, v * s4 + oy4);

                Color c;

                // Large blob shapes determine the main camo pattern
                if (n1 < 0.30f)
                    c = black;          // dark splotches
                else if (n1 < 0.42f)
                    c = darkGreen;      // dark forest green
                else if (n1 < 0.58f)
                {
                    // Medium zone: mix in brown patches using second noise
                    c = n2 < 0.40f ? brown : medGreen;
                }
                else if (n1 < 0.72f)
                    c = lightGreen;     // lighter sage patches
                else
                {
                    // Light zone: alternate between light green and medium
                    c = n4 < 0.45f ? medGreen : lightGreen;
                }

                // Add fine grain texture variation
                float grain = (n3 - 0.5f) * 0.06f;
                c.r = Mathf.Clamp01(c.r + grain);
                c.g = Mathf.Clamp01(c.g + grain);
                c.b = Mathf.Clamp01(c.b + grain);

                tex.SetPixel(x, y, c);
            }
        }

        tex.Apply();
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        return tex;
    }

    static AnimationClip FindClip(string directory, string nameContains, bool loop = true)
    {
        if (!Directory.Exists(directory)) return null;

        var files = Directory.GetFiles(directory, "*.fbx", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            string fname = Path.GetFileNameWithoutExtension(file);
            if (fname.ToLower().Contains(nameContains.ToLower()))
            {
                var clip = LoadClipFromFBX(file, loop);
                if (clip != null) return clip;
            }
        }
        return null;
    }

    // Matches files that end with the search term (after @ prefix)
    // e.g. "AssaultRifle_Aim01" matches "HumanM@AssaultRifle_Aim01" but NOT "HumanM@AssaultRifle_Aim01_Shoot01"
    static AnimationClip FindClipExact(string directory, string nameEndsWith, bool loop = true)
    {
        if (!Directory.Exists(directory)) return null;

        var files = Directory.GetFiles(directory, "*.fbx", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            string fname = Path.GetFileNameWithoutExtension(file);
            if (fname.ToLower().EndsWith(nameEndsWith.ToLower()))
            {
                var clip = LoadClipFromFBX(file, loop);
                if (clip != null) return clip;
            }
        }
        return null;
    }

    static AnimationClip LoadClipFromFBX(string file, bool loop = true)
    {
        string assetPath = file.Replace("\\", "/");

        // Make sure it's configured as humanoid
        var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
        if (importer != null)
        {
            bool needsReimport = false;

            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                needsReimport = true;
            }

            // Configure looping — Mixamo/asset store FBX files default to
            // loopTime=false, so animations play once and freeze on the last
            // frame, causing visible sliding for walk cycles.
            var clips = importer.clipAnimations;
            if (clips.Length == 0)
                clips = importer.defaultClipAnimations;

            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i].loopTime != loop)
                {
                    clips[i].loopTime = loop;
                    needsReimport = true;
                }
            }
            if (needsReimport)
            {
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
            }
        }

        // Load animation clip from the FBX
        var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        foreach (var asset in assets)
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__"))
            {
                Debug.Log($"Found animation: '{clip.name}' in {assetPath}, loop={loop}");
                return clip;
            }
        }
        return null;
    }
}

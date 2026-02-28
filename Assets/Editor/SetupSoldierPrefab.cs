using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.IO;

public class SetupSoldierPrefab : EditorWindow
{
    [MenuItem("Tools/Setup Soldier Prefab")]
    static void Setup()
    {
        Debug.Log("=== Setting up Soldier Prefab from Mixamo/Pro Rifle Pack ===");

        string proRiflePack = "Assets/Models/Enemy/Pro Rifle Pack";
        string enemyModels = "Assets/Models/Enemy";

        // Locate the Swat model
        string swatPath = proRiflePack + "/Swat.fbx";
        string characterPath = enemyModels + "/character.fbx";
        string modelPath = File.Exists(swatPath) ? swatPath : characterPath;

        if (!File.Exists(modelPath))
        {
            EditorUtility.DisplayDialog("Missing Model",
                "Could not find Swat.fbx or character.fbx.\n\n" +
                "Expected at:\n" + swatPath + "\n" + characterPath,
                "OK");
            return;
        }
        Debug.Log("Using model: " + modelPath);

        // Step 1: Configure FBX rig as Humanoid
        ConfigureHumanoidRig(modelPath);

        // Step 2: Load animation clips (idle/walk/fire loop, die does not)
        AnimationClip idleClip = LoadClipFromFBX(proRiflePack + "/idle aiming.fbx", true);
        AnimationClip walkClip = LoadClipFromFBX(enemyModels + "/Crouch Walking.fbx", true);
        if (walkClip == null)
            walkClip = LoadClipFromFBX(proRiflePack + "/walk forward.fbx", true);
        AnimationClip dieClip = LoadClipFromFBX(proRiflePack + "/death from the front.fbx", false);

        // Try multiple sources for firing animation
        AnimationClip fireClip = LoadClipFromFBX(enemyModels + "/Firing Rifle.fbx", true);
        if (fireClip == null)
            fireClip = LoadClipFromFBX(proRiflePack + "/Firing Rifle.fbx", true);
        if (fireClip == null)
            fireClip = idleClip; // fallback: use idle aiming as fire pose

        Debug.Log("Idle: " + (idleClip != null ? idleClip.name : "MISSING"));
        Debug.Log("Walk: " + (walkClip != null ? walkClip.name : "MISSING"));
        Debug.Log("Fire: " + (fireClip != null ? fireClip.name : "MISSING"));
        Debug.Log("Die: " + (dieClip != null ? dieClip.name : "MISSING"));

        // Step 3: Create AnimatorController
        string controllerPath = "Assets/Resources/SoldierAnimator.controller";
        if (!Directory.Exists("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (File.Exists(controllerPath))
            AssetDatabase.DeleteAsset(controllerPath);

        var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        controller.AddParameter("Walking", AnimatorControllerParameterType.Bool);
        controller.AddParameter("InRange", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);

        var sm = controller.layers[0].stateMachine;

        // Idle state (default)
        var idleState = sm.AddState("Idle", new Vector3(200, 0, 0));
        idleState.motion = idleClip;
        sm.defaultState = idleState;

        // Walk state
        AnimatorState walkState = null;
        if (walkClip != null)
        {
            walkState = sm.AddState("Walk", new Vector3(200, 80, 0));
            walkState.motion = walkClip;

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

            // Idle -> Fire (InRange=true)
            var idleToFire = idleState.AddTransition(fireState);
            idleToFire.AddCondition(AnimatorConditionMode.If, 0, "InRange");
            idleToFire.hasExitTime = false;
            idleToFire.duration = 0.15f;

            // Walk -> Fire (InRange=true)
            if (walkState != null)
            {
                var walkToFire = walkState.AddTransition(fireState);
                walkToFire.AddCondition(AnimatorConditionMode.If, 0, "InRange");
                walkToFire.hasExitTime = false;
                walkToFire.duration = 0.15f;
            }

            // Fire -> Walk/Idle (InRange=false)
            var fireOut = fireState.AddTransition(walkState ?? idleState);
            fireOut.AddCondition(AnimatorConditionMode.IfNot, 0, "InRange");
            fireOut.hasExitTime = false;
            fireOut.duration = 0.2f;
        }

        // Die state (Any State -> Die)
        if (dieClip != null)
        {
            var dieState = sm.AddState("Die", new Vector3(200, -100, 0));
            dieState.motion = dieClip;

            var anyToDie = sm.AddAnyStateTransition(dieState);
            anyToDie.AddCondition(AnimatorConditionMode.If, 0, "Die");
            anyToDie.hasExitTime = false;
            anyToDie.duration = 0.1f;
        }

        Debug.Log("AnimatorController created at " + controllerPath);

        // Step 4: Build the prefab
        // Structure: SoldierEnemy (root) > ModelPivot (rotation fix) > [Swat model]
        // Enemy.cs rotates the root; the pivot corrects the Mixamo mesh orientation
        // so the visual faces +Z. Humanoid animator lives on the model and is untouched.
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null)
        {
            EditorUtility.DisplayDialog("Error", "Could not load model at " + modelPath, "OK");
            return;
        }

        // Create wrapper root
        GameObject instance = new GameObject("SoldierEnemy");

        // Create rotation pivot — Mixamo Swat faces +X, rotate 90 Y to face +Z
        GameObject pivot = new GameObject("ModelPivot");
        pivot.transform.SetParent(instance.transform);
        pivot.transform.localPosition = Vector3.zero;
        pivot.transform.localRotation = Quaternion.identity;

        // Instantiate the actual model under the pivot
        GameObject modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        PrefabUtility.UnpackPrefabInstance(modelInstance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        modelInstance.transform.SetParent(pivot.transform);
        modelInstance.transform.localPosition = Vector3.zero;
        modelInstance.transform.localRotation = Quaternion.identity;
        modelInstance.transform.localScale = Vector3.one;
        modelInstance.name = "Model";

        // Configure Animator (lives on the model, not the root)
        var animator = modelInstance.GetComponentInChildren<Animator>();
        if (animator == null)
            animator = modelInstance.AddComponent<Animator>();

        Avatar modelAvatar = animator.avatar;
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        if (modelAvatar != null)
            animator.avatar = modelAvatar;
        Debug.Log($"Animator: controller={controller.name}, avatar={(animator.avatar != null ? animator.avatar.name : "NONE")}, isHuman={animator.isHuman}");

        // Create military materials — Mixamo models have no textures, so apply to ALL renderers
        var urpShader = Shader.Find("Universal Render Pipeline/Lit");
        Material bodyMat = null;
        Material gunMat = null;

        if (urpShader != null)
        {
            // Try to load existing materials from Assets/Soldier/
            bodyMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Soldier/SoldierBody.mat");
            gunMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Soldier/SoldierGunBlack.mat");

            // Create body material if not found
            if (bodyMat == null)
            {
                bodyMat = new Material(urpShader);
                bodyMat.name = "SoldierBody";
                bodyMat.SetColor("_BaseColor", new Color(0.35f, 0.38f, 0.28f));
                bodyMat.SetFloat("_Smoothness", 0.15f);
                bodyMat.SetFloat("_Metallic", 0f);
                if (!Directory.Exists("Assets/Soldier"))
                    AssetDatabase.CreateFolder("Assets", "Soldier");
                AssetDatabase.CreateAsset(bodyMat, "Assets/Soldier/SoldierBody.mat");
            }

            // Create gun material if not found
            if (gunMat == null)
            {
                gunMat = new Material(urpShader);
                gunMat.name = "SoldierGunBlack";
                gunMat.SetColor("_BaseColor", new Color(0.04f, 0.04f, 0.05f));
                gunMat.SetFloat("_Smoothness", 0.55f);
                gunMat.SetFloat("_Metallic", 0.8f);
                if (!Directory.Exists("Assets/Soldier"))
                    AssetDatabase.CreateFolder("Assets", "Soldier");
                AssetDatabase.CreateAsset(gunMat, "Assets/Soldier/SoldierGunBlack.mat");
            }

            // Apply materials to ALL renderers — weapon parts get gun material, body gets body material
            // Mixamo models have no usable textures, so we must override everything
            Debug.Log("=== RENDERER DUMP ===");
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = true;
                var mats = renderer.sharedMaterials;
                string objName = renderer.gameObject.name.ToLower();

                for (int i = 0; i < mats.Length; i++)
                {
                    string matName = mats[i] != null ? mats[i].name.ToLower() : "";

                    // Weapon parts get black metallic material
                    if (matName.Contains("prop") || matName.Contains("gun") || matName.Contains("weapon") ||
                        matName.Contains("rifle") || matName.Contains("metal") ||
                        objName.Contains("rifle") || objName.Contains("gun") ||
                        objName.Contains("weapon") || objName.Contains("knife") ||
                        objName.Contains("pistol"))
                    {
                        mats[i] = gunMat;
                    }
                    else
                    {
                        mats[i] = bodyMat;
                    }
                }
                renderer.sharedMaterials = mats;
                Debug.Log($"  {renderer.gameObject.name}: {(mats[0] == gunMat ? "GunBlack" : "Body")}");
            }
            Debug.Log("=== END RENDERERS ===");
        }

        // Enable all child objects
        foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
        {
            if (!child.gameObject.activeSelf)
                child.gameObject.SetActive(true);
        }

        // Body collider (torso/legs, stops at neck)
        if (instance.GetComponent<Collider>() == null)
        {
            var capsule = instance.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.7f, 0f);
            capsule.radius = 0.3f;
            capsule.height = 1.4f;
        }

        // Head hitbox for headshot detection
        var existingHead = instance.transform.Find("HeadHitbox");
        if (existingHead == null)
        {
            GameObject headHitbox = new GameObject("HeadHitbox");
            headHitbox.transform.SetParent(instance.transform);
            headHitbox.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            var headSphere = headHitbox.AddComponent<SphereCollider>();
            headSphere.radius = 0.2f;
        }

        // Log all hierarchy nodes for debugging
        Debug.Log("=== HIERARCHY ===");
        foreach (var t in instance.GetComponentsInChildren<Transform>(true))
            Debug.Log($"  {t.name} (parent={( t.parent != null ? t.parent.name : "ROOT")})");
        Debug.Log("=== END HIERARCHY ===");

        // Find right hand bone for attaching a rifle
        Transform rightHand = FindBone(instance.transform, "RightHand");
        if (rightHand == null)
            rightHand = FindBone(instance.transform, "mixamorig:RightHand");
        if (rightHand == null)
            rightHand = FindBoneContaining(instance.transform, "righthand");
        if (rightHand == null)
            rightHand = FindBoneContaining(instance.transform, "hand_r");

        if (rightHand != null)
            Debug.Log("Found right hand bone: " + rightHand.name);
        else
            Debug.Log("Right hand bone NOT found — rifle will use fallback position");

        // Swat.fbx has no weapon mesh — build a primitive rifle and parent to hand bone
        var existingRifle = FindChildRecursive(instance.transform, "AssaultRifle");
        if (existingRifle != null)
            Object.DestroyImmediate(existingRifle.gameObject);

        Transform rifleParent = rightHand != null ? rightHand : instance.transform;
        GameObject rifle = BuildPrimitiveRifle(rifleParent, gunMat, rightHand != null);

        // Create muzzle point at the barrel tip of the rifle
        var existingMuzzle = FindChildRecursive(instance.transform, "EnemyMuzzlePoint");
        if (existingMuzzle != null)
            Object.DestroyImmediate(existingMuzzle.gameObject);

        GameObject muzzle = new GameObject("EnemyMuzzlePoint");
        Transform barrelTip = FindChildRecursive(rifle.transform, "BarrelTip");
        if (barrelTip != null)
        {
            muzzle.transform.SetParent(barrelTip);
            muzzle.transform.localPosition = Vector3.zero;
            Debug.Log("Muzzle at barrel tip of primitive rifle");
        }
        else
        {
            muzzle.transform.SetParent(rifle.transform);
            muzzle.transform.localPosition = new Vector3(0f, 0f, 0.45f);
            Debug.Log("Muzzle at rifle root offset");
        }

        // Add ModelOrientationFix — runs in LateUpdate after Animator to correct facing
        var orientFix = instance.GetComponent<ModelOrientationFix>();
        if (orientFix == null)
            orientFix = instance.AddComponent<ModelOrientationFix>();
        // Add Enemy component
        if (instance.GetComponent<Enemy>() == null)
            instance.AddComponent<Enemy>();

        // Save as prefab
        string prefabPath = "Assets/Resources/SoldierEnemy.prefab";
        if (File.Exists(prefabPath))
            AssetDatabase.DeleteAsset(prefabPath);
        PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        Object.DestroyImmediate(instance);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string summary = "Soldier Prefab Ready! (Mixamo/Pro Rifle Pack)\n\n";
        summary += "Model: " + Path.GetFileName(modelPath) + "\n";
        summary += "Animations:\n";
        summary += "  Idle: " + (idleClip != null ? idleClip.name : "MISSING") + "\n";
        summary += "  Walk: " + (walkClip != null ? walkClip.name : "MISSING") + "\n";
        summary += "  Fire: " + (fireClip != null ? fireClip.name : "MISSING") + "\n";
        summary += "  Die: " + (dieClip != null ? dieClip.name : "MISSING") + "\n";
        summary += "\nPrefab saved to: " + prefabPath;
        summary += "\nController saved to: " + controllerPath;
        summary += "\n\nPress Play to test!";

        Debug.Log(summary);
        EditorUtility.DisplayDialog("Setup Complete", summary, "OK");
    }

    static void ConfigureHumanoidRig(string fbxPath)
    {
        var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null) return;

        bool needsReimport = false;

        if (importer.animationType != ModelImporterAnimationType.Human)
        {
            Debug.Log("Configuring " + Path.GetFileName(fbxPath) + " as Humanoid rig...");
            importer.animationType = ModelImporterAnimationType.Human;
            needsReimport = true;
        }

        if (importer.bakeAxisConversion)
        {
            importer.bakeAxisConversion = false;
            needsReimport = true;
        }

        if (needsReimport)
            importer.SaveAndReimport();
    }

    static AnimationClip LoadClipFromFBX(string fbxPath, bool loop = true)
    {
        if (!File.Exists(fbxPath))
        {
            Debug.Log("Animation FBX not found: " + fbxPath);
            return null;
        }

        // Configure as Humanoid so clips can retarget
        ConfigureHumanoidRig(fbxPath);

        // Configure looping on the clip import settings.
        // Mixamo FBX files default to loopTime=false, so animations play once
        // and freeze on the last frame — causing visible sliding for walk cycles.
        var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer != null)
        {
            var clips = importer.clipAnimations;
            if (clips.Length == 0)
                clips = importer.defaultClipAnimations;

            bool needsReimport = false;
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
                Debug.Log($"Configured loopTime={loop} on {Path.GetFileName(fbxPath)}");
            }
        }

        var assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        foreach (var asset in assets)
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__"))
            {
                Debug.Log($"Loaded clip '{clip.name}' from {Path.GetFileName(fbxPath)}, loop={loop}");
                return clip;
            }
        }

        Debug.LogWarning("No animation clip found in: " + fbxPath);
        return null;
    }

    static Transform FindBone(Transform root, string exactName)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == exactName)
                return t;
        }
        return null;
    }

    static Transform FindBoneContaining(Transform root, string namePart)
    {
        string lower = namePart.ToLower();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name.ToLower().Contains(lower))
                return t;
        }
        return null;
    }

    static Transform FindChildRecursive(Transform parent, string name)
    {
        foreach (var t in parent.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name)
                return t;
        }
        return null;
    }

    static GameObject BuildPrimitiveRifle(Transform parent, Material gunMat, bool isHandBone)
    {
        GameObject rifle = new GameObject("AssaultRifle");
        rifle.transform.SetParent(parent);

        if (isHandBone)
        {
            // Rifle barrel extends along +Z in rifle local space.
            // Runtime hand bone data shows: hand +Z = forward, hand +Y = right, hand +X = down.
            // Euler(0,0,90) keeps barrel along hand +Z (forward) and rolls the rifle
            // so its +Y (receiver top) maps to hand -X (upward).
            rifle.transform.localPosition = new Vector3(0f, 0.08f, 0.02f);
            rifle.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Debug.Log($"RightHand bone world forward={parent.forward}, up={parent.up}");
        }
        else
        {
            // Fallback: approximate position in front of soldier chest
            rifle.transform.localPosition = new Vector3(0.2f, 1.1f, 0.3f);
            rifle.transform.localRotation = Quaternion.identity;
        }

        // Receiver (main body) — scaled up 1.5x for visibility
        var receiver = GameObject.CreatePrimitive(PrimitiveType.Cube);
        receiver.name = "RifleBody";
        receiver.transform.SetParent(rifle.transform);
        receiver.transform.localPosition = Vector3.zero;
        receiver.transform.localRotation = Quaternion.identity;
        receiver.transform.localScale = new Vector3(0.06f, 0.07f, 0.45f);
        Object.DestroyImmediate(receiver.GetComponent<Collider>());

        // Barrel
        var barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        barrel.name = "Barrel";
        barrel.transform.SetParent(rifle.transform);
        barrel.transform.localPosition = new Vector3(0f, 0.015f, 0.40f);
        barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        barrel.transform.localScale = new Vector3(0.025f, 0.20f, 0.025f);
        Object.DestroyImmediate(barrel.GetComponent<Collider>());

        // Barrel tip (muzzle anchor)
        var barrelTip = new GameObject("BarrelTip");
        barrelTip.transform.SetParent(rifle.transform);
        barrelTip.transform.localPosition = new Vector3(0f, 0.015f, 0.60f);

        // Stock
        var stock = GameObject.CreatePrimitive(PrimitiveType.Cube);
        stock.name = "Stock";
        stock.transform.SetParent(rifle.transform);
        stock.transform.localPosition = new Vector3(0f, -0.015f, -0.28f);
        stock.transform.localRotation = Quaternion.identity;
        stock.transform.localScale = new Vector3(0.04f, 0.08f, 0.25f);
        Object.DestroyImmediate(stock.GetComponent<Collider>());

        // Magazine
        var magazine = GameObject.CreatePrimitive(PrimitiveType.Cube);
        magazine.name = "Magazine";
        magazine.transform.SetParent(rifle.transform);
        magazine.transform.localPosition = new Vector3(0f, -0.08f, 0.03f);
        magazine.transform.localRotation = Quaternion.Euler(10f, 0f, 0f);
        magazine.transform.localScale = new Vector3(0.035f, 0.10f, 0.05f);
        Object.DestroyImmediate(magazine.GetComponent<Collider>());

        // Handguard
        var handguard = GameObject.CreatePrimitive(PrimitiveType.Cube);
        handguard.name = "Handguard";
        handguard.transform.SetParent(rifle.transform);
        handguard.transform.localPosition = new Vector3(0f, 0f, 0.22f);
        handguard.transform.localRotation = Quaternion.identity;
        handguard.transform.localScale = new Vector3(0.065f, 0.07f, 0.20f);
        Object.DestroyImmediate(handguard.GetComponent<Collider>());

        // Apply gun material to all rifle parts
        if (gunMat != null)
        {
            foreach (var renderer in rifle.GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterial = gunMat;
        }

        Debug.Log("Built primitive rifle, parented to: " + parent.name);
        return rifle;
    }
}

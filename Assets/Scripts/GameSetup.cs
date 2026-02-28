using UnityEngine;

public class GameSetup : MonoBehaviour
{
    public int enemyCount = 10;
    public float spawnRadius = 30f;
    public GameObject soldierPrefab;

    Material uniformMat;
    Material skinMat;
    Material helmetMat;
    Material bootMat;
    Material beltMat;
    Material groundMat;
    Material gunMetalMat;

    // Environment materials
    Material treeBarkMat;
    Material leafMat;
    Material leafLightMat;
    Material wallMat;
    Material roofMat;
    Material windowMat;
    Material doorMat;
    Material roadMat;
    Material curbMat;

    bool hasSoldierPrefab;

    // Wave scoring
    int waveNumber;
    int killCount;
    int waveKillCount;
    int waveSuperKillCount;
    float waveStartTime;

    // UI references
    Canvas uiCanvas;
    UnityEngine.UI.Image healthBarFill;
    UnityEngine.UI.Text healthText;
    UnityEngine.UI.Text hudText;
    UnityEngine.UI.Image damageOverlay;
    FPSController playerController;

    // Wave summary
    GameObject waveSummaryPanel;
    UnityEngine.UI.Text waveSummaryText;
    float waveSummaryTimer;

    // Game over
    GameObject gameOverPanel;
    UnityEngine.UI.Text gameOverText;

    void Start()
    {
        // Grab input focus immediately
        Application.runInBackground = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
#if UNITY_EDITOR
        // Force the Game view to take focus so keyboard works without clicking
        var gameViewType = System.Type.GetType("UnityEditor.GameView,UnityEditor");
        if (gameViewType != null)
            UnityEditor.EditorWindow.FocusWindowIfItsOpen(gameViewType);
#endif

        // Try to load soldier prefab
        if (soldierPrefab == null)
            soldierPrefab = Resources.Load<GameObject>("SoldierEnemy");

        // Also try finding soldier prefab in loaded assets
        if (soldierPrefab == null)
        {
            var allEnemies = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (var go in allEnemies)
            {
                if (go.name == "SoldierEnemy" && go.GetComponent<Animator>() != null)
                {
                    soldierPrefab = go;
                    break;
                }
            }
        }

        hasSoldierPrefab = soldierPrefab != null;
        if (hasSoldierPrefab)
            Debug.Log("Using soldier prefab");
        else
            Debug.Log("Soldier prefab not found - using primitive soldiers. Run Tools > Setup Soldier first.");

        CreateMaterials();
        BuildGround();
        BuildEnvironment();
        BuildPlayer();
        BuildCrosshair();
        BuildHealthUI();
        BuildHelmetOverlay();
        StartBackgroundMusic();

        waveNumber = 0;
        killCount = 0;
        SpawnWave();
    }

    void Update()
    {
        // Update health bar
        if (playerController != null && healthBarFill != null)
        {
            float ratio = playerController.health / playerController.maxHealth;
            healthBarFill.rectTransform.localScale = new Vector3(ratio, 1f, 1f);
            // Green to red gradient based on health
            healthBarFill.color = Color.Lerp(Color.red, Color.green, ratio);
            if (healthText != null)
                healthText.text = Mathf.CeilToInt(playerController.health).ToString();
        }

        // Update HUD
        if (hudText != null)
            hudText.text = $"Wave: {waveNumber}  |  Kills: {killCount}";

        // Game over state
        if (playerController != null && playerController.dead)
        {
            ShowGameOver();
            if (UnityEngine.InputSystem.Keyboard.current.rKey.wasPressedThisFrame)
                RestartGame();
            return;
        }

        // Wave summary countdown
        if (waveSummaryTimer > 0f)
        {
            waveSummaryTimer -= Time.deltaTime;
            if (waveSummaryTimer <= 0f)
            {
                if (waveSummaryPanel != null)
                    waveSummaryPanel.SetActive(false);
                SpawnWave();
            }
            return;
        }

        // Check if wave is complete (no living enemies, not already showing summary)
        int enemiesAlive = 0;
        foreach (var e in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
        {
            // Count enemies that aren't dead (the dead ones get destroyed after delay)
            if (e.health > 0f)
                enemiesAlive++;
        }
        if (enemiesAlive == 0 && waveNumber > 0)
            ShowWaveSummary();

        if (UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    public void OnEnemyKilled(bool superSoldier = false)
    {
        killCount++;
        waveKillCount++;
        if (superSoldier)
            waveSuperKillCount++;
    }

    void ShowWaveSummary()
    {
        if (waveSummaryPanel == null) return;

        float elapsed = Time.time - waveStartTime;
        string summary = $"Wave {waveNumber} Complete!\n\nEnemies Killed: {waveKillCount}";
        if (waveSuperKillCount > 0)
            summary += $"\nSuper Soldiers Killed: {waveSuperKillCount}";
        summary += $"\nTime: {elapsed:F1}s";
        waveSummaryText.text = summary;
        waveSummaryPanel.SetActive(true);
        waveSummaryTimer = 5f;
    }

    void ShowGameOver()
    {
        if (gameOverPanel != null && !gameOverPanel.activeSelf)
        {
            gameOverText.text = $"GAME OVER\n\nWaves Survived: {waveNumber}\nTotal Kills: {killCount}\n\nPress R to Restart";
            gameOverPanel.SetActive(true);
        }
    }

    void RestartGame()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
    }

    void BuildHealthUI()
    {
        // Reuse the existing canvas from BuildCrosshair
        uiCanvas = FindFirstObjectByType<Canvas>();
        if (uiCanvas == null) return;

        var canvasObj = uiCanvas.gameObject;

        // Add CanvasScaler if missing
        if (canvasObj.GetComponent<UnityEngine.UI.CanvasScaler>() == null)
        {
            var scaler = canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
        }

        // --- Damage overlay (fullscreen red flash) ---
        GameObject overlayObj = new GameObject("DamageOverlay");
        overlayObj.transform.SetParent(canvasObj.transform);
        var overlayRT = overlayObj.AddComponent<RectTransform>();
        overlayRT.anchorMin = Vector2.zero;
        overlayRT.anchorMax = Vector2.one;
        overlayRT.offsetMin = Vector2.zero;
        overlayRT.offsetMax = Vector2.zero;
        damageOverlay = overlayObj.AddComponent<UnityEngine.UI.Image>();
        damageOverlay.color = new Color(0.8f, 0f, 0f, 0f);
        damageOverlay.raycastTarget = false;

        // --- Health bar (bottom-left) ---
        // Background
        GameObject healthBg = new GameObject("HealthBarBG");
        healthBg.transform.SetParent(canvasObj.transform);
        var bgRT = healthBg.AddComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0f, 0f);
        bgRT.anchorMax = new Vector2(0f, 0f);
        bgRT.pivot = new Vector2(0f, 0f);
        bgRT.anchoredPosition = new Vector2(20f, 20f);
        bgRT.sizeDelta = new Vector2(200f, 20f);
        var bgImg = healthBg.AddComponent<UnityEngine.UI.Image>();
        bgImg.color = new Color(0.3f, 0f, 0f, 0.8f);

        // Fill
        GameObject healthFill = new GameObject("HealthBarFill");
        healthFill.transform.SetParent(healthBg.transform);
        var fillRT = healthFill.AddComponent<RectTransform>();
        fillRT.anchorMin = new Vector2(0f, 0f);
        fillRT.anchorMax = new Vector2(1f, 1f);
        fillRT.offsetMin = new Vector2(2f, 2f);
        fillRT.offsetMax = new Vector2(-2f, -2f);
        fillRT.pivot = new Vector2(0f, 0.5f);
        healthBarFill = healthFill.AddComponent<UnityEngine.UI.Image>();
        healthBarFill.color = Color.green;

        // Health text
        GameObject healthLabel = new GameObject("HealthText");
        healthLabel.transform.SetParent(healthBg.transform);
        var labelRT = healthLabel.AddComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;
        healthText = healthLabel.AddComponent<UnityEngine.UI.Text>();
        healthText.text = "100";
        healthText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (healthText.font == null)
            healthText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        healthText.fontSize = 14;
        healthText.alignment = TextAnchor.MiddleCenter;
        healthText.color = Color.white;

        // --- HUD text (top center) ---
        GameObject hudObj = new GameObject("HUDText");
        hudObj.transform.SetParent(canvasObj.transform);
        var hudRT = hudObj.AddComponent<RectTransform>();
        hudRT.anchorMin = new Vector2(0.5f, 1f);
        hudRT.anchorMax = new Vector2(0.5f, 1f);
        hudRT.pivot = new Vector2(0.5f, 1f);
        hudRT.anchoredPosition = new Vector2(0f, -10f);
        hudRT.sizeDelta = new Vector2(400f, 30f);
        hudText = hudObj.AddComponent<UnityEngine.UI.Text>();
        hudText.text = "Wave: 1  |  Kills: 0";
        hudText.font = healthText.font;
        hudText.fontSize = 20;
        hudText.alignment = TextAnchor.MiddleCenter;
        hudText.color = Color.white;

        // --- Wave summary panel (center, hidden by default) ---
        waveSummaryPanel = new GameObject("WaveSummary");
        waveSummaryPanel.transform.SetParent(canvasObj.transform);
        var wsRT = waveSummaryPanel.AddComponent<RectTransform>();
        wsRT.anchorMin = new Vector2(0.5f, 0.5f);
        wsRT.anchorMax = new Vector2(0.5f, 0.5f);
        wsRT.pivot = new Vector2(0.5f, 0.5f);
        wsRT.anchoredPosition = Vector2.zero;
        wsRT.sizeDelta = new Vector2(350f, 180f);
        var wsBg = waveSummaryPanel.AddComponent<UnityEngine.UI.Image>();
        wsBg.color = new Color(0f, 0f, 0f, 0.75f);

        GameObject wsTextObj = new GameObject("Text");
        wsTextObj.transform.SetParent(waveSummaryPanel.transform);
        var wsTextRT = wsTextObj.AddComponent<RectTransform>();
        wsTextRT.anchorMin = Vector2.zero;
        wsTextRT.anchorMax = Vector2.one;
        wsTextRT.offsetMin = new Vector2(10f, 10f);
        wsTextRT.offsetMax = new Vector2(-10f, -10f);
        waveSummaryText = wsTextObj.AddComponent<UnityEngine.UI.Text>();
        waveSummaryText.font = healthText.font;
        waveSummaryText.fontSize = 24;
        waveSummaryText.alignment = TextAnchor.MiddleCenter;
        waveSummaryText.color = Color.white;
        waveSummaryPanel.SetActive(false);

        // --- Game over panel (center, hidden by default) ---
        gameOverPanel = new GameObject("GameOver");
        gameOverPanel.transform.SetParent(canvasObj.transform);
        var goRT = gameOverPanel.AddComponent<RectTransform>();
        goRT.anchorMin = Vector2.zero;
        goRT.anchorMax = Vector2.one;
        goRT.offsetMin = Vector2.zero;
        goRT.offsetMax = Vector2.zero;
        var goBg = gameOverPanel.AddComponent<UnityEngine.UI.Image>();
        goBg.color = new Color(0.15f, 0f, 0f, 0.8f);

        GameObject goTextObj = new GameObject("Text");
        goTextObj.transform.SetParent(gameOverPanel.transform);
        var goTextRT = goTextObj.AddComponent<RectTransform>();
        goTextRT.anchorMin = Vector2.zero;
        goTextRT.anchorMax = Vector2.one;
        goTextRT.offsetMin = Vector2.zero;
        goTextRT.offsetMax = Vector2.zero;
        gameOverText = goTextObj.AddComponent<UnityEngine.UI.Text>();
        gameOverText.font = healthText.font;
        gameOverText.fontSize = 32;
        gameOverText.alignment = TextAnchor.MiddleCenter;
        gameOverText.color = Color.white;
        gameOverPanel.SetActive(false);

        // Wire up damage overlay to player (player is built before this)
        playerController = FindFirstObjectByType<FPSController>();
        if (playerController != null)
            playerController.SetDamageOverlay(damageOverlay);
    }

    void BuildHelmetOverlay()
    {
        // Simple vignette overlay - dark soft edges, subtle green tint
        GameObject helmetCanvasObj = new GameObject("HelmetOverlayCanvas");
        var helmetCanvas = helmetCanvasObj.AddComponent<Canvas>();
        helmetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        helmetCanvas.sortingOrder = 90;

        // Subtle green visor tint
        GameObject tint = new GameObject("VisorTint");
        tint.transform.SetParent(helmetCanvasObj.transform);
        var tintRT = tint.AddComponent<RectTransform>();
        tintRT.anchorMin = Vector2.zero;
        tintRT.anchorMax = Vector2.one;
        tintRT.offsetMin = Vector2.zero;
        tintRT.offsetMax = Vector2.zero;
        var tintImg = tint.AddComponent<UnityEngine.UI.Image>();
        tintImg.color = new Color(0.05f, 0.15f, 0.05f, 0.06f);
        tintImg.raycastTarget = false;

        // Vignette - soft dark edges using a procedural texture
        int texSize = 256;
        Texture2D vignetteTex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
        float c = texSize / 2f;
        for (int y = 0; y < texSize; y++)
        {
            for (int x = 0; x < texSize; x++)
            {
                float dx = (x - c) / c;
                float dy = (y - c) / c;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.SmoothStep(0f, 0.7f, (dist - 0.6f) / 0.7f);
                vignetteTex.SetPixel(x, y, new Color(0f, 0f, 0f, alpha));
            }
        }
        vignetteTex.Apply();
        vignetteTex.filterMode = FilterMode.Bilinear;

        Sprite vignetteSprite = Sprite.Create(vignetteTex,
            new Rect(0, 0, texSize, texSize), new Vector2(0.5f, 0.5f));

        GameObject vignette = new GameObject("Vignette");
        vignette.transform.SetParent(helmetCanvasObj.transform);
        var vRT = vignette.AddComponent<RectTransform>();
        vRT.anchorMin = Vector2.zero;
        vRT.anchorMax = Vector2.one;
        vRT.offsetMin = Vector2.zero;
        vRT.offsetMax = Vector2.zero;
        var vImg = vignette.AddComponent<UnityEngine.UI.Image>();
        vImg.sprite = vignetteSprite;
        vImg.type = UnityEngine.UI.Image.Type.Simple;
        vImg.preserveAspect = false;
        vImg.raycastTarget = false;
    }

    void CreateMaterials()
    {
        string lit = "Universal Render Pipeline/Lit";

        // Bright desert sand uniform - high contrast against green environment
        uniformMat = new Material(Shader.Find(lit));
        uniformMat.SetColor("_BaseColor", new Color(0.82f, 0.72f, 0.52f));
        uniformMat.SetFloat("_Smoothness", 0.15f);
        uniformMat.SetFloat("_Metallic", 0f);

        // Bright skin tone
        skinMat = new Material(Shader.Find(lit));
        skinMat.SetColor("_BaseColor", new Color(0.9f, 0.75f, 0.6f));
        skinMat.SetFloat("_Smoothness", 0.35f);
        skinMat.SetFloat("_Metallic", 0f);

        // Bright sand helmet
        helmetMat = new Material(Shader.Find(lit));
        helmetMat.SetColor("_BaseColor", new Color(0.75f, 0.67f, 0.48f));
        helmetMat.SetFloat("_Smoothness", 0.3f);
        helmetMat.SetFloat("_Metallic", 0f);

        // Lighter tan boots
        bootMat = new Material(Shader.Find(lit));
        bootMat.SetColor("_BaseColor", new Color(0.55f, 0.42f, 0.28f));
        bootMat.SetFloat("_Smoothness", 0.4f);
        bootMat.SetFloat("_Metallic", 0f);

        // Bright canvas belt/webbing
        beltMat = new Material(Shader.Find(lit));
        beltMat.SetColor("_BaseColor", new Color(0.7f, 0.6f, 0.42f));
        beltMat.SetFloat("_Smoothness", 0.2f);
        beltMat.SetFloat("_Metallic", 0f);

        // Ground - earthy green
        groundMat = new Material(Shader.Find(lit));
        groundMat.SetColor("_BaseColor", new Color(0.3f, 0.4f, 0.25f));
        groundMat.SetFloat("_Smoothness", 0.1f);
        groundMat.SetFloat("_Metallic", 0f);

        // Gun metal - dark metallic
        gunMetalMat = new Material(Shader.Find(lit));
        gunMetalMat.SetColor("_BaseColor", new Color(0.10f, 0.10f, 0.12f));
        gunMetalMat.SetFloat("_Smoothness", 0.6f);
        gunMetalMat.SetFloat("_Metallic", 0.7f);

        // Tree bark - brown
        treeBarkMat = new Material(Shader.Find(lit));
        treeBarkMat.SetColor("_BaseColor", new Color(0.35f, 0.22f, 0.10f));
        treeBarkMat.SetFloat("_Smoothness", 0.2f);

        // Leaf - dark green foliage
        leafMat = new Material(Shader.Find(lit));
        leafMat.SetColor("_BaseColor", new Color(0.15f, 0.4f, 0.1f));
        leafMat.SetFloat("_Smoothness", 0.15f);

        // Leaf light - lighter green foliage
        leafLightMat = new Material(Shader.Find(lit));
        leafLightMat.SetColor("_BaseColor", new Color(0.25f, 0.5f, 0.15f));
        leafLightMat.SetFloat("_Smoothness", 0.15f);

        // Wall - stucco/plaster
        wallMat = new Material(Shader.Find(lit));
        wallMat.SetColor("_BaseColor", new Color(0.75f, 0.7f, 0.6f));
        wallMat.SetFloat("_Smoothness", 0.2f);

        // Roof - dark brown/red tiles
        roofMat = new Material(Shader.Find(lit));
        roofMat.SetColor("_BaseColor", new Color(0.35f, 0.15f, 0.1f));
        roofMat.SetFloat("_Smoothness", 0.25f);

        // Window - dark glass
        windowMat = new Material(Shader.Find(lit));
        windowMat.SetColor("_BaseColor", new Color(0.2f, 0.25f, 0.35f));
        windowMat.SetFloat("_Smoothness", 0.7f);

        // Door - wooden door
        doorMat = new Material(Shader.Find(lit));
        doorMat.SetColor("_BaseColor", new Color(0.3f, 0.18f, 0.08f));
        doorMat.SetFloat("_Smoothness", 0.3f);

        // Road - dark asphalt
        roadMat = new Material(Shader.Find(lit));
        roadMat.SetColor("_BaseColor", new Color(0.2f, 0.2f, 0.2f));
        roadMat.SetFloat("_Smoothness", 0.3f);

        // Curb - light concrete
        curbMat = new Material(Shader.Find(lit));
        curbMat.SetColor("_BaseColor", new Color(0.6f, 0.6f, 0.55f));
        curbMat.SetFloat("_Smoothness", 0.2f);
    }

    void BuildGround()
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.position = Vector3.zero;
        ground.transform.localScale = new Vector3(10f, 1f, 10f);
        ground.GetComponent<MeshRenderer>().material = groundMat;
    }

    void BuildEnvironment()
    {
        GameObject envParent = new GameObject("Environment");

        // --- Roads ---
        BuildRoads(envParent.transform);

        // --- Trees ---
        // Combat zone edges (sparse, partial cover)
        BuildTree(new Vector3(-12f, 0f, 22f), 1.0f, envParent.transform);
        BuildTree(new Vector3(14f, 0f, 25f), 0.9f, envParent.transform);
        BuildTree(new Vector3(-18f, 0f, 30f), 1.1f, envParent.transform);
        BuildTree(new Vector3(20f, 0f, 18f), 0.8f, envParent.transform);
        BuildTree(new Vector3(-8f, 0f, 35f), 1.2f, envParent.transform);

        // Periphery left side
        BuildTree(new Vector3(-35f, 0f, 10f), 1.3f, envParent.transform);
        BuildTree(new Vector3(-40f, 0f, 20f), 1.0f, envParent.transform);
        BuildTree(new Vector3(-38f, 0f, -5f), 0.9f, envParent.transform);
        BuildTree(new Vector3(-42f, 0f, -20f), 1.1f, envParent.transform);

        // Periphery right side
        BuildTree(new Vector3(35f, 0f, 5f), 1.2f, envParent.transform);
        BuildTree(new Vector3(38f, 0f, -10f), 0.8f, envParent.transform);
        BuildTree(new Vector3(40f, 0f, 25f), 1.0f, envParent.transform);

        // Behind player
        BuildTree(new Vector3(-10f, 0f, -20f), 1.1f, envParent.transform);
        BuildTree(new Vector3(8f, 0f, -25f), 0.9f, envParent.transform);
        BuildTree(new Vector3(-5f, 0f, -35f), 1.3f, envParent.transform);
        BuildTree(new Vector3(15f, 0f, -30f), 1.0f, envParent.transform);

        // Far ahead flanks
        BuildTree(new Vector3(-25f, 0f, 40f), 1.0f, envParent.transform);
        BuildTree(new Vector3(28f, 0f, 42f), 0.7f, envParent.transform);

        // --- Bushes ---
        // Near trees in combat zone
        BuildBush(new Vector3(-10f, 0f, 20f), 0.7f, envParent.transform);
        BuildBush(new Vector3(-14f, 0f, 23f), 0.5f, envParent.transform);
        BuildBush(new Vector3(12f, 0f, 24f), 0.6f, envParent.transform);
        BuildBush(new Vector3(16f, 0f, 27f), 0.8f, envParent.transform);
        BuildBush(new Vector3(-6f, 0f, 28f), 0.5f, envParent.transform);
        BuildBush(new Vector3(8f, 0f, 32f), 0.7f, envParent.transform);

        // Scattered mid-field
        BuildBush(new Vector3(-20f, 0f, 12f), 0.6f, envParent.transform);
        BuildBush(new Vector3(22f, 0f, 10f), 0.5f, envParent.transform);
        BuildBush(new Vector3(-3f, 0f, 15f), 0.4f, envParent.transform);
        BuildBush(new Vector3(5f, 0f, 18f), 0.6f, envParent.transform);

        // Near houses
        BuildBush(new Vector3(-38f, 0f, 32f), 0.7f, envParent.transform);
        BuildBush(new Vector3(-36f, 0f, 35f), 0.5f, envParent.transform);
        BuildBush(new Vector3(36f, 0f, 38f), 0.6f, envParent.transform);
        BuildBush(new Vector3(-42f, 0f, -22f), 0.8f, envParent.transform);
        BuildBush(new Vector3(37f, 0f, -12f), 0.5f, envParent.transform);

        // Behind player
        BuildBush(new Vector3(-8f, 0f, -18f), 0.6f, envParent.transform);
        BuildBush(new Vector3(6f, 0f, -22f), 0.5f, envParent.transform);
        BuildBush(new Vector3(-12f, 0f, -30f), 0.7f, envParent.transform);
        BuildBush(new Vector3(10f, 0f, -28f), 0.4f, envParent.transform);

        // Along roads
        BuildBush(new Vector3(18f, 0f, 16f), 0.5f, envParent.transform);
        BuildBush(new Vector3(13f, 0f, 8f), 0.6f, envParent.transform);
        BuildBush(new Vector3(-22f, 0f, 16f), 0.5f, envParent.transform);
        BuildBush(new Vector3(17f, 0f, -5f), 0.4f, envParent.transform);

        // Extra periphery clusters
        BuildBush(new Vector3(-30f, 0f, -15f), 0.7f, envParent.transform);
        BuildBush(new Vector3(32f, 0f, 15f), 0.6f, envParent.transform);

        // --- Houses ---
        // Left side periphery
        BuildHouse(new Vector3(-40f, 0f, 35f), 30f, envParent.transform);
        BuildHouse(new Vector3(-42f, 0f, -25f), -15f, envParent.transform);

        // Right side periphery
        BuildHouse(new Vector3(38f, 0f, 38f), -45f, envParent.transform);
        BuildHouse(new Vector3(40f, 0f, -15f), 10f, envParent.transform);

        // Far ahead flanks (outside enemy spawn)
        BuildHouse(new Vector3(-30f, 0f, 45f), 0f, envParent.transform);

        // Behind player
        BuildHouse(new Vector3(-8f, 0f, -40f), 180f, envParent.transform);
    }

    void BuildTree(Vector3 pos, float scale, Transform parent)
    {
        GameObject tree = new GameObject("Tree");
        tree.transform.SetParent(parent);
        tree.transform.position = pos;
        tree.transform.localScale = Vector3.one * scale;

        // Trunk - cylinder
        GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        trunk.name = "Trunk";
        trunk.transform.SetParent(tree.transform);
        trunk.transform.localPosition = new Vector3(0f, 1.75f, 0f);
        trunk.transform.localScale = new Vector3(0.3f, 1.75f, 0.3f);
        trunk.GetComponent<MeshRenderer>().material = treeBarkMat;
        // Keep collider on trunk for bullet/player collision

        // Canopy - overlapping spheres
        float canopyBase = 3.0f;
        Vector3[] canopyOffsets = {
            new Vector3(0f, canopyBase + 0.5f, 0f),
            new Vector3(0.4f, canopyBase + 0.2f, 0.3f),
            new Vector3(-0.5f, canopyBase + 0.1f, -0.2f),
            new Vector3(0.2f, canopyBase + 0.9f, -0.3f),
            new Vector3(-0.3f, canopyBase + 0.7f, 0.4f),
        };
        float[] canopyRadii = { 1.8f, 1.4f, 1.5f, 1.2f, 1.3f };
        Material[] canopyMats = { leafMat, leafLightMat, leafMat, leafLightMat, leafMat };

        for (int i = 0; i < canopyOffsets.Length; i++)
        {
            GameObject leaf = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            leaf.name = "Canopy" + i;
            leaf.transform.SetParent(tree.transform);
            leaf.transform.localPosition = canopyOffsets[i];
            leaf.transform.localScale = Vector3.one * canopyRadii[i];
            Object.Destroy(leaf.GetComponent<Collider>());
            leaf.GetComponent<MeshRenderer>().material = canopyMats[i];
        }
    }

    void BuildBush(Vector3 pos, float scale, Transform parent)
    {
        GameObject bush = new GameObject("Bush");
        bush.transform.SetParent(parent);
        bush.transform.position = pos;

        Vector3[] offsets = {
            new Vector3(0f, 0.25f, 0f),
            new Vector3(0.2f, 0.2f, 0.15f),
            new Vector3(-0.15f, 0.3f, -0.1f),
            new Vector3(0.1f, 0.15f, -0.2f),
        };
        float[] radii = { 0.6f, 0.45f, 0.5f, 0.4f };
        Material[] mats = { leafMat, leafLightMat, leafMat, leafLightMat };

        for (int i = 0; i < offsets.Length; i++)
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "BushPart" + i;
            sphere.transform.SetParent(bush.transform);
            sphere.transform.localPosition = offsets[i] * scale;
            sphere.transform.localScale = Vector3.one * radii[i] * scale;
            Object.Destroy(sphere.GetComponent<Collider>());
            sphere.GetComponent<MeshRenderer>().material = mats[i];
        }
    }

    void BuildHouse(Vector3 pos, float yRot, Transform parent)
    {
        GameObject house = new GameObject("House");
        house.transform.SetParent(parent);
        house.transform.position = pos;
        house.transform.rotation = Quaternion.Euler(0f, yRot, 0f);

        float w = 6f, h = 4f, d = 5f;

        // Main wall box (single cube for collision)
        GameObject walls = GameObject.CreatePrimitive(PrimitiveType.Cube);
        walls.name = "Walls";
        walls.transform.SetParent(house.transform);
        walls.transform.localPosition = new Vector3(0f, h / 2f, 0f);
        walls.transform.localScale = new Vector3(w, h, d);
        walls.GetComponent<MeshRenderer>().material = wallMat;
        // Keep collider for physics

        // Roof - two angled planes forming A-frame
        float roofWidth = w * 0.6f;
        float roofLen = d + 0.4f;
        float roofThick = 0.15f;
        float roofPeak = h + 1.5f;
        float roofMid = h + 0.75f;

        GameObject roofLeft = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roofLeft.name = "RoofLeft";
        roofLeft.transform.SetParent(house.transform);
        roofLeft.transform.localPosition = new Vector3(-w / 4f, roofMid, 0f);
        roofLeft.transform.localRotation = Quaternion.Euler(0f, 0f, 30f);
        roofLeft.transform.localScale = new Vector3(roofWidth, roofThick, roofLen);
        Object.Destroy(roofLeft.GetComponent<Collider>());
        roofLeft.GetComponent<MeshRenderer>().material = roofMat;

        GameObject roofRight = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roofRight.name = "RoofRight";
        roofRight.transform.SetParent(house.transform);
        roofRight.transform.localPosition = new Vector3(w / 4f, roofMid, 0f);
        roofRight.transform.localRotation = Quaternion.Euler(0f, 0f, -30f);
        roofRight.transform.localScale = new Vector3(roofWidth, roofThick, roofLen);
        Object.Destroy(roofRight.GetComponent<Collider>());
        roofRight.GetComponent<MeshRenderer>().material = roofMat;

        // Door - front face (local +Z)
        GameObject door = GameObject.CreatePrimitive(PrimitiveType.Cube);
        door.name = "Door";
        door.transform.SetParent(house.transform);
        door.transform.localPosition = new Vector3(0f, 1.1f, d / 2f + 0.02f);
        door.transform.localScale = new Vector3(0.9f, 2.2f, 0.08f);
        Object.Destroy(door.GetComponent<Collider>());
        door.GetComponent<MeshRenderer>().material = doorMat;

        // Windows - front face
        GameObject winFL = GameObject.CreatePrimitive(PrimitiveType.Cube);
        winFL.name = "WindowFL";
        winFL.transform.SetParent(house.transform);
        winFL.transform.localPosition = new Vector3(-1.8f, 2.5f, d / 2f + 0.02f);
        winFL.transform.localScale = new Vector3(0.8f, 0.8f, 0.06f);
        Object.Destroy(winFL.GetComponent<Collider>());
        winFL.GetComponent<MeshRenderer>().material = windowMat;

        GameObject winFR = GameObject.CreatePrimitive(PrimitiveType.Cube);
        winFR.name = "WindowFR";
        winFR.transform.SetParent(house.transform);
        winFR.transform.localPosition = new Vector3(1.8f, 2.5f, d / 2f + 0.02f);
        winFR.transform.localScale = new Vector3(0.8f, 0.8f, 0.06f);
        Object.Destroy(winFR.GetComponent<Collider>());
        winFR.GetComponent<MeshRenderer>().material = windowMat;

        // Windows - left side (-X)
        GameObject winL1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        winL1.name = "WindowL1";
        winL1.transform.SetParent(house.transform);
        winL1.transform.localPosition = new Vector3(-w / 2f - 0.02f, 2.5f, 1f);
        winL1.transform.localScale = new Vector3(0.06f, 0.8f, 0.8f);
        Object.Destroy(winL1.GetComponent<Collider>());
        winL1.GetComponent<MeshRenderer>().material = windowMat;

        GameObject winL2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        winL2.name = "WindowL2";
        winL2.transform.SetParent(house.transform);
        winL2.transform.localPosition = new Vector3(-w / 2f - 0.02f, 2.5f, -1f);
        winL2.transform.localScale = new Vector3(0.06f, 0.8f, 0.8f);
        Object.Destroy(winL2.GetComponent<Collider>());
        winL2.GetComponent<MeshRenderer>().material = windowMat;

        // Windows - right side (+X)
        GameObject winR1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        winR1.name = "WindowR1";
        winR1.transform.SetParent(house.transform);
        winR1.transform.localPosition = new Vector3(w / 2f + 0.02f, 2.5f, 1f);
        winR1.transform.localScale = new Vector3(0.06f, 0.8f, 0.8f);
        Object.Destroy(winR1.GetComponent<Collider>());
        winR1.GetComponent<MeshRenderer>().material = windowMat;

        GameObject winR2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        winR2.name = "WindowR2";
        winR2.transform.SetParent(house.transform);
        winR2.transform.localPosition = new Vector3(w / 2f + 0.02f, 2.5f, -1f);
        winR2.transform.localScale = new Vector3(0.06f, 0.8f, 0.8f);
        Object.Destroy(winR2.GetComponent<Collider>());
        winR2.GetComponent<MeshRenderer>().material = windowMat;

        // Chimney
        GameObject chimney = GameObject.CreatePrimitive(PrimitiveType.Cube);
        chimney.name = "Chimney";
        chimney.transform.SetParent(house.transform);
        chimney.transform.localPosition = new Vector3(w / 4f, roofPeak + 0.3f, -d / 4f);
        chimney.transform.localScale = new Vector3(0.5f, 1.2f, 0.5f);
        Object.Destroy(chimney.GetComponent<Collider>());
        chimney.GetComponent<MeshRenderer>().material = roofMat;
    }

    void BuildRoads(Transform parent)
    {
        float roadThick = 0.1f;
        float roadWidth = 4f;
        float roadLen = 100f;

        // Main road along X-axis at Z~15
        GameObject roadX = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roadX.name = "RoadX";
        roadX.transform.SetParent(parent);
        roadX.transform.position = new Vector3(0f, roadThick / 2f, 15f);
        roadX.transform.localScale = new Vector3(roadLen, roadThick, roadWidth);
        Object.Destroy(roadX.GetComponent<Collider>());
        roadX.GetComponent<MeshRenderer>().material = roadMat;

        // Curbs for X road
        for (int side = -1; side <= 1; side += 2)
        {
            GameObject curb = GameObject.CreatePrimitive(PrimitiveType.Cube);
            curb.name = "CurbX" + (side < 0 ? "S" : "N");
            curb.transform.SetParent(parent);
            curb.transform.position = new Vector3(0f, roadThick / 2f + 0.03f, 15f + side * (roadWidth / 2f + 0.15f));
            curb.transform.localScale = new Vector3(roadLen, roadThick + 0.06f, 0.25f);
            Object.Destroy(curb.GetComponent<Collider>());
            curb.GetComponent<MeshRenderer>().material = curbMat;
        }

        // Cross road along Z-axis at X~15
        GameObject roadZ = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roadZ.name = "RoadZ";
        roadZ.transform.SetParent(parent);
        roadZ.transform.position = new Vector3(15f, roadThick / 2f, 0f);
        roadZ.transform.localScale = new Vector3(roadWidth, roadThick, roadLen);
        Object.Destroy(roadZ.GetComponent<Collider>());
        roadZ.GetComponent<MeshRenderer>().material = roadMat;

        // Curbs for Z road
        for (int side = -1; side <= 1; side += 2)
        {
            GameObject curb = GameObject.CreatePrimitive(PrimitiveType.Cube);
            curb.name = "CurbZ" + (side < 0 ? "W" : "E");
            curb.transform.SetParent(parent);
            curb.transform.position = new Vector3(15f + side * (roadWidth / 2f + 0.15f), roadThick / 2f + 0.03f, 0f);
            curb.transform.localScale = new Vector3(0.25f, roadThick + 0.06f, roadLen);
            Object.Destroy(curb.GetComponent<Collider>());
            curb.GetComponent<MeshRenderer>().material = curbMat;
        }
    }

    void BuildPlayer()
    {
        GameObject player = new GameObject("Player");
        player.transform.position = new Vector3(0f, 1.5f, 0f);

        var cc = player.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.4f;
        cc.center = new Vector3(0f, -0.5f, 0f);

        player.AddComponent<AudioSource>();

        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.transform.SetParent(player.transform);
            cam.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            cam.transform.localRotation = Quaternion.identity;
            cam.nearClipPlane = 0.1f;
        }

        BuildAssaultRifle(cam.transform);
        player.AddComponent<FPSController>();
    }

    void BuildAssaultRifle(Transform parent)
    {
        string unlit = "Universal Render Pipeline/Unlit";
        var darkMetal = new Material(Shader.Find(unlit));
        darkMetal.color = new Color(0.1f, 0.1f, 0.12f);
        var lightMetal = new Material(Shader.Find(unlit));
        lightMetal.color = new Color(0.18f, 0.18f, 0.2f);
        var wood = new Material(Shader.Find(unlit));
        wood.color = new Color(0.25f, 0.15f, 0.08f);

        GameObject gun = new GameObject("AssaultRifle");
        gun.transform.SetParent(parent);
        gun.transform.localPosition = new Vector3(0.28f, -0.22f, 0.4f);
        gun.transform.localRotation = Quaternion.identity;

        // Receiver (main body)
        var receiver = CreatePart("Receiver", gun.transform,
            new Vector3(0f, 0f, 0.05f), Vector3.zero,
            new Vector3(0.05f, 0.06f, 0.28f), darkMetal);

        // Barrel
        CreatePart("Barrel", gun.transform,
            new Vector3(0f, 0.005f, 0.32f), Vector3.zero,
            new Vector3(0.025f, 0.025f, 0.30f), lightMetal);

        // Barrel shroud / handguard
        CreatePart("Handguard", gun.transform,
            new Vector3(0f, -0.005f, 0.24f), Vector3.zero,
            new Vector3(0.04f, 0.045f, 0.16f), darkMetal);

        // Flash hider at barrel tip
        CreatePart("FlashHider", gun.transform,
            new Vector3(0f, 0.005f, 0.48f), Vector3.zero,
            new Vector3(0.03f, 0.03f, 0.04f), lightMetal);

        // Stock
        CreatePart("Stock", gun.transform,
            new Vector3(0f, -0.01f, -0.18f), Vector3.zero,
            new Vector3(0.04f, 0.07f, 0.20f), darkMetal);

        // Stock butt pad
        CreatePart("ButtPad", gun.transform,
            new Vector3(0f, -0.01f, -0.29f), Vector3.zero,
            new Vector3(0.04f, 0.08f, 0.03f), new Material(Shader.Find(unlit)) { color = new Color(0.05f, 0.05f, 0.05f) });

        // Pistol grip
        CreatePart("Grip", gun.transform,
            new Vector3(0f, -0.07f, -0.02f), new Vector3(15f, 0f, 0f),
            new Vector3(0.03f, 0.08f, 0.035f), darkMetal);

        // Magazine
        CreatePart("Magazine", gun.transform,
            new Vector3(0f, -0.09f, 0.08f), new Vector3(10f, 0f, 0f),
            new Vector3(0.03f, 0.10f, 0.05f), darkMetal);

        // Top rail / carry handle
        CreatePart("Rail", gun.transform,
            new Vector3(0f, 0.04f, 0.05f), Vector3.zero,
            new Vector3(0.02f, 0.015f, 0.22f), lightMetal);

        // Front sight
        CreatePart("FrontSight", gun.transform,
            new Vector3(0f, 0.05f, 0.20f), Vector3.zero,
            new Vector3(0.008f, 0.025f, 0.008f), darkMetal);

        // Rear sight
        CreatePart("RearSight", gun.transform,
            new Vector3(0f, 0.05f, -0.02f), Vector3.zero,
            new Vector3(0.015f, 0.02f, 0.01f), darkMetal);

        // Muzzle point (invisible, used for tracer origin)
        GameObject muzzle = new GameObject("MuzzlePoint");
        muzzle.transform.SetParent(gun.transform);
        muzzle.transform.localPosition = new Vector3(0f, 0.005f, 0.50f);
    }

    GameObject CreatePart(string name, Transform parent, Vector3 localPos, Vector3 localEuler, Vector3 localScale, Material mat)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        part.transform.SetParent(parent);
        part.transform.localPosition = localPos;
        part.transform.localRotation = Quaternion.Euler(localEuler);
        part.transform.localScale = localScale;
        Object.Destroy(part.GetComponent<Collider>());
        part.GetComponent<MeshRenderer>().material = mat;
        return part;
    }

    void BuildCrosshair()
    {
        GameObject canvasObj = new GameObject("CrosshairCanvas");
        var canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        // Generate circle ring texture for bullseye
        int texSize = 128;
        Texture2D circleTex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false);
        float c = texSize / 2f;
        for (int y = 0; y < texSize; y++)
        {
            for (int x = 0; x < texSize; x++)
            {
                float dist = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                float alpha = 0f;
                // Outer ring (radius 50-54)
                if (dist >= 48f && dist <= 54f)
                    alpha = 1f;
                // Inner ring (radius 20-23)
                else if (dist >= 19f && dist <= 23f)
                    alpha = 0.9f;
                // Center dot (radius 0-4)
                else if (dist <= 4f)
                    alpha = 1f;
                circleTex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        circleTex.Apply();
        circleTex.filterMode = FilterMode.Bilinear;

        Sprite circleSprite = Sprite.Create(circleTex,
            new Rect(0, 0, texSize, texSize), new Vector2(0.5f, 0.5f));

        // Bullseye image
        GameObject bullseye = new GameObject("Bullseye");
        bullseye.transform.SetParent(canvasObj.transform);
        var bRT = bullseye.AddComponent<RectTransform>();
        bRT.anchorMin = new Vector2(0.5f, 0.5f);
        bRT.anchorMax = new Vector2(0.5f, 0.5f);
        bRT.pivot = new Vector2(0.5f, 0.5f);
        bRT.anchoredPosition = Vector2.zero;
        bRT.sizeDelta = new Vector2(64f, 64f);
        var bImg = bullseye.AddComponent<UnityEngine.UI.Image>();
        bImg.sprite = circleSprite;
        bImg.raycastTarget = false;
        bImg.color = new Color(1f, 1f, 1f, 0.85f);

        // Cross lines extending from outer ring
        CreateCrosshairLine(canvasObj.transform, 50f, 2f, new Vector2(0f, 0f));   // horizontal
        CreateCrosshairLine(canvasObj.transform, 2f, 50f, new Vector2(0f, 0f));   // vertical
    }

    void CreateCrosshairLine(Transform parent, float width, float height, Vector2 offset)
    {
        GameObject line = new GameObject("Line");
        line.transform.SetParent(parent);
        var rt = line.AddComponent<RectTransform>();
        rt.anchoredPosition = offset;
        rt.sizeDelta = new Vector2(width, height);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);

        var img = line.AddComponent<UnityEngine.UI.Image>();
        img.color = new Color(1f, 1f, 1f, 0.7f);
        img.raycastTarget = false;
    }

    void StartBackgroundMusic()
    {
        GameObject musicObj = new GameObject("BackgroundMusic");
        var musicSource = musicObj.AddComponent<AudioSource>();
        musicSource.clip = GenerateBackgroundMusic();
        musicSource.loop = true;
        musicSource.volume = 0.35f;
        musicSource.Play();
    }

    AudioClip GenerateBackgroundMusic()
    {
        int sampleRate = 44100;
        int lengthSeconds = 32;
        int samples = sampleRate * lengthSeconds;
        float[] data = new float[samples];

        System.Random rng = new System.Random(777);

        // Notes shifted up to ranges MacBook speakers can reproduce
        // Dark C minor: C3, Eb3, F3, G3, Ab3, C4
        float C3 = 130.81f;
        float Eb3 = 155.56f;
        float F3 = 174.61f;
        float G3 = 196.00f;
        float Ab3 = 207.65f;
        float C4 = 261.63f;
        float Eb4 = 311.13f;
        float G4 = 392.00f;

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / sampleRate;
            float sample = 0f;

            // Layer 1: Bass drone (C3) with harmonics - the foundation
            {
                float bassEnv = 0.4f + 0.1f * Mathf.Sin(2f * Mathf.PI * 0.05f * t);
                float bass = Mathf.Sin(2f * Mathf.PI * C3 * t) * 0.45f;
                float harmSweep = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 0.03f * t);
                bass += Mathf.Sin(2f * Mathf.PI * C3 * 2f * t) * 0.25f * harmSweep;
                bass += Mathf.Sin(2f * Mathf.PI * C3 * 3f * t) * 0.15f * harmSweep;
                bass += Mathf.Sin(2f * Mathf.PI * C3 * 4f * t) * 0.08f;
                sample += bass * bassEnv;
            }

            // Layer 2: Fifth (G3) - power chord feel
            {
                float fifthEnv = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 0.04f * t - 1f);
                fifthEnv = Mathf.Max(0f, fifthEnv);
                float fifth = Mathf.Sin(2f * Mathf.PI * G3 * t) * 0.2f;
                fifth += Mathf.Sin(2f * Mathf.PI * G3 * 2f * t) * 0.1f;
                sample += fifth * fifthEnv;
            }

            // Layer 3: Minor third pad (Eb4) - dark tension
            {
                float padEnv = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 0.07f * t + 2f);
                padEnv = Mathf.Max(0f, padEnv) * 0.15f;
                float pad = Mathf.Sin(2f * Mathf.PI * Eb4 * t);
                pad += Mathf.Sin(2f * Mathf.PI * (Eb4 * 1.003f) * t); // detune
                pad *= 0.5f;
                sample += pad * padEnv;
            }

            // Layer 4: High melody note (G4) - eerie intermittent
            {
                float highPhase = Mathf.Sin(2f * Mathf.PI * 0.025f * t);
                float highEnv = Mathf.Max(0f, highPhase * highPhase * highPhase) * 0.08f;
                float high = Mathf.Sin(2f * Mathf.PI * G4 * t);
                high += Mathf.Sin(2f * Mathf.PI * (G4 * 0.998f) * t);
                high *= 0.5f;
                sample += high * highEnv;
            }

            // Layer 5: Rhythmic pulse at ~90 BPM - military march feel
            {
                float pulseRate = 1.5f;
                float pulsePhase = (t * pulseRate) % 1f;
                float pulse = 0f;
                if (pulsePhase < 0.06f)
                    pulse = Mathf.Sin(pulsePhase / 0.06f * Mathf.PI) * 0.9f;
                else if (pulsePhase > 0.12f && pulsePhase < 0.18f)
                    pulse = Mathf.Sin((pulsePhase - 0.12f) / 0.06f * Mathf.PI) * 0.5f;

                // Thump with mid-bass harmonics laptops can play
                float pulseTone = Mathf.Sin(2f * Mathf.PI * 130f * t) * 0.4f +
                                  Mathf.Sin(2f * Mathf.PI * 200f * t) * 0.3f +
                                  Mathf.Sin(2f * Mathf.PI * 300f * t) * 0.2f;
                sample += pulseTone * pulse * 0.25f;
            }

            // Layer 6: Wind / atmosphere noise
            {
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float windEnv = 0.025f + 0.02f * Mathf.Sin(2f * Mathf.PI * 0.08f * t);
                sample += noise * windEnv;
            }

            // Layer 7: Brass stabs (every ~8s) - shifted up
            {
                float stabCycle = t % 8f;
                if (stabCycle < 0.5f)
                {
                    float stabEnv = Mathf.Exp(-stabCycle * 4f) * 0.2f;
                    int section = (int)(t / 8f) % 4;
                    float stabFreq = section == 0 ? C3 : section == 1 ? Ab3 : section == 2 ? F3 : Eb3;
                    float stab = Mathf.Sin(2f * Mathf.PI * stabFreq * t);
                    stab += Mathf.Sin(2f * Mathf.PI * stabFreq * 2f * t) * 0.4f;
                    stab += Mathf.Sin(2f * Mathf.PI * stabFreq * 3f * t) * 0.2f;
                    stab += Mathf.Sin(2f * Mathf.PI * stabFreq * 4f * t) * 0.1f;
                    sample += stab * stabEnv;
                }
            }

            // Layer 8: Snare-like hits on off-beats
            {
                float tickRate = 3f;
                float tickPhase = (t * tickRate) % 1f;
                if (tickPhase < 0.008f)
                {
                    float tickEnv = 1f - (tickPhase / 0.008f);
                    float tick = (float)(rng.NextDouble() * 2.0 - 1.0);
                    // Add some tonal body so it's not just noise
                    tick += Mathf.Sin(2f * Mathf.PI * 400f * t) * 0.3f;
                    sample += tick * tickEnv * 0.06f;
                }
            }

            // Crossfade at loop boundary
            if (i > samples - sampleRate)
            {
                float fade = (float)(samples - i) / sampleRate;
                sample *= fade;
            }
            if (i < sampleRate)
            {
                float fadeIn = (float)i / sampleRate;
                sample *= fadeIn;
            }

            data[i] = Mathf.Clamp(sample, -1f, 1f);
        }

        // Light smoothing - keep it punchy
        for (int i = 1; i < samples; i++)
            data[i] = data[i] * 0.85f + data[i - 1] * 0.15f;

        AudioClip clip = AudioClip.Create("BGMusic", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    void SpawnWave()
    {
        waveNumber++;
        waveKillCount = 0;
        waveSuperKillCount = 0;
        waveStartTime = Time.time;

        // Reset player health at the start of each wave
        var playerObj = FindFirstObjectByType<FPSController>();
        if (playerObj != null)
        {
            playerObj.health = playerObj.maxHealth;
        }
        Vector3 playerPos = playerObj != null ? playerObj.transform.position : Vector3.zero;
        Vector3 playerFwd = playerObj != null ? playerObj.transform.forward : Vector3.forward;

        // From wave 2+, add super soldiers (1 per wave after the first)
        int superCount = waveNumber > 1 ? Mathf.Min(waveNumber - 1, 4) : 0;
        int normalCount = enemyCount;

        for (int i = 0; i < normalCount + superCount; i++)
        {
            bool isSuper = i >= normalCount;

            // Cluster in front: 20-35m ahead, spread +-15m sideways
            float dist = Random.Range(20f, 35f);
            float lateral = Random.Range(-15f, 15f);
            Vector3 right = Vector3.Cross(Vector3.up, playerFwd).normalized;
            Vector3 pos = playerPos + playerFwd * dist + right * lateral;
            pos.y = 0f;

            GameObject soldier;
            if (hasSoldierPrefab)
                soldier = SpawnSoldier(pos);
            else
                soldier = SpawnPrimitiveSoldier(pos);

            if (soldier != null)
            {
                var enemy = soldier.GetComponent<Enemy>();
                if (enemy != null) enemy.waveNumber = waveNumber;

                if (isSuper)
                {
                    // Super soldier: large, tough, hits hard
                    if (enemy != null)
                    {
                        enemy.isSuperSoldier = true;
                        enemy.health = 1360f; // 40 body shots or 8 headshots
                        enemy.fireRange = 8f;
                        enemy.moveSpeed = 0.5f;
                    }
                    float s = Random.Range(1.8f, 2.2f);
                    Vector3 superScale = soldier.transform.localScale * s;
                    superScale.x *= 1.25f;
                    superScale.z *= 1.25f;
                    soldier.transform.localScale = superScale;

                    // Make super soldier helmet and gun red, leave body unchanged
                    string urpLit = "Universal Render Pipeline/Lit";

                    var superRedMat = new Material(Shader.Find(urpLit));
                    superRedMat.SetColor("_BaseColor", new Color(0.7f, 0.08f, 0.05f));
                    superRedMat.SetFloat("_Smoothness", 0.5f);
                    superRedMat.SetFloat("_Metallic", 0.5f);

                    foreach (var r in soldier.GetComponentsInChildren<Renderer>())
                    {
                        string n = r.gameObject.name.ToLower();
                        bool isWeaponOrHelmet = n.Contains("rifle") || n.Contains("gun") ||
                                                n.Contains("weapon") || n.Contains("mag") ||
                                                n.Contains("barrel") || n.Contains("stock") ||
                                                n.Contains("handguard") || n.Contains("helmet") ||
                                                n.Contains("brim");

                        if (!isWeaponOrHelmet)
                        {
                            // Check material slots for prop/weapon materials
                            var mats = r.materials;
                            bool hasWeaponSlot = false;
                            for (int mi = 0; mi < mats.Length; mi++)
                            {
                                string matName = mats[mi] != null ? mats[mi].name.ToLower() : "";
                                if (matName.Contains("prop") || matName.Contains("gun") || matName.Contains("weapon"))
                                {
                                    mats[mi] = superRedMat;
                                    hasWeaponSlot = true;
                                }
                            }
                            if (hasWeaponSlot)
                                r.materials = mats;
                        }
                        else
                        {
                            r.material = superRedMat;
                        }
                    }

                    // Add a red helmet to the animated model's head bone
                    var anim = soldier.GetComponentInChildren<Animator>();
                    if (anim != null && anim.isHuman)
                    {
                        Transform headBone = anim.GetBoneTransform(HumanBodyBones.Head);
                        if (headBone != null)
                        {
                            var helmet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                            helmet.name = "SuperHelmet";
                            helmet.transform.SetParent(headBone);
                            helmet.transform.localPosition = new Vector3(0f, 0.12f, 0f);
                            helmet.transform.localRotation = Quaternion.identity;
                            helmet.transform.localScale = new Vector3(0.28f, 0.2f, 0.28f);
                            helmet.GetComponent<Renderer>().material = superRedMat;
                            Object.Destroy(helmet.GetComponent<Collider>());
                        }
                    }
                }
                else
                {
                    // Regular soldier size/health variation
                    float roll = Random.value;
                    if (roll < 0.3f)
                    {
                        float s = Random.Range(1.3f, 1.6f);
                        soldier.transform.localScale *= s;
                        if (enemy != null) enemy.health = 340f;
                    }
                    else if (roll < 0.7f)
                    {
                        if (enemy != null) enemy.health = 170f;
                    }
                    else
                    {
                        float s = Random.Range(0.6f, 0.8f);
                        soldier.transform.localScale *= s;
                        if (enemy != null) enemy.health = 102f;
                    }
                }
            }
        }

        // Make closest enemy fire immediately
        float closestDist = float.MaxValue;
        Enemy closestEnemy = null;
        foreach (var e in FindObjectsByType<Enemy>(FindObjectsSortMode.None))
        {
            if (e.health <= 0f) continue;
            float d = (e.transform.position - playerPos).sqrMagnitude;
            if (d < closestDist)
            {
                closestDist = d;
                closestEnemy = e;
            }
        }
        if (closestEnemy != null)
            closestEnemy.startFiringImmediately = true;
    }

    GameObject SpawnSoldier(Vector3 position)
    {
        GameObject soldier = Instantiate(soldierPrefab, position, Quaternion.identity);
        soldier.name = "Enemy";

        // Ensure reasonable scale (some FBX models import very small)
        if (soldier.transform.localScale.x < 0.5f)
        {
            soldier.transform.localScale = Vector3.one;
            Debug.Log("Fixed soldier scale to 1,1,1");
        }

        // Enable all child objects
        foreach (Transform child in soldier.GetComponentsInChildren<Transform>(true))
        {
            if (!child.gameObject.activeSelf)
                child.gameObject.SetActive(true);
        }

        // Enable all renderers and fix broken materials at runtime
        int rendererCount = 0;
        string urpLit = "Universal Render Pipeline/Lit";
        foreach (var renderer in soldier.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = true;
            rendererCount++;

            var mats = renderer.materials;
            bool needsFix = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null || mats[i].shader == null ||
                    mats[i].shader.name == "Hidden/InternalErrorShader" ||
                    (!mats[i].shader.name.Contains("Universal") && !mats[i].shader.name.Contains("URP")))
                {
                    var fixedMat = new Material(Shader.Find(urpLit));
                    string objName = renderer.gameObject.name.ToLower();
                    if (objName.Contains("rifle") || objName.Contains("gun") || objName.Contains("weapon"))
                    {
                        // Black metallic for weapons
                        fixedMat.SetColor("_BaseColor", new Color(0.04f, 0.04f, 0.05f));
                        fixedMat.SetFloat("_Smoothness", 0.55f);
                        fixedMat.SetFloat("_Metallic", 0.8f);
                    }
                    else
                    {
                        // Bright sand fallback for body
                        fixedMat.SetColor("_BaseColor", new Color(0.78f, 0.68f, 0.5f));
                        fixedMat.SetFloat("_Smoothness", 0.15f);
                    }
                    mats[i] = fixedMat;
                    needsFix = true;
                }
            }
            if (needsFix)
                renderer.materials = mats;
        }
        Debug.Log($"Soldier spawned at {position} with {rendererCount} renderers, scale={soldier.transform.localScale}");

        // Ensure it has an Enemy script
        if (soldier.GetComponent<Enemy>() == null)
            soldier.AddComponent<Enemy>();

        // Body collider - only covers torso/legs, stops at neck
        if (soldier.GetComponent<Collider>() == null)
        {
            var capsule = soldier.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.7f, 0f);
            capsule.radius = 0.3f;
            capsule.height = 1.4f;
        }

        // Head hitbox - separate so raycast can distinguish headshots
        GameObject headHitbox = new GameObject("HeadHitbox");
        headHitbox.transform.SetParent(soldier.transform);
        headHitbox.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        var headSphere = headHitbox.AddComponent<SphereCollider>();
        headSphere.radius = 0.2f;

        return soldier;
    }

    GameObject SpawnPrimitiveSoldier(Vector3 position)
    {
        GameObject soldier = new GameObject("Enemy");
        soldier.transform.position = position;

        // --- Torso (static body core) ---
        var torso = MakeCube("Torso", soldier.transform,
            new Vector3(0f, 1.05f, 0f), Vector3.zero,
            new Vector3(0.42f, 0.45f, 0.22f), uniformMat);
        torso.AddComponent<BoxCollider>();

        // Chest plate / vest
        MakeCube("ChestPlate", soldier.transform,
            new Vector3(0f, 1.12f, 0.05f), Vector3.zero,
            new Vector3(0.40f, 0.30f, 0.10f), helmetMat);

        // Shoulder pads
        MakeCube("LeftPad", soldier.transform,
            new Vector3(-0.26f, 1.30f, 0f), new Vector3(0f, 0f, 15f),
            new Vector3(0.12f, 0.06f, 0.18f), helmetMat);
        MakeCube("RightPad", soldier.transform,
            new Vector3(0.26f, 1.30f, 0f), new Vector3(0f, 0f, -15f),
            new Vector3(0.12f, 0.06f, 0.18f), helmetMat);

        // Belt + pouches
        MakeCube("Belt", soldier.transform,
            new Vector3(0f, 0.80f, 0f), Vector3.zero,
            new Vector3(0.44f, 0.06f, 0.24f), beltMat);
        MakeCube("Pouch1", soldier.transform,
            new Vector3(-0.18f, 0.80f, 0.12f), Vector3.zero,
            new Vector3(0.08f, 0.08f, 0.06f), helmetMat);
        MakeCube("Pouch2", soldier.transform,
            new Vector3(0.18f, 0.80f, 0.12f), Vector3.zero,
            new Vector3(0.08f, 0.08f, 0.06f), helmetMat);

        // Collar
        MakeCube("Collar", soldier.transform,
            new Vector3(0f, 1.32f, 0f), Vector3.zero,
            new Vector3(0.20f, 0.06f, 0.18f), uniformMat);

        // Neck
        MakeCube("Neck", soldier.transform,
            new Vector3(0f, 1.38f, 0f), Vector3.zero,
            new Vector3(0.09f, 0.07f, 0.09f), skinMat);

        // --- Head group (bobs with walk) ---
        GameObject headGroup = new GameObject("HeadGroup");
        headGroup.transform.SetParent(soldier.transform);
        headGroup.transform.localPosition = new Vector3(0f, 1.55f, 0f);
        headGroup.transform.localRotation = Quaternion.identity;

        // Head
        var head = MakeSphere("Head", headGroup.transform,
            new Vector3(0f, 0f, 0f),
            new Vector3(0.21f, 0.24f, 0.21f), skinMat, true);
        // Helmet
        MakeSphere("Helmet", headGroup.transform,
            new Vector3(0f, 0.06f, -0.01f),
            new Vector3(0.28f, 0.18f, 0.30f), helmetMat, false);
        // Helmet brim
        MakeCube("HelmetBrim", headGroup.transform,
            new Vector3(0f, 0.02f, 0.10f), new Vector3(10f, 0f, 0f),
            new Vector3(0.24f, 0.03f, 0.08f), helmetMat);
        // Eyes (two small dark spheres)
        MakeSphere("LeftEye", headGroup.transform,
            new Vector3(-0.05f, 0.01f, 0.09f),
            new Vector3(0.03f, 0.03f, 0.02f), bootMat, false);
        MakeSphere("RightEye", headGroup.transform,
            new Vector3(0.05f, 0.01f, 0.09f),
            new Vector3(0.03f, 0.03f, 0.02f), bootMat, false);

        // --- Left arm pivot (at shoulder) ---
        GameObject leftArmPivot = new GameObject("LeftArmPivot");
        leftArmPivot.transform.SetParent(soldier.transform);
        leftArmPivot.transform.localPosition = new Vector3(-0.27f, 1.28f, 0f);
        leftArmPivot.transform.localRotation = Quaternion.identity;

        // Upper arm
        MakeCube("LeftUpperArm", leftArmPivot.transform,
            new Vector3(-0.02f, -0.15f, 0f), Vector3.zero,
            new Vector3(0.11f, 0.28f, 0.11f), uniformMat);
        // Elbow
        MakeSphere("LeftElbow", leftArmPivot.transform,
            new Vector3(-0.02f, -0.30f, 0f),
            new Vector3(0.09f, 0.09f, 0.09f), uniformMat, false);
        // Forearm
        MakeCube("LeftForearm", leftArmPivot.transform,
            new Vector3(-0.02f, -0.42f, 0f), Vector3.zero,
            new Vector3(0.10f, 0.22f, 0.10f), uniformMat);
        // Hand
        MakeCube("LeftHand", leftArmPivot.transform,
            new Vector3(-0.02f, -0.55f, 0.02f), Vector3.zero,
            new Vector3(0.07f, 0.08f, 0.05f), skinMat);

        // --- Right arm pivot (at shoulder) ---
        GameObject rightArmPivot = new GameObject("RightArmPivot");
        rightArmPivot.transform.SetParent(soldier.transform);
        rightArmPivot.transform.localPosition = new Vector3(0.27f, 1.28f, 0f);
        rightArmPivot.transform.localRotation = Quaternion.identity;

        // Upper arm
        MakeCube("RightUpperArm", rightArmPivot.transform,
            new Vector3(0.02f, -0.15f, 0f), Vector3.zero,
            new Vector3(0.11f, 0.28f, 0.11f), uniformMat);
        // Elbow
        MakeSphere("RightElbow", rightArmPivot.transform,
            new Vector3(0.02f, -0.30f, 0f),
            new Vector3(0.09f, 0.09f, 0.09f), uniformMat, false);
        // Forearm
        MakeCube("RightForearm", rightArmPivot.transform,
            new Vector3(0.02f, -0.42f, 0f), Vector3.zero,
            new Vector3(0.10f, 0.22f, 0.10f), uniformMat);
        // Hand
        MakeCube("RightHand", rightArmPivot.transform,
            new Vector3(0.02f, -0.55f, 0.02f), Vector3.zero,
            new Vector3(0.07f, 0.08f, 0.05f), skinMat);

        // Rifle (held in right hand, pointing forward)
        MakeCube("RifleBody", rightArmPivot.transform,
            new Vector3(0.06f, -0.48f, 0.20f), new Vector3(-70f, 0f, 0f),
            new Vector3(0.035f, 0.035f, 0.45f), gunMetalMat);
        MakeCube("RifleStock", rightArmPivot.transform,
            new Vector3(0.06f, -0.40f, 0.05f), new Vector3(-50f, 0f, 0f),
            new Vector3(0.03f, 0.05f, 0.15f), gunMetalMat);
        MakeCube("RifleMag", rightArmPivot.transform,
            new Vector3(0.06f, -0.54f, 0.18f), new Vector3(-80f, 0f, 0f),
            new Vector3(0.025f, 0.07f, 0.035f), gunMetalMat);

        // Muzzle point - parented to arm pivot
        GameObject enemyMuzzle = new GameObject("EnemyMuzzlePoint");
        enemyMuzzle.transform.SetParent(rightArmPivot.transform);
        enemyMuzzle.transform.localPosition = new Vector3(0.06f, -0.269f, 0.277f);


        // --- Left leg pivot (at hip) ---
        GameObject leftLegPivot = new GameObject("LeftLegPivot");
        leftLegPivot.transform.SetParent(soldier.transform);
        leftLegPivot.transform.localPosition = new Vector3(-0.11f, 0.78f, 0f);
        leftLegPivot.transform.localRotation = Quaternion.identity;

        // Upper leg (thigh)
        MakeCube("LeftThigh", leftLegPivot.transform,
            new Vector3(0f, -0.18f, 0f), Vector3.zero,
            new Vector3(0.15f, 0.32f, 0.15f), uniformMat);
        // Knee
        MakeSphere("LeftKnee", leftLegPivot.transform,
            new Vector3(0f, -0.35f, 0f),
            new Vector3(0.11f, 0.11f, 0.11f), uniformMat, false);
        // Lower leg (shin)
        MakeCube("LeftShin", leftLegPivot.transform,
            new Vector3(0f, -0.50f, 0f), Vector3.zero,
            new Vector3(0.13f, 0.28f, 0.13f), uniformMat);
        // Boot
        MakeCube("LeftBoot", leftLegPivot.transform,
            new Vector3(0f, -0.68f, 0.03f), Vector3.zero,
            new Vector3(0.13f, 0.12f, 0.20f), bootMat);

        // --- Right leg pivot (at hip) ---
        GameObject rightLegPivot = new GameObject("RightLegPivot");
        rightLegPivot.transform.SetParent(soldier.transform);
        rightLegPivot.transform.localPosition = new Vector3(0.11f, 0.78f, 0f);
        rightLegPivot.transform.localRotation = Quaternion.identity;

        // Upper leg (thigh)
        MakeCube("RightThigh", rightLegPivot.transform,
            new Vector3(0f, -0.18f, 0f), Vector3.zero,
            new Vector3(0.15f, 0.32f, 0.15f), uniformMat);
        // Knee
        MakeSphere("RightKnee", rightLegPivot.transform,
            new Vector3(0f, -0.35f, 0f),
            new Vector3(0.11f, 0.11f, 0.11f), uniformMat, false);
        // Lower leg (shin)
        MakeCube("RightShin", rightLegPivot.transform,
            new Vector3(0f, -0.50f, 0f), Vector3.zero,
            new Vector3(0.13f, 0.28f, 0.13f), uniformMat);
        // Boot
        MakeCube("RightBoot", rightLegPivot.transform,
            new Vector3(0f, -0.68f, 0.03f), Vector3.zero,
            new Vector3(0.13f, 0.12f, 0.20f), bootMat);

        soldier.AddComponent<Enemy>();
        return soldier;
    }

    GameObject MakeCube(string name, Transform parent, Vector3 localPos, Vector3 localEuler, Vector3 localScale, Material mat)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        part.transform.SetParent(parent);
        part.transform.localPosition = localPos;
        part.transform.localRotation = Quaternion.Euler(localEuler);
        part.transform.localScale = localScale;
        Object.Destroy(part.GetComponent<Collider>());
        part.GetComponent<MeshRenderer>().material = mat;
        return part;
    }

    GameObject MakeSphere(string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat, bool keepCollider)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        part.name = name;
        part.transform.SetParent(parent);
        part.transform.localPosition = localPos;
        part.transform.localScale = localScale;
        if (!keepCollider) Object.Destroy(part.GetComponent<Collider>());
        part.GetComponent<MeshRenderer>().material = mat;
        return part;
    }
}

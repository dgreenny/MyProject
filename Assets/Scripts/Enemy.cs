using UnityEngine;

public class Enemy : MonoBehaviour
{
    public float health = 100f;
    public float moveSpeed = 0.8f;
    public float fireRange = 5f;
    public float rotationSpeed = 2f;

    Transform player;
    Animator animator;
    bool dead;
    bool hasAnimator;

    // Fallback limb pivots (for primitive soldiers)
    Transform leftArmPivot;
    Transform rightArmPivot;
    Transform leftLegPivot;
    Transform rightLegPivot;
    Transform headTransform;
    Transform muzzlePoint;
    Transform rifleBodyTransform;

    float walkCycle;
    float swingAmount = 35f;
    float bobAmount = 0.03f;
    float baseY;
    float debugTimer;

    Quaternion targetRotation;

    // Zombie groans
    float groanTimer;
    AudioSource audioSource;

    // Firing
    float fireCooldown;
    AudioClip enemyGunshotClip;
    FPSController playerController;
    public int waveNumber = 1;
    public bool startFiringImmediately;
    public bool isSuperSoldier;

    void Start()
    {
        player = Camera.main.transform.parent;

        animator = GetComponentInChildren<Animator>();
        hasAnimator = animator != null && animator.runtimeAnimatorController != null;

        if (hasAnimator)
        {
            animator.applyRootMotion = false;
            Debug.Log($"Enemy '{gameObject.name}': Animator mode ON, controller={animator.runtimeAnimatorController.name}");
            Debug.Log($"  Animator on: '{animator.gameObject.name}', avatar={(animator.avatar != null ? animator.avatar.name : "NONE")}, isHuman={animator.isHuman}");
            Debug.Log($"  Animator enabled={animator.enabled}, hasStates={animator.layerCount}, speed={animator.speed}");

            // Log all parameters to verify they exist
            foreach (var p in animator.parameters)
                Debug.Log($"  Animator param: {p.name} ({p.type})");
        }
        else
        {
            Debug.Log($"Enemy '{gameObject.name}': Animator mode OFF (animator={(animator != null ? "yes" : "no")}, controller={(animator != null && animator.runtimeAnimatorController != null ? "yes" : "no")})");
            leftArmPivot = transform.Find("LeftArmPivot");
            rightArmPivot = transform.Find("RightArmPivot");
            leftLegPivot = transform.Find("LeftLegPivot");
            rightLegPivot = transform.Find("RightLegPivot");
            headTransform = transform.Find("HeadGroup");
        }

        // Find rifle body mesh for dynamic muzzle tip calculation
        var allTransforms = GetComponentsInChildren<Transform>(true);
        foreach (var t in allTransforms)
        {
            string n = t.name.ToLower();
            if (n == "riflebody" || n == "rifle_body")
            {
                rifleBodyTransform = t;
                break;
            }
        }
        // Fallback: find any rifle/gun mesh
        if (rifleBodyTransform == null)
        {
            foreach (var t in allTransforms)
            {
                string n = t.name.ToLower();
                if ((n.Contains("rifle") || n.Contains("gun") || n.Contains("weapon")) &&
                    t.GetComponent<MeshRenderer>() != null)
                {
                    rifleBodyTransform = t;
                    break;
                }
            }
        }
        // Also find muzzle point as last fallback
        foreach (var t in allTransforms)
        {
            if (t.name == "EnemyMuzzlePoint")
            {
                muzzlePoint = t;
                break;
            }
        }

        baseY = transform.position.y;
        targetRotation = transform.rotation;

        // Vary speed per zombie
        moveSpeed = Random.Range(0.5f, 1.0f);

        // Audio for groans
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.spatialBlend = 1f;
        audioSource.maxDistance = 20f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        groanTimer = Random.Range(2f, 6f);

        // Firing setup
        fireCooldown = startFiringImmediately ? 0f : Random.Range(0.5f, 1.5f);
        if (isSuperSoldier)
        {
            enemyGunshotClip = GenerateSuperSoldierGunshotClip();
        }
        else
        {
            enemyGunshotClip = Resources.Load<AudioClip>("gun-shot-enemy");
            if (enemyGunshotClip == null)
                enemyGunshotClip = GenerateEnemyGunshotClip();
        }
        playerController = player.GetComponent<FPSController>();
    }

    void Update()
    {
        if (dead || player == null) return;

        Vector3 dir = player.position - transform.position;
        dir.y = 0f;
        float dist = dir.magnitude;

        bool inRange = dist <= fireRange;
        bool walking = dist > 2f && !inRange;

        // Slow zombie rotation toward the player
        if (dir.sqrMagnitude > 0.01f)
        {
            targetRotation = Quaternion.LookRotation(dir.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);
        }

        if (walking)
        {
            dir.Normalize();
            transform.position += dir * moveSpeed * Time.deltaTime;

            if (!hasAnimator)
            {
                walkCycle += moveSpeed * Time.deltaTime * 4f;
                float bob = Mathf.Sin(walkCycle * 2f) * bobAmount;
                Vector3 pos = transform.position;
                pos.y = baseY + bob;
                transform.position = pos;
            }
        }

        if (hasAnimator)
        {
            animator.SetBool("Walking", walking);
            animator.SetBool("InRange", inRange);

            // Scale animation speed to match movement speed so feet don't slide
            animator.speed = walking ? moveSpeed : 1f;

            // Periodic debug: log animator state every 3 seconds (first enemy only)
            debugTimer -= Time.deltaTime;
            if (debugTimer <= 0f && gameObject.name.Contains("Enemy"))
            {
                var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                Debug.Log($"[{gameObject.name}] walking={walking}, inRange={inRange}, dist={dist:F1}, state='{stateInfo.shortNameHash}', normalizedTime={stateInfo.normalizedTime:F2}, length={stateInfo.length:F2}, isHuman={animator.isHuman}, avatar={(animator.avatar != null ? animator.avatar.name : "NONE")}");
                debugTimer = 3f;
            }
        }
        else
        {
            AnimateLimbs(walking);
        }

        // Zombie groans
        groanTimer -= Time.deltaTime;
        if (groanTimer <= 0f)
        {
            PlayGroan();
            groanTimer = Random.Range(4f, 10f);
        }
    }

    // LateUpdate: keep facing the player after animator updates
    void LateUpdate()
    {
        if (dead || player == null) return;

        Vector3 dir = player.position - transform.position;
        dir.y = 0f;

        if (dir.sqrMagnitude > 0.01f)
        {
            targetRotation = Quaternion.LookRotation(dir.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);
        }

        // Keep feet on the ground
        Vector3 pos = transform.position;
        pos.y = baseY;
        transform.position = pos;

        // Fire at player when in range (done in LateUpdate so Animator bone poses are current)
        float dist = dir.magnitude;
        bool inRange = dist <= fireRange;
        if (inRange)
        {
            fireCooldown -= Time.deltaTime;
            if (fireCooldown <= 0f)
            {
                FireAtPlayer();
                float speedMul = 1f / (1f + (waveNumber - 1) * 0.25f);
                fireCooldown = Random.Range(1f, 2f) * speedMul;
            }
        }
    }

    void AnimateLimbs(bool walking)
    {
        float target = walking ? Mathf.Sin(walkCycle) * swingAmount : 0f;
        float speed = walking ? 10f : 5f;

        if (leftLegPivot != null)
        {
            float angle = Mathf.LerpAngle(leftLegPivot.localEulerAngles.x, target, Time.deltaTime * speed);
            leftLegPivot.localRotation = Quaternion.Euler(angle, 0f, 0f);
        }
        if (rightLegPivot != null)
        {
            float angle = Mathf.LerpAngle(rightLegPivot.localEulerAngles.x, -target, Time.deltaTime * speed);
            rightLegPivot.localRotation = Quaternion.Euler(angle, 0f, 0f);
        }
        if (leftArmPivot != null)
        {
            float angle = Mathf.LerpAngle(leftArmPivot.localEulerAngles.x, -target * 0.7f, Time.deltaTime * speed);
            leftArmPivot.localRotation = Quaternion.Euler(angle, 0f, 0f);
        }
        if (rightArmPivot != null)
        {
            float angle = Mathf.LerpAngle(rightArmPivot.localEulerAngles.x, target * 0.7f, Time.deltaTime * speed);
            rightArmPivot.localRotation = Quaternion.Euler(angle, 0f, 0f);
        }
        if (headTransform != null && walking)
        {
            float headTilt = Mathf.Sin(walkCycle * 2f) * 2f;
            headTransform.localRotation = Quaternion.Euler(0f, 0f, headTilt);
        }
    }

    void FireAtPlayer()
    {
        if (player == null) return;

        // Snap to face the player, including the 40-degree yOffset that
        // ModelOrientationFix will apply, so the muzzle position is correct
        Vector3 faceDir = player.position - transform.position;
        faceDir.y = 0f;
        if (faceDir.sqrMagnitude > 0.01f)
        {
            // For scaled-up soldiers (super soldiers), tilt the model down so
            // the gun points at the player instead of over their head
            float gunWorldHeight = transform.position.y + 1.2f * transform.localScale.y;
            float playerChestHeight = player.position.y + 1.0f;
            float pitch = Mathf.Atan2(gunWorldHeight - playerChestHeight, faceDir.magnitude) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(pitch, 0f, 30f);

            transform.rotation = Quaternion.LookRotation(faceDir.normalized) * Quaternion.Euler(-pitch, 40f, 0f);
        }

        // Force animator to re-evaluate bone poses with the updated rotation
        if (hasAnimator)
            animator.Update(0f);

        // Snap arm to rest position for consistent muzzle placement
        if (rightArmPivot != null)
            rightArmPivot.localRotation = Quaternion.identity;

        // Compute origin from muzzle point if available
        Vector3 origin;
        if (muzzlePoint != null)
        {
            origin = muzzlePoint.position;
        }
        else if (rightArmPivot != null)
        {
            origin = rightArmPivot.TransformPoint(new Vector3(-0.07f, -0.03f, 0.5f));
        }
        else
        {
            origin = transform.TransformPoint(new Vector3(0.20f, 1.25f, 0.5f));
        }
        Vector3 toPlayer = (player.position - origin).normalized;

        // Super soldiers are more accurate and deal more damage
        float spread = isSuperSoldier ? Random.Range(0.4f, 1.5f) : Random.Range(0.8f, 2.5f);
        Vector3 aimDir = toPlayer + Random.insideUnitSphere * (spread * 0.1f);
        aimDir.Normalize();

        Vector3 endPoint;
        bool hitPlayer = false;

        if (Physics.Raycast(origin, aimDir, out RaycastHit hit, fireRange + 5f))
        {
            endPoint = hit.point;

            var pc = hit.collider.GetComponent<FPSController>();
            if (pc == null) pc = hit.collider.GetComponentInParent<FPSController>();

            if (pc != null)
            {
                float damage = isSuperSoldier ? Random.Range(18f, 28f) : Random.Range(8f, 12f);
                pc.TakeHit(damage);
                hitPlayer = true;
            }
        }
        else
        {
            endPoint = origin + aimDir * (fireRange + 5f);
        }

        // Sound - super soldiers are louder
        if (enemyGunshotClip != null)
            audioSource.PlayOneShot(enemyGunshotClip, isSuperSoldier ? 0.8f : 0.5f);

        // Muzzle flash + bullet tracer
        StartCoroutine(ShowEnemyMuzzleFlash(origin));
        StartCoroutine(ShowEnemyTracer(origin, endPoint));

        if (hitPlayer)
            StartCoroutine(ShowBloodSplat(endPoint));
    }

    System.Collections.IEnumerator ShowEnemyMuzzleFlash(Vector3 flashPos)
    {
        GameObject flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(flash, 0.5f); // safety net if coroutine is killed by StopAllCoroutines
        flash.transform.position = flashPos;
        float flashScale = isSuperSoldier ? 0.35f : 0.15f;
        flash.transform.localScale = Vector3.one * flashScale;
        Object.Destroy(flash.GetComponent<Collider>());
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.color = isSuperSoldier ? new Color(1f, 0.4f, 0.1f) : new Color(1f, 0.8f, 0.3f);
        flash.GetComponent<MeshRenderer>().material = mat;

        var light = flash.AddComponent<Light>();
        light.color = isSuperSoldier ? new Color(1f, 0.3f, 0.1f) : new Color(1f, 0.7f, 0.3f);
        light.intensity = isSuperSoldier ? 6f : 3f;
        light.range = isSuperSoldier ? 8f : 5f;

        yield return null;
        yield return null;
        if (isSuperSoldier) yield return null; // extra frame for bigger flash
        Object.Destroy(flash);
    }

    System.Collections.IEnumerator ShowEnemyTracer(Vector3 from, Vector3 to)
    {
        GameObject tracer = new GameObject("EnemyTracer");
        Object.Destroy(tracer, 0.5f); // safety net if coroutine is killed by StopAllCoroutines
        var lr = tracer.AddComponent<LineRenderer>();
        var tracerMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));

        if (isSuperSoldier)
        {
            // Big red/orange tracers for super soldiers
            tracerMat.color = new Color(1f, 0.2f, 0.05f);
            lr.material = tracerMat;
            lr.startWidth = 0.08f;
            lr.endWidth = 0.04f;
            lr.startColor = new Color(1f, 0.3f, 0.05f, 1f);
            lr.endColor = new Color(1f, 0.15f, 0.0f, 0.7f);
        }
        else
        {
            tracerMat.color = new Color(1f, 0.6f, 0.2f);
            lr.material = tracerMat;
            lr.startWidth = 0.025f;
            lr.endWidth = 0.01f;
            lr.startColor = new Color(1f, 0.7f, 0.2f, 1f);
            lr.endColor = new Color(1f, 0.5f, 0.1f, 0.6f);
        }

        lr.positionCount = 2;
        lr.SetPosition(0, from);
        lr.SetPosition(1, to);

        float elapsed = 0f;
        float duration = isSuperSoldier ? 0.15f : 0.1f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float alpha = 1f - (elapsed / duration);
            if (isSuperSoldier)
            {
                lr.startColor = new Color(1f, 0.3f, 0.05f, alpha);
                lr.endColor = new Color(1f, 0.15f, 0.0f, alpha * 0.6f);
            }
            else
            {
                lr.startColor = new Color(1f, 0.7f, 0.2f, alpha);
                lr.endColor = new Color(1f, 0.5f, 0.1f, alpha * 0.5f);
            }
            yield return null;
        }
        Object.Destroy(tracer);
    }

    System.Collections.IEnumerator ShowBloodSplat(Vector3 point)
    {
        string unlit = "Universal Render Pipeline/Unlit";
        int count = 8;
        GameObject[] drops = new GameObject[count];
        Vector3[] vel = new Vector3[count];
        float[] sizes = new float[count];

        for (int i = 0; i < count; i++)
        {
            drops[i] = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(drops[i], 1f); // safety net if coroutine is killed
            drops[i].transform.position = point;
            sizes[i] = Random.Range(0.03f, 0.08f);
            drops[i].transform.localScale = Vector3.one * sizes[i];
            Object.Destroy(drops[i].GetComponent<Collider>());

            var mat = new Material(Shader.Find(unlit));
            float r = Random.Range(0.5f, 0.8f);
            mat.color = new Color(r, 0f, 0f);
            drops[i].GetComponent<MeshRenderer>().material = mat;

            vel[i] = Random.onUnitSphere * Random.Range(1f, 4f);
        }

        float elapsed = 0f;
        float duration = 0.4f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            for (int i = 0; i < count; i++)
            {
                if (drops[i] == null) continue;
                drops[i].transform.position += vel[i] * Time.deltaTime;
                vel[i] += Vector3.down * 10f * Time.deltaTime;
                drops[i].transform.localScale = Vector3.one * sizes[i] * (1f - t);
            }
            yield return null;
        }

        for (int i = 0; i < count; i++)
            if (drops[i] != null) Object.Destroy(drops[i]);
    }

    AudioClip GenerateEnemyGunshotClip()
    {
        int sampleRate = 44100;
        int samples = sampleRate; // 1 second
        float[] data = new float[samples];
        float[] noiseBuf = new float[samples];

        System.Random rng = new System.Random(gameObject.GetInstanceID());
        for (int i = 0; i < samples; i++)
            noiseBuf[i] = (float)(rng.NextDouble() * 2.0 - 1.0);

        // Enemy gun heard at distance: mid-bass emphasis for laptop speakers
        float Tp = 0.0025f;

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / sampleRate;
            float sample = 0f;

            // Friedlander muzzle blast (attenuated by distance)
            if (t < Tp * 6f)
            {
                float tNorm = t / Tp;
                float friedlander = (1f - tNorm) * Mathf.Exp(-4f * tNorm);
                sample += friedlander * 1.5f;
            }

            // Initial crack impulse (0-1ms) - reduced
            if (t < 0.001f)
            {
                float env = 1f - (t / 0.001f);
                env = env * env * env;
                sample += noiseBuf[i] * env * 1.4f;
            }

            // Supersonic snap (0.2-5ms) - lower frequencies
            if (t >= 0.0002f && t < 0.005f)
            {
                float bt = t - 0.0002f;
                float env = Mathf.Exp(-bt * 450f);
                float crack = noiseBuf[i] * 0.25f +
                              Mathf.Sin(2f * Mathf.PI * 1500f * t) * 0.25f +
                              Mathf.Sin(2f * Mathf.PI * 2500f * t) * 0.15f +
                              Mathf.Sin(2f * Mathf.PI * 3500f * t) * 0.06f;
                sample += crack * env * 1.3f;
            }

            // Muzzle blast body (1-25ms) - mid-weighted
            if (t >= 0.001f && t < 0.025f)
            {
                float bt = t - 0.001f;
                float env = Mathf.Exp(-bt * 90f);
                float barrel = Mathf.Sin(2f * Mathf.PI * 250f * t) * 0.25f +
                               Mathf.Sin(2f * Mathf.PI * 500f * t) * 0.3f +
                               Mathf.Sin(2f * Mathf.PI * 800f * t) * 0.2f +
                               Mathf.Sin(2f * Mathf.PI * 1200f * t) * 0.1f;
                sample += (noiseBuf[i] * 0.25f + barrel * 0.75f) * env * 1.4f;
            }

            // Low concussion (2-120ms) - boosted mid-bass harmonics
            if (t >= 0.002f && t < 0.12f)
            {
                float bt = t - 0.002f;
                float env = Mathf.Exp(-bt * 18f);
                float freq = Mathf.Lerp(280f, 100f, Mathf.Min(bt / 0.06f, 1f));
                float boom = Mathf.Sin(2f * Mathf.PI * freq * t);
                // Mid-bass that laptop speakers can reproduce
                float bass = Mathf.Sin(2f * Mathf.PI * 120f * t) * 0.35f +
                             Mathf.Sin(2f * Mathf.PI * 180f * t) * 0.45f +
                             Mathf.Sin(2f * Mathf.PI * 250f * t) * 0.4f +
                             Mathf.Sin(2f * Mathf.PI * 350f * t) * 0.3f;
                sample += (boom * 0.4f + bass * 0.6f) * env * 1.2f;
            }

            // Echo / reverb (50ms-1s) - warm mids
            if (t >= 0.05f)
            {
                float bt = t - 0.05f;
                float env = Mathf.Exp(-bt * 4f);
                float echo = noiseBuf[i] * 0.08f +
                             Mathf.Sin(2f * Mathf.PI * 120f * t) * 0.25f +
                             Mathf.Sin(2f * Mathf.PI * 220f * t) * 0.2f +
                             Mathf.Sin(2f * Mathf.PI * 350f * t) * 0.12f;
                sample += echo * env * 0.4f;
            }

            // Saturation
            sample *= 1.8f;
            if (sample > 0f)
            {
                sample = sample / (1f + sample);
                sample *= 1.4f;
            }
            else
            {
                float abs = -sample;
                sample = -(abs / (1f + abs * 0.8f));
                sample *= 1.4f;
            }

            data[i] = Mathf.Clamp(sample, -1f, 1f);
        }

        // Two-pass smoothing for warmer tone
        for (int i = 1; i < samples; i++)
            data[i] = data[i] * 0.82f + data[i - 1] * 0.18f;
        for (int i = 1; i < samples; i++)
            data[i] = data[i] * 0.88f + data[i - 1] * 0.12f;

        AudioClip clip = AudioClip.Create("EnemyGunshot", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    AudioClip GenerateSuperSoldierGunshotClip()
    {
        int sampleRate = 44100;
        int samples = (int)(sampleRate * 2.0f);
        float[] data = new float[samples];
        float[] noiseBuf = new float[samples];

        System.Random rng = new System.Random(gameObject.GetInstanceID() + 999);
        for (int i = 0; i < samples; i++)
            noiseBuf[i] = (float)(rng.NextDouble() * 2.0 - 1.0);

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / sampleRate;
            float sample = 0f;

            // Massive initial blast (0-3ms)
            if (t < 0.003f)
            {
                float env = 1f - (t / 0.003f);
                env = env * env * env;
                sample += noiseBuf[i] * env * 2.5f;
            }

            // Heavy cannon boom (0-50ms) - deep barrel resonance
            if (t < 0.05f)
            {
                float env = Mathf.Exp(-t * 50f);
                float boom = Mathf.Sin(2f * Mathf.PI * 120f * t) * 0.5f +
                             Mathf.Sin(2f * Mathf.PI * 200f * t) * 0.4f +
                             Mathf.Sin(2f * Mathf.PI * 350f * t) * 0.3f +
                             Mathf.Sin(2f * Mathf.PI * 500f * t) * 0.15f;
                sample += (noiseBuf[i] * 0.2f + boom * 0.8f) * env * 2.2f;
            }

            // Deep concussion wave (2-300ms) - heavy chest thump
            if (t >= 0.002f && t < 0.3f)
            {
                float bt = t - 0.002f;
                float env = Mathf.Exp(-bt * 8f);
                float freq = Mathf.Lerp(350f, 60f, Mathf.Min(bt / 0.1f, 1f));
                float blast = Mathf.Sin(2f * Mathf.PI * freq * t);
                float bass = Mathf.Sin(2f * Mathf.PI * 80f * t) * 0.5f +
                             Mathf.Sin(2f * Mathf.PI * 130f * t) * 0.6f +
                             Mathf.Sin(2f * Mathf.PI * 200f * t) * 0.5f +
                             Mathf.Sin(2f * Mathf.PI * 300f * t) * 0.35f;
                sample += (blast * 0.3f + bass * 0.7f) * env * 2.0f;
            }

            // Mid-range body (10-150ms)
            if (t >= 0.01f && t < 0.15f)
            {
                float bt = t - 0.01f;
                float env = Mathf.Exp(-bt * 25f);
                float body = Mathf.Sin(2f * Mathf.PI * 180f * t) * 0.4f +
                             Mathf.Sin(2f * Mathf.PI * 280f * t) * 0.3f +
                             Mathf.Sin(2f * Mathf.PI * 450f * t) * 0.2f;
                sample += (noiseBuf[i] * 0.15f + body * 0.85f) * env * 1.5f;
            }

            // Heavy echo / reverb (100ms-1.5s)
            if (t >= 0.1f && t < 1.5f)
            {
                float bt = t - 0.1f;
                float env = Mathf.Exp(-bt * 2.5f);
                float echo = noiseBuf[i] * 0.06f +
                             Mathf.Sin(2f * Mathf.PI * 100f * t) * 0.3f +
                             Mathf.Sin(2f * Mathf.PI * 180f * t) * 0.25f +
                             Mathf.Sin(2f * Mathf.PI * 280f * t) * 0.15f;
                sample += echo * env * 0.5f;
            }

            // Rolling thunder tail (500ms-2s)
            if (t >= 0.5f)
            {
                float bt = t - 0.5f;
                float env = Mathf.Exp(-bt * 2f);
                float rumble = Mathf.Sin(2f * Mathf.PI * 80f * t) * 0.3f +
                               Mathf.Sin(2f * Mathf.PI * 140f * t) * 0.25f +
                               Mathf.Sin(2f * Mathf.PI * 220f * t) * 0.15f;
                sample += (noiseBuf[i] * 0.05f + rumble * 0.95f) * env * 0.3f;
            }

            // Heavy saturation
            sample *= 2.2f;
            if (sample > 0f)
            {
                sample = sample / (1f + sample);
                sample *= 1.6f;
            }
            else
            {
                float abs = -sample;
                sample = -(abs / (1f + abs * 0.7f));
                sample *= 1.6f;
            }

            data[i] = Mathf.Clamp(sample, -1f, 1f);
        }

        // Heavy smoothing for deep warm tone
        for (int i = 1; i < samples; i++)
            data[i] = data[i] * 0.78f + data[i - 1] * 0.22f;
        for (int i = 1; i < samples; i++)
            data[i] = data[i] * 0.82f + data[i - 1] * 0.18f;

        AudioClip clip = AudioClip.Create("SuperGunshot", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    void PlayGroan()
    {
        if (audioSource == null || audioSource.isPlaying) return;

        int sampleRate = 22050;
        float duration = Random.Range(0.5f, 1.5f);
        int samples = (int)(sampleRate * duration);
        float[] data = new float[samples];

        // Higher base frequency so laptop speakers reproduce the fundamental
        float baseFreq = Random.Range(120f, 200f);
        float freqDrift = Random.Range(-25f, 25f);
        float growlFreq = Random.Range(60f, 100f);

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / sampleRate;
            float env = Mathf.Sin(t / duration * Mathf.PI);
            env *= env;

            float freq = baseFreq + freqDrift * t / duration;
            // Stronger harmonics in the 200-600Hz range for body
            float voice = Mathf.Sin(2f * Mathf.PI * freq * t) * 0.35f;
            voice += Mathf.Sin(2f * Mathf.PI * freq * 2.02f * t) * 0.3f;
            voice += Mathf.Sin(2f * Mathf.PI * freq * 3.01f * t) * 0.2f;
            voice += Mathf.Sin(2f * Mathf.PI * freq * 4.03f * t) * 0.1f;

            float growl = Mathf.Sin(2f * Mathf.PI * growlFreq * t) * 0.25f;
            // Add growl harmonics for warmth
            growl += Mathf.Sin(2f * Mathf.PI * growlFreq * 2f * t) * 0.15f;
            float noise = (Random.value * 2f - 1f) * 0.06f;

            float vibrato = Mathf.Sin(2f * Mathf.PI * 5f * t) * 10f;
            voice += Mathf.Sin(2f * Mathf.PI * (freq + vibrato) * t) * 0.15f;

            data[i] = (voice + growl + noise) * env;
        }

        AudioClip clip = AudioClip.Create("Groan", samples, 1, sampleRate, false);
        clip.SetData(data, 0);

        audioSource.clip = clip;
        audioSource.pitch = Random.Range(0.8f, 1.2f);
        audioSource.volume = Random.Range(0.15f, 0.3f);
        audioSource.Play();
    }

    public void TakeHit(float damage, bool headshot = false)
    {
        if (dead) return;

        health -= damage;

        if (!hasAnimator)
            StartCoroutine(HitFlash());

        if (headshot)
            StartCoroutine(ShowHeadshotIndicator());

        if (health <= 0f)
            Die();
    }

    System.Collections.IEnumerator ShowHeadshotIndicator()
    {
        // Brief red flash on head to confirm headshot
        var headObj = transform.Find("HeadGroup");
        if (headObj == null) headObj = transform.Find("HeadHitbox");
        if (headObj == null) yield break;

        var renderers = headObj.GetComponentsInChildren<MeshRenderer>();
        Material red = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        red.color = Color.red;

        Material[] origMats = new Material[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            origMats[i] = renderers[i].material;
            renderers[i].material = red;
        }

        yield return new WaitForSeconds(0.1f);

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].material = origMats[i];
        }
    }

    System.Collections.IEnumerator HitFlash()
    {
        var renderers = GetComponentsInChildren<MeshRenderer>();
        Material flash = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        flash.color = Color.white;

        Material[] origMats = new Material[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            origMats[i] = renderers[i].material;
            renderers[i].material = flash;
        }

        yield return new WaitForSeconds(0.05f);

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].material = origMats[i];
        }
    }

    void Die()
    {
        dead = true;

        // Stop all audio and coroutines immediately
        if (audioSource != null)
            audioSource.Stop();
        StopAllCoroutines();

        // Stop ModelOrientationFix from overriding rotation during death animation
        var orientFix = GetComponentInChildren<ModelOrientationFix>();
        if (orientFix != null)
            orientFix.enabled = false;

        // Notify GameSetup of kill
        var gameSetup = Object.FindFirstObjectByType<GameSetup>();
        if (gameSetup != null)
            gameSetup.OnEnemyKilled(isSuperSoldier);

        if (hasAnimator)
        {
            // Enable root motion so the death animation naturally lowers
            // the character to the ground (without this, the transform stays
            // at standing height and the lying-down pose floats in the air)
            animator.applyRootMotion = true;
            animator.SetTrigger("Die");
            foreach (var col in GetComponentsInChildren<Collider>())
                col.enabled = false;
            Object.Destroy(gameObject, 4f);
        }
        else
        {
            foreach (var col in GetComponentsInChildren<Collider>())
                col.enabled = false;

            var rb = gameObject.AddComponent<Rigidbody>();
            rb.AddForce(Vector3.up * 2f + Random.onUnitSphere * 3f, ForceMode.Impulse);
            rb.AddTorque(Random.onUnitSphere * 10f, ForceMode.Impulse);

            Object.Destroy(gameObject, 3f);
        }
    }
}

using UnityEngine;
using UnityEngine.InputSystem;

public class FPSController : MonoBehaviour
{
    public float moveSpeed = 6f;
    public float mouseSensitivity = 2f;
    public float jumpForce = 5f;
    public float keyboardTurnSpeed = 120f;

    public float health = 250f;
    public float maxHealth = 250f;
    public bool dead { get; private set; }

    CharacterController controller;
    Transform cameraTransform;
    Transform muzzlePoint;
    AudioSource audioSource;
    AudioClip gunshotClip;
    AudioClip shellCasingClip;
    Material tracerMat;
    Material flashMat;

    // Red damage flash overlay
    UnityEngine.UI.Image damageOverlay;

    // Health state audio
    AudioSource healthAudioSource;
    AudioClip heartbeatClip;
    AudioClip breathingClip;
    bool heartbeatPlaying;
    bool breathingPlaying;

    float cameraPitch;
    float verticalVelocity;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        cameraTransform = GetComponentInChildren<Camera>().transform;
        audioSource = GetComponent<AudioSource>();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        gunshotClip = Resources.Load<AudioClip>("gun-shot");
        if (gunshotClip == null)
            gunshotClip = GenerateGunshotClip();
        shellCasingClip = GenerateShellCasingClip();

        tracerMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        tracerMat.color = new Color(1f, 0.9f, 0.3f);

        flashMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        flashMat.color = new Color(1f, 0.95f, 0.7f);

        // Find muzzle point (set by GameSetup)
        var mp = cameraTransform.Find("AssaultRifle/MuzzlePoint");
        if (mp != null) muzzlePoint = mp;

        // Health state audio
        healthAudioSource = gameObject.AddComponent<AudioSource>();
        healthAudioSource.loop = true;
        healthAudioSource.volume = 0f;
        heartbeatClip = GenerateHeartbeatClip();
        breathingClip = GenerateBreathingClip();
    }

    void Update()
    {
        if (dead) return;

        HandleMouseLook();
        HandleMovement();
        HandleShooting();

        // Fade out damage overlay
        if (damageOverlay != null && damageOverlay.color.a > 0f)
        {
            Color c = damageOverlay.color;
            c.a = Mathf.MoveTowards(c.a, 0f, Time.deltaTime * 3f);
            damageOverlay.color = c;
        }

        UpdateHealthAudio();
    }

    void HandleMouseLook()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        float mouseX = mouse.delta.x.ReadValue() * mouseSensitivity * 0.1f;
        float mouseY = mouse.delta.y.ReadValue() * mouseSensitivity * 0.1f;

        cameraPitch -= mouseY;
        cameraPitch = Mathf.Clamp(cameraPitch, -89f, 89f);

        cameraTransform.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
        transform.Rotate(Vector3.up * mouseX);
    }

    void HandleMovement()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        float moveX = 0f;
        float moveZ = 0f;

        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) moveZ += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) moveZ -= 1f;
        if (keyboard.aKey.isPressed) moveX -= 1f;
        if (keyboard.dKey.isPressed) moveX += 1f;

        // Arrow left/right rotate the player to look around
        float turn = 0f;
        if (keyboard.leftArrowKey.isPressed) turn -= 1f;
        if (keyboard.rightArrowKey.isPressed) turn += 1f;
        if (turn != 0f)
            transform.Rotate(Vector3.up * turn * keyboardTurnSpeed * Time.deltaTime);

        Vector3 move = transform.right * moveX + transform.forward * moveZ;
        if (move.sqrMagnitude > 1f) move.Normalize();

        if (controller.isGrounded)
        {
            verticalVelocity = -1f;
            if (keyboard.spaceKey.wasPressedThisFrame)
                verticalVelocity = jumpForce;
        }
        else
        {
            verticalVelocity += Physics.gravity.y * Time.deltaTime;
        }

        move.y = verticalVelocity;
        controller.Move(move * moveSpeed * Time.deltaTime);
    }

    void HandleShooting()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            audioSource.PlayOneShot(gunshotClip, 1.0f);
            StartCoroutine(PlayDelayedShellCasing());

            Vector3 origin = muzzlePoint != null ? muzzlePoint.position : cameraTransform.position;

            Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            Vector3 hitPoint = ray.origin + ray.direction * 200f;
            bool hitEnemy = false;
            Vector3 enemyHitPoint = Vector3.zero;

            // Use RaycastAll to find all hits, then prefer headshots
            RaycastHit[] hits = Physics.RaycastAll(ray, 200f);
            if (hits.Length > 0)
            {
                // Sort by distance
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

                // Find the closest enemy hit, preferring head over body
                Enemy hitEnemyRef = null;
                bool headshot = false;
                float bestDist = float.MaxValue;

                foreach (var h in hits)
                {
                    var enemy = h.collider.GetComponentInParent<Enemy>();
                    if (enemy == null)
                    {
                        // Hit a non-enemy (ground, tree, house) — stop here
                        if (h.distance < bestDist && hitEnemyRef == null)
                            hitPoint = h.point;
                        break;
                    }

                    bool isHead = h.collider.gameObject.name == "Head"
                               || h.collider.gameObject.name == "Helmet"
                               || h.collider.gameObject.name == "HeadHitbox";

                    // If this is the same or closer enemy, prefer headshot
                    if (hitEnemyRef == null || enemy == hitEnemyRef)
                    {
                        if (hitEnemyRef == null || (isHead && !headshot))
                        {
                            hitEnemyRef = enemy;
                            headshot = isHead;
                            bestDist = h.distance;
                            enemyHitPoint = h.point;
                        }
                    }
                    else if (h.distance > bestDist + 1f)
                    {
                        break; // past the first enemy, stop
                    }
                }

                if (hitEnemyRef != null)
                {
                    float damage = headshot ? 170f : 34f; // headshot = 5x (one-shot normal enemies)
                    hitEnemyRef.TakeHit(damage, headshot);
                    hitEnemy = true;
                    hitPoint = enemyHitPoint;
                }
                else if (hits.Length > 0)
                {
                    hitPoint = hits[0].point;
                }
            }

            StartCoroutine(ShowBulletTracer(origin, hitPoint));
            StartCoroutine(ShowMuzzleFlash());

            if (hitEnemy)
                StartCoroutine(ShowExplosion(enemyHitPoint));
            else if (Physics.Raycast(ray, out RaycastHit groundHit, 200f))
                StartCoroutine(ShowGroundImpact(groundHit.point, groundHit.normal));
        }
    }

    System.Collections.IEnumerator PlayDelayedShellCasing()
    {
        yield return new WaitForSeconds(0.08f);
        audioSource.PlayOneShot(shellCasingClip, 0.25f);
    }

    System.Collections.IEnumerator ShowBulletTracer(Vector3 from, Vector3 to)
    {
        GameObject tracer = new GameObject("Tracer");
        var lr = tracer.AddComponent<LineRenderer>();
        lr.material = tracerMat;
        lr.startWidth = 0.03f;
        lr.endWidth = 0.015f;
        lr.positionCount = 2;
        lr.SetPosition(0, from);
        lr.SetPosition(1, to);
        lr.startColor = new Color(1f, 0.9f, 0.3f, 1f);
        lr.endColor = new Color(1f, 0.7f, 0.2f, 0.5f);

        float elapsed = 0f;
        float duration = 0.08f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float alpha = 1f - (elapsed / duration);
            lr.startColor = new Color(1f, 0.9f, 0.3f, alpha);
            lr.endColor = new Color(1f, 0.7f, 0.2f, alpha * 0.5f);
            yield return null;
        }
        Object.Destroy(tracer);
    }

    System.Collections.IEnumerator ShowMuzzleFlash()
    {
        if (muzzlePoint == null) yield break;

        GameObject flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        flash.transform.position = muzzlePoint.position;
        flash.transform.localScale = Vector3.one * 0.12f;
        Object.Destroy(flash.GetComponent<Collider>());
        flash.GetComponent<MeshRenderer>().material = flashMat;

        yield return null;
        yield return null;
        Object.Destroy(flash);
    }

    System.Collections.IEnumerator ShowExplosion(Vector3 point)
    {
        string unlit = "Universal Render Pipeline/Unlit";

        // Big initial flash
        GameObject flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        flash.transform.position = point;
        flash.transform.localScale = Vector3.one * 0.8f;
        Object.Destroy(flash.GetComponent<Collider>());
        var flashMaterial = new Material(Shader.Find(unlit));
        flashMaterial.color = new Color(1f, 0.3f, 0.1f);
        flash.GetComponent<MeshRenderer>().material = flashMaterial;

        // Blood splatter particles
        int bloodCount = 24;
        GameObject[] blood = new GameObject[bloodCount];
        Vector3[] bloodVel = new Vector3[bloodCount];
        Material[] bloodMats = new Material[bloodCount];
        float[] bloodSizes = new float[bloodCount];

        for (int i = 0; i < bloodCount; i++)
        {
            blood[i] = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            blood[i].transform.position = point;
            bloodSizes[i] = Random.Range(0.04f, 0.15f);
            blood[i].transform.localScale = Vector3.one * bloodSizes[i];
            Object.Destroy(blood[i].GetComponent<Collider>());

            float r = Random.Range(0.4f, 0.8f);
            float g = Random.Range(0f, 0.05f);
            float b = Random.Range(0f, 0.02f);
            bloodMats[i] = new Material(Shader.Find(unlit));
            bloodMats[i].color = new Color(r, g, b);
            blood[i].GetComponent<MeshRenderer>().material = bloodMats[i];

            bloodVel[i] = Random.onUnitSphere * Random.Range(3f, 10f);
            bloodVel[i].y = Mathf.Abs(bloodVel[i].y) * 0.5f + 1f;
        }

        // Fire/smoke particles
        int fireCount = 16;
        GameObject[] fire = new GameObject[fireCount];
        Vector3[] fireVel = new Vector3[fireCount];
        Material[] fireMats = new Material[fireCount];
        float[] fireSizes = new float[fireCount];

        for (int i = 0; i < fireCount; i++)
        {
            fire[i] = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            fire[i].transform.position = point;
            fireSizes[i] = Random.Range(0.08f, 0.3f);
            fire[i].transform.localScale = Vector3.one * fireSizes[i];
            Object.Destroy(fire[i].GetComponent<Collider>());

            float r = Random.Range(0.8f, 1f);
            float g = Random.Range(0.2f, 0.5f);
            float b = Random.Range(0f, 0.05f);
            fireMats[i] = new Material(Shader.Find(unlit));
            fireMats[i].color = new Color(r, g, b);
            fire[i].GetComponent<MeshRenderer>().material = fireMats[i];

            fireVel[i] = Random.onUnitSphere * Random.Range(1f, 5f);
            fireVel[i].y = Mathf.Abs(fireVel[i].y) + 2f;
        }

        float elapsed = 0f;
        float duration = 0.7f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            // Shrink flash quickly
            if (flash != null)
            {
                float flashScale = Mathf.Lerp(0.8f, 0f, t * 3f);
                if (flashScale <= 0f)
                {
                    Object.Destroy(flash);
                }
                else
                {
                    flash.transform.localScale = Vector3.one * flashScale;
                }
            }

            // Animate blood
            for (int i = 0; i < bloodCount; i++)
            {
                if (blood[i] == null) continue;
                blood[i].transform.position += bloodVel[i] * Time.deltaTime;
                bloodVel[i] += Vector3.down * 12f * Time.deltaTime;
                float scale = bloodSizes[i] * (1f - t * 0.5f);
                blood[i].transform.localScale = Vector3.one * scale;
            }

            // Animate fire/smoke - rises and fades to dark smoke
            for (int i = 0; i < fireCount; i++)
            {
                if (fire[i] == null) continue;
                fire[i].transform.position += fireVel[i] * Time.deltaTime;
                fireVel[i] *= 0.97f;
                float scale = fireSizes[i] * Mathf.Lerp(1f, 2f, t);
                fire[i].transform.localScale = Vector3.one * scale;
                // Fade from orange to dark smoke
                Color c = fireMats[i].color;
                fireMats[i].color = new Color(
                    Mathf.Lerp(c.r, 0.15f, Time.deltaTime * 4f),
                    Mathf.Lerp(c.g, 0.12f, Time.deltaTime * 4f),
                    Mathf.Lerp(c.b, 0.1f, Time.deltaTime * 4f),
                    1f - t
                );
            }

            yield return null;
        }

        if (flash != null) Object.Destroy(flash);
        for (int i = 0; i < bloodCount; i++)
            if (blood[i] != null) Object.Destroy(blood[i]);
        for (int i = 0; i < fireCount; i++)
            if (fire[i] != null) Object.Destroy(fire[i]);
    }

    System.Collections.IEnumerator ShowGroundImpact(Vector3 point, Vector3 normal)
    {
        GameObject impact = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        impact.transform.position = point + normal * 0.01f;
        impact.transform.localScale = Vector3.one * 0.1f;
        Object.Destroy(impact.GetComponent<Collider>());

        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.color = new Color(0.4f, 0.3f, 0.1f);
        impact.GetComponent<MeshRenderer>().material = mat;

        yield return new WaitForSeconds(0.3f);
        Object.Destroy(impact);
    }

    public void SetDamageOverlay(UnityEngine.UI.Image overlay)
    {
        damageOverlay = overlay;
    }

    public void TakeHit(float damage)
    {
        if (dead) return;

        health -= damage;
        health = Mathf.Max(health, 0f);

        // Red screen flash
        if (damageOverlay != null)
        {
            Color c = damageOverlay.color;
            c.a = Mathf.Min(c.a + 0.4f, 0.6f);
            damageOverlay.color = c;
        }

        if (health <= 0f)
            Die();
    }

    void Die()
    {
        dead = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void UpdateHealthAudio()
    {
        float ratio = health / maxHealth;

        if (ratio <= 0.2f && !breathingPlaying)
        {
            // Critical: switch to heavy breathing (includes heartbeat underneath)
            healthAudioSource.clip = breathingClip;
            healthAudioSource.volume = 0.6f;
            healthAudioSource.Play();
            breathingPlaying = true;
            heartbeatPlaying = false;
        }
        else if (ratio <= 0.5f && ratio > 0.2f && !heartbeatPlaying)
        {
            // Wounded: heartbeat
            healthAudioSource.clip = heartbeatClip;
            healthAudioSource.volume = 0.4f;
            healthAudioSource.Play();
            heartbeatPlaying = true;
            breathingPlaying = false;
        }
        else if (ratio > 0.5f && (heartbeatPlaying || breathingPlaying))
        {
            // Healthy again: stop
            healthAudioSource.Stop();
            healthAudioSource.volume = 0f;
            heartbeatPlaying = false;
            breathingPlaying = false;
        }

        // Ramp volume with severity
        if (breathingPlaying)
        {
            float urgency = 1f - (ratio / 0.2f); // 0 at 20%, 1 at 0%
            healthAudioSource.volume = Mathf.Lerp(0.45f, 0.75f, urgency);
        }
        else if (heartbeatPlaying)
        {
            float urgency = 1f - ((ratio - 0.2f) / 0.3f); // 0 at 50%, 1 at 20%
            healthAudioSource.volume = Mathf.Lerp(0.25f, 0.5f, urgency);
        }
    }

    AudioClip GenerateHeartbeatClip()
    {
        int sampleRate = 44100;
        int lengthSeconds = 4;
        int samples = sampleRate * lengthSeconds;
        float[] data = new float[samples];

        // Two-beat heartbeat pattern: LUB-dub ... LUB-dub
        float bpm = 100f;
        float beatPeriod = 60f / bpm;

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / sampleRate;
            float beatTime = t % beatPeriod;
            float sample = 0f;

            // LUB (first beat, louder, lower) at t=0
            if (beatTime < 0.12f)
            {
                float env = Mathf.Sin(beatTime / 0.12f * Mathf.PI);
                env *= env;
                float thump = Mathf.Sin(2f * Mathf.PI * 45f * beatTime) * 0.6f +
                              Mathf.Sin(2f * Mathf.PI * 90f * beatTime) * 0.4f +
                              Mathf.Sin(2f * Mathf.PI * 150f * beatTime) * 0.25f +
                              Mathf.Sin(2f * Mathf.PI * 200f * beatTime) * 0.15f;
                sample += thump * env * 0.8f;
            }

            // dub (second beat, softer, slightly higher) at ~0.2s after LUB
            float dubTime = beatTime - 0.18f;
            if (dubTime >= 0f && dubTime < 0.08f)
            {
                float env = Mathf.Sin(dubTime / 0.08f * Mathf.PI);
                env *= env;
                float thump = Mathf.Sin(2f * Mathf.PI * 60f * dubTime) * 0.4f +
                              Mathf.Sin(2f * Mathf.PI * 120f * dubTime) * 0.3f +
                              Mathf.Sin(2f * Mathf.PI * 180f * dubTime) * 0.2f +
                              Mathf.Sin(2f * Mathf.PI * 250f * dubTime) * 0.1f;
                sample += thump * env * 0.5f;
            }

            data[i] = Mathf.Clamp(sample, -1f, 1f);
        }

        // Warm smoothing
        for (int i = 1; i < samples; i++)
            data[i] = data[i] * 0.8f + data[i - 1] * 0.2f;

        AudioClip clip = AudioClip.Create("Heartbeat", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    AudioClip GenerateBreathingClip()
    {
        int sampleRate = 44100;
        int lengthSeconds = 4;
        int samples = sampleRate * lengthSeconds;
        float[] data = new float[samples];

        System.Random rng = new System.Random(333);
        float[] noiseBuf = new float[samples];
        for (int i = 0; i < samples; i++)
            noiseBuf[i] = (float)(rng.NextDouble() * 2.0 - 1.0);

        // Rapid labored breathing: ~30 breaths/min = 2s per cycle
        float breathPeriod = 1.8f;

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / sampleRate;
            float breathTime = t % breathPeriod;
            float sample = 0f;

            // Inhale (0 - 0.6s): rising filtered noise with vocal resonance
            if (breathTime < 0.6f)
            {
                float phase = breathTime / 0.6f;
                float env = Mathf.Sin(phase * Mathf.PI);
                env *= env;

                // Breathy noise filtered through throat
                float breath = noiseBuf[i] * 0.15f;
                breath += Mathf.Sin(2f * Mathf.PI * 250f * t) * noiseBuf[i] * 0.1f;
                // Vocal cord resonance
                float vocal = Mathf.Sin(2f * Mathf.PI * 180f * t) * 0.15f +
                              Mathf.Sin(2f * Mathf.PI * 350f * t) * 0.1f +
                              Mathf.Sin(2f * Mathf.PI * 500f * t) * 0.06f;
                sample += (breath + vocal) * env * 0.7f;
            }

            // Exhale (0.7 - 1.4s): longer, more strained, with wheeze
            if (breathTime >= 0.7f && breathTime < 1.4f)
            {
                float phase = (breathTime - 0.7f) / 0.7f;
                float env = Mathf.Sin(phase * Mathf.PI);
                env *= env;

                float breath = noiseBuf[i] * 0.2f;
                // Wheeze / strained throat sound
                float wheeze = Mathf.Sin(2f * Mathf.PI * 400f * t) * 0.12f +
                               Mathf.Sin(2f * Mathf.PI * 600f * t) * 0.08f;
                // Pain groan undertone
                float groan = Mathf.Sin(2f * Mathf.PI * 130f * t) * 0.15f +
                              Mathf.Sin(2f * Mathf.PI * 260f * t) * 0.08f;
                sample += (breath + wheeze + groan) * env * 0.8f;
            }

            // Underlying rapid heartbeat at ~120 BPM
            float hbPeriod = 0.5f;
            float hbTime = t % hbPeriod;
            if (hbTime < 0.08f)
            {
                float env = Mathf.Sin(hbTime / 0.08f * Mathf.PI);
                env *= env;
                float thump = Mathf.Sin(2f * Mathf.PI * 50f * hbTime) * 0.4f +
                              Mathf.Sin(2f * Mathf.PI * 100f * hbTime) * 0.3f +
                              Mathf.Sin(2f * Mathf.PI * 160f * hbTime) * 0.2f;
                sample += thump * env * 0.35f;
            }
            float dubT = hbTime - 0.12f;
            if (dubT >= 0f && dubT < 0.05f)
            {
                float env = Mathf.Sin(dubT / 0.05f * Mathf.PI);
                env *= env;
                float thump = Mathf.Sin(2f * Mathf.PI * 70f * dubT) * 0.25f +
                              Mathf.Sin(2f * Mathf.PI * 140f * dubT) * 0.15f;
                sample += thump * env * 0.2f;
            }

            data[i] = Mathf.Clamp(sample, -1f, 1f);
        }

        // Smoothing
        for (int i = 1; i < samples; i++)
            data[i] = data[i] * 0.82f + data[i - 1] * 0.18f;

        AudioClip clip = AudioClip.Create("LaboredBreathing", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Multi-layered realistic assault rifle sound
    AudioClip GenerateGunshotClip()
    {
        int sampleRate = 44100;
        int samples = (int)(sampleRate * 1.8f);
        float[] data = new float[samples];
        float[] noise = new float[samples];

        System.Random rng = new System.Random(42);
        for (int i = 0; i < samples; i++)
            noise[i] = (float)(rng.NextDouble() * 2.0 - 1.0);

        float Tp = 0.003f;

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / sampleRate;
            float sample = 0f;

            // === MUZZLE BLAST: Friedlander wave ===
            if (t < Tp * 8f)
            {
                float tNorm = t / Tp;
                float friedlander = (1f - tNorm) * Mathf.Exp(-3.5f * tNorm);
                sample += friedlander * 2.5f;
            }

            // === INITIAL DETONATION: broadband impulse (0-1.5ms) ===
            if (t < 0.0015f)
            {
                float env = 1f - (t / 0.0015f);
                env = env * env * env * env;
                sample += noise[i] * env * 1.6f;
            }

            // === SUPERSONIC CRACK (0.3-6ms) - reduced treble ===
            if (t >= 0.0003f && t < 0.006f)
            {
                float bt = t - 0.0003f;
                float nLen = 0.004f;
                float env = Mathf.Exp(-bt * 400f);
                float nwave = Mathf.Sin(Mathf.PI * bt / nLen);
                // Pulled frequencies down, less high-end
                float crack = noise[i] * 0.3f +
                              Mathf.Sin(2f * Mathf.PI * 1800f * t) * 0.25f +
                              Mathf.Sin(2f * Mathf.PI * 2800f * t) * 0.15f +
                              Mathf.Sin(2f * Mathf.PI * 4000f * t) * 0.06f;
                sample += (nwave * 0.5f + crack * 0.5f) * env * 1.4f;
            }

            // === MUZZLE BLAST BODY (1-40ms) - mid-heavy barrel resonances ===
            if (t >= 0.001f && t < 0.04f)
            {
                float bt = t - 0.001f;
                float env = Mathf.Exp(-bt * 70f);
                // Barrel resonance weighted toward mids
                float barrel = Mathf.Sin(2f * Mathf.PI * 250f * t) * 0.25f +
                               Mathf.Sin(2f * Mathf.PI * 500f * t) * 0.35f +
                               Mathf.Sin(2f * Mathf.PI * 800f * t) * 0.25f +
                               Mathf.Sin(2f * Mathf.PI * 1200f * t) * 0.15f +
                               Mathf.Sin(2f * Mathf.PI * 1800f * t) * 0.08f;
                sample += (noise[i] * 0.3f + barrel * 0.7f) * env * 2.0f;
            }

            // === LOW CONCUSSION (2-200ms) - boosted mid-bass ===
            // Psychoacoustic bass: harmonics at 100-400Hz let small speakers
            // perceive bass weight even without true sub-bass reproduction
            if (t >= 0.001f && t < 0.2f)
            {
                float bt = t - 0.001f;
                float env = Mathf.Exp(-bt * 13f);
                float freq = Mathf.Lerp(300f, 80f, Mathf.Min(bt / 0.08f, 1f));
                float blast = Mathf.Sin(2f * Mathf.PI * freq * t);
                // Mid-bass emphasis: strong 100-400Hz content
                float bass = Mathf.Sin(2f * Mathf.PI * 100f * t) * 0.4f +
                             Mathf.Sin(2f * Mathf.PI * 150f * t) * 0.5f +
                             Mathf.Sin(2f * Mathf.PI * 200f * t) * 0.6f +
                             Mathf.Sin(2f * Mathf.PI * 300f * t) * 0.5f +
                             Mathf.Sin(2f * Mathf.PI * 400f * t) * 0.3f;
                sample += (blast * 0.4f + bass * 0.6f) * env * 1.8f;
            }

            // === MECHANICAL ACTION (20-80ms) - softer, lower pitched ===
            if (t >= 0.02f && t < 0.08f)
            {
                float bt = t - 0.02f;
                float env = Mathf.Exp(-bt * 80f);
                float metal = Mathf.Sin(2f * Mathf.PI * 2200f * t) * 0.2f +
                              Mathf.Sin(2f * Mathf.PI * 3500f * t) * 0.12f +
                              Mathf.Sin(2f * Mathf.PI * 800f * t) * 0.15f;
                sample += (noise[i] * 0.25f + metal * 0.75f) * env * 0.25f;
            }

            // === FIRST ECHO (30-250ms) - warm, mid-range ===
            if (t >= 0.03f && t < 0.25f)
            {
                float bt = t - 0.03f;
                float env = Mathf.Exp(-bt * 10f);
                float echo = noise[i] * 0.12f +
                             Mathf.Sin(2f * Mathf.PI * 150f * t) * 0.3f +
                             Mathf.Sin(2f * Mathf.PI * 300f * t) * 0.25f +
                             Mathf.Sin(2f * Mathf.PI * 500f * t) * 0.15f;
                sample += echo * env * 0.7f;
            }

            // === OUTDOOR REVERB (150ms-1s) - warm decay ===
            if (t >= 0.15f && t < 1.0f)
            {
                float bt = t - 0.15f;
                float env = Mathf.Exp(-bt * 4f);
                float reverb = noise[i] * 0.08f +
                               Mathf.Sin(2f * Mathf.PI * 120f * t) * 0.3f +
                               Mathf.Sin(2f * Mathf.PI * 200f * t) * 0.25f +
                               Mathf.Sin(2f * Mathf.PI * 350f * t) * 0.15f;
                sample += reverb * env * 0.45f;
            }

            // === DISTANT RUMBLE (400ms-1.8s) - mid-bass rumble ===
            if (t >= 0.4f)
            {
                float bt = t - 0.4f;
                float env = Mathf.Exp(-bt * 2.5f);
                float rumble = Mathf.Sin(2f * Mathf.PI * 100f * t) * 0.25f +
                               Mathf.Sin(2f * Mathf.PI * 160f * t) * 0.3f +
                               Mathf.Sin(2f * Mathf.PI * 250f * t) * 0.2f;
                sample += (noise[i] * 0.1f + rumble * 0.9f) * env * 0.25f;
            }

            // Saturation - drives the mids
            sample *= 2.0f;
            if (sample > 0f)
            {
                sample = sample / (1f + sample);
                sample *= 1.5f;
            }
            else
            {
                float abs = -sample;
                sample = -(abs / (1f + abs * 0.8f));
                sample *= 1.5f;
            }

            data[i] = Mathf.Clamp(sample, -1f, 1f);
        }

        // Two-pass smoothing to tame harsh treble on laptop speakers
        for (int i = 1; i < samples; i++)
            data[i] = data[i] * 0.82f + data[i - 1] * 0.18f;
        for (int i = 1; i < samples; i++)
            data[i] = data[i] * 0.85f + data[i - 1] * 0.15f;

        AudioClip clip = AudioClip.Create("Gunshot", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Shell casing hitting the ground sound
    AudioClip GenerateShellCasingClip()
    {
        int sampleRate = 44100;
        int samples = sampleRate / 4;
        float[] data = new float[samples];

        System.Random rng = new System.Random(99);

        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / sampleRate;
            float sample = 0f;

            // First bounce - lower-pitched metal with body thump
            {
                float env = Mathf.Exp(-t * 80f);
                // Lower metal resonances so it doesn't sound tinny
                float metal = Mathf.Sin(2f * Mathf.PI * 1800f * t) * 0.3f +
                              Mathf.Sin(2f * Mathf.PI * 2800f * t) * 0.25f +
                              Mathf.Sin(2f * Mathf.PI * 4200f * t) * 0.12f;
                // Impact body thump
                float thump = Mathf.Sin(2f * Mathf.PI * 300f * t) * 0.4f +
                              Mathf.Sin(2f * Mathf.PI * 600f * t) * 0.2f;
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                sample += (metal * 0.5f + thump * 0.3f + n * 0.2f) * env;
            }

            // Second bounce at ~60ms
            if (t >= 0.06f)
            {
                float bt = t - 0.06f;
                float env = Mathf.Exp(-bt * 100f) * 0.5f;
                float metal = Mathf.Sin(2f * Mathf.PI * 2200f * t) * 0.35f +
                              Mathf.Sin(2f * Mathf.PI * 3400f * t) * 0.2f;
                float thump = Mathf.Sin(2f * Mathf.PI * 350f * t) * 0.25f;
                sample += (metal + thump) * env;
            }

            // Third tiny bounce at ~100ms
            if (t >= 0.10f)
            {
                float bt = t - 0.10f;
                float env = Mathf.Exp(-bt * 150f) * 0.25f;
                float metal = Mathf.Sin(2f * Mathf.PI * 2500f * t) * 0.4f;
                sample += metal * env;
            }

            data[i] = sample * 0.4f;
        }

        // Light smoothing to soften the highs
        for (int i = 1; i < samples; i++)
            data[i] = data[i] * 0.85f + data[i - 1] * 0.15f;

        AudioClip clip = AudioClip.Create("ShellCasing", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}

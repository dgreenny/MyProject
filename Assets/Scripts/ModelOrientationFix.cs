using UnityEngine;

[DefaultExecutionOrder(100)]
public class ModelOrientationFix : MonoBehaviour
{
    float yOffset = 40f;
    Vector3 rifleRotation = new Vector3(-90f, 10f, 90f);
    Vector3 riflePosition = new Vector3(0f, 0.08f, 0.04f);
    Transform player;
    Transform rifle;

    void Start()
    {
        var cam = Camera.main;
        if (cam != null)
            player = cam.transform.parent;

        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "AssaultRifle")
            {
                rifle = t;
                break;
            }
        }

        if (rifle != null)
        {
            rifle.localRotation = Quaternion.Euler(rifleRotation);
            rifle.localPosition = riflePosition;
        }
    }

    void LateUpdate()
    {
        if (player == null) return;

        Vector3 dir = player.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(dir.normalized) * Quaternion.Euler(0f, yOffset, 0f);
    }
}

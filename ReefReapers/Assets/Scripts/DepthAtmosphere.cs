using UnityEngine;
using UnityEngine.Rendering;

public class DepthAtmosphere : MonoBehaviour
{
    [Header("HDRP Volumes")]
    public Volume shallowVolume;
    public Volume midVolume;
    public Volume deepVolume;

    [Header("Depth Thresholds (world Y)")]
    public float shallowY = -1f;
    public float midY = -4f;
    public float deepY = -8f;

    [Header("Blend Speed")]
    public float blendSpeed = 2f;

    void Update()
    {
        float y = transform.position.y;

        // 0=shallow, 1=mid, 2=deep based on Y
        float t = Mathf.InverseLerp(shallowY, deepY, y); // 0 at shallow, 1 at deep
        float tMid = Mathf.Clamp01(t * 2f);              // 0→1 over shallow→mid
        float tDeep = Mathf.Clamp01((t - 0.5f) * 2f);    // 0→1 over mid→deep

        float targetShallow = Mathf.Clamp01(1f - tMid);
        float targetMid     = Mathf.Clamp01(tMid - tDeep);
        float targetDeep    = tDeep;

        float spd = blendSpeed * Time.deltaTime;
        shallowVolume.weight = Mathf.MoveTowards(shallowVolume.weight, targetShallow, spd);
        midVolume.weight     = Mathf.MoveTowards(midVolume.weight,     targetMid,     spd);
        deepVolume.weight    = Mathf.MoveTowards(deepVolume.weight,    targetDeep,    spd);
    }
}
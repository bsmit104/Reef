using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

public class FlashlightController : MonoBehaviour
{
    [Header("Flicker")]
    public bool flicker = true;
    public float flickerSpeed = 8f;
    public float flickerAmount = 0.05f; // subtle, not annoying

    [Header("Bobbing")]
    public bool bob = true;
    public float bobSpeed = 1.5f;
    public float bobAmount = 0.8f; // degrees of angle shift while walking

    private HDAdditionalLightData hdLight;
    private float baseIntensity;
    private float baseAngle;

    void Start()
    {
        hdLight = GetComponent<HDAdditionalLightData>();
        var light = GetComponent<Light>();
        baseIntensity = light.intensity;
        baseAngle = light.spotAngle;
    }

    void Update()
    {
        var light = GetComponent<Light>();

        if (flicker)
        {
            float noise = Mathf.PerlinNoise(Time.time * flickerSpeed, 0f);
            light.intensity = baseIntensity * (1f - flickerAmount + noise * flickerAmount * 2f);
        }

        if (bob)
        {
            // Subtle angle shift simulating hand-held movement
            float bobOffset = Mathf.Sin(Time.time * bobSpeed) * bobAmount;
            light.spotAngle = baseAngle + bobOffset;
        }
    }
}
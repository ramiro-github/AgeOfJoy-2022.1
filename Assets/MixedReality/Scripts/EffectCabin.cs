using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EffectCabin : MonoBehaviour
{
    public Renderer targetRenderer;
    public Light targetLight;
    public float blinkSpeed = 10f;
    private bool isLightOn = false;
    private float timer = 0f;


    void Start()
    {
        Material[] materials = targetRenderer.materials;
        materials[1].SetFloat("_Opacity", 0);
    }

    void Update()
    {
            float glowValue = Mathf.PingPong(Time.time, 10f);

            timer += Time.deltaTime * blinkSpeed;

            // Alterna entre 0 e 100 quando o timer atinge 1
            if (timer >= 1f)
            {
                isLightOn = !isLightOn;
                targetLight.intensity = isLightOn ? 50f : 0f; // Define a intensidade
                timer = 0f;
            }

            Material[] materials = targetRenderer.materials;
            materials[0].SetFloat("_GlowMin", glowValue);
            materials[2].SetFloat("_GlowMin", glowValue);
            materials[3].SetFloat("_GlowMin", glowValue);
    }
}

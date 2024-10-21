Shader "AgeOfJoy/WorldAligned_Wall_Roughness_Specular"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        _Tiling("Tiling", Float) = 1.0
        _Color("Color Multiplier", Color) = (1,1,1,1)
        _Metallic("Metallic", Range(0,1)) = 0.5 // Add metallic property
        _LightPosition("Light Position", Vector) = (0,10,0) // Add Light Position
        _SpecularFalloff("Specular Falloff", Float) = 8.0 // Add Specular Falloff (using power)
        _LightColor("Light Color", Color) = (1,1,1,1) // Add Light Color
    }
    
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows

        // Add instancing support for efficient GPU instancing.
        #pragma multi_compile_instancing

        struct Input
        {
            float2 uv_MainTex;
            float3 worldNormal;
            float3 worldPos;
        };

        sampler2D _MainTex;
        float _Tiling;
        fixed4 _Color;
        float _Metallic; // Declare metallic variable
        float3 _LightPosition; // Light position
        float _SpecularFalloff; // Specular falloff power
        fixed4 _LightColor; // Light color

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            // Scale the world position by the tiling factor
            float3 scaledWorldPos = IN.worldPos * _Tiling;

            // Determine UV coordinates based on world position and normal direction
            float2 uv;
            if (abs(IN.worldNormal.x) > abs(IN.worldNormal.z))
            {
                uv = scaledWorldPos.zy; // Surface facing X axis
            }
            else
            {
                uv = scaledWorldPos.xy; // Surface facing Z axis
            }

            // Sample the texture and apply the color multiplier
            fixed4 c = tex2D(_MainTex, uv) * _Color;
            o.Albedo = c.rgb;

            // Use the alpha channel of the texture for smoothness
            o.Smoothness = c.a;

            // Set the metallic value
            o.Metallic = _Metallic;

            // Lambertian reflection (diffuse term)
            float3 normal = normalize(IN.worldNormal);
            float3 lightDir = normalize(_LightPosition - IN.worldPos); // Light direction (light to pixel)
            float lambertian = max(dot(normal, lightDir), 0.0); // Diffuse reflection term

            // Calculate the fake specular
            float3 viewDir = normalize(UnityWorldSpaceViewDir(IN.worldPos)); // View direction (camera to pixel)
            float3 reflectDir = reflect(-lightDir, normal); // Reflect light direction off the normal

            // Use the smoothness value from the alpha channel to modulate the specular sharpness
            float smoothness = c.a; // Retrieve smoothness from alpha channel

            // Modify the specular power with smoothness to control the sharpness of the highlight
            float spec = pow(max(dot(viewDir, reflectDir), 0.0), _SpecularFalloff * smoothness);

            // Apply light color and intensity to the specular, modulated by diffuse (Lambertian)
            fixed3 specularHighlight = lambertian * spec * _LightColor.rgb;

            // Blend the specular highlight with the diffuse texture color
            fixed3 finalSpecular = specularHighlight * c.rgb;

            // Add the blended specular highlight to the emissive output
            o.Emission = finalSpecular;
        }
        ENDCG
    }
    FallBack "Diffuse"
}

// GPU version of the player equipment recolor. Mirrors HsvSwapper + the swap strategies:
// a pixel is replaced only when its hue, saturation, value and alpha are all inside the (inclusive) ranges
// and it lies inside the optional pixel block.
Shader "Hidden/Nitrox/ColorSwap"
{
    Properties
    {
        // Original texture. Filter conditions and the replacement are always computed from this one.
        _MainTex ("Original", 2D) = "white" {}
        // Result of the previous filter pass. Pixels not covered by this pass are copied from it.
        _AccumTex ("Accumulated", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _AccumTex;

            float4 _HueRange;   // min, max
            float4 _SatRange;
            float4 _ValRange;
            float4 _AlphaRange;
            float4 _Replacement; // hue, saturation, value of the player color
            float _Mode;         // 0 = replace hue only, 1 = replace hue, saturation and value
            float4 _Block;       // normalized x, y, width, height. Whole texture when width/height are 1

            // Same math as UnityEngine.Color.RGBToHSV
            float3 RGBToHSV(float3 c)
            {
                float maxC = max(c.r, max(c.g, c.b));
                float minC = min(c.r, min(c.g, c.b));
                float d = maxC - minC;
                float h = 0;
                if (d > 0)
                {
                    if (c.r >= maxC) h = (c.g - c.b) / d + (c.g < c.b ? 6 : 0);
                    else if (c.g >= maxC) h = 2 + (c.b - c.r) / d;
                    else h = 4 + (c.r - c.g) / d;
                    h /= 6;
                }
                return float3(h, maxC > 0 ? d / maxC : 0, maxC);
            }

            // Same math as UnityEngine.Color.HSVToRGB
            float3 HSVToRGB(float3 hsv)
            {
                float3 rgb = saturate(abs(frac(hsv.x + float3(0, 2.0 / 3.0, 1.0 / 3.0)) * 6 - 3) - 1);
                return hsv.z * lerp(float3(1, 1, 1), rgb, hsv.y);
            }

            bool Covers(float2 range, float v)
            {
                return range.x <= v && v <= range.y;
            }

            fixed4 frag(v2f_img i) : SV_Target
            {
                fixed4 accum = tex2D(_AccumTex, i.uv);

                float2 inBlock = (i.uv - _Block.xy) / _Block.zw;
                if (any(inBlock < 0) || any(inBlock >= 1))
                {
                    return accum;
                }

                fixed4 orig = tex2D(_MainTex, i.uv);
                float3 hsv = RGBToHSV(orig.rgb);

                if (!(Covers(_HueRange.xy, hsv.x) && Covers(_SatRange.xy, hsv.y) &&
                      Covers(_ValRange.xy, hsv.z) && Covers(_AlphaRange.xy, orig.a)))
                {
                    return accum;
                }

                float3 swapped = _Mode < 0.5
                    ? HSVToRGB(float3(_Replacement.x, hsv.y, hsv.z))
                    : HSVToRGB(_Replacement.xyz);
                return fixed4(swapped, orig.a);
            }
            ENDCG
        }
    }
}

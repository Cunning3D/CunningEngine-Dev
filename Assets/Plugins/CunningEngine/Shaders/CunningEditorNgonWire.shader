Shader "Hidden/CunningEditorNgonWire" {
    Properties {
        _Color ("Color", Color) = (1,1,1,1)
        _LineWidth ("Line Width", Float) = 2
    }

    SubShader {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+50" }

        Pass {
            Blend SrcAlpha OneMinusSrcAlpha
            AlphaToMask On
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float _LineWidth;

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 other : TEXCOORD1;
            };

            struct v2f {
                float4 pos : SV_POSITION;
                float side : TEXCOORD0;
            };

            v2f vert(appdata v) {
                v2f o;

                float4 currentClip = UnityObjectToClipPos(v.vertex);
                float4 otherClip = UnityObjectToClipPos(float4(v.other.xyz, 1.0));

                float2 currentNdc = currentClip.xy / max(currentClip.w, 1e-6);
                float2 otherNdc = otherClip.xy / max(otherClip.w, 1e-6);
                float2 dir = otherNdc - currentNdc;
                float dirLen = length(dir);
                dir = dirLen > 1e-6 ? dir / dirLen : float2(1.0, 0.0);

                float2 perp = float2(-dir.y, dir.x);
                float2 pixelToNdc = 2.0 / _ScreenParams.xy;
                float2 offsetNdc = perp * (v.uv.y * max(_LineWidth, 0.5) * 0.5) * pixelToNdc;

                currentClip.xy += offsetNdc * currentClip.w;
                o.pos = currentClip;
                o.side = v.uv.y;
                return o;
            }

            half4 frag(v2f i) : SV_Target {
                float width = abs(i.side);
                float aa = max(fwidth(width) * 2.0, 1e-4);
                float alpha = 1.0 - smoothstep(1.0 - aa * 2.0, 1.0, width);
                return half4(_Color.rgb, _Color.a * alpha);
            }
            ENDHLSL
        }
    }
}

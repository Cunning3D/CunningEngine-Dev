Shader "Hidden/CunningDepthOnly" {
    SubShader {
        Tags { "RenderType"="Opaque" "Queue"="Geometry-1" }
        Pass {
            ZWrite On
            ZTest LEqual
            Cull Back
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; };
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); return o; }
            half4 frag(v2f i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}


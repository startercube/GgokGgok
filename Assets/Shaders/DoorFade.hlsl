#ifndef DOOR_FADE_INCLUDED
#define DOOR_FADE_INCLUDED

// DoorFadeLit.shader 공용 — URP 라이브러리 include 뒤에 넣는다
float  _Fade;
float  _FadeSoftness;
float  _FadeMinY;
float  _FadeMaxY;
half4  _FadeEdgeColor;

// 경계선이 아래(-softness)에서 위(1)로 올라가며, 경계선 아래는 투명
half DoorFadeAlpha(float3 positionWS)
{
    float y = TransformWorldToObject(positionWS).y;
    float h = saturate((y - _FadeMinY) / max(_FadeMaxY - _FadeMinY, 1e-4));
    float soft = max(_FadeSoftness, 1e-4);
    float edge = lerp(-soft, 1.0, _Fade);
    return saturate((h - edge) / soft);
}

#endif

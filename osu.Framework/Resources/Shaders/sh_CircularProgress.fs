﻿#define HIGH_PRECISION_VERTEX

#include "sh_Utils.h"
#include "sh_Masking.h"
#include "sh_TextureWrapping.h"
#include "sh_CircularProgressUtils.h"

IN(2) highp vec2 v_TexCoord;

uniform mediump float progress;
uniform mediump float innerRadius;
uniform highp float texelSize;
uniform bool roundedCaps;

UNIFORM_TEXTURE(1, m_Texture, m_Sampler);

OUT(0) vec4 o_Colour;

void main(void)
{
    highp vec2 resolution = v_TexRect.zw - v_TexRect.xy;
    highp vec2 pixelPos = v_TexCoord / resolution;
    
    highp vec2 wrappedCoord = wrap(v_TexCoord, v_TexRect);
    lowp vec4 textureColour = getRoundedColor(wrappedSampler(wrappedCoord, v_TexRect, m_Texture, m_Sampler, -0.9), wrappedCoord);

    o_Colour = vec4(textureColour.rgb, textureColour.a * progressAlphaAt(pixelPos, progress, innerRadius, roundedCaps, texelSize));
}

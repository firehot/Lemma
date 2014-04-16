#include "EffectCommon.fxh"
#include "EffectSamplers.fxh"
#include "BloomCommon.fxh"

void DownsampleBloomPS(in PostProcessPSInput input, out float4 output : COLOR0)
{
	float2 pixelSize = 1.0f / SourceDimensions0;

	float2 pos = floor(input.texCoord * SourceDimensions0) * pixelSize;

	float3 bl = tex2D(SourceSampler0, pos + float2(0, 0));
	float3 br = tex2D(SourceSampler0, pos + float2(pixelSize.x, 0));
	float3 tl = tex2D(SourceSampler0, pos + float2(0, pixelSize.y));
	float3 tr = tex2D(SourceSampler0, pos + pixelSize);

	output = float4(EncodeColor(max(max(max(bl, br), max(tl, tr)) - BloomThreshold, float3(0, 0, 0))), 1);
}

void DownsampleDepthPS(in PostProcessPSInput input, out float4 output : COLOR0)
{
	float2 pixelSize = 1.0f / SourceDimensions0;
	float depth0 = tex2D(SourceSampler0, input.texCoord).r;
	float depth1 = tex2D(SourceSampler0, input.texCoord + float2(pixelSize.x, 0)).r;
	float depth2 = tex2D(SourceSampler0, input.texCoord + float2(0, pixelSize.y)).r;
	float depth3 = tex2D(SourceSampler0, input.texCoord + pixelSize).r;
	output = float4(max(max(depth0, depth1), max(depth2, depth3)), 0, 0, 1);
}

technique DownsampleBloom
{
	pass p0
	{
		VertexShader = compile vs_3_0 PostProcessVS();
		PixelShader = compile ps_3_0 DownsampleBloomPS();
		
		ZEnable = false;
		ZWriteEnable = false;
		AlphaBlendEnable = false;
	}
}

technique DownsampleDepth
{
	pass p0
	{
		VertexShader = compile vs_3_0 PostProcessVS();
		PixelShader = compile ps_3_0 DownsampleDepthPS();
		
		ZEnable = false;
		ZWriteEnable = false;
		AlphaBlendEnable = false;
	}
}
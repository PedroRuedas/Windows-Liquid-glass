// Liquid Glass — refração, aberração cromática, brilho especular e sombra
// sobre uma forma de retângulo arredondado com bordas suavizadas.
//
// Entrada (s0): recorte do papel de parede exatamente atrás do elemento.
// Todas as medidas estão em pixels independentes de dispositivo (DIP).
//
// Compilar: fxc /nologo /T ps_3_0 /E main /O3 /Fo LiquidGlass.ps LiquidGlass.fx

sampler2D Backdrop : register(s0);

float2 Size : register(c0);              // tamanho do elemento (forma + margem da sombra)
float Padding : register(c1);            // margem entre a borda do elemento e a forma
float CornerRadius : register(c2);
float BezelWidth : register(c3);         // largura da faixa curva da borda que refrata
float Refraction : register(c4);         // deslocamento máximo da refração na borda
float ChromaticAberration : register(c5);
float4 Tint : register(c6);              // rgb = cor, a = intensidade
float Highlight : register(c7);
float Shadow : register(c8);
float Saturation : register(c9);
float2 LightDirection : register(c10);   // direção de onde vem a luz (normalizada)

static const float3 LumaWeights = float3(0.2126, 0.7152, 0.0722);

float RoundedBoxDistance(float2 p, float2 halfSize, float radius)
{
    float2 q = abs(p) - halfSize + radius;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
}

float2 RoundedBoxNormal(float2 p, float2 halfSize, float radius)
{
    const float e = 0.5;
    float dx = RoundedBoxDistance(p + float2(e, 0), halfSize, radius) - RoundedBoxDistance(p - float2(e, 0), halfSize, radius);
    float dy = RoundedBoxDistance(p + float2(0, e), halfSize, radius) - RoundedBoxDistance(p - float2(0, e), halfSize, radius);
    float2 gradient = float2(dx, dy);
    return gradient / max(length(gradient), 0.0001);
}

float3 SampleBackdrop(float2 p)
{
    return tex2D(Backdrop, p / Size + 0.5).rgb;
}

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 p = (uv - 0.5) * Size;
    float2 halfShape = max(Size * 0.5 - Padding, 1.0);
    float radius = min(CornerRadius, min(halfShape.x, halfShape.y));

    float distance = RoundedBoxDistance(p, halfShape, radius);
    float2 normal = RoundedBoxNormal(p, halfShape, radius);
    float depth = max(-distance, 0.0);

    // Perfil da borda: 1 na borda, 0 a partir de BezelWidth para dentro.
    float bezel = saturate(1.0 - depth / max(BezelWidth, 0.001));
    float lens = bezel * bezel;

    // Refração: a borda curva "puxa" o conteúdo de dentro da forma.
    float2 offset = normal * Refraction * lens;
    float spread = 1.0 + ChromaticAberration * lens;
    float3 color;
    color.r = SampleBackdrop(p - offset * spread).r;
    color.g = SampleBackdrop(p - offset).g;
    color.b = SampleBackdrop(p - offset / spread).b;

    // Vidro levemente mais vivo e claro que o fundo.
    float luma = dot(color, LumaWeights);
    color = lerp(luma.xxx, color, Saturation);
    color = lerp(color, Tint.rgb, Tint.a);
    color += lens * bezel * 0.06;

    // Brilho especular: aro fino iluminado do lado da luz e reflexo mais fraco do lado oposto.
    float rim = saturate(1.0 - depth / 1.6);
    float facing = dot(normal, -LightDirection);
    float key = pow(saturate(facing), 1.5);
    float back = pow(saturate(-facing), 2.0) * 0.45;
    float sheen = pow(bezel, 3.0) * (0.5 + 0.5 * facing) * 0.18;
    color += (rim * (key + back + 0.12) + sheen) * Highlight;

    // Contorno suavizado.
    float coverage = saturate(0.5 - distance);

    // Sombra suave fora da forma, deslocada para longe da luz.
    float shadowDistance = RoundedBoxDistance(p + LightDirection * 2.0, halfShape, radius);
    float shadowFalloff = 1.0 - smoothstep(-3.0, max(Padding, 1.0), shadowDistance);
    float shadowAlpha = Shadow * shadowFalloff * shadowFalloff;

    float alpha = coverage + shadowAlpha * (1.0 - coverage);
    return float4(saturate(color) * coverage, alpha);
}

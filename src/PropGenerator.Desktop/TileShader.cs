namespace PropGenerator.Desktop;

/// <summary>
/// The view's shaders (GLSL 3.30 core, or 3.00 ES with its header), in two passes:
/// <list type="number">
/// <item>Compose: a full-screen triangle drawn into a colour texture of the layers' own size, one pixel per layer texel,
/// turns the painter's seven tone layers into colour with exactly the formula written at the top of ToneComposer.cs.
/// It runs only when the layers, the palette or the tone change; mipmaps are then made from its result.</item>
/// <item>Floor: the tiles sample only that colour texture, mipmapped, so far rows and thin seams are averaged instead
/// of hitting or missing single texels. Nothing is lit: the painting already has its light and shade.</item>
/// </list>
/// The layers themselves are never mipmapped: a region number cannot be averaged, and the formula is not linear.
/// Around the floor and the rock: the sky, the ground and its grid, and the rock lit as easy-props lights it (with
/// three.js's tone mapping and fog), and the depth-only pass of the sun's shadow.
/// </summary>
internal static class TileShader
{
    /// <summary>The first line(s) for desktop OpenGL.</summary>
    public const string DesktopHead = "#version 330 core\n";

    /// <summary>The first lines for OpenGL ES: the layers are read at high precision.</summary>
    public const string EsHead = "#version 300 es\nprecision highp float;\nprecision highp int;\nprecision highp sampler2D;\nprecision highp sampler2DShadow;\n";

    /// <summary>A triangle that covers the whole target, from the vertex number alone (no vertex buffer).</summary>
    public const string ComposeVertex = """
        void main()
        {
            vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
            gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    /// <summary>Places the tile in the floor and on screen, and passes its atlas position on.</summary>
    public const string Vertex = """
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec2 aUv;
        uniform mat4 uMvp;
        out vec2 vUv;
        void main()
        {
            vUv = aUv;
            gl_Position = uMvp * vec4(aPos, 1.0);
        }
        """;

    /// <summary>
    /// ToneComposer's formula, one output pixel per layer texel: the target is uSize pixels square, so each pixel's
    /// centre is a texel's centre and the layers are read exactly (no filtering, no shift).
    /// </summary>
    public const string ComposeFragment = """
        out vec4 fragColour;
        uniform sampler2D uMarks;
        uniform sampler2D uRest;
        uniform sampler2D uDrift;
        uniform sampler2D uRegion;
        uniform sampler2D uBaseMul;
        uniform sampler2D uSeam;
        uniform sampler2D uAa;
        uniform vec3 uBase;
        uniform vec3 uLitTint;
        uniform vec3 uShadeTint;
        uniform vec3 uRedder;
        uniform vec3 uGreyer;
        uniform vec3 uEndMul;
        uniform vec3 uSeamDeep;
        uniform vec3 uSeamLight;
        uniform float uContrast;
        uniform float uDriftAmount;
        uniform float uSize;
        void main()
        {
            vec2 uv = gl_FragCoord.xy / uSize;
            float v = texture(uMarks, uv).r * uContrast + texture(uRest, uv).r;
            float bm = texture(uBaseMul, uv).r;
            float h = clamp(texture(uDrift, uv).r * uDriftAmount, -1.0, 1.0);
            float region = texture(uRegion, uv).r;
            float aa = texture(uAa, uv).r;
            float seam = texture(uSeam, uv).r;
            vec3 sc = uSeamLight + (uSeamDeep - uSeamLight) * seam;
            vec3 col;
            if (region > 0.5 && region < 1.5)
            {
                col = uBase * bm * uEndMul * (1.0 + v);
            }
            else
            {
                vec3 b = uBase * bm;
                vec3 tint = v > 0.0
                    ? vec3(1.0) + (uLitTint - vec3(1.0)) * clamp(v / 0.15, 0.0, 1.0)
                    : vec3(1.0) + (uShadeTint - vec3(1.0)) * clamp(-v / 0.15, 0.0, 1.0);
                vec3 dr = h > 0.0
                    ? vec3(1.0) + (uRedder - vec3(1.0)) * h
                    : vec3(1.0) + (uGreyer - vec3(1.0)) * -h;
                col = clamp(b * (1.0 + v) * tint * dr, 0.0, 1.0);
            }
            col = (1.0 - aa) * col + aa * sc;
            if (region > 1.5)
            {
                col = sc;
            }
            fragColour = vec4(clamp(col, 0.0, 1.0), 1.0);
        }
        """;

    /// <summary>
    /// The floor: the composed colour, mipmapped. Before any paint has arrived, a flat neutral colour. uUvShift moves a
    /// preview's texel grid onto the full paint's (0.5/N - 0.5/1024 for a preview of N pixels, 0 at 1024): the colour
    /// texture has the preview's own N x N grid, so the shift belongs where the atlas coordinates are used, here.
    /// </summary>
    public const string Fragment = """
        in vec2 vUv;
        out vec4 fragColour;
        uniform sampler2D uColour;
        uniform vec2 uUvShift;
        uniform int uHasColour;
        void main()
        {
            if (uHasColour == 0)
            {
                fragColour = vec4(0.42, 0.40, 0.38, 1.0);
                return;
            }
            fragColour = vec4(texture(uColour, vUv + uUvShift).rgb, 1.0);
        }
        """;

    /// <summary>Places a mesh from Blender (already in the world's frame) on screen.</summary>
    public const string PieceVertex = """
        layout(location = 0) in vec3 aPos;
        uniform mat4 uMvp;
        out vec3 vPos;
        void main()
        {
            vPos = aPos;
            gl_Position = uMvp * vec4(aPos, 1.0);
        }
        """;

    /// <summary>
    /// A mesh from Blender in plain neutral grey: lit flat from a fixed light above and a light at the camera, the
    /// face turned towards the viewer whichever way its triangles wind.
    /// </summary>
    public const string PieceFragment = """
        in vec3 vPos;
        out vec4 fragColour;
        uniform vec3 uEye;
        void main()
        {
            vec3 n = normalize(cross(dFdx(vPos), dFdy(vPos)));
            vec3 v = normalize(uEye - vPos);
            if (dot(n, v) < 0.0)
            {
                n = -n;
            }
            float key = max(dot(n, normalize(vec3(0.35, 0.25, 0.9))), 0.0);
            float head = max(dot(n, v), 0.0);
            float shade = 0.22 + 0.38 * key + 0.40 * head;
            fragColour = vec4(vec3(0.66) * shade, 1.0);
        }
        """;

    /// <summary>
    /// What the scene's shaders share: Khronos PBR Neutral tone mapping exactly as three.js's NeutralToneMapping
    /// (adapted from three.js, MIT, © 2010–2026 three.js authors, after the Khronos PBR Neutral specification), the
    /// exact sRGB curve, and three.js's linear fog, mixed in after the colour is encoded, as three.js does, by the depth
    /// in front of the camera.
    /// </summary>
    public const string Finish = """
        uniform float uExposure;
        uniform vec3 uFogColour;
        uniform vec2 uFog;
        uniform vec3 uEye;
        uniform vec3 uForward;
        vec3 neutral(vec3 color)
        {
            const float startCompression = 0.8 - 0.04;
            const float desaturation = 0.15;
            color *= uExposure;
            float x = min(color.r, min(color.g, color.b));
            float offset = x < 0.08 ? x - 6.25 * x * x : 0.04;
            color -= offset;
            float peak = max(color.r, max(color.g, color.b));
            if (peak < startCompression)
            {
                return color;
            }
            float d = 1.0 - startCompression;
            float newPeak = 1.0 - d * d / (peak + d - startCompression);
            color *= newPeak / peak;
            float g = 1.0 - 1.0 / (desaturation * (peak - newPeak) + 1.0);
            return mix(color, vec3(newPeak), g);
        }
        vec3 srgb(vec3 c)
        {
            c = max(c, vec3(0.0));
            vec3 lo = c * 12.92;
            vec3 hi = 1.055 * pow(c, vec3(0.41666)) - 0.055;
            return mix(hi, lo, vec3(lessThanEqual(c, vec3(0.0031308))));
        }
        vec3 finish(vec3 linear, vec3 pos)
        {
            vec3 c = srgb(neutral(linear));
            float fog = smoothstep(uFog.x, uFog.y, dot(pos - uEye, uForward));
            return mix(c, uFogColour, fog);
        }

        """;

    /// <summary>
    /// Places a vertex-coloured mesh (a rock, already in the world's frame; the ground, turned by uModel to the view's
    /// slope, the identity for everything else; the person for scale, moved by uOffset to where it stands) on screen, and where it falls in the sun's shadow map: moved along its normal by the
    /// normal bias first, as three.js does. aTex is where a printed vertex reads the map (u, v, and 1 when printed);
    /// a mesh without it reads (0, 0, 0), printed nowhere. aFlutter is how much a vertex flutters in the wind on top of
    /// the whole prop's uWindFoliage; a mesh without it reads 0. aShine is 1 - the vertex's roughness (a wet part
    /// shines); a mesh without it reads 0, fully rough.
    /// </summary>
    public const string RockVertex = """
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec3 aColour;
        layout(location = 3) in vec3 aTex;
        layout(location = 4) in float aFlutter;
        layout(location = 5) in float aShine;
        uniform mat4 uMvp;
        uniform mat4 uModel;
        uniform mat4 uShadowMatrix;
        uniform float uNormalBias;
        uniform vec3 uOffset;
        uniform float uWindStrength;
        uniform float uWindTime;
        uniform float uWindHeight;
        uniform float uWindReach;
        uniform float uWindFoliage;
        out vec3 vPos;
        out vec3 vNormal;
        out vec3 vColour;
        out vec3 vTex;
        out vec4 vShadow;
        out float vRough;
        void main()
        {
            vec3 world = (uModel * vec4(aPos, 1.0)).xyz + uOffset;
            vec3 normal = mat3(uModel) * aNormal;
            if (uWindStrength > 0.0)
            {
                // easy-props' live wind with the pivot at the origin, Z-up: height is z, the ground is xy, and its
                // direction (1, 0, 0.3) and local (x, y, z) Y-up are (1, -0.3, 0) and (x, z, -y) here.
                float windOut = clamp(length(world.xy) / uWindReach, 0.0, 1.0);
                float windSway = pow(clamp(world.z / uWindHeight, 0.0, 1.0), 1.5) * (0.6 + 0.4 * windOut);
                // A whole foliage prop flutters everywhere; a tree only where its vertices say (its leaves, not its wood or fruit).
                float windFlutter = clamp(uWindFoliage + aFlutter, 0.0, 1.0) * (0.5 + 0.5 * windOut);
                float windWave = sin(uWindTime * 1.1) + 0.4 * sin(uWindTime * 2.53);
                vec3 windPush = normalize(vec3(1.0, -0.3, 0.0)) * windWave * windSway * 0.25;
                float windLocal = world.x * 3.1 + world.z * 2.3 - world.y * 3.7;
                world += windPush * uWindStrength
                    + normalize(normal) * sin(uWindTime * 5.0 + windLocal) * windFlutter * 0.04 * uWindStrength;
            }

            vPos = world;
            vNormal = normal;
            vColour = aColour;
            vTex = aTex;
            vRough = 1.0 - clamp(aShine, 0.0, 1.0);
            vShadow = uShadowMatrix * vec4(world + normalize(normal) * uNormalBias, 1.0);
            gl_Position = uMvp * vec4(world, 1.0);
        }
        """;

    /// <summary>
    /// A rock or the ground as easy-props draws it with three.js. Lit (uMode 0): a non-metal MeshStandardMaterial, as
    /// rough as its vertex says (1 unless the part is wet: three.js's roughness, floored at 0.0525 and widened by how fast
    /// the normal turns across the pixel), its colour the vertex colour times uBase, lit by the hemisphere light, the sun (with its
    /// soft shadow: five hardware-filtered taps on a Vogel disk turned per pixel, three.js's PCF) and the fill, with
    /// three.js's GGX specular, Lambert diffuse and energy terms (the DFG term is Karis' fit, which three.js used before
    /// its lookup table). Flat (1): the colour alone. Wireframe (2): lines in uWire. All are tone mapped, encoded and
    /// fogged. A printed point (a leaf card) multiplies its colour by uMap (sRGB, so read linear) and is not drawn where
    /// the map's alpha is under uAlphaCut, as three.js's map and alphaTest; both sides are lit, its normal turned to the
    /// eye. Adapted from three.js (MIT, © 2010–2026 three.js authors).
    /// </summary>
    public const string RockFragment = """
        in vec3 vPos;
        in vec3 vNormal;
        in vec3 vColour;
        in vec3 vTex;
        in vec4 vShadow;
        in float vRough;
        uniform sampler2D uMap;
        uniform float uAlphaCut;
        uniform sampler2D uBark;
        uniform sampler2D uRelief;
        out vec4 fragColour;
        uniform int uMode;
        uniform vec3 uBase;
        uniform vec3 uWire;
        uniform vec3 uHemiSky;
        uniform vec3 uHemiGround;
        uniform vec3 uSunDir;
        uniform vec3 uSunColour;
        uniform vec3 uFillDir;
        uniform vec3 uFillColour;
        uniform int uShadowOn;
        uniform float uShadowStrength;
        uniform float uShadowBias;
        uniform float uShadowRadius;
        uniform vec2 uShadowSize;
        uniform sampler2DShadow uShadowMap;
        const float PI = 3.141592653589793;
        float ign(vec2 p)
        {
            return fract(52.9829189 * fract(dot(p, vec2(0.06711056, 0.00583715))));
        }
        vec2 vogel(int i, int count, float phi)
        {
            const float goldenAngle = 2.399963229728653;
            float r = sqrt((float(i) + 0.5) / float(count));
            float theta = float(i) * goldenAngle + phi;
            return vec2(cos(theta), sin(theta)) * r;
        }
        float sunShadow()
        {
            vec3 c = vShadow.xyz / vShadow.w;
            c.z += uShadowBias;
            bool inside = c.x >= 0.0 && c.x <= 1.0 && c.y >= 0.0 && c.y <= 1.0 && c.z <= 1.0;
            if (!inside)
            {
                return 1.0;
            }
            float radius = uShadowRadius / uShadowSize.x;
            float phi = ign(gl_FragCoord.xy) * 2.0 * PI;
            float s = 0.0;
            for (int i = 0; i < 5; i++)
            {
                s += texture(uShadowMap, vec3(c.xy + vogel(i, 5, phi) * radius, c.z));
            }
            return mix(1.0, s * 0.2, uShadowStrength);
        }
        vec3 schlick(vec3 f0, float f90, float dotVH)
        {
            float fresnel = exp2((-5.55473 * dotVH - 6.98316) * dotVH);
            return f0 * (1.0 - fresnel) + f90 * fresnel;
        }
        vec2 dfgApprox(float dotNV, float roughness)
        {
            const vec4 c0 = vec4(-1.0, -0.0275, -0.572, 0.022);
            const vec4 c1 = vec4(1.0, 0.0425, 1.04, -0.04);
            vec4 r = roughness * c0 + c1;
            float a004 = min(r.x * r.x, exp2(-9.28 * dotNV)) * r.x + r.y;
            return vec2(-1.04, 1.04) * a004 + r.zw;
        }
        vec3 direct(vec3 l, vec3 light, vec3 n, vec3 v, vec3 albedo, vec3 f0, vec3 compensation, float roughness)
        {
            float alpha = roughness * roughness;
            float dotNL = clamp(dot(n, l), 0.0, 1.0);
            vec3 irradiance = dotNL * light;
            vec3 h = normalize(l + v);
            float dotNV = clamp(dot(n, v), 0.0, 1.0);
            float dotNH = clamp(dot(n, h), 0.0, 1.0);
            float dotVH = clamp(dot(v, h), 0.0, 1.0);
            vec3 f = schlick(f0, 1.0, dotVH);
            float a2 = alpha * alpha;
            float gV = dotNL * sqrt(a2 + (1.0 - a2) * dotNV * dotNV);
            float gL = dotNV * sqrt(a2 + (1.0 - a2) * dotNL * dotNL);
            float vis = 0.5 / max(gV + gL, 1e-6);
            float denom = dotNH * dotNH * (a2 - 1.0) + 1.0;
            float d = a2 / (PI * denom * denom);
            vec3 specular = irradiance * f * (vis * d) * compensation;
            vec3 diffuse = irradiance * (albedo / PI) * (1.0 - f);
            return specular + diffuse;
        }
        // three.js's getTangentFrame: the surface's frame from screen derivatives, so a normal map needs no tangents.
        mat3 tangentFrame(vec3 pos, vec3 n, vec2 uv)
        {
            vec3 q0 = dFdx(pos);
            vec3 q1 = dFdy(pos);
            vec2 st0 = dFdx(uv);
            vec2 st1 = dFdy(uv);
            vec3 q1perp = cross(q1, n);
            vec3 q0perp = cross(n, q0);
            vec3 t = q1perp * st0.x + q0perp * st1.x;
            vec3 b = q1perp * st0.y + q0perp * st1.y;
            float det = max(dot(t, t), dot(b, b));
            float scale = det == 0.0 ? 0.0 : inversesqrt(det);
            return mat3(t * scale, b * scale, n);
        }
        void main()
        {
            if (uMode == 2)
            {
                fragColour = vec4(finish(uWire, vPos), 1.0);
                return;
            }
            float faceDirection = gl_FrontFacing ? 1.0 : -1.0;
            vec3 plainNormal = normalize(vNormal) * faceDirection;
            mat3 tbn = tangentFrame(vPos, plainNormal, vTex.xy);
            // three.js's roughness: at least 0.0525, plus how fast the unbent normal turns across the pixel.
            vec3 dxy = max(abs(dFdx(plainNormal)), abs(dFdy(plainNormal)));
            float roughness = min(max(vRough, 0.0525) + max(max(dxy.x, dxy.y), dxy.z), 1.0);
            vec3 albedo = vColour * uBase;
            bool bark = vTex.z > 1.5;
            if (bark)
            {
                albedo *= texture(uBark, vTex.xy).rgb;
            }
            else if (vTex.z > 0.5)
            {
                vec4 texel = texture(uMap, vTex.xy);
                if (texel.a < uAlphaCut)
                {
                    discard;
                }
                albedo *= texel.rgb;
            }
            if (uMode == 1)
            {
                fragColour = vec4(finish(albedo, vPos), 1.0);
                return;
            }
            vec3 n = normalize(vNormal);
            vec3 v = normalize(uEye - vPos);
            if (bark)
            {
                // The bark's relief bends the light as three.js's normalMap does (normalScale 1, 1).
                tbn[0] *= faceDirection;
                tbn[1] *= faceDirection;
                n = normalize(tbn * (texture(uRelief, vTex.xy).xyz * 2.0 - 1.0));
            }
            else if (dot(n, v) < 0.0)
            {
                n = -n;
            }
            float dotNV = clamp(dot(n, v), 0.0, 1.0);
            vec3 f0 = vec3(0.04);
            vec2 dfg = dfgApprox(dotNV, roughness);
            vec3 compensation = 1.0 + f0 * (1.0 / (dfg.x + dfg.y) - 1.0);
            float shadow = uShadowOn == 1 ? sunShadow() : 1.0;
            vec3 colour = direct(uSunDir, uSunColour * shadow, n, v, albedo, f0, compensation, roughness);
            colour += direct(uFillDir, uFillColour, n, v, albedo, f0, compensation, roughness);
            vec3 hemi = mix(uHemiGround, uHemiSky, 0.5 * n.z + 0.5);
            vec3 single = f0 * dfg.x + dfg.y;
            float ess = dfg.x + dfg.y;
            vec3 favg = f0 + (1.0 - f0) * 0.047619;
            vec3 multi = single * favg / (1.0 - (1.0 - ess) * favg) * (1.0 - ess);
            colour += hemi * (albedo / PI) * (1.0 - single - multi);
            fragColour = vec4(finish(colour, vPos), 1.0);
        }
        """;

    /// <summary>Places a point of the shadow caster in the sun's clip space, for the depth-only shadow pass.</summary>
    public const string DepthVertex = """
        layout(location = 0) in vec3 aPos;
        layout(location = 3) in vec3 aTex;
        uniform mat4 uMvp;
        out vec3 vTex;
        void main()
        {
            vTex = aTex;
            gl_Position = uMvp * vec4(aPos, 1.0);
        }
        """;

    /// <summary>The shadow pass writes depth only, and none where a printed point is cut out, so a leaf card casts a leaf's shadow.</summary>
    public const string DepthFragment = """
        in vec3 vTex;
        uniform sampler2D uMap;
        uniform float uAlphaCut;
        void main()
        {
            if (vTex.z > 0.5 && vTex.z < 1.5 && texture(uMap, vTex.xy).a < uAlphaCut)
            {
                discard;
            }
        }
        """;

    /// <summary>
    /// The sky, as easy-props' canvas gradient drawn across the view: uTop at the top, uHorizon from 62 % of the way
    /// down to the bottom, mixed in sRGB as a canvas mixes. Not tone mapped (three.js leaves an sRGB sky alone).
    /// </summary>
    public const string SkyFragment = """
        out vec4 fragColour;
        uniform vec3 uTop;
        uniform vec3 uHorizon;
        uniform float uHeight;
        uniform float uHorizonAt;
        void main()
        {
            float down = 1.0 - gl_FragCoord.y / uHeight;
            fragColour = vec4(mix(uTop, uHorizon, clamp(down / uHorizonAt, 0.0, 1.0)), 1.0);
        }
        """;

    /// <summary>
    /// Places a grid line's point on screen, turned by uModel (the view's slope for the grids, the identity for the
    /// collision shapes); vLocal keeps it untilted, for the rim check.
    /// </summary>
    public const string GridVertex = """
        layout(location = 0) in vec3 aPos;
        uniform mat4 uMvp;
        uniform mat4 uModel;
        out vec3 vPos;
        out vec3 vLocal;
        void main()
        {
            vec4 world = uModel * vec4(aPos, 1.0);
            vPos = world.xyz;
            vLocal = aPos;
            gl_Position = uMvp * world;
        }
        """;

    /// <summary>
    /// A grid line: its linear colour tone mapped, encoded and fogged, at the grid's opacity; nothing past the ground
    /// disc's rim (measured on the untilted grid, so a sloping disc's rim still cuts it).
    /// </summary>
    public const string GridFragment = """
        in vec3 vPos;
        in vec3 vLocal;
        out vec4 fragColour;
        uniform vec3 uColour;
        uniform float uOpacity;
        uniform float uRadius;
        void main()
        {
            if (length(vLocal.xy) > uRadius)
            {
                discard;
            }
            fragColour = vec4(finish(uColour, vPos), uOpacity);
        }
        """;
}

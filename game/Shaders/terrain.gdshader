shader_type spatial;
render_mode cull_disabled;

// Surface weights, indexed by SurfaceKind: COLOR = (grass, mowed grass, dirt, gravel),
// CUSTOM0 = (wheat, ploughed, rock, snow), CUSTOM1.r = needles.
// Texture layers (built by TerrainMaterial.cs): 0 grass, 1 dirt, 2 gravel, 3 soil, 4 rock, 5 snow, 6 needles.
uniform sampler2DArray albedo_layers : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray normal_layers : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
// Multiplier over each surface's layer colour (MapAmbience.TerrainTints). Some are pushed far past a "natural"
// multiplier: the sky ambient desaturates and cools textured, normal-mapped surfaces well below a flat-coloured
// vertex of the same albedo (wheat is tinted grass and needed 3.0/1.8/0.15 to read golden).
uniform vec3 tints[9];
uniform float tile_metres = 4.0;

const int SURFACES = 9;
const int ROCK = 6;
// Texture layer of each surface kind: grass, mowed grass and wheat are tinted grass.
const int LAYER[9] = { 0, 0, 1, 2, 0, 3, 4, 5, 6 };

varying vec4 w0;
varying vec4 w1;
varying float w2;
varying vec3 world_pos;

void vertex() {
    w0 = COLOR;
    w1 = CUSTOM0;
    w2 = CUSTOM1.r;
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

// Layers are sampled inside per-weight branches, so the UV gradients are taken outside them (textureGrad)
// to keep mipmapping correct across the branch boundaries.
struct Plane { vec2 uv; vec2 dx; vec2 dy; };

Plane plane(vec2 p) {
    vec2 uv = p / tile_metres;
    return Plane(uv, dFdx(uv), dFdy(uv));
}

// Two scales mixed so the repetition of a 4 m tile is not visible from the air.
vec3 albedo(int layer, Plane p) {
    float l = float(layer);
    return mix(textureGrad(albedo_layers, vec3(p.uv, l), p.dx, p.dy).rgb,
               textureGrad(albedo_layers, vec3(p.uv * 0.23, l), p.dx * 0.23, p.dy * 0.23).rgb, 0.4);
}

vec3 normal_tex(int layer, Plane p) {
    return textureGrad(normal_layers, vec3(p.uv, float(layer)), p.dx, p.dy).rgb;
}

void fragment() {
    float w[9] = { w0.r, w0.g, w0.b, w0.a, w1.r, w1.g, w1.b, w1.a, w2 };
    // Macro noise on the weights breaks the grid's stair-stepped transitions.
    float sum = 0.0;
    for (int k = 0; k < SURFACES; k++) {
        w[k] *= 0.75 + 0.5 * noise(world_pos.xz / 6.0 + float(k));
        sum += w[k];
    }
    for (int k = 0; k < SURFACES; k++) w[k] /= max(sum, 1e-4);

    // Slopes turn to rock whatever the painted surface, from about 39 degrees (normal.y = 0.78) to fully
    // at 52 degrees (normal.y = 0.62).
    vec3 normal_world = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
    float steep = 1.0 - smoothstep(0.62, 0.78, normal_world.y);
    for (int k = 0; k < SURFACES; k++) w[k] *= 1.0 - steep;
    w[ROCK] += steep;

    Plane ground = plane(world_pos.xz);
    // Cliffs: rock projected on the vertical xy and zy planes, blended by the normal, so it is not stretched.
    Plane cliff_x = plane(world_pos.zy);
    Plane cliff_z = plane(world_pos.xy);
    float side = abs(normal_world.x) / max(abs(normal_world.x) + abs(normal_world.z), 1e-4);

    vec3 c = vec3(0.0);
    vec3 n = vec3(0.0);
    for (int k = 0; k < SURFACES; k++) {
        if (w[k] <= 0.001) continue;
        int layer = LAYER[k];
        vec3 a = albedo(layer, ground);
        vec3 t = normal_tex(layer, ground);
        if (k == ROCK && steep > 0.001) {
            vec3 ca = mix(albedo(layer, cliff_z), albedo(layer, cliff_x), side);
            vec3 ct = mix(normal_tex(layer, cliff_z), normal_tex(layer, cliff_x), side);
            a = mix(a, ca, steep);
            t = mix(t, ct, steep);
        }
        c += a * tints[k] * w[k];
        n += t * w[k];
    }
    float macro = 0.85 + 0.3 * (0.6 * noise(world_pos.xz / 180.0) + 0.4 * noise(world_pos.xz / 55.0));
    ALBEDO = c * macro;
    NORMAL_MAP = n;
    ROUGHNESS = 1.0;
}

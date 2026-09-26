shader_type spatial;
render_mode cull_disabled;

// Surface weights: COLOR = (grass, mowed grass, dirt, gravel), CUSTOM0 = (wheat, ploughed, 0, 0).
uniform sampler2D grass_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D grass_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D dirt_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D dirt_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D soil_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D soil_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float tile_metres = 4.0;
// Tints over the grass texture for the surfaces that have no texture of their own. wheat_tint is pushed far
// past a "natural" multiplier (fix round 1): this scene's sky ambient (SkyTop/SkyHorizon are strongly blue,
// see ClubMap.Ambience) desaturates and cools textured, normal-mapped surfaces well below a flat-coloured
// vertex of the same albedo, so a modest golden tint rendered as a dull olive-green until boosted like this.
uniform vec3 grass_tint = vec3(1.0);
uniform vec3 mowed_tint = vec3(1.15, 1.25, 1.05);
uniform vec3 wheat_tint = vec3(3.0, 1.8, 0.15);
// Tints over the gravel/soil textures' own colour, for the same reason: both textures rendered brighter and
// bluer than intended (gravel clipped toward pale blue-white, soil toward mauve) until pulled back down and
// warmed up here.
uniform vec3 gravel_tint = vec3(1.25, 1.00, 0.72);
uniform vec3 soil_tint = vec3(1.55, 1.12, 0.42);

varying vec4 w0;
varying vec4 w1;
varying vec3 world_pos;

void vertex() {
    w0 = COLOR;
    w1 = CUSTOM0;
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

// Two scales mixed so the repetition of a 4 m tile is not visible from the air.
vec3 albedo(sampler2D s, vec2 uv) { return mix(texture(s, uv).rgb, texture(s, uv * 0.23).rgb, 0.4); }

void fragment() {
    vec2 uv = world_pos.xz / tile_metres;
    vec3 grass = albedo(grass_albedo, uv);
    vec3 c = grass * grass_tint * w0.r + grass * mowed_tint * w0.g + albedo(dirt_albedo, uv) * w0.b
           + albedo(gravel_albedo, uv) * gravel_tint * w0.a + grass * wheat_tint * w1.r
           + albedo(soil_albedo, uv) * soil_tint * w1.g;
    vec3 n = texture(grass_normal, uv).rgb * (w0.r + w0.g + w1.r) + texture(dirt_normal, uv).rgb * w0.b
           + texture(gravel_normal, uv).rgb * w0.a + texture(soil_normal, uv).rgb * w1.g;
    float macro = 0.85 + 0.3 * (0.6 * noise(world_pos.xz / 180.0) + 0.4 * noise(world_pos.xz / 55.0));
    ALBEDO = c * macro;
    NORMAL_MAP = n;
    ROUGHNESS = 1.0;
}

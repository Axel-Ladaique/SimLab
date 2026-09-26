shader_type spatial;
render_mode cull_disabled;

// Surface weights: COLOR = (grass, mowed grass, dirt, gravel), CUSTOM0 = (wheat, ploughed, 0, 0).
uniform vec3 grass_color : source_color = vec3(0.14, 0.34, 0.10);
uniform vec3 mowed_color : source_color = vec3(0.20, 0.44, 0.14);
uniform vec3 dirt_color : source_color = vec3(0.40, 0.31, 0.21);
uniform vec3 gravel_color : source_color = vec3(0.55, 0.52, 0.47);
uniform vec3 wheat_color : source_color = vec3(0.62, 0.55, 0.28);
uniform vec3 ploughed_color : source_color = vec3(0.30, 0.22, 0.15);

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

void fragment() {
    vec3 c = grass_color * w0.r + mowed_color * w0.g + dirt_color * w0.b + gravel_color * w0.a
           + wheat_color * w1.r + ploughed_color * w1.g;
    // Large-scale tint variation so the ground never looks uniform.
    float macro = 0.85 + 0.3 * (0.6 * noise(world_pos.xz / 180.0) + 0.4 * noise(world_pos.xz / 55.0));
    ALBEDO = c * macro;
    ROUGHNESS = 1.0;
}

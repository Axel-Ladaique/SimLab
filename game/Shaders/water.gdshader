shader_type spatial;

// Lake and stream surface (built by WaterMeshes.cs). COLOR.r = water depth / 15 m, baked per vertex. UV is in
// metres on the surface (the lake: world x and -y; the stream ribbon: along and across the ribbon), with tangents,
// so the waves are drawn in UV space and move along `flow` (m/s in UV), downstream on the ribbon.
uniform vec3 shallow_colour : source_color = vec3(0.18, 0.32, 0.30);
uniform vec3 deep_colour : source_color = vec3(0.04, 0.10, 0.14);
uniform vec3 sky_horizon : source_color = vec3(0.62, 0.74, 0.88);
uniform vec2 flow = vec2(0.05, 0.03);
uniform bool has_normal_map = false;
uniform sampler2D water_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
// Wave sizes (m) of the two samples.
uniform float large_scale = 9.0;
uniform float small_scale = 2.3;
// Stream ribbons only: pull the surface toward the camera by this fraction of its distance (capped at 4 m). The
// ribbon lies in a channel 2 m deep that the terrain's coarse far level of detail (16 m) does not resolve, and the
// 5 m ribbon steps cut the 4 m grid's triangles, so without it the far stream is buried in dashes.
uniform float camera_pull = 0.0;

varying float depth;

void vertex() {
    depth = COLOR.r;
    // POSITION is written unconditionally: once the shader writes it, Godot no longer computes it for any vertex.
    vec4 view_pos = MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
    float dist = length(view_pos.xyz);
    view_pos.xyz *= 1.0 - min(dist * camera_pull, 4.0) / max(dist, 1e-3);
    POSITION = PROJECTION_MATRIX * view_pos;
}

// Slope (d height / d u, d height / d v) of a sum of sines, a stand-in for the normal map. p is in wave periods
// and footprint is the pixel's size in the same unit: each sine fades out before it gets smaller than two pixels,
// which would otherwise alias into a regular lattice over the far lake.
vec2 sine_slope(vec2 p, float footprint) {
    vec2 s = vec2(0.0);
    const vec2 DIRS[4] = { vec2(1.0, 0.0), vec2(0.6, 0.8), vec2(-0.4, 0.92), vec2(0.8, -0.6) };
    const float FREQ[4] = { 1.0, 1.7, 2.9, 4.3 };
    for (int k = 0; k < 4; k++) {
        float phase = 6.2832 * FREQ[k] * dot(DIRS[k], p) + float(k) * 1.3;
        float fade = 1.0 - smoothstep(0.15, 0.5, footprint * FREQ[k]);
        s += DIRS[k] * cos(phase) * 0.08 * fade;
    }
    return s;
}

vec3 wave_normal(vec2 uv, float footprint) {
    vec2 a = (uv - flow * TIME) / large_scale;
    vec2 b = (uv - flow * 1.6 * TIME) / small_scale + vec2(0.37, 0.71);
    if (has_normal_map) {
        vec3 na = texture(water_normal, a).rgb * 2.0 - 1.0;
        vec3 nb = texture(water_normal, b).rgb * 2.0 - 1.0;
        return normalize(vec3(na.xy + nb.xy, na.z * nb.z));
    }
    vec2 slope = sine_slope(a, footprint / large_scale) + sine_slope(b, footprint / small_scale) * 0.6;
    return normalize(vec3(-slope, 1.0));
}

void fragment() {
    vec3 water = mix(shallow_colour, deep_colour, clamp(depth, 0.0, 1.0));
    // Tangent-space normal, encoded as NORMAL_MAP expects (0.5 = flat).
    NORMAL_MAP = wave_normal(UV, length(fwidth(UV))) * 0.5 + 0.5;
    NORMAL_MAP_DEPTH = 0.6;
    // Schlick Fresnel on the geometric normal (view space): the grazing surface reflects the horizon sky.
    float facing = clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0);
    float fresnel = 0.02 + 0.98 * pow(1.0 - facing, 5.0);
    ALBEDO = water * (1.0 - fresnel);
    EMISSION = sky_horizon * fresnel * 0.5;
    ROUGHNESS = 0.08;
    METALLIC = 0.0;
    SPECULAR = 0.6;
}

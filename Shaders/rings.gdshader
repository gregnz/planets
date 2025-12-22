shader_type spatial;
render_mode blend_mix, cull_disabled, unshaded;

uniform vec4 ring_color : source_color = vec4(0.6, 0.5, 0.4, 1.0);
uniform float seed : hint_range(0.0, 1000.0) = 0.0;
uniform float rotation_speed : hint_range(0.0, 1.0) = 0.02;

void vertex() {
    // Simple rotation in vertex shader to avoid complex fragment math
    float theta = TIME * rotation_speed;
    float c = cos(theta);
    float s = sin(theta);
    
    // Rotate around Y axis
    mat3 rot = mat3(
        vec3(c, 0, s),
        vec3(0, 1, 0),
        vec3(-s, 0, c)
    );
    
    // Rotate normal and vertex
    NORMAL = rot * NORMAL;
    VERTEX = rot * VERTEX;
}

// Simple pseudo-random function
float random(float x) {
    return fract(sin(x) * 43758.5453123);
}

void fragment() {
    // UV layout for TorusMesh:
    // UV.x = Around the ring (0 to 1)
    // UV.y = Inner edge to Outer edge (0 to 1)
    
    // Generate Radial Bands based on UV.y
    float band_pos = UV.y * 10.0 + seed;
    float noise = sin(band_pos * 3.0) * 0.5 + 0.5;
    
    // Add high frequency detail
    noise *= (sin(band_pos * 20.0) * 0.2 + 0.8);
    
    // Hard cuts for cartoon look (Banding)
    float cartoon_band = step(0.4, noise);
    
    // Main Ring Alpha
    // Fade out edges
    float edge_alpha = 1.0 - abs((UV.y - 0.5) * 2.0);
    edge_alpha = smoothstep(0.0, 0.2, edge_alpha);
    
    vec4 final_color = ring_color;
    
    // Gaps in the rings
    if (noise < 0.2) {
        discard; // Actual gaps
    }

    ALBEDO = final_color.rgb;
    ALPHA = final_color.a * edge_alpha * cartoon_band;
}

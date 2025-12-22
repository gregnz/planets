shader_type spatial;
render_mode blend_add, cull_disabled;

uniform vec4 albedo : source_color = vec4(0.0, 0.5, 1.0, 1.0);
uniform vec4 emission : source_color = vec4(0.5, 0.8, 1.0, 1.0);
uniform float speed = 5.0;
uniform float tiling = 10.0;
uniform float width = 0.3;

void fragment() {
    // UV.y is along length (scaled by Energy.cs)
    // UV.x is around circumference
    
    // Create scrolling noise/wave pattern
    float wave = sin(UV.y * tiling + TIME * speed);
    
    // Create strands around cylinder
    // sin(UV.x * 20.0) creates 10 strands?
    // Add wave distortion to X
    float distortion = wave * 0.1;
    float strands = sin((UV.x + distortion) * 20.0);
    
    // Sharp falloff for contrast (electric look)
    float core = smoothstep(1.0 - width, 1.0, strands);
    
    // Pulsing alpha
    float pulse = 0.5 + 0.5 * sin(TIME * 10.0);
    
    // Final color
    vec3 color_out = albedo.rgb * core;
    
    // Add emission core
    vec3 emission_out = emission.rgb * core * 2.0 * pulse;
    
    ALBEDO = color_out;
    EMISSION = emission_out;
    ALPHA = core; // Transparent where black
    
    // Clean up ends?
    // Not needed if geometry clips or uniform scale
}

shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_test_disabled;

uniform vec4 albedo : source_color = vec4(1.0, 0.0, 0.0, 1.0);
uniform float pulse_speed = 1.0;

void fragment() {
    // Pulse alpha
    float pulse = 0.1 + 0.5 * sin(TIME * pulse_speed);
    
    // Scanline effect (vertical stripes moving)
    float scan = 0.1 + 0.9 * sin(UV.y * 50.0 + TIME * 10.0);
    
    // Edge Fade (fresnel-like but UV based for torus)
    // Or just simple constant color for now
    vec3 final_color = albedo.rgb;
    
    ALBEDO = final_color;
    ALPHA = albedo.a *  pulse *  scan;
}

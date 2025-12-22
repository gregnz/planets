shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never;

// === UNIFORMS ===
uniform vec3 camera_position = vec3(0.0);
uniform float time_scale : hint_range(0.1, 2.0) = 0.3;

// Star layer configuration
uniform float layer1_scale : hint_range(50.0, 500.0) = 250.0;  // Far stars
uniform float layer2_scale : hint_range(20.0, 200.0) = 120.0;  // Mid stars
uniform float layer3_scale : hint_range(10.0, 100.0) = 60.0;   // Near stars

uniform float layer1_parallax : hint_range(0.0, 1.0) = 0.1;    // Slowest (far stars)
uniform float layer2_parallax : hint_range(0.0, 2.0) = 0.4;    // Medium
uniform float layer3_parallax : hint_range(0.0, 4.0) = 1.0;    // Fastest (near stars)

uniform float star_brightness : hint_range(0.5, 4.0) = 2.0;
uniform float twinkle_intensity : hint_range(0.0, 1.0) = 0.6;
uniform float twinkle_speed : hint_range(0.5, 5.0) = 2.5;

// Nebula settings
uniform bool enable_nebula = true;
uniform float nebula_intensity : hint_range(0.0, 0.6) = 0.25;
uniform vec3 nebula_color1 : source_color = vec3(0.4, 0.1, 0.5);  // Vivid purple
uniform vec3 nebula_color2 : source_color = vec3(0.1, 0.3, 0.6);  // Vivid blue
uniform vec3 nebula_color3 : source_color = vec3(0.5, 0.2, 0.3);  // Pink/red

// === NOISE FUNCTIONS ===

// Hash function for pseudo-random values
float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}

// 2D noise
float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    
    float a = hash(i);
    float b = hash(i + vec2(1.0, 0.0));
    float c = hash(i + vec2(0.0, 1.0));
    float d = hash(i + vec2(1.0, 1.0));
    
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

// Fractal Brownian Motion for nebula
float fbm(vec2 p) {
    float value = 0.0;
    float amplitude = 0.5;
    float frequency = 1.0;
    
    for (int i = 0; i < 5; i++) {
        value += amplitude * noise(p * frequency);
        amplitude *= 0.5;
        frequency *= 2.0;
    }
    return value;
}

// === STAR GENERATION ===

// Generate a single star layer - FIXED to use stable UV coordinates
vec3 star_layer(vec2 uv, float scale, float seed, float time) {
    vec2 grid_uv = uv * scale;
    vec2 grid_id = floor(grid_uv);
    vec2 grid_fract = fract(grid_uv) - 0.5;
    
    vec3 color = vec3(0.0);
    
    // Check 3x3 grid around current cell for stars
    for (int y = -1; y <= 1; y++) {
        for (int x = -1; x <= 1; x++) {
            vec2 offset = vec2(float(x), float(y));
            vec2 cell_id = grid_id + offset;
            
            // Random star existence check
            float h = hash(cell_id + seed);
            if (h > 0.94) {  // Slightly more stars
                vec2 star_pos = offset + vec2(hash(cell_id * 1.1 + seed), hash(cell_id * 1.3 + seed)) - 0.5;
                float dist = length(grid_fract - star_pos);
                
                // Star size variation - more variation for stylised look
                float base_size = 0.01 + hash(cell_id * 2.0 + seed) * 0.04;
                
                // Twinkle effect - varies size and brightness
                float twinkle_phase = hash(cell_id * 5.0) * 6.28;
                float twinkle_freq = 1.0 + hash(cell_id * 3.0) * twinkle_speed;
                float twinkle = sin(time * twinkle_freq + twinkle_phase) * 0.5 + 0.5;
                
                float size = base_size * (1.0 + twinkle * twinkle_intensity * 0.5);
                float brightness = smoothstep(size, 0.0, dist);
                brightness *= mix(0.6, 1.0, twinkle * twinkle_intensity);
                
                // STYLISED star colors - more vibrant!
                float temp = hash(cell_id * 4.0 + seed);
                vec3 star_color;
                if (temp < 0.12) {
                    // Deep red/orange stars
                    star_color = vec3(1.0, 0.4, 0.2);
                } else if (temp < 0.22) {
                    // Golden yellow stars
                    star_color = vec3(1.0, 0.85, 0.4);
                } else if (temp < 0.35) {
                    // Cyan-tinted stars
                    star_color = vec3(0.6, 1.0, 1.0);
                } else if (temp < 0.55) {
                    // Pure white (bright)
                    star_color = vec3(1.0, 1.0, 1.0);
                } else if (temp < 0.75) {
                    // Blue-white stars
                    star_color = vec3(0.7, 0.85, 1.0);
                } else if (temp < 0.88) {
                    // Violet/purple tinted
                    star_color = vec3(0.85, 0.7, 1.0);
                } else {
                    // Bright blue stars
                    star_color = vec3(0.5, 0.7, 1.0);
                }
                
                // Add glow for brighter stars
                float glow = smoothstep(size * 3.0, 0.0, dist) * 0.3;
                
                color += star_color * (brightness + glow) * star_brightness;
            }
        }
    }
    
    return color;
}

// === NEBULA GENERATION ===

vec3 nebula(vec2 uv, float time) {
    vec2 p = uv * 1.5;
    
    // Multiple slowly drifting noise layers
    float n1 = fbm(p + vec2(time * 0.008, time * 0.005));
    float n2 = fbm(p * 0.7 + vec2(-time * 0.006, time * 0.01) + 50.0);
    float n3 = fbm(p * 1.2 + vec2(time * 0.004, -time * 0.007) + 100.0);
    
    // Combine noise layers with different colors
    vec3 color = vec3(0.0);
    color += nebula_color1 * smoothstep(0.3, 0.7, n1) * 0.8;
    color += nebula_color2 * smoothstep(0.35, 0.75, n2) * 0.6;
    color += nebula_color3 * smoothstep(0.4, 0.8, n3) * 0.4;
    
    // Add some bright spots
    float spots = pow(n1 * n2, 2.0);
    color += vec3(0.8, 0.6, 1.0) * spots * 0.5;
    
    return color * nebula_intensity;
}

// === MAIN FRAGMENT ===

void fragment() {
    // Use mesh UV as base - this stays stable regardless of mesh position
    vec2 base_uv = UV - 0.5;  // Center the UV
    base_uv *= 2.0;  // Scale to cover the plane properly
    
    // Apply parallax offset ONLY from camera position
    // This makes stars shift as you move, creating depth
    // Larger multiplier = more obvious parallax movement
    vec2 parallax1 = camera_position.xz * layer1_parallax * 0.01;  // Far stars move least
    vec2 parallax2 = camera_position.xz * layer2_parallax * 0.01;
    vec2 parallax3 = camera_position.xz * layer3_parallax * 0.01;  // Near stars move most
    
    float time = TIME * time_scale;
    
    // Generate star layers with parallax offsets
    vec3 stars = vec3(0.0);
    stars += star_layer(base_uv + parallax1, layer1_scale, 0.0, time) * 0.5;   // Distant, dimmer
    stars += star_layer(base_uv + parallax2, layer2_scale, 100.0, time) * 0.75; // Mid
    stars += star_layer(base_uv + parallax3, layer3_scale, 200.0, time) * 1.0;  // Near, brighter
    
    // Deep space background
    vec3 background = vec3(0.01, 0.01, 0.03);  // Very dark blue-black
    
    // Add nebula background (using slow parallax)
    if (enable_nebula) {
        background += nebula(base_uv + parallax1 * 0.5, time);
    }
    
    // Final composition
    vec3 final_color = background + stars;
    
    // Subtle vignette for atmosphere
    float vignette = 1.0 - length(UV - 0.5) * 0.4;
    final_color *= vignette;
    
    ALBEDO = final_color;
    ALPHA = 1.0;
}

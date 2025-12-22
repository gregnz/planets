shader_type spatial;
render_mode specular_schlick_ggx;

// Uniforms for procedural generation
uniform float seed : hint_range(0.0, 1000.0) = 0.0;
uniform float pixels : hint_range(10.0, 1000.0) = 1000.0; // Resolution/Pixelation effect if desired, or just high for smooth
uniform float rotation_speed : hint_range(0.0, 1.0) = 0.05;

// Colors
uniform vec4 color_deep_ocean : source_color = vec4(0.0, 0.1, 0.3, 1.0);
uniform vec4 color_shallow_water : source_color = vec4(0.0, 0.4, 0.8, 1.0);
uniform vec4 color_land : source_color = vec4(0.2, 0.6, 0.1, 1.0);
uniform vec4 color_mountain : source_color = vec4(0.4, 0.3, 0.2, 1.0);
uniform vec4 color_snow : source_color = vec4(1.0, 1.0, 1.0, 1.0);

// Thresholds
uniform float sea_level : hint_range(0.0, 1.0) = 0.5;
uniform float mountain_level : hint_range(0.0, 1.0) = 0.7;
uniform float snow_level : hint_range(0.0, 1.0) = 0.85;

// Atmosphere
uniform vec4 atmosphere_color : source_color = vec4(0.4, 0.6, 1.0, 1.0);
uniform float atmosphere_intensity : hint_range(0.0, 10.0) = 3.0;

// Clouds
uniform float cloud_cover : hint_range(0.0, 1.0) = 0.4;
uniform float cloud_speed : hint_range(0.0, 0.5) = 0.02;


// --- NOISE FUNCTIONS (Simplex 3D approx) ---
vec3 mod289(vec3 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
vec4 mod289(vec4 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
vec4 permute(vec4 x) { return mod289(((x * 34.0) + 1.0) * x); }
vec4 taylorInvSqrt(vec4 r) { return 1.79284291400159 - 0.85373472095314 * r; }

float snoise(vec3 v) {
    const vec2 C = vec2(1.0 / 6.0, 1.0 / 3.0);
    const vec4 D = vec4(0.0, 0.5, 1.0, 2.0);

    vec3 i = floor(v + dot(v, C.yyy));
    vec3 x0 = v - i + dot(i, C.xxx);

    vec3 g = step(x0.yzx, x0.xyz);
    vec3 l = 1.0 - g;
    vec3 i1 = min(g.xyz, l.zxy);
    vec3 i2 = max(g.xyz, l.zxy);

    vec3 x1 = x0 - i1 + C.xxx;
    vec3 x2 = x0 - i2 + C.yyy;
    vec3 x3 = x0 - D.yyy;

    i = mod289(i);
    vec4 p = permute(permute(permute(
        i.z + vec4(0.0, i1.z, i2.z, 1.0))
        + i.y + vec4(0.0, i1.y, i2.y, 1.0))
        + i.x + vec4(0.0, i1.x, i2.x, 1.0));

    float n_ = 0.142857142857; // 1.0/7.0
    vec3 ns = n_ * D.wyz - D.xzx;

    vec4 j = p - 49.0 * floor(p * ns.z * ns.z);

    vec4 x_ = floor(j * ns.z);
    vec4 y_ = floor(j - 7.0 * x_);

    vec4 x = x_ * ns.x + ns.yyyy;
    vec4 y = y_ * ns.x + ns.yyyy;
    vec4 h = 1.0 - abs(x) - abs(y);

    vec4 b0 = vec4(x.xy, y.xy);
    vec4 b1 = vec4(x.zw, y.zw);

    vec4 s0 = floor(b0) * 2.0 + 1.0;
    vec4 s1 = floor(b1) * 2.0 + 1.0;
    vec4 sh = -step(h, vec4(0.0));

    vec4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
    vec4 a1 = b1.xzyw + s1.xzyw * sh.zzww;

    vec3 p0 = vec3(a0.xy, h.x);
    vec3 p1 = vec3(a0.zw, h.y);
    vec3 p2 = vec3(a1.xy, h.z);
    vec3 p3 = vec3(a1.zw, h.w);

    vec4 norm = taylorInvSqrt(vec4(dot(p0, p0), dot(p1, p1), dot(p2, p2), dot(p3, p3)));
    p0 *= norm.x;
    p1 *= norm.y;
    p2 *= norm.z;
    p3 *= norm.w;

    vec4 m = max(0.6 - vec4(dot(x0, x0), dot(x1, x1), dot(x2, x2), dot(x3, x3)), 0.0);
    m = m * m;
    return 42.0 * dot(m * m, vec4(dot(p0, x0), dot(p1, x1), dot(p2, x2), dot(p3, x3)));
}

// FBM for detail
float fbm(vec3 x, int octaves) {
    float v = 0.0;
    float a = 0.5;
    vec3 shift = vec3(100.0);
    for (int i = 0; i < octaves; ++i) {
        v += a * snoise(x);
        x = x * 2.0 + shift;
        a *= 0.5;
    }
    return v;
}

void vertex() {
    // Basic rotation just for the texture lookup, not the mesh itself
    // Or we can rotate the mesh in _process
}

void fragment() {
    // 1. Calculate Base Noise Coordinate (Sphere Surface)
    // Rotate over time
    float theta = TIME * rotation_speed;
    mat3 rotY = mat3(
        vec3(cos(theta), 0.0, sin(theta)),
        vec3(0.0, 1.0, 0.0),
        vec3(-sin(theta), 0.0, cos(theta))
    );
    
    vec3 noise_coord = (rotY * normalize(VERTEX)) * 5.0; // Scale 5.0
    noise_coord += vec3(seed); // Offset by seed

    // 2. Sample Height Noise
    float height = fbm(noise_coord, 5); // Range approx -1 to 1
    height = (height + 1.0) * 0.5; // Remap 0 to 1
    
    // CARTOON STEP: Quantize height to create distinct "terraces" or bands
    // But for biomes, we just want sharp transitions
    
    // 3. Determine Color (Biomes) -- USING SHARP TRANSITIONS
    vec3 land_color;
    float roughness_val = 1.0;
    float specular_val = 0.0;
    
    // Smoothness of transition (0.01 = sharp, 0.1 = smooth)
    float blend = 0.01; 

    if (height < sea_level) {
        // Water
        // Quantize depth for "bands" of water color
        float water_depth = height / sea_level;
        if (water_depth < 0.8) land_color = color_deep_ocean.rgb;
        else land_color = color_shallow_water.rgb;
        
        roughness_val = 0.1; // Wet
        specular_val = 1.0;
    } else {
        // Land
        if (height < mountain_level) {
             // Flat Land
             land_color = color_land.rgb;
        } else if (height < snow_level) {
             // Mountain
             land_color = color_mountain.rgb;
        } else {
             // Snow
             land_color = color_snow.rgb;
        }
    }
    
    // 4. Clouds (Separate Noise Layer)
    float cloud_time = TIME * cloud_speed;
    vec3 cloud_coord = (rotY * normalize(VERTEX)) * 8.0; 
    cloud_coord.x += cloud_time;
    float cloud_noise = fbm(cloud_coord + vec3(100.0), 3);
    cloud_noise = (cloud_noise + 0.5) * 0.5; // remap roughly
    
    // Cartoon Cloud Masking (Hard Step)
    if (cloud_noise > (1.0 - cloud_cover)) {
        vec3 cloud_col = vec3(1.0); // White clouds
        land_color = cloud_col; // Solid overwrite
        roughness_val = 1.0; 
        specular_val = 0.0;
    }

    // 5. Atmosphere / Fresnel (Quantized Rim)
    float fresnel = sqrt(1.0 - dot(NORMAL, VIEW));
    fresnel = pow(fresnel, atmosphere_intensity);
    
    // Cartoon Rim Light
    float rim_step = step(0.6, fresnel); // Hard cut
    vec3 emission = atmosphere_color.rgb * rim_step * 0.5;
    
    // Toon Shading Note: real toon shading requires LIGHT calculation in light() function
    // For now we rely on Albedo and Roughness to convey the style in Environment

    ALBEDO = land_color;
    ROUGHNESS = roughness_val;
    SPECULAR = specular_val;
    EMISSION = emission;
}

shader_type spatial;
render_mode blend_add, depth_draw_always, cull_back, unshaded;

uniform vec4 shield_color : source_color = vec4(0.0, 0.8, 1.0, 0.1); // Base color
// Stores normalized health for (Front, Back, Left, Right)
// x=Front, y=Back, z=Left, w=Right
uniform vec4 quadrant_intensities = vec4(1.0, 1.0, 1.0, 1.0);
// Vaporization progress per quadrant (0.0 to 1.0). 1.0 = fully dissolved.
uniform vec4 vaporize_factors = vec4(0.0, 0.0, 0.0, 0.0);

uniform float rim_power : hint_range(0.1, 10.0) = 4.0;
uniform float rim_intensity : hint_range(0.0, 5.0) = 1.5;

uniform vec3 impact_pos; // Local space
uniform float impact_radius : hint_range(0.1, 10.0) = 2.0;
uniform float impact_strength : hint_range(0.0, 5.0) = 0.0; // Controls visibility of impact

varying vec3 local_pos;

void vertex() {
	local_pos = VERTEX;
}

void fragment() {
	// 1. Determine Quadrant and Gap
	// Godot: -Z is Forward, +X is Right

	float x = local_pos.x;
	float z = local_pos.z;

	float abs_x = abs(x);
	float abs_z = abs(z);

	float current_intensity = 0.0;
    float current_vaporize = 0.0;

	// Gap calculation
	// We want a gap where abs(x) is close to abs(z)
	// difference between them tells us how far we are from the diagonal
	float dist_from_diagonal = abs(abs_x - abs_z);
	float gap_width = 0.1; // Adjustable border width
	float border_mask = smoothstep(0.0, gap_width, dist_from_diagonal);

	if (abs_z > abs_x) {
		// Front or Back
		if (z < 0.0) {
			// Front (-Z)
			current_intensity = quadrant_intensities.x;
            current_vaporize = vaporize_factors.x;
		} else {
			// Back (+Z)
			current_intensity = quadrant_intensities.y;
            current_vaporize = vaporize_factors.y;
		}
	} else {
		// Left or Right
		if (x < 0.0) {
			// Left (-X)
			current_intensity = quadrant_intensities.z;
            current_vaporize = vaporize_factors.z;
		} else {
			// Right (+X)
			current_intensity = quadrant_intensities.w;
            current_vaporize = vaporize_factors.w;
		}
	}

	// If quadrant is at near-zero intensity, hide it immediately (simple per-quadrant discard)
	if (current_intensity < 0.05) {
		discard;
	}

    // DISSOVE EFFECT
    // Simple hashed noise
    vec3 p = local_pos * 10.0; // Scale noise
    float noise = fract(sin(dot(p, vec3(12.9898, 78.233, 45.164))) * 43758.5453);

    vec3 burn_emission = vec3(0.0);
    float burn_alpha = 0.0;

    // If vaporizing, erode alpha
    if (current_vaporize > 0.0) {
        // Fully dissolved - discard entire quadrant
        if (current_vaporize >= 1.0) {
            discard;
        }

        if (noise < current_vaporize) {
            discard;
        }
        // Add a burning edge
        // Make range wider for visibility
        float edge_width = 0.15;
        float edge = smoothstep(current_vaporize, current_vaporize + edge_width, noise);

        // Edge is 0 near the dissolve line, 1 further away.
        // We want 1 near the line.
        float glow_intensity = 1.0 - edge;

        // Boost intensity significantly
        // Use a hot color (Orange/White)
        vec3 hot_color = vec3(1.0, 0.5, 0.2) * 5.0;
        burn_emission = hot_color * glow_intensity;
        burn_alpha = glow_intensity;
    }

	// 2. Fresnel Effect (Rim)
	float NdotV = dot(NORMAL, VIEW);
	float fresnel = pow(1.0 - clamp(NdotV, 0.0, 1.0), rim_power);

	// Modulate base emission by the specific quadrant intensity and the border mask
	vec3 base_emission = shield_color.rgb * fresnel * rim_intensity * current_intensity * border_mask;
	float base_alpha = fresnel * shield_color.a * current_intensity * border_mask;

	// 3. Impact Effect (remains global for now, or could be masked too)
	float dist = distance(local_pos, impact_pos);

	// Create a ripple/glow at impact point
	float impact_glow = 0.0;
	if (dist < impact_radius) {
		float d = dist / impact_radius;
		// A gradient from center outward
		impact_glow = (1.0 - d * d) * impact_strength;
	}

	vec3 final_color = base_emission + burn_emission + (vec3(1.0, 1.0, 1.0) * impact_glow);
	float final_alpha = base_alpha + burn_alpha + impact_glow;

	ALBEDO = final_color;
	ALPHA = clamp(final_alpha, 0.0, 1.0);
}

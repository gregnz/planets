shader_type spatial;

void fragment() {
	// Place fragment code here.
}

void vertex() {
	VERTEX.y += cos(VERTEX.x) * sin(VERTEX.z);
}
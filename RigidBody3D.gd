extends RigidBody3D

# Player variables
var move_speed = 10
var rotation_speed = 2

func _ready():
	print("Ready RB3d")
	
func _process(delta):
	# Handle player input and movement
	var direction = Vector3.ZERO
	if Input.is_action_pressed("ui_right"):
		direction.x += 1
	if Input.is_action_pressed("ui_left"):
		direction.x -= 1
	if Input.is_action_pressed("ui_down"):
		direction.z += 1
	if Input.is_action_pressed("ui_up"):
		direction.z -= 1
	
	direction = direction.normalized()

	# Move the player
	var player_transform = transform
	player_transform.origin += direction * move_speed * delta
	transform = player_transform

	# Rotate the player
	var rotation_angle = 0
	if direction != Vector3.ZERO:
		rotation_angle = atan2(-direction.x, direction.z)
# var player_rotation = Rotation(Vector3.UP, rotation_angle)
	rotate_object_local(Vector3.UP, rotation_angle)

extends Camera3D

var player
var height: float = 10.0

func _ready():
	player = get_node_or_null("/root/Root3D/Player")

func _process(delta):
	if not player:
		# Try to find player in group "Player" (added by PlayerController.cs)
		var players = get_tree().get_nodes_in_group("Player")
		if players.size() > 0:
			player = players[0]
	
	if not player: return
		
	var target_h = 30.0
	
	# Try calling directly. has_method is unreliable for C#
	var t = player.call("GetTargetNode")
	if t:
		# t is now a Node3D (from C# return type)
		var dist = player.global_position.distance_to(t.global_position)
		target_h = 10 + dist * 0.9
		target_h = clamp(target_h, 5.0, 100.0)
	
	height = lerp(height, target_h, float(delta) * 10.0)
	
	var target_pos = player.global_position
	# Position camera above and behind player, looking at them
	# Added small X offset (0.1) to prevent "Target and up vectors are colinear" warning
	global_position = target_pos + Vector3(0.1, height, height * 0.5)
	look_at(target_pos, Vector3.UP)

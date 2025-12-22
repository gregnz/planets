using Godot;

/// <summary>
/// Controls the procedural starfield background.
/// Updates shader uniforms based on camera/player position for parallax effect.
/// </summary>
public partial class Starfield : MeshInstance3D
{
	private Node3D _player;
	private ShaderMaterial _material;
	
	// Starfield position offset from camera
	[Export] public float VerticalOffset = -150.0f;
	
	public override void _Ready()
	{
		// Get player reference
		_player = GetNode<Node3D>("/root/Root3D/Player");
		
		// Get shader material
		_material = GetActiveMaterial(0) as ShaderMaterial;
		
		if (_material == null)
		{
			GD.PrintErr("Starfield: No ShaderMaterial found on mesh!");
		}
	}

	public override void _Process(double delta)
	{
		if (_player == null || _material == null) return;
		
		// Follow player horizontally, stay at fixed vertical offset
		Vector3 targetPos = _player.Position;
		Position = new Vector3(targetPos.X, VerticalOffset, targetPos.Z);
		
		// Update shader with camera position for parallax
		_material.SetShaderParameter("camera_position", _player.Position);
	}
}

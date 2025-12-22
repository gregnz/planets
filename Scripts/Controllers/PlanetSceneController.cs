using Godot;
using System;
using Planetsgodot.Scripts.Core;
using Planetsgodot.Scripts.Controllers;

public partial class PlanetSceneController : Node3D
{
    private PlanetSurface _surface;
    private GameController _gc;

    public override void _Ready()
    {
        _surface = GetNode<PlanetSurface>("PlanetSurface");
        
        // Find GameController (Autoload) to get player data
        // Note: Since we switched scenes, the previous nodes might be gone, 
        // BUT GameController might be an Autoload (Singleton) which persists.
        // The user's GameController is at /root/GameController usually.
        _gc = GetNodeOrNull<GameController>("/root/GameController");
        
        if (_gc != null)
        {
             GD.Print("PlanetSceneController: Connected to GameController. Preparing for landing...");
             SpawnPlayer();
        }
        else
        {
             GD.Print("PlanetSceneController: GameController not found. Running independent test?");
             // Spawn default player for testing
        }
    }
    
    private void SpawnPlayer()
    {
        var playerScene = GD.Load<PackedScene>("res://player.tscn");
        if (playerScene == null)
        {
             GD.PrintErr("Failed to load player.tscn");
             return;
        }
        
        var player = playerScene.Instantiate<PlayerController>();
        player.Name = "Player";
        AddChild(player);
        
        // Position Player
        // Center of map is 0,0. Terrain height varies.
        // Spawn at 0, 10, 0 (above terrain)
        player.GlobalPosition = new Vector3(0, 20, 0);
        
        // Enable Surface Mode
        player.SurfaceMode = true;
        
        // Setup Camera
        var camera = GetNode<Camera3D>("Camera3D");
        if (camera != null)
        {
             // Inject player into the GDScript
             camera.Set("player", player);
             camera.Current = true;
        }
        
        GD.Print("Player Spawned on Surface in SurfaceMode.");
    }
    
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.L)
        {
            GD.Print("Taking Off... Returning to Orbit.");
            if (_gc != null)
                _gc.ReturnToSpace();
            else
                GetTree().ChangeSceneToFile("res://level.tscn");
        }
    }
}

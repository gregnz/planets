using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using Planetsgodot.Scripts.Combat;
using Planetsgodot.Scripts.Controllers;
using Planetsgodot.Scripts.Environment;
using Planetsgodot.Scripts.Missions;
using Planetsgodot.Scripts.AI;

namespace Planetsgodot.Scripts.Core;

public partial class GameController : Node3D
{
    // public SolarSystem solarSystem;
    public PlayerController player;

    // public TargetingReticule targetingReticule;
    // public Camera referenceCamera;
    public List<ITarget> possibleTargets { get; } = new();
    int targetIndex = 0;
    public readonly CombatDirector combatDirector = new();
    public readonly MissionController Mc = new();


    private SignalBus signalBus;


    // Start is called before the first frame update

    public override void _Ready()
    {
        Engine.TimeScale = 1f;
        GetTree().AutoAcceptQuit = false;

        // Initialize BulletManager
        // We defer this or check in Process because CurrentScene might be null in _Ready of Autoload?
        // Actually, Autoload _Ready runs before Main Scene _Ready. CurrentScene IS null here.
        // We need to add BulletManager when the Level loads.
        // Quick fix: Add to root, but ensure it finds the world? 
        // Better: GameController shouldn't own it directly as child if we want it in Scene.
        // BUT, for now, let's keep it here but ensure we use the right logic.

        // Strategy Change: 
        // 1. Create BulletManager as a "Scene singleton" managed by us.
        // 2. Or just add to Root (`GetTree().Root.AddChild(bm)`). Root is a Viewport.
        // This puts it in the main viewport, sharing World3D with siblings.

        var bulletManager = new Planetsgodot.Scripts.Combat.BulletManager();
        bulletManager.Name = "BulletManager";
        GetTree().Root.CallDeferred("add_child", bulletManager);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            GD.Print("Quit Request received. Quitting gracefully...");
            GetTree().Quit();
        }
    }

    public override void _EnterTree()
    {
        Mc.gc = this;
        CallDeferred("_started");
    }

    public Node3D SectorRoot;
    public bool InSpace = true;

    public void _started()
    {
        SectorRoot = new Node3D { Name = "SectorRoot" };
        AddChild(SectorRoot);

        Mc.gc = this;
        // combatDirector.Gc = this; // Removed


        // --- Missile Manager ---
        var mm = new MissileManager();
        mm.Name = "MissileManager";
        AddChild(mm);

        // --- Campaign Integration ---
        // Initialize CampaignManager (Manual singleton setup for now)
        if (CampaignManager.Instance == null)
        {
            var cm = new CampaignManager();
            cm.Name = "CampaignManager";
            GetTree().Root.CallDeferred("add_child", cm);
        }

        // Initialize UI
        var ui = new MissionUI();
        ui.Name = "MissionUI";
        AddChild(ui);

        // Wait for next frame or just force it?
        // For robustness in this script, let's look for it or wait.
        // Actually, let's just use a deferred call to start the mission so CM has time to Ready()
        CallDeferred(nameof(StartCampaignMission));

        signalBus = GetNode<SignalBus>("/root/SignalBus");
        SpawnPlanets();
    }

    private void StartCampaignMission()
    {
        // Ensure CM is loaded
        if (CampaignManager.Instance == null)
        {
            GD.Print("Waiting for CampaignManager...");
            // Retrieve it manually if Instance static isn't set yet (race condition)
            var cm = GetTree().Root.GetNodeOrNull<CampaignManager>("CampaignManager");
            if (cm != null)
                cm.LoadGame();
        }

        if (CampaignManager.Instance != null && CampaignManager.Instance.State != null)
        {
            string missionId = CampaignManager.Instance.State.CurrentMissionId;
            GD.Print($"Campaign Mode: Loading {missionId}");
            Mc.LoadMission(missionId);
        }
        else
        {
            GD.PrintErr("Campaign Manager not ready. Starting Default Mission.");
            Mc.LoadMission("mission_01_patrol");
        }

        signalBus = GetNode<SignalBus>("/root/SignalBus");
        SpawnPlanets();
    }

    /// <summary>
    /// Spawns an optimized asteroid field from mission data.
    /// Called from MissionController.Mission.Setup()
    /// </summary>
    public OptimizedAsteroidField SpawnOptimizedAsteroidField(Vector3 center, float radius, int count)
    {
        var field = new OptimizedAsteroidField();
        field.Name = $"OptimizedAsteroidField_{center}";
        field.AsteroidCount = count;
        field.FieldRadius = radius;
        field.FieldCenter = center;
        field.OrbitSpeed = 0.3f; // Default, can be updated by caller if returned

        SectorRoot.AddChild(field);
        GD.Print($"Spawned OptimizedAsteroidField with {count} asteroids at {center}");
        return field;
    }

    public void EnterPlanetMode(string scenePath)
    {
        GD.Print("Entering Planet Mode. Suspending Sector...");
        InSpace = false;
        player = null; // Clear reference to space player
        SectorRoot.ProcessMode = Node.ProcessModeEnum.Disabled;
        SectorRoot.Visible = false;
        GetTree().ChangeSceneToFile(scenePath);
    }

    public void ReturnToSpace()
    {
        GD.Print("Returning to Space. Resuming Sector...");
        GetTree().ChangeSceneToFile("res://level.tscn");
        // We defer the re-enable slightly ensures the scene transition handles first
        // But simply setting it here is fine as GC overlays.
        InSpace = true;
        SectorRoot.ProcessMode = Node.ProcessModeEnum.Inherit;
        SectorRoot.Visible = true;
    }

    public override void _Process(double delta)
    {
        if (!InSpace)
            return;

        // Legacy Director Update removed
    }

    public override void _Input(InputEvent @event)
    {
        if (!InSpace)
            return;

        if (@event is InputEventKey key)
        {
            // Landing Logic
            if (key.Keycode == Key.L && key.Pressed)
            {
                HandleLandingInput();
            }
        }
    }

    public void RegisterTarget(ITarget t)
    {
        GD.Print($"GameController: Registering Target {t}");
        possibleTargets.Add(t);
    }

    public void DeregisterTarget(ITarget t)
    {
        possibleTargets.Remove(t);
    }

    public ITarget requestTarget(int inc)
    {
        // Prune invalid targets first
        possibleTargets.RemoveAll(t => !GodotObject.IsInstanceValid(t as Node));

        GD.Print($"Select target. Count={possibleTargets.Count} Index={targetIndex}");
        if (possibleTargets.Count == 0)
            return null;

        // Loop to find next valid target
        int startIndex = targetIndex;
        int attempts = possibleTargets.Count;

        for (int i = 0; i < attempts; i++)
        {
            targetIndex += inc;

            // Wrap handling
            if (targetIndex < 0) targetIndex = possibleTargets.Count - 1;
            if (targetIndex >= possibleTargets.Count) targetIndex = 0;

            ITarget candidate = possibleTargets[targetIndex];

            // Filter: Only select Enemies
            if (candidate.Attitude == CombatDirector.Squadron.Attitude.Enemy)
            {
                candidate.SetTargeted(true);
                signalBus.EmitSignal("NpcTargetChanged", candidate.GetStatus());
                return candidate;
            }
        }

        // No match found
        GD.Print("No MATCHING target found.");
        return null;
    }

    public ITarget RequestPrevTarget()
    {
        return requestTarget(-1);
    }

    public ITarget RequestNextTarget() => requestTarget(1);

    public ITarget GetCurrentTarget()
    {
        if (possibleTargets.Count == 0)
            return null;
        if (targetIndex >= possibleTargets.Count)
            targetIndex = possibleTargets.Count - 1;
        return possibleTargets[targetIndex];
    }

    private void SpawnPlanets()
    {
        GD.Print("Attempting to spawn planets...");
        var planetScene = GD.Load<PackedScene>("res://Planet.tscn");
        if (planetScene == null)
        {
            GD.PrintErr("Failed to load Planet.tscn");
            return;
        }

        // Move closer for visibility. Camera sees approx 30-40 units.
        Vector3[] positions = { new Vector3(70, 0, 10), new Vector3(-80, 0, -20) };
        for (int i = 0; i < positions.Length; i++)
        {
            var p = planetScene.Instantiate<Planet>();
            p.Name = $"Planet {i}";
            p.PlanetName = $"Proxima {i}";
            p.Position = positions[i];

            // Add to SectorRoot so planets get hidden when landing
            SectorRoot.CallDeferred("add_child", p);

            RegisterTarget(p); // Make it targetable
            GD.Print($"Spawned {p.PlanetName} at {p.Position}");
        }
    }

    private Planet _selectedPlanet;

    private void HandleLandingInput()
    {
        var planets = GetTree().GetNodesInGroup("Planet");

        // Robust check for player validity
        if (
            planets.Count == 0
            || player == null
            || !GodotObject.IsInstanceValid(player)
            || !player.IsInsideTree()
        )
            return;

        Planet nearest = null;
        float minDist = float.MaxValue;

        foreach (Node node in planets)
        {
            if (node is Planet p)
            {
                float dist = player.GlobalPosition.DistanceTo(p.GlobalPosition);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = p;
                }
            }
        }

        if (nearest != null && minDist < 500.0f) // Max selection range
        {
            if (_selectedPlanet == nearest)
            {
                // Already selected -> Land!
                if (minDist < 150.0f) // Landing range (Planet radius is 50, so 100 units away)
                {
                    GD.Print($"Landing on {nearest.PlanetName}...");
                    // Use new method, NO Clear()
                    EnterPlanetMode("res://Scenes/PlanetSurface.tscn");
                }
                else
                {
                    GD.Print($"Too far to land! Distance: {minDist:F0} (Req: 150)");
                }
            }
            else
            {
                // Select it
                if (_selectedPlanet != null)
                    _selectedPlanet.SetTargeted(false);
                _selectedPlanet = nearest;
                _selectedPlanet.SetTargeted(true);
                GD.Print($"Selected {_selectedPlanet.PlanetName}. Press L again to land.");
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using Newtonsoft.Json;
using Planetsgodot.Scripts.Core;
using Planetsgodot.Scripts.Combat;
using Planetsgodot.Scripts.Controllers;
using Planetsgodot.Scripts.Environment;

namespace Planetsgodot.Scripts.Missions;

public class MissionController
{
    public GameController gc;
    public Node3D waypoint;

    public MissionBase ActiveMission; // The current C# mission script

    private PackedScene npcPrefab = GD.Load<PackedScene>("res://npc.tscn");
    private PackedScene tigersClawPrefab = GD.Load<PackedScene>("res://tigersclaw.tscn");

    // Legacy support (to be removed/refactored)
    // Mission currentMission;

    class Waypoint
    {
        MissionController _mc;

        public Waypoint(MissionController mc, int v1, int v2)
        {
            _mc = mc;
            x = v1;
            y = v2;
        }

        public float x { get; set; }
        public float y { get; set; }

        internal bool isCloseTo(Vector3 position)
        {
            Vector3 d = position - new Vector3(x, 0, y);
            if (d.LengthSquared() < 6 * 6)
            {
                return true;
            }

            return false;
        }

        public void instantiate(MissionController mc)
        {
            _mc = mc;
            // Instantiate(_mc.waypoint, new Vector3(x, 0, y), Quaternion.identity);
            //
            // Bullet b = (Bullet)ballisticShot.Instantiate();
            // pc.GetTree().Root.AddChild(b);
            // b.Rotation = pc.Rotation;
            // GD.Print($"{pc.Position} + {position} + {barrelOffset} * {barrelRotation};");
            // // b.Position = pc.Position + position + barrelOffset * barrelRotation;
            // b.Position = hp.hardpointContent.GlobalPosition + barrelOffset * barrelRotation;
            //
            // b.enabled = true;
            // b.damage = currentHardpointSpec.Damage;
            // b.range = RangeToScreenLength(currentWeaponSpec);
            // b.AddCollisionExceptionWith(pc);
        }
    }

    class Squadron
    {
        public List<NPC> npcs { get; set; }
        public string attitude { get; set; }
    }

    class NPC
    {
        private MissionController _mc;
        public Waypoint waypoint { get; set; }
        public string type { get; set; }
        public int count { get; set; }
        private PackedScene npcPrefab = GD.Load<PackedScene>("res://npc.tscn");
        private PackedScene tigersClawPrefab = GD.Load<PackedScene>("res://tigersclaw.tscn");

        internal Npc instantiate(MissionController mc, string callsign)
        {
            _mc = mc;

            // Use appropriate scene for ship type
            PackedScene prefab = type == "TigersClaw" ? tigersClawPrefab : npcPrefab;
            Npc npc = (Npc)prefab.Instantiate();
            npc.name = callsign;
            npc.gameController = _mc.gc;
            npc.shipSpecification = ShipFactory.presetFromString(type);
            mc.gc.RegisterTarget(npc);
            // Add to SectorRoot to allow persistence/disabling during planet visits
            mc.gc.SectorRoot.CallDeferred("add_child", npc);
            return npc;
        }
    }

    /*
Note: Success/failure paths
1. Waypoints
2. Escort via waypoints
3. Capital ship
4. Escort + Enemy Ace
5. Escort hospital ship
6. Intercept troop transport
7. Defend capital ships
- Other NPCs
- Timing - eg: fight ships, other ships arrive after x seconds
*/
    class Mission
    {
        int currentWaypointGoalIndex;
        public List<Waypoint> Waypoints { get; set; }

        public List<Squadron> Squads { get; set; }



        public string MissionName { get; set; }

        // serializer.Deserialize<Mission>()

        /*
        Enyo System (Win –> McAuliffe;     Lose –> Gateway)

McAuliffe System (Win –> Gimle;     Lose –> Brimstone)

Gateway System (Win –> Brimstone;     Lose –> Cheng Du)

Gimle System (Win –> Dakota;     Lose –> Brimstone)

Brimstone System (Win –> Dakota;     Lose –> Port Hedland)

Cheng-Du System (Win –> Brimstone;     Lose –> Port Hedland)

Dakota System (Win –> Kurasawa;     Lose –> Rostov)

Port Hedland (Win –> Rostov;     Lose –> Hubble’s Star)

Kurasawa System (Win –> Venice;     Lose –> Rostov)

Rostov System (Win –> Venice;     Lose –> Hell’s Kitchen)

Hubble’s Star (Win –> Rostov;     Lose –> Hell’s Kitchen)

Hell’s Kitchen (You Lose)

Venice System (You Win)
        */

        internal void Evaluate(Vector3 position)
        {
            if (Waypoints == null || Waypoints.Count == 0)
                return;
            if (Waypoints[currentWaypointGoalIndex].isCloseTo(position))
            {
                Debug.Print("Waypoint " + currentWaypointGoalIndex + " hit. Moving to next");
                currentWaypointGoalIndex++;
                if (currentWaypointGoalIndex >= Waypoints.Count)
                {
                    currentWaypointGoalIndex = Waypoints.Count - 1;
                    Debug.Print("End of waypoints");
                }
            }
        }

        public void Setup(MissionController mc)
        {
            foreach (Waypoint w in Waypoints)
                w.instantiate(mc);

            int squadronId = 0;
            foreach (Squadron s in Squads)
            {
                CombatDirector.Squadron.Attitude t;
                Enum.TryParse(s.attitude, true, out t);

                CombatDirector.Squadron newSquad = mc.gc.combatDirector.RegisterSquadron(
                    squadronId,
                    t
                );
                if (t == CombatDirector.Squadron.Attitude.Enemy)
                    newSquad.target = mc.gc.player;

                foreach (NPC w in s.npcs)
                {
                    for (int i = 0; i < w.count; i++)
                    {
                        Npc npc = w.instantiate(mc, Callsigns[i]);

                        // Offset spawn position so NPCs don't spawn on top of each other
                        float spawnOffset = 5.0f; // Distance between spawned NPCs
                        float angle = (i * 360f / Mathf.Max(w.count, 1)) * (Mathf.Pi / 180f);
                        Vector3 offset = new Vector3(
                            Mathf.Cos(angle) * spawnOffset * (i > 0 ? 1 : 0),
                            0,
                            Mathf.Sin(angle) * spawnOffset * (i > 0 ? 1 : 0)
                        );
                        npc.Position = new Vector3(w.waypoint.x, 0, w.waypoint.y) + offset;

                        GD.Print($"Spawned NPC {i}: {npc.name} at {npc.Position}");
                        newSquad.Add(npc);
                    }

                    // if (s.attitude == SquadronController.Squadron.Attitude.Enemy) s.target = mc.player.gameObject;
                    // else if (s.attitude == SquadronController.Squadron.Attitude.Friend) s.target = null;
                }
                squadronId++;
            }


        }

        public String[] Callsigns =
        {
            "Aegis",
            "Apex",
            "Banshee",
            "Cyclone",
            "Eclipse",
            "Enigma",
            "Fury",
            "Havoc",
            "Inferno",
            "Nebula",
            "Nemesis",
            "Omega",
            "Phoenix",
            "Raptor",
            "Scythe",
            "Seraph",
            "Serpent",
            "Shadow",
            "Spectre",
            "Tempest",
            "Thunder",
            "Vortex",
            "Venom",
            "Void",
            "Wraith",
        };

        public string[] shipTypes =
        {
            "SidewinderMkI",
            "EagleMkII",
            "Hauler",
            "Adder",
            "ImperialEagle",
            "ViperMkIII",
            "CobraMkIII",
            "ViperMkIV",
            "DiamondbackScout",
            "CobraMkIV",
            "Type6Transporter",
            "Dolphin",
            "DiamondbackExplorer",
            "ImperialCourier",
            "Keelback",
            "AspScout",
            "Vulture",
            "AspExplorer",
            "FederalDropship",
            "Type7Transporter",
            "AllianceChieftain",
            "FederalAssaultShip",
            "ImperialClipper",
            "AllianceChallenger",
            "AllianceCrusader",
            "FederalGunship",
            "KraitMkII",
            "Orca",
            "FerDeLance",
            "Python",
            "Type9Heavy",
            "BelugaLiner",
            "Type10Defender",
            "Anaconda",
            "FederalCorvette",
            "ImperialCutter",
        };
    }

    public void LoadMission(string missionId)
    {
        // Simple factory for now - in a real game use reflection or a registry
        if (missionId == "mission_01_patrol")
        {
            var mission = new Scripts.Missions.M01_Patrol(); // We will create this next
            StartMission(mission);
        }
        else
        {
            GD.PrintErr($"Mission ID not found: {missionId}");
            // Fallback for testing
            // StartMission(new Scripts.Missions.M01_Patrol());
        }
    }

    private OptimizedAsteroidField _activeAsteroidField;

    public void StartMission(MissionBase mission)
    {
        if (ActiveMission != null)
        {
            ActiveMission.QueueFree();
        }

        // Cleanup previous mission environment
        if (_activeAsteroidField != null)
        {
            if (GodotObject.IsInstanceValid(_activeAsteroidField))
                _activeAsteroidField.QueueFree();
            _activeAsteroidField = null;
        }

        ActiveMission = mission;
        gc.AddChild(ActiveMission); // Add as child so it updates? Or manual update?
        // MissionBase is a Node, so we can just add it to the tree

        // Setup Environment (Asteroids)
        if (ActiveMission.Asteroids.Enabled)
        {
            _activeAsteroidField = gc.SpawnOptimizedAsteroidField(
                ActiveMission.Asteroids.Center,
                ActiveMission.Asteroids.Radius,
                ActiveMission.Asteroids.Count
            );
            _activeAsteroidField.OrbitSpeed = ActiveMission.Asteroids.OrbitSpeed;
        }

        ActiveMission.Setup(this);
        GD.Print($"Started Mission: {ActiveMission.Title}");
    }

    public void Update(double delta)
    {
        if (ActiveMission != null)
        {
            ActiveMission.Update((float)delta);
        }
    }

    // --- Spawning Helpers for Scripts ---

    public void SpawnWing(
        string type,
        int count,
        Vector3 position,
        CombatDirector.Squadron.Attitude attitude
    )
    {
        int squadronId = 999; // Using a high ID for scripted squads to avoid conflict with legacy for now
        CombatDirector.Squadron newSquad = gc.combatDirector.RegisterSquadron(squadronId, attitude);

        if (attitude == CombatDirector.Squadron.Attitude.Enemy)
            newSquad.target = gc.player;

        PackedScene prefab = type == "TigersClaw" ? tigersClawPrefab : npcPrefab;

        for (int i = 0; i < count; i++)
        {
            Npc npc = (Npc)prefab.Instantiate();
            npc.Name = $"{type}_{System.Guid.NewGuid().ToString().Substring(0, 4)}"; // Unique simple name
            npc.gameController = gc;
            npc.shipSpecification = ShipFactory.presetFromString(type);

            // Offset spawn position so NPCs don't spawn on top of each other
            float spawnOffset = 15.0f;
            float angle = (i * 360f / Mathf.Max(count, 1)) * (Mathf.Pi / 180f);
            Vector3 offset = new Vector3(
                Mathf.Cos(angle) * spawnOffset * (i > 0 ? 1 : 0),
                0,
                Mathf.Sin(angle) * spawnOffset * (i > 0 ? 1 : 0)
            );
            npc.Position = position + offset;

            gc.RegisterTarget(npc);
            gc.SectorRoot.CallDeferred("add_child", npc);
            newSquad.Add(npc);

            GD.Print($"[Script] Spawned {npc.Name} ({type}) at {npc.Position}");
        }
    }
    public void SpawnWingman(string type, string callsign, Vector3 position)
    {
        int squadronId = 0; // Player Squadron ID
        CombatDirector.Squadron playerSquad = gc.combatDirector.RegisterSquadron(squadronId, CombatDirector.Squadron.Attitude.Friend);
        playerSquad.playerSquadron = true;
        playerSquad.target = null; // Follow player logic handles targeting

        PackedScene prefab = type == "TigersClaw" ? tigersClawPrefab : npcPrefab;
        Npc npc = (Npc)prefab.Instantiate();
        npc.Name = callsign;
        npc.gameController = gc;
        npc.shipSpecification = ShipFactory.presetFromString(type);
        
        // Wingman Personality
        // npc.MyPersonality = Npc.Personality.Wingman; // Assuming accessible or Default

        npc.Position = position;

        gc.RegisterTarget(npc);
        gc.SectorRoot.CallDeferred("add_child", npc);
        playerSquad.Add(npc);
        
        // Default Order
        npc.ReceiveOrder(new CombatDirector.SquadOrder(CombatDirector.OrderType.FormUp));

        GD.Print($"[Script] Spawned Wingman {npc.Name} at {npc.Position}");
    }
}

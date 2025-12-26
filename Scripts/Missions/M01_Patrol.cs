using System;
using Godot;
using Planetsgodot.Scripts.Combat;
using Planetsgodot.Scripts.Controllers;
using Planetsgodot.Scripts.Core;
using Planetsgodot.Scripts.Environment;

namespace Planetsgodot.Scripts.Missions;

public partial class M01_Patrol : MissionBase
{
    public M01_Patrol()
    {
        // Asteroids = AsteroidFieldSpec.Create(50, 40f);
        // Asteroids.OrbitSpeed = 0.0f;
        // Asteroids.X = 20f; // Center on player start
        // Asteroids.Y = -2000f;
    }

    public override void Setup(MissionController controller)
    {
        base.Setup(controller);
        Title = "Mission 1: Patrol Sector Alpha";
        Description = "Launch from Tigers Claw, patrol Nav Points Alpha, Beta, Gamma.";

        // --- Waypoints ---
        Vector3 wp1 = new Vector3(0, 0, -2000); // Asteroids
        Vector3 wp2 = new Vector3(3000, 0, -2000); // Open Space
        Vector3 wp3 = new Vector3(3000, 0, 0); // Ambush

        // --- Objectives ---
        AddObjective("launch", "Launch and proceed to Nav Alpha", true);
        AddObjective("nav_alpha", "Patrol Nav Alpha (Asteroid Field)", true, true);
        AddObjective("nav_beta", "Proceed to Nav Beta (Open Space)", true, true);
        AddObjective("nav_gamma", "Proceed to Nav Gamma", true, true);
        AddObjective("kill_pirates", "Destroy Pirate Ambush", true, true);

        // --- Initial State ---
        // Asteroids ACTIVE at start
        Asteroids = AsteroidFieldSpec.Create(1000, 100f);
        Asteroids.OrbitSpeed = 0.2f;
        Asteroids.X = 0;
        Asteroids.Y = -2000;

        // --- Triggers & Sequence ---

        // 0. Start / Launch
        AddTrigger(
            new TimeTrigger(1.0f),
            () =>
            {
                // Spawn Carrier
                Vector3 carrierPos = new Vector3(10, 0, 10);
                var carrier = controller.SpawnShip(
                    "TigersClaw",
                    "Tiger's Claw",
                    carrierPos,
                    CombatDirector.Squadron.Attitude.Neutral
                );

                // Give carrier a patrol/defend position outside asteroid field
                Vector3 carrierPatrolPos = new Vector3(-200, 0, -200);
                carrier?.ReceiveOrder(new CombatDirector.SquadOrder(
                    CombatDirector.OrderType.Defend,
                    null,
                    carrierPatrolPos
                ));

                // Message
                PlayMessage(
                    "Tiger's Claw",
                    "Maverick, you are clear for launch. Proceed to Nav Alpha."
                );
                SetObjectiveStatus("launch", ObjectiveStatus.Active);
                SetObjectiveStatus("nav_alpha", ObjectiveStatus.Active);
                SetNavPoint(wp1);
            }
        );

        // 1. Reaching Nav Alpha (WP1)
        AddTrigger(
            new DistanceTrigger(() => controller.PlayerShip, wp1, 40f),
            () =>
            {
                PlayMessage("Command", "Nav Alpha reached. Caution, asteroid density high.");
                SetObjectiveStatus("nav_alpha", ObjectiveStatus.Complete);
                SetObjectiveStatus("nav_beta", ObjectiveStatus.Active);
                SetNavPoint(wp2);


                // Ensure asteroids are THICK here if possible, or assume they are already on.
            }
        );

        // 2. Reaching Nav Beta (WP2)
        AddTrigger(
            new DistanceTrigger(() => controller.PlayerShip, wp2, 40f),
            () =>
            {
                PlayMessage("Command", "Nav Beta reached. Sensors clear. Proceeding to Gamma.");
                SetObjectiveStatus("nav_beta", ObjectiveStatus.Complete);
                SetObjectiveStatus("nav_gamma", ObjectiveStatus.Active);
                SetNavPoint(wp3);

                // Disable Asteroids?
                // Assuming GameController checks Mission.Asteroids properties every frame or so?
                // If not, we might need a way to Signal the asteroid field.
                // For now, let's try setting count to 0?
                // Asteroids.Count = 0;
                // Note: This relies on OptimizedAsteroidField.cs responding to this change.
            }
        );

        // 3. Reaching Nav Gamma (WP3) - Ambush
        AddTrigger(
            new DistanceTrigger(() => controller.PlayerShip, wp3, 300f),
            () =>
            {
                PlayMessage("Command", "Nav Gamma reached... wait, contacts!");
                SetObjectiveStatus("nav_gamma", ObjectiveStatus.Complete);
                SetObjectiveStatus("kill_pirates", ObjectiveStatus.Active);
                ClearNavPoint();

                // Spawn Pirates
                SpawnWing(
                    "Adder",
                    2,
                    wp3 + new Vector3(0, 0, 0),
                    CombatDirector.Squadron.Attitude.Enemy
                );
                PlayMessage("Pirate Leader", "Target locked! Fire!");
            }
        );

        // 4. Win Condition (All Enemies Dead)
        // We know we spawn 2 enemies.
        // We can check if "Enemy" count is 0 AFTER the spawning phase.
        // But checking "0 enemies" at start triggers immediately.
        // So we need a state flag or check if Ambush has triggered.

        bool ambushTriggered = false;
        AddTrigger(
            new DistanceTrigger(() => controller.PlayerShip, wp3, 300f),
            () => { ambushTriggered = true; }
        );

        AddTrigger(
            new UnitCountCheckTrigger(
                () => controller.GetShipCount(CombatDirector.Squadron.Attitude.Enemy),
                0
            ),
            () =>
            {
                if (ambushTriggered)
                {
                    PlayMessage("Command", "Bandits splashed. Great work, Maverick. RTB.");
                    SetObjectiveStatus("kill_pirates", ObjectiveStatus.Complete);
                    EndMission(true);
                }
            }
        );
    }
}

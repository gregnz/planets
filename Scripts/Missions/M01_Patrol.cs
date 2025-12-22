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
        Asteroids = AsteroidFieldSpec.Create(4000, 500f);
        Asteroids.OrbitSpeed = 0.2f;
        Asteroids.X = 0f; // Center on player start
        Asteroids.Y = 0f;
    }

    public override void Setup(MissionController controller)
    {
        base.Setup(controller);
        Title = "Mission 1: Patrol Sector Alpha";
        Description = "Standard patrol route. Keep your eyes open, Pilot.";

        // Objectives
        AddObjective("patrol_alpha", "Patrol Alpha Nav Point", true);
        AddObjective("survive_ambush", "Survive Ambush", true, true);

        // --- Intro Sequence ---
        AddTrigger(
            new TimeTrigger(1.0f),
            () =>
            {
                PlayMessage("Command", "Maverick, this is Command. Proceed to patrol coordinates.");
            }
        );

        AddTrigger(
            new TimeTrigger(1.0f),
            () =>
            {
                for (int i = 0; i < 1; i++)
                {
                    var offset = new Vector3(-10 + (i * 3), 0, -10 + (i % 2 * 3)); // Basic scatter
                    controller.SpawnWingman("ImperialEagle", $"Alpha {i + 2}", offset);
                }
                PlayMessage("Alpha 2", "Squadron Alpha forming up on your wing, Lead.");
            }
        );

        // --- The Ambush ---
        // Simulating "Arriving at Nav Point" with a timer for now
        // (In real game, use ZoneEnterTrigger(Player, NavAlpha))
        AddTrigger(
            new TimeTrigger(1.0f),
            () =>
            {
                PlayMessage("Wingman", "Wait... I'm picking up spikes on the radar!");
                SpawnWing(
                    "Adder",
                    1,
                    new Vector3(20, 0, 20),
                    CombatDirector.Squadron.Attitude.Enemy
                );

                SetObjectiveStatus("patrol_alpha", ObjectiveStatus.Complete);
                SetObjectiveStatus("survive_ambush", ObjectiveStatus.Active);
                PlayMessage("Command", "Pirates! Engage and destroy!");
            }
        );

        // --- Win Condition ---
        // Simplified: Win after 30 seconds or when enemies dead (need UnitDestroyed trigger for that)
        // For this milestone, we'll just time it out to demonstrate flow
        AddTrigger(
            new TimeTrigger(30.0f),
            () =>
            {
                PlayMessage("Command", "Sector clear. Good work, Pilot. RTB.");
                EndMission(true);
            }
        );
    }
}

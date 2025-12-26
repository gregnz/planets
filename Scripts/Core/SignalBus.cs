using System;
using System.Collections.Generic;
using Godot;

public partial class SignalBus : Node
{
    [Signal]
    public delegate void HealthChangedEventHandler(int health);

    [Signal]
    public delegate void StatusChangedEventHandler(int health);

    [Signal]
    public delegate void WeaponStatusChangedEventHandler(Godot.Collections.Dictionary status);

    [Signal]
    public delegate void NpcTargetChangedEventHandler(Godot.Collections.Dictionary status);

    [Signal]
    public delegate void DiedEventHandler();

    // --- Mission Signals ---
    [Signal]
    public delegate void MissionMessageEventHandler(string sender, string message);

    [Signal]
    public delegate void MissionObjectiveUpdatedEventHandler(string id, string desc, int status);

    [Signal]
    public delegate void SetNavPointEventHandler(Vector3 position);

    [Signal]
    public delegate void ClearNavPointEventHandler();

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
    {
    }
}

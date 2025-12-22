using System;
using System.Collections.Generic;
using Godot;
using Planetsgodot.Scripts.Core;
using Planetsgodot.Scripts.Combat;
using Planetsgodot.Scripts.Controllers;
using Planetsgodot.Scripts.Environment;

namespace Planetsgodot.Scripts.Missions;

public abstract partial class MissionBase : Node
{
    public string Id { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }

    protected MissionController _controller;

    private List<MissionTrigger> _triggers = new();
    public Dictionary<string, MissionObjective> Objectives { get; private set; } = new();

    public float MissionTime { get; private set; }

    public AsteroidFieldSpec Asteroids { get; set; } = new AsteroidFieldSpec();

    public virtual void Setup(MissionController controller)
    {
        _controller = controller;
        MissionTime = 0;
        // Subclasses implement specific setup here
    }

    public override void _Process(double delta)
    {
        Update((float)delta);
    }

    public virtual void Update(float delta)
    {
        MissionTime += delta;
        foreach (var trigger in _triggers)
        {
            trigger.Evaluate(this);
        }
    }

    // --- Scripting Helpers ---

    protected void AddTrigger(MissionTrigger trigger, Action action)
    {
        trigger.OnTrigger = action;
        _triggers.Add(trigger);
    }

    protected void AddObjective(
        string id,
        string desc,
        bool isPrimary = true,
        bool isHidden = false
    )
    {
        Objectives[id] = new MissionObjective
        {
            Id = id,
            Description = desc,
            IsPrimary = isPrimary,
            IsHidden = isHidden,
            Status = ObjectiveStatus.Active,
        };
    }

    protected void SetObjectiveStatus(string id, ObjectiveStatus status)
    {
        if (Objectives.ContainsKey(id))
        {
            Objectives[id].Status = status;
            GD.Print($"Objective Updated: {Objectives[id].Description} -> {status}");
            var bus = GetNode<SignalBus>("/root/SignalBus");
            bus.EmitSignal(
                SignalBus.SignalName.MissionObjectiveUpdated,
                id,
                Objectives[id].Description,
                (int)status
            );
        }
    }

    protected void SpawnWing(string wingId)
    {
        // Delegate to MissionController to spawn the defined wing
        // _controller.SpawnWing(wingId);
        GD.Print($"[Script] Spawning Wing: {wingId}");
    }

    protected void SpawnWing(
        string type,
        int count,
        Vector3 position,
        CombatDirector.Squadron.Attitude attitude
    )
    {
        _controller.SpawnWing(type, count, position, attitude);
    }

    protected void PlayMessage(string sender, string message)
    {
        GD.Print($"[Comms] {sender}: {message}");
        var bus = GetNode<SignalBus>("/root/SignalBus");
        bus.EmitSignal(SignalBus.SignalName.MissionMessage, sender, message);
    }

    protected void EndMission(bool success)
    {
        GD.Print($"[Mission] Mission Ended. Success: {success}");
        CampaignManager.Instance.CompleteMission(success);
        // _controller.EndMission(success);
    }
}

// --- Trigger Classes ---

public abstract class MissionTrigger
{
    public bool OneShot { get; set; } = true;
    public bool HasTriggered { get; set; } = false;
    public Action OnTrigger { get; set; }

    public void Evaluate(MissionBase mission)
    {
        if (OneShot && HasTriggered)
            return;

        if (CheckCondition(mission))
        {
            OnTrigger?.Invoke();
            HasTriggered = true;
        }
    }

    protected abstract bool CheckCondition(MissionBase mission);
}

public class TimeTrigger : MissionTrigger
{
    private float _triggerTime;

    public TimeTrigger(float time)
    {
        _triggerTime = time;
    }

    protected override bool CheckCondition(MissionBase mission)
    {
        return mission.MissionTime >= _triggerTime;
    }
}

// --- Objective Classes ---

public class MissionObjective
{
    public string Id;
    public string Description;
    public bool IsPrimary;
    public bool IsHidden;
    public ObjectiveStatus Status;
}

public enum ObjectiveStatus
{
    Active,
    Complete,
    Failed,
}

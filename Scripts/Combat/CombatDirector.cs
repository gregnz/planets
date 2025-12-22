using System;
using System.Collections.Generic;
using Godot;
using Planetsgodot.Scripts.Controllers;
using Planetsgodot.Scripts.Core;
using Planetsgodot.Scripts.AI;

namespace Planetsgodot.Scripts.Combat;

/**
 * CombatDirector: Manages Squadrons and Issues Orders.
 * 
 * Simplified from original prototype. Now focuses on:
 * - Registering Squadrons
 * - Maintaining references to squad members
 * - Broadcasting commands (Orders) to squad members
 */
public class CombatDirector
{
    // New Order Types
    public enum OrderType
    {
        None,           // Default/No Order
        FormUp,         // Follow Leader / Return to Formation
        AttackTarget,   // Attack specific target
        FreeFire,       // Attack nearest threats
        Defend,         // Protect a target
        Evasion,        // Break and evade
        HoldFire        // Do not fire
    }

    public struct SquadOrder
    {
        public OrderType Type;
        public ITarget Target;
        public Vector3? Position;

        public SquadOrder(OrderType type, ITarget target = null, Vector3? position = null)
        {
            Type = type;
            Target = target;
            Position = position;
        }

        public override string ToString() => $"{Type} (Tg: {Target}, Pos: {Position})";
    }

    public class Squadron
    {
        public int id;
        public bool playerSquadron;

        public enum Attitude
        {
            Friend,
            Enemy,
            Neutral,
            None
        }

        public Attitude attitude { get; set; }
        public List<Npc> squad = new List<Npc>();
        public ITarget target;

        public Squadron(int id, Attitude state)
        {
            this.id = id;
            this.attitude = state;
        }

        internal void Add(Npc squadmember)
        {
            if (!squad.Contains(squadmember)) 
            {
                squad.Add(squadmember);
                squadmember.SetSquadron(this);
            }
        }

        public void IssueOrder(SquadOrder order)
        {
             GD.Print($"Squadron {id}: Received Order {order}");

            if (order.Type == OrderType.FormUp)
            {
                // === SMART FORMATION ASSIGNMENT ===
                // Assigns ships to the closest formation slot to prevent crossing paths
                
                // 1. Get Leader (Player or Squad Leader)
                Node3D leader = null;
                if (playerSquadron)
                {
                    // Assuming GameController has static access or we find it
                    // For now, finding player via Group as fallback/convenience
                     var playerNode = ((SceneTree)Engine.GetMainLoop()).GetFirstNodeInGroup("Player") as Node3D;
                     leader = playerNode;
                }
                
                if (leader != null)
                {
                    FormationManager formation = new FormationManager(); // Use default settings (Wedge)
                    // If we want to support changing formation type, we'd need to pass that in the Order or store it in Squadron
                    
                    // 2. Get Targets
                    // Slots needed = squad size. 
                    // Note: Formation Index 0 is Leader. Wingmen start at 1.
                    // So we need slots 1 to squad.Count
                    
                    var assignments = new Dictionary<Npc, int>();
                    var availableSlots = new List<int>();
                    for (int i = 1; i <= squad.Count; i++) availableSlots.Add(i);
                    
                    // 3. Greedy Assignment
                    // For each ship, find closest slot? Or for each slot, find closest ship?
                    // Global Optimization is hard (Hungarian Alg). Greedy is "Good Enough".
                    
                    var unassignedShips = new List<Npc>(squad);
                    
                    // Filter out dead/null ships first
                    unassignedShips.RemoveAll(x => x == null || x.IsDead());

                    Vector3 leaderPos = leader.GlobalPosition;
                    float leaderRot = leader.Rotation.Y;

                    // Simple approach: Iterate through slots, pick closest ship for each
                    foreach (int slotIndex in availableSlots)
                    {
                        if (unassignedShips.Count == 0) break;

                        Vector3 slotWorldPos = formation.GetFormationPosition(slotIndex, leaderPos, leaderRot);
                        
                        Npc closestShip = null;
                        float minDistSq = float.MaxValue;
                        
                        foreach (var ship in unassignedShips)
                        {
                            float d = ship.GlobalPosition.DistanceSquaredTo(slotWorldPos);
                            if (d < minDistSq)
                            {
                                minDistSq = d;
                                closestShip = ship;
                            }
                        }
                        
                        if (closestShip != null)
                        {
                            assignments[closestShip] = slotIndex;
                            unassignedShips.Remove(closestShip);
                        }
                    }
                    
                    // 4. Apply Assignments
                    foreach (var kvp in assignments)
                    {
                        Npc ship = kvp.Key;
                        int index = kvp.Value;
                        
                        // We need to set this on the AIController. 
                        // The Npc.ReceiveOrder will eventually set state to Formation.
                        // We must pre-seed the index logic on the Npc or AI.
                        // Currently Npc.ReceiveOrder calls: shipAi.ForceState(AIController.AIState.Formation);
                        // It does NOT currently let us set the Index from outside easily except via Npc property?
                        // Let's modify AIController to expose FormationIndex publicly (it is public).
                        // We need access to AIController from NPC. Npc has 'shipAi'.
                        // But Npc.shipAi is private.
                        // We can add a method to NPC 'SetFormationIndex(int i)'
                        
                        ship.SetFormationIndex(index);
                    }
                }
            }

             foreach (Npc member in squad)
             {
                 if (member != null && !member.IsDead())
                 {
                     member.ReceiveOrder(order);
                 }
             }
        }
    }

    Dictionary<int, Squadron> squadrons = new();

    public Squadron RegisterSquadron(int id, Squadron.Attitude state)
    {
        if (!squadrons.ContainsKey(id))
        {
            squadrons.Add(id, new Squadron(id, state));
        }

        return squadrons[id];
    }

    public void IssuePlayerSquadronOrder(SquadOrder order)
    {
        foreach (var kvp in squadrons)
        {
            if (kvp.Value.playerSquadron)
            {
                kvp.Value.IssueOrder(order);
            }
        }
    }
    
    public void Clear()
    {
        squadrons.Clear();
    }
}

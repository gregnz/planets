using System;
using System.Collections.Generic;
using Godot;
using Planetsgodot.Scripts.Combat;

namespace Planetsgodot.Scripts.AI
{
    /// <summary>
    /// Represents a log entry in the AI's decision history.
    /// </summary>
    public class DecisionLog
    {
        public double Timestamp { get; private set; }
        public string Action { get; private set; }
        public string Reason { get; private set; }
        public float Score { get; private set; }

        public DecisionLog(string action, string reason, float score)
        {
            Timestamp = Time.GetTicksMsec() / 1000.0;
            Action = action;
            Reason = reason;
            Score = score;
        }

        public override string ToString()
        {
            return $"[{Timestamp:F1}s] {Action}: {Reason} (Score: {Score:F2})";
        }
    }

    /// <summary>
    /// Interface for any evaluation logic (Consideration).
    /// </summary>
    public interface IConsideration
    {
        string Name { get; }

        /// <summary>
        /// Evaluate this consideration and return a score (0 to 1).
        /// If score > 0, reason should be populated.
        /// </summary>
        EvaluationResult Evaluate(AIController ai);
    }

    public struct EvaluationResult
    {
        public float Score;
        public string Reason;
        public AIController.AIState SuggestedState;

        // Optional: Tactical position if relevant
        public TacticalPosition? SuggestedTactic;
    }

    /// <summary>
    /// The "Brain" that manages considerations, makes decisions, and tracks history.
    /// </summary>
    public class AIDecisionEngine
    {
        private AIController _ai;
        private List<IConsideration> _considerations = new List<IConsideration>();

        // History
        public List<DecisionLog> History { get; private set; } = new List<DecisionLog>();
        private const int MaxHistorySize = 50;

        // State
        // State
        public string CurrentThought { get; private set; } = "Thinking...";
        private EvaluationResult _lastResult;

        // Timing
        private float _thinkingTimer = 0f;
        private float _reactionInterval = 0.25f; // "Human" reaction time

        private float _commitmentTimer = 0f;
        private float _minCommitmentDuration = 3.0f; // Stick to decision for 3s
        private float _currentScore = 0f; // Score of the currently active decision

        public AIDecisionEngine(AIController ai)
        {
            _ai = ai;
            InitializeConsiderations();
        }

        private void InitializeConsiderations()
        {
            // Priority 1: Self Preservation (Retreat)
            _considerations.Add(new LowHealthConsideration());

            // Priority 2: Dogfight (Attack)
            _considerations.Add(new AggressiveConsideration());

            // Priority 3: Collision/Safety (Evade)
            // Note: Collision is often reactive override, but can be here too
            _considerations.Add(new EvasionConsideration());

            // Priority 4: Orders (High priority, overrides standard aggression)
            _considerations.Add(new SquadOrderConsideration());
        }

        public void Update(double delta)
        {
            float dt = (float)delta;
            _thinkingTimer -= dt;
            _commitmentTimer -= dt;

            // Simulate Reaction Time (Don't evaluate every frame)
            if (_thinkingTimer > 0) return;

            // Randomize next thought interval (0.1s - 0.3s)
            _thinkingTimer = (float)GD.RandRange(0.1f, 0.3f);

            EvaluationResult bestResult = new EvaluationResult { Score = -1f };

            // 1. Evaluate all considerations
            foreach (var consideration in _considerations)
            {
                var result = consideration.Evaluate(_ai);
                if (result.Score > bestResult.Score)
                {
                    bestResult = result;
                }
            }

            // 2. Decision Logic with Commitment
            bool stateChanged = bestResult.SuggestedState != _ai.CurrentState;

            // If we have a valid winner
            if (bestResult.Score > 0)
            {
                bool shouldSwitch = false;

                // Rule: If currently committed, only switch if the new score is significantly better (Interrupt)
                // Threshold: 0.8 (Critical) vs current score + bias
                if (_commitmentTimer > 0)
                {
                    // Interrupt Condition: New score is very high (Critical) AND better than current
                    // OR New score is significantly better than current (> +0.25)
                    if (bestResult.Score > 0.8f && bestResult.Score > _currentScore)
                    {
                        shouldSwitch = true;
                    }
                    else if (bestResult.Score > _currentScore + 0.25f)
                    {
                        shouldSwitch = true;
                    }
                    // Else: Stick to current plan
                }
                else
                {
                    // Commitment expired: Switch if different and better/equal
                    if (bestResult.Score >= _currentScore) // bias towards new if tied?
                    {
                        shouldSwitch = true;
                    }
                }

                if (shouldSwitch && stateChanged && bestResult.SuggestedState != AIController.AIState.Idle)
                {
                    LogDecision(bestResult.SuggestedState.ToString(), bestResult.Reason, bestResult.Score);
                    _ai.CurrentState = bestResult.SuggestedState;

                    if (bestResult.SuggestedTactic.HasValue)
                    {
                        _ai.AttackTactic = bestResult.SuggestedTactic.Value;
                    }

                    // Reset Commitment
                    _commitmentTimer = (float)GD.RandRange(2.0f, 4.0f);
                    _currentScore = bestResult.Score;
                }
                else if (!stateChanged)
                {
                    // Just updating thought/score for current state
                    _currentScore = bestResult.Score;
                }

                CurrentThought = $"{bestResult.Reason} ({bestResult.Score:F2})";
                if (_commitmentTimer > 0) CurrentThought += $" [Locked {_commitmentTimer:F1}s]";
            }

            _lastResult = bestResult;
        }

        private void LogDecision(string action, string reason, float score)
        {
            var log = new DecisionLog(action, reason, score);
            History.Add(log);
            if (History.Count > MaxHistorySize) History.RemoveAt(0);

            GD.Print($"AI DECISION: {log}");
        }
    }

    // === IMPLEMENTATIONS ===

    public class LowHealthConsideration : IConsideration
    {
        public string Name => "Self Preservation";

        public EvaluationResult Evaluate(AIController ai)
        {
            if (ai._ship == null) return new EvaluationResult();

            float health = ai._ship.HealthPercent;

            if (health < 0.25f)
            {
                // CRITICAL DANGER
                return new EvaluationResult
                {
                    Score = 0.9f, // Very high priority
                    Reason = $"Health Critical ({health:P0}) - Retreating!",
                    SuggestedState = AIController.AIState.Retreat
                };
            }
            else if (health < 0.5f && ai.CurrentState == AIController.AIState.Retreat)
            {
                // Stick to retreat until healthy enough
                return new EvaluationResult
                {
                    Score = 0.8f,
                    Reason = $"Recovering Shields ({health:P0})",
                    SuggestedState = AIController.AIState.Retreat
                };
            }

            return new EvaluationResult { Score = 0f };
        }
    }

    public class AggressiveConsideration : IConsideration
    {
        public string Name => "Aggression";

        public EvaluationResult Evaluate(AIController ai)
        {
            // Default behavior if healthy and has target
            if (ai.TargetNode != null && ai._ship != null && ai._ship.HealthPercent >= 0.25f)
            {
                float dist = ai.TargetNode.GlobalPosition.DistanceTo(ai._ship.Rb.GlobalPosition);

                // Score can scale with opportunity? For now constant baseline.
                return new EvaluationResult
                {
                    Score = 0.5f, // Base importance
                    Reason = $"Engaging Target (Dist: {dist:F0})",
                    SuggestedState = AIController.AIState.AttackRun
                };
            }

            return new EvaluationResult { Score = 0f };
        }
    }

    public class EvasionConsideration : IConsideration
    {
        public string Name => "Evasion";

        public EvaluationResult Evaluate(AIController ai)
        {
            // Check if we were recently hit (Pain response)
            double timeSinceHit = Time.GetTicksMsec() / 1000.0 - ai._ship.LastHitTime;

            if (timeSinceHit < 2.0f)
            {
                return new EvaluationResult
                {
                    Score = 0.7f, // Higher than Aggression (0.5) but lower than Critical Health (0.9)
                    Reason = "Taking Fire! Evasive Maneuvers",
                    SuggestedState = AIController.AIState.Evasion,
                    SuggestedTactic = TacticalPosition.Evade
                };
            }

            return new EvaluationResult { Score = 0f };
        }
    }

    public class SquadOrderConsideration : IConsideration
    {
        public string Name => "Squad Orders";

        public EvaluationResult Evaluate(AIController ai)
        {
            switch (ai.CurrentOrder)
            {
                case CombatDirector.OrderType.FormUp:
                    return new EvaluationResult
                    {
                        Score = 0.95f, // Priority over almost everything except critical survival
                        Reason = "Following Squad Order: Form Up",
                        SuggestedState = AIController.AIState.Formation
                    };

                case CombatDirector.OrderType.HoldFire:
                    return new EvaluationResult
                    {
                        Score = 0.95f,
                        Reason = "Following Squad Order: Hold Fire",
                        SuggestedState = AIController.AIState.Idle // Or similar non-combat state
                    };

                case CombatDirector.OrderType.AttackTarget:
                    if (ai.TargetNode != null && ai._ship != null)
                    {
                        return new EvaluationResult
                        {
                            Score = 0.85f, // Higher priority than standard aggression
                            Reason = "Following Squad Order: Attack Target",
                            SuggestedState = AIController.AIState.AttackRun
                        };
                    }

                    break;

                case CombatDirector.OrderType.Evasion:
                    return new EvaluationResult
                    {
                        Score = 0.95f,
                        Reason = "Following Squad Order: Break/Evasion",
                        SuggestedState = AIController.AIState.CombatFly, // Should actually be Evasion
                        SuggestedTactic = TacticalPosition.Evade
                    };
            }

            return new EvaluationResult { Score = 0f };
        }
    }
}

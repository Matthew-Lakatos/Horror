using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Eidolon.Core;
using Eidolon.Data;

namespace Eidolon.AI
{
    /// <summary>
    /// Utility AI brain. Evaluates all available actions by scoring them
    /// against the current world context and returns the highest-scoring option.
    /// Weights are defined per-enemy in ThreatProfile ScriptableObjects.
    /// </summary>
    public class UtilityBrain : MonoBehaviour
    {
        [SerializeField] private List<UtilityActionEntry> _actions = new List<UtilityActionEntry>();
        [SerializeField] private float _noiseThreshold = 0.01f; // ignore near-zero scores

        private EnemyActor _actor;

        private void Awake()
        {
            _actor = GetComponent<EnemyActor>();
        }

        // ─── Evaluation ──────────────────────────────────────────────────────

        /// <summary>
        /// Scores all registered actions and returns the best candidate.
        /// Ties broken randomly to prevent deterministic loops.
        /// </summary>
        public EnemyAction Evaluate(EnemyActor actor)
        {
            var context  = BuildContext(actor);
            float bestScore = _noiseThreshold;
            EnemyAction bestAction = null;

            foreach (var entry in _actions)
            {
                if (entry.Action == null || !entry.Action.IsReady) continue;

                float score = ScoreAction(entry, context);
                if (score > bestScore)
                {
                    bestScore  = score;
                    bestAction = entry.Action;
                }
                // Tie-breaking: slight random noise prevents loops
                else if (Mathf.Approximately(score, bestScore) && Random.value > 0.5f)
                {
                    bestAction = entry.Action;
                }
            }

            return bestAction;
        }

        // ─── Context Building ────────────────────────────────────────────────

        private UtilityContext BuildContext(EnemyActor actor)
        {
            var memory    = actor.MemoryModule;
            var blackboard = AIManager.Instance?.SharedBlackboard;
            var director  = GameDirector.Instance;

            return new UtilityContext
            {
                PlayerVisible         = (actor.SensorModule as VisionSensorModule)?.PlayerVisible ?? false,
                HasRecentSighting     = memory?.LastSightingTime > 0 && Time.time - memory.LastSightingTime < 15f,
                HasRecentNoise        = blackboard?.HasRecentNoise() ?? false,
                LastKnownPlayerPos    = memory?.LastKnownPlayerPos ?? Vector3.zero,
                DistanceToLastKnown   = memory != null ? Vector3.Distance(actor.transform.position, memory.LastKnownPlayerPos) : float.MaxValue,
                CurrentTension        = director?.CurrentTension ?? TensionState.Calm,
                CurrentPhase          = director?.CurrentPhase   ?? GamePhase.EarlyGame,
                AIAggression          = director?.AIAggressionMultiplier ?? 0f,
                PlayerIsInjured       = blackboard?.PlayerIsInjured ?? false,
                PlayerInPanic         = blackboard?.PlayerInPanic   ?? false,
                IsChasing             = actor.IsChasing,
                Actor                 = actor
            };
        }

        // ─── Scoring ────────────────────────────────────────────────────────

        private float ScoreAction(UtilityActionEntry entry, UtilityContext ctx)
        {
            float score = entry.BaseWeight;

            foreach (var scorer in entry.Scorers)
                score *= scorer.Score(ctx);

            // Apply global aggression multiplier for hostile actions
            if (entry.IsHostileAction)
                score *= ctx.AIAggression;

            return Mathf.Clamp01(score);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // UTILITY CONTEXT
    // ─────────────────────────────────────────────────────────────────────────

    public struct UtilityContext
    {
        public bool        PlayerVisible;
        public bool        HasRecentSighting;
        public bool        HasRecentNoise;
        public Vector3     LastKnownPlayerPos;
        public float       DistanceToLastKnown;
        public TensionState CurrentTension;
        public GamePhase   CurrentPhase;
        public float       AIAggression;
        public bool        PlayerIsInjured;
        public bool        PlayerInPanic;
        public bool        IsChasing;
        public EnemyActor  Actor;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // UTILITY ACTION ENTRY
    // ─────────────────────────────────────────────────────────────────────────

    [System.Serializable]
    public class UtilityActionEntry
    {
        [SerializeField] public EnemyAction       Action;
        [SerializeField] public float             BaseWeight      = 1f;
        [SerializeField] public bool              IsHostileAction = false;
        [SerializeField] public List<UtilityScorer> Scorers       = new List<UtilityScorer>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // UTILITY SCORERS — pluggable scoring functions
    // ─────────────────────────────────────────────────────────────────────────

    public abstract class UtilityScorer : ScriptableObject
    {
        public abstract float Score(UtilityContext ctx);
    }

    [CreateAssetMenu(menuName = "Eidolon/AI/Scorer/PlayerVisible")]
    public class PlayerVisibleScorer : UtilityScorer
    {
        [SerializeField] private float _trueMultiplier  = 2f;
        [SerializeField] private float _falseMultiplier = 0.2f;
        public override float Score(UtilityContext ctx)
            => ctx.PlayerVisible ? _trueMultiplier : _falseMultiplier;
    }

    [CreateAssetMenu(menuName = "Eidolon/AI/Scorer/DistanceToPlayer")]
    public class DistanceToPlayerScorer : UtilityScorer
    {
        [SerializeField] private float _maxRange = 20f;
        [SerializeField] private AnimationCurve _curve = AnimationCurve.EaseInOut(0, 1, 1, 0);
        public override float Score(UtilityContext ctx)
        {
            float t = Mathf.Clamp01(ctx.DistanceToLastKnown / _maxRange);
            return _curve.Evaluate(t);
        }
    }

    [CreateAssetMenu(menuName = "Eidolon/AI/Scorer/TensionState")]
    public class TensionStateScorer : UtilityScorer
    {
        [SerializeField] private TensionState _requiredState;
        [SerializeField] private float _matchMultiplier    = 2f;
        [SerializeField] private float _nonMatchMultiplier = 0.5f;
        public override float Score(UtilityContext ctx)
            => ctx.CurrentTension == _requiredState ? _matchMultiplier : _nonMatchMultiplier;
    }

    [CreateAssetMenu(menuName = "Eidolon/AI/Scorer/PlayerInjured")]
    public class PlayerInjuredScorer : UtilityScorer
    {
        [SerializeField] private float _injuredMultiplier = 1.8f;
        public override float Score(UtilityContext ctx)
            => ctx.PlayerIsInjured ? _injuredMultiplier : 1f;
    }

    [CreateAssetMenu(menuName = "Eidolon/AI/Scorer/IsChasing")]
    public class IsChasingScorer : UtilityScorer
    {
        [SerializeField] private float _chasingMultiplier    = 1.5f;
        [SerializeField] private float _notChasingMultiplier = 1f;
        public override float Score(UtilityContext ctx)
            => ctx.IsChasing ? _chasingMultiplier : _notChasingMultiplier;
    }

    [CreateAssetMenu(menuName = "Eidolon/AI/Scorer/HasRecentNoise")]
    public class HasRecentNoiseScorer : UtilityScorer
    {
        [SerializeField] private float _noisyMultiplier = 1.5f;
        public override float Score(UtilityContext ctx)
            => ctx.HasRecentNoise ? _noisyMultiplier : 1f;
    }

    [CreateAssetMenu(menuName = "Eidolon/AI/Scorer/GamePhase")]
    public class GamePhaseScorer : UtilityScorer
    {
        [SerializeField] private GamePhase _requiredPhase;
        [SerializeField] private float _matchMultiplier = 2f;
        [SerializeField] private float _nonMatch        = 0.3f;
        public override float Score(UtilityContext ctx)
            => ctx.CurrentPhase == _requiredPhase ? _matchMultiplier : _nonMatch;
    }
}

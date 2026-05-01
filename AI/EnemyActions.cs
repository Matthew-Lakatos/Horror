using UnityEngine;
using UnityEngine.AI;
using Eidolon.Core;

namespace Eidolon.AI
{
    // ── PATROL ACTION ────────────────────────────────────────────────────────
    // Wanders between waypoints. Used by all enemies when no better option scores.

    [CreateAssetMenu(menuName = "Eidolon/AI/Actions/Patrol")]
    public class PatrolAction : EnemyAction
    {
        [SerializeField] private float _waypointRadius = 8f;

        protected override void OnExecute(EnemyActor actor)
        {
            // Pick a random point on the NavMesh near current position
            var randomDir = Random.insideUnitSphere * _waypointRadius;
            randomDir += actor.transform.position;

            if (NavMesh.SamplePosition(randomDir, out NavMeshHit hit, _waypointRadius, NavMesh.AllAreas))
                actor.MovementModule?.SetDestination(hit.position);
        }
    }

    // ── INVESTIGATE NOISE ACTION ─────────────────────────────────────────────
    // Moves toward last known noise location from the shared blackboard.

    [CreateAssetMenu(menuName = "Eidolon/AI/Actions/InvestigateNoise")]
    public class InvestigateNoiseAction : EnemyAction
    {
        protected override void OnExecute(EnemyActor actor)
        {
            var bb = AIManager.Instance?.SharedBlackboard;
            if (bb == null || !bb.HasRecentNoise()) return;

            actor.MovementModule?.SetDestination(bb.LastNoiseZone);
        }
    }

    // ── INVESTIGATE SIGHTING ACTION ──────────────────────────────────────────
    // Moves toward last known player position from memory.

    [CreateAssetMenu(menuName = "Eidolon/AI/Actions/InvestigateSighting")]
    public class InvestigateSightingAction : EnemyAction
    {
        protected override void OnExecute(EnemyActor actor)
        {
            var pos = actor.MemoryModule?.LastKnownPlayerPos;
            if (pos == null || pos == Vector3.zero) return;

            actor.MovementModule?.SetDestination(pos.Value);
        }
    }

    // ── CHASE ACTION ─────────────────────────────────────────────────────────
    // Directly pursues the player. High aggression, hostile actions only.

    [CreateAssetMenu(menuName = "Eidolon/AI/Actions/Chase")]
    public class ChaseAction : EnemyAction
    {
        [SerializeField] private float _chaseSpeed = 6f;
        [SerializeField] private float _attackDamage = 30f;
        [SerializeField] private float _attackRange  = 1.5f;

        protected override void OnExecute(EnemyActor actor)
        {
            var player = Actors.PlayerController.Instance;
            if (player == null) return;

            actor.MovementModule.Speed = _chaseSpeed;
            actor.MovementModule?.SetDestination(player.transform.position);
            actor.IsChasing = true;

            float dist = Vector3.Distance(actor.transform.position, player.transform.position);
            if (dist <= _attackRange)
            {
                EventBus.Publish(new PlayerDamagedEvent
                {
                    Amount   = _attackDamage,
                    Source   = DamageSource.BigE, // override per enemy via ThreatProfile
                    HitPoint = player.transform.position
                });
                actor.IsChasing = false;
            }
        }
    }

    // ── STAND AND WATCH ACTION ───────────────────────────────────────────────
    // Big E specific. Stops moving, faces player. Pure psychological pressure.

    [CreateAssetMenu(menuName = "Eidolon/AI/Actions/StandAndWatch")]
    public class StandAndWatchAction : EnemyAction
    {
        [SerializeField] private float _watchDuration = 5f;

        protected override void OnExecute(EnemyActor actor)
        {
            actor.MovementModule?.Stop();

            var player = Actors.PlayerController.Instance;
            if (player == null) return;

            var dir = player.transform.position - actor.transform.position;
            dir.y = 0;
            if (dir != Vector3.zero)
                actor.transform.rotation = Quaternion.LookRotation(dir);
        }
    }

    // ── RETREAT ACTION ───────────────────────────────────────────────────────
    // Moves away from player. Used post-attack by Big E, or when losing chase.

    [CreateAssetMenu(menuName = "Eidolon/AI/Actions/Retreat")]
    public class RetreatAction : EnemyAction
    {
        [SerializeField] private float _retreatDistance = 15f;

        protected override void OnExecute(EnemyActor actor)
        {
            var player = Actors.PlayerController.Instance;
            if (player == null) return;

            var awayDir = (actor.transform.position - player.transform.position).normalized;
            var target  = actor.transform.position + awayDir * _retreatDistance;

            if (NavMesh.SamplePosition(target, out NavMeshHit hit, _retreatDistance, NavMesh.AllAreas))
                actor.MovementModule?.SetDestination(hit.position);

            actor.IsChasing = false;
        }
    }
}

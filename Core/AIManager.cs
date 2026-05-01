using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Eidolon.AI;

namespace Eidolon.Core
{
    /// <summary>
    /// Central registry and tick scheduler for all active EnemyActors.
    /// Manages tick budgets per enemy type to avoid Update() abuse.
    /// Applies phase-based monster activation gates.
    /// </summary>
    public class AIManager : MonoBehaviour
    {
        public static AIManager Instance { get; private set; }

        [Header("Tick Intervals (seconds)")]
        [SerializeField] private float _bigETickInterval      = 0.5f;
        [SerializeField] private float _houndTickInterval     = 0.15f;
        [SerializeField] private float _trapperTickInterval   = 0.6f;
        [SerializeField] private float _mimicTickInterval     = 1.0f;
        [SerializeField] private float _nurseTickInterval     = 0.5f;
        [SerializeField] private float _childUnitTickInterval = 0.8f;
        [SerializeField] private float _hollowManTickInterval = 1.2f;
        [SerializeField] private float _facilityTickInterval  = 2.0f;

        // ─── Registry ────────────────────────────────────────────────────────

        private readonly Dictionary<EnemyType, List<EnemyActor>> _enemies
            = new Dictionary<EnemyType, List<EnemyActor>>();

        private readonly Dictionary<EnemyType, Coroutine> _tickCoroutines
            = new Dictionary<EnemyType, Coroutine>();

        // Shared blackboard for partial intel sharing between enemies
        public SharedBlackboard SharedBlackboard { get; private set; } = new SharedBlackboard();

        // ─── Lifecycle ───────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            foreach (EnemyType t in System.Enum.GetValues(typeof(EnemyType)))
                _enemies[t] = new List<EnemyActor>();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<GamePhaseChangedEvent>(OnPhaseChanged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<GamePhaseChangedEvent>(OnPhaseChanged);
        }

        private void Start()
        {
            // Start tick loops for phase-appropriate enemies
            ActivatePhasedEnemies(GamePhase.EarlyGame);
        }

        // ─── Registration ────────────────────────────────────────────────────

        public void RegisterEnemy(EnemyActor actor)
        {
            if (actor == null) return;
            var list = _enemies[actor.EnemyType];
            if (!list.Contains(actor))
            {
                list.Add(actor);
                Debug.Log($"[AIManager] Registered {actor.EnemyType} ({actor.name})");
            }
            EnsureTickLoopRunning(actor.EnemyType);
        }

        public void UnregisterEnemy(EnemyActor actor)
        {
            if (actor == null) return;
            _enemies[actor.EnemyType].Remove(actor);
        }

        // ─── Tick Loops ──────────────────────────────────────────────────────

        private void EnsureTickLoopRunning(EnemyType type)
        {
            if (_tickCoroutines.ContainsKey(type) && _tickCoroutines[type] != null) return;
            _tickCoroutines[type] = StartCoroutine(TickLoop(type, GetTickInterval(type)));
        }

        private IEnumerator TickLoop(EnemyType type, float interval)
        {
            while (true)
            {
                yield return new WaitForSeconds(interval);
                var enemies = _enemies[type];
                for (int i = 0; i < enemies.Count; i++)
                {
                    if (enemies[i] == null || !enemies[i].isActiveAndEnabled) continue;
                    if (!GameDirector.Instance.IsMonsterTypeActiveThisPhase(type)) continue;
                    enemies[i].Tick();
                }
            }
        }

        private float GetTickInterval(EnemyType type) => type switch
        {
            EnemyType.BigE          => _bigETickInterval,
            EnemyType.Hound         => _houndTickInterval,
            EnemyType.Trapper       => _trapperTickInterval,
            EnemyType.Mimic         => _mimicTickInterval,
            EnemyType.Nurse         => _nurseTickInterval,
            EnemyType.ChildUnit     => _childUnitTickInterval,
            EnemyType.HollowMan     => _hollowManTickInterval,
            EnemyType.FacilityBrain => _facilityTickInterval,
            _ => 0.5f
        };

        // ─── Phase-Based Activation ──────────────────────────────────────────

        private void ActivatePhasedEnemies(GamePhase phase)
        {
            foreach (EnemyType type in System.Enum.GetValues(typeof(EnemyType)))
            {
                bool shouldBeActive = GameDirector.Instance.IsMonsterTypeActiveThisPhase(type);
                foreach (var enemy in _enemies[type])
                {
                    if (enemy != null)
                        enemy.SetPhaseActive(shouldBeActive);
                }
            }
        }

        private void OnPhaseChanged(GamePhaseChangedEvent evt)
        {
            ActivatePhasedEnemies(evt.Current);
            Debug.Log($"[AIManager] Phase changed to {evt.Current} — re-evaluating enemy activation");
        }

        // ─── Blackboard Updates ──────────────────────────────────────────────

        public void BroadcastNoiseEvent(PlayerNoiseEvent evt)
        {
            SharedBlackboard.LastNoiseZone      = evt.Origin;
            SharedBlackboard.LastNoiseTime      = Time.time;
            SharedBlackboard.LastNoiseRadius    = evt.Radius;
        }

        public void BroadcastPlayerSeen(Vector3 position, string byEnemyId)
        {
            SharedBlackboard.LastKnownPlayerPosition = position;
            SharedBlackboard.LastSeenTime            = Time.time;
            SharedBlackboard.LastSightingEnemyId     = byEnemyId;
        }

        // ─── Queries ────────────────────────────────────────────────────────

        public List<EnemyActor> GetEnemiesOfType(EnemyType type)
            => _enemies.TryGetValue(type, out var list) ? list : new List<EnemyActor>();

        public bool AnyEnemyInChase()
        {
            foreach (var list in _enemies.Values)
                foreach (var e in list)
                    if (e != null && e.IsChasing) return true;
            return false;
        }
    }
}

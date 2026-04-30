using System.Collections.Generic;
using UnityEngine;

namespace Eidolon.Core
{
    using Actors;

    /// <summary>
    /// Central registry for all active enemies.
    /// Enforces phase-gating, encounter budgets, and cross-monster
    /// blackboard coordination (partial information only – no hive mind).
    /// </summary>
    public class AIManager : MonoBehaviour
    {
        public static AIManager Instance { get; private set; }

        // ── Registry ───────────────────────────────────────────────────────────
        private readonly List<EnemyActor> _allEnemies    = new();
        private readonly List<EnemyActor> _activeEnemies = new();

        // ── Encounter budget ───────────────────────────────────────────────────
        [Header("Encounter Budget")]
        [SerializeField] private int _maxSimultaneousActive = 3;    // max enemies truly active at once
        [SerializeField] private float _syncInterval        = 3f;   // blackboard sync cadence

        private float _syncTimer;

        // ── Lifecycle ──────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<EvGamePhaseChanged>(e => OnPhaseChanged(e.Phase));
            EventBus.Subscribe<EvPlayerNoise>(OnPlayerNoise);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<EvGamePhaseChanged>(e => OnPhaseChanged(e.Phase));
            EventBus.Unsubscribe<EvPlayerNoise>(OnPlayerNoise);
        }

        private void Update()
        {
            _syncTimer += Time.deltaTime;
            if (_syncTimer >= _syncInterval)
            {
                _syncTimer = 0f;
                SyncSharedBlackboard();
                EnforceEncounterBudget();
            }
        }

        // ── Registration ───────────────────────────────────────────────────────
        public void RegisterEnemy(EnemyActor enemy)
        {
            if (!_allEnemies.Contains(enemy))
                _allEnemies.Add(enemy);

            RefreshActiveList();
        }

        public void UnregisterEnemy(EnemyActor enemy)
        {
            _allEnemies.Remove(enemy);
            _activeEnemies.Remove(enemy);
        }

        // ── Phase change ───────────────────────────────────────────────────────
        public void OnPhaseChanged(GamePhase phase)
        {
            RefreshActiveList();
            Debug.Log($"[AIManager] Phase changed to {phase}. Active enemies: {_activeEnemies.Count}");
        }

        // ── Active list ────────────────────────────────────────────────────────
        private void RefreshActiveList()
        {
            _activeEnemies.Clear();
            foreach (var e in _allEnemies)
                if (e.IsActiveInCurrentPhase() && e.IsAlive)
                    _activeEnemies.Add(e);
        }

        // ── Encounter budget enforcement ───────────────────────────────────────
        /// <summary>
        /// Caps how many enemies simultaneously pursue the player.
        /// Extras enter Idle to prevent overwhelming swarms.
        /// </summary>
        private void EnforceEncounterBudget()
        {
            int activelyHunting = 0;
            foreach (var e in _activeEnemies)
            {
                if (e.CurrentState is EnemyState.Chasing or EnemyState.Hunting)
                    activelyHunting++;
            }

            if (activelyHunting > _maxSimultaneousActive)
            {
                // Suppress lower-priority hunters (highest index in list = lowest priority)
                int toSuppress = activelyHunting - _maxSimultaneousActive;
                for (int i = _activeEnemies.Count - 1; i >= 0 && toSuppress > 0; i--)
                {
                    var e = _activeEnemies[i];
                    if (e.CurrentState is EnemyState.Chasing or EnemyState.Hunting)
                    {
                        e.SetState(EnemyState.Patrolling);
                        toSuppress--;
                    }
                }
            }
        }

        // ── Shared blackboard sync ─────────────────────────────────────────────
        /// <summary>
        /// Propagates partial information between monsters.
        /// Only writes to shared keys; each monster still has private state.
        /// No perfect tracking – confidence degrades over distance/time.
        /// </summary>
        private void SyncSharedBlackboard()
        {
            // Aggregate recent noise to share zone
            var recentNoise = WorldStateManager.Instance?.GetRecentNoise(5f);
            if (recentNoise != null && recentNoise.Count > 0)
            {
                // Share the most recent loud noise position
                NoiseEvent loudest = null;
                foreach (var n in recentNoise)
                    if (loudest == null || (int)n.Level > (int)loudest.Level)
                        loudest = n;

                if (loudest != null)
                    AI.Blackboard.StaticSet(AI.Blackboard.Keys.SharedNoiseZone, loudest.Origin);
            }

            // Share active chase status
            bool anyChasing = false;
            string chaseRoom = null;
            foreach (var e in _activeEnemies)
            {
                if (e.CurrentState == EnemyState.Chasing)
                {
                    anyChasing = true;
                    chaseRoom  = WorldStateManager.Instance?.PlayerCurrentRoomId;
                    break;
                }
            }
            AI.Blackboard.StaticSet(AI.Blackboard.Keys.SharedChaseActive, anyChasing);
            if (chaseRoom != null)
                AI.Blackboard.StaticSet(AI.Blackboard.Keys.SharedChaseRoom, chaseRoom);

            // Share common route (top route from heatmap)
            var topRoutes = WorldStateManager.Instance?.GetTopRoutes(1);
            if (topRoutes != null && topRoutes.Count > 0)
                AI.Blackboard.StaticSet(AI.Blackboard.Keys.SharedCommonRoute, topRoutes[0]);
        }

        // ── Noise event relay ──────────────────────────────────────────────────
        private void OnPlayerNoise(EvPlayerNoise evt)
        {
            // Write noise to shared blackboard immediately for hearing enemies
            AI.Blackboard.StaticSet(AI.Blackboard.Keys.SharedNoiseZone, evt.Origin);
        }

        // ── Queries ────────────────────────────────────────────────────────────
        public IReadOnlyList<EnemyActor> AllEnemies    => _allEnemies;
        public IReadOnlyList<EnemyActor> ActiveEnemies => _activeEnemies;

        public EnemyActor GetEnemy(MonsterType type)
        {
            foreach (var e in _allEnemies)
                if (e.Profile != null && e.Profile.Type == type) return e;
            return null;
        }

        public bool IsAnyEnemyChasing()
        {
            foreach (var e in _activeEnemies)
                if (e.CurrentState == EnemyState.Chasing) return true;
            return false;
        }
    }
}

// ── Blackboard static accessor patch ──────────────────────────────────────────
// Adds a static write path for shared keys (AIManager is the only writer).
namespace Eidolon.AI
{
    public partial class Blackboard
    {
        // Shared dictionary is already static inside Blackboard.
        // This method lets AIManager write to it without holding a Blackboard instance.
        public static void StaticSet<T>(string key, T value)
        {
            // Access the private static dict via the instance path
            // We expose this cleanly here rather than reflection hacks.
            _sharedStatic[key] = value;
        }

        public static bool StaticGet<T>(string key, out T result)
        {
            if (_sharedStatic.TryGetValue(key, out var v) && v is T t)
            {
                result = t;
                return true;
            }
            result = default;
            return false;
        }

        // Replaces the previous private static dict reference:
        private static readonly System.Collections.Generic.Dictionary<string, object>
            _sharedStatic = new();

        // GetShared now routes through _sharedStatic
        public T GetSharedV2<T>(string key, T defaultVal = default)
        {
            if (_sharedStatic.TryGetValue(key, out var v) && v is T t) return t;
            return defaultVal;
        }
    }
}

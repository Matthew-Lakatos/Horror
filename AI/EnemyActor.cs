using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Eidolon.Core;
using Eidolon.Data;

namespace Eidolon.AI
{
    // ─────────────────────────────────────────────────────────────────────────
    // INTERFACES
    // ─────────────────────────────────────────────────────────────────────────

    public interface ISensorModule      { void Tick(EnemyActor actor); }
    public interface IMovementModule    { void SetDestination(Vector3 dest); void Stop(); float Speed { get; set; } }
    public interface IMemoryModule      { void RecordNoise(Vector3 pos, float time); void RecordSighting(Vector3 pos, float time); Vector3 LastKnownPlayerPos { get; } float LastSightingTime { get; } }
    public interface IPresenceModule    { void Show(); void Hide(); bool IsVisible { get; } }
    public interface IActionExecutor    { void ExecuteAction(EnemyAction action, EnemyActor actor); }

    // ─────────────────────────────────────────────────────────────────────────
    // ENEMY ACTOR — Base class for all enemies
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Composition-based enemy root. Uniqueness comes entirely from
    /// swapping modules, utility weights, and ThreatProfile data.
    /// No per-monster logic lives here — only the shared tick pipeline.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyActor : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private string           _enemyId;
        [SerializeField] private EnemyType        _enemyType;
        [SerializeField] private ThreatProfile    _threatProfile;

        // ─── Module References (assigned in Inspector or by subcomponents) ──

        public ISensorModule    SensorModule   { get; set; }
        public IMovementModule  MovementModule { get; set; }
        public IMemoryModule    MemoryModule   { get; set; }
        public UtilityBrain     UtilityBrain   { get; set; }
        public IPresenceModule  PresenceModule { get; set; }
        public IActionExecutor  ActionExecutor { get; set; }

        // ─── Runtime State ──────────────────────────────────────────────────

        public string     EnemyId      => _enemyId;
        public EnemyType  EnemyType    => _enemyType;
        public ThreatProfile ThreatProfile => _threatProfile;
        public bool       IsChasing    { get; set; }
        public bool       IsPhaseActive{ get; private set; } = true;
        public bool       IsDormant    { get; set; }

        private float _presenceCooldown;

        // ─── Lifecycle ───────────────────────────────────────────────────────

        protected virtual void Awake()
        {
            // Resolve modules from child components if not set
            SensorModule   ??= GetComponentInChildren<ISensorModule>()   as MonoBehaviour as ISensorModule;
            MovementModule ??= GetComponent<NavMeshMovementModule>();
            MemoryModule   ??= GetComponent<EnemyMemoryModule>();
            UtilityBrain   ??= GetComponent<UtilityBrain>();
            PresenceModule ??= GetComponent<EnemyPresenceModule>();
            ActionExecutor ??= GetComponent<EnemyActionExecutor>();
        }

        protected virtual void Start()
        {
            AIManager.Instance?.RegisterEnemy(this);
        }

        protected virtual void OnDestroy()
        {
            AIManager.Instance?.UnregisterEnemy(this);
        }

        // ─── Tick Pipeline (called by AIManager on typed intervals) ─────────

        public void Tick()
        {
            if (!IsPhaseActive || IsDormant) return;

            // 1. Sense
            SensorModule?.Tick(this);

            // 2. Decide
            EnemyAction chosenAction = UtilityBrain?.Evaluate(this);

            // 3. Act
            if (chosenAction != null)
                ActionExecutor?.ExecuteAction(chosenAction, this);

            // 4. Update shared blackboard
            if (MemoryModule != null && MemoryModule.LastSightingTime > 0)
                AIManager.Instance?.BroadcastPlayerSeen(MemoryModule.LastKnownPlayerPos, _enemyId);
        }

        // ─── Presence Cooldown ───────────────────────────────────────────────

        public bool CanAppear()
        {
            if (_threatProfile == null) return true;
            return Time.time >= _presenceCooldown;
        }

        public void SetPresenceCooldown()
        {
            if (_threatProfile != null)
                _presenceCooldown = Time.time + _threatProfile.PresenceCooldownDuration;
        }

        // ─── Phase Activation ────────────────────────────────────────────────

        public void SetPhaseActive(bool active)
        {
            IsPhaseActive = active;
            if (!active)
            {
                IsChasing = false;
                MovementModule?.Stop();
                PresenceModule?.Hide();
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SHARED BLACKBOARD
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Partial intel shared between enemies — not a perfect hive mind.
    /// Enemies read from this opportunistically, not instantly.
    /// </summary>
    public class SharedBlackboard
    {
        public Vector3 LastKnownPlayerPosition { get; set; }
        public float   LastSeenTime            { get; set; }
        public string  LastSightingEnemyId     { get; set; }

        public Vector3 LastNoiseZone   { get; set; }
        public float   LastNoiseTime   { get; set; }
        public float   LastNoiseRadius { get; set; }

        public bool    PlayerIsInjured      { get; set; }
        public bool    PlayerInPanic        { get; set; }
        public string  ActiveChaseSector    { get; set; }

        public bool HasRecentSighting(float maxAge = 10f)
            => LastSeenTime > 0 && Time.time - LastSeenTime < maxAge;

        public bool HasRecentNoise(float maxAge = 5f)
            => LastNoiseTime > 0 && Time.time - LastNoiseTime < maxAge;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NAV MESH MOVEMENT MODULE
    // ─────────────────────────────────────────────────────────────────────────

    public class NavMeshMovementModule : MonoBehaviour, IMovementModule
    {
        private NavMeshAgent _agent;

        public float Speed
        {
            get => _agent != null ? _agent.speed : 0f;
            set { if (_agent != null) _agent.speed = value; }
        }

        private void Awake() => _agent = GetComponent<NavMeshAgent>();

        public void SetDestination(Vector3 dest)
        {
            if (_agent != null && _agent.isOnNavMesh)
                _agent.SetDestination(dest);
        }

        public void Stop()
        {
            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.ResetPath();
                _agent.velocity = Vector3.zero;
            }
        }

        public bool HasReachedDestination(float threshold = 0.4f)
            => _agent != null && !_agent.pathPending
               && _agent.remainingDistance <= threshold;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ENEMY MEMORY MODULE
    // ─────────────────────────────────────────────────────────────────────────

    public class EnemyMemoryModule : MonoBehaviour, IMemoryModule
    {
        [SerializeField] private int   _maxMemoryEntries = 8;
        [SerializeField] private float _noiseMemoryDecay = 30f;

        private readonly List<MemoryEntry> _noiseHistory    = new List<MemoryEntry>();
        private readonly List<MemoryEntry> _sightingHistory = new List<MemoryEntry>();

        public Vector3 LastKnownPlayerPos { get; private set; }
        public float   LastSightingTime   { get; private set; }

        public void RecordNoise(Vector3 pos, float time)
        {
            if (_noiseHistory.Count >= _maxMemoryEntries) _noiseHistory.RemoveAt(0);
            _noiseHistory.Add(new MemoryEntry { Position = pos, Time = time });
        }

        public void RecordSighting(Vector3 pos, float time)
        {
            if (_sightingHistory.Count >= _maxMemoryEntries) _sightingHistory.RemoveAt(0);
            _sightingHistory.Add(new MemoryEntry { Position = pos, Time = time });
            LastKnownPlayerPos = pos;
            LastSightingTime   = time;
        }

        /// <summary>Returns probable player location based on noise + sighting history.</summary>
        public Vector3 GetBestGuessPosition()
        {
            if (LastSightingTime > 0 && Time.time - LastSightingTime < 15f)
                return LastKnownPlayerPos;

            if (_noiseHistory.Count > 0)
            {
                // Return the most recent noise position
                float best = -1f;
                Vector3 pos = Vector3.zero;
                foreach (var entry in _noiseHistory)
                {
                    if (entry.Time > best)
                    {
                        best = entry.Time;
                        pos  = entry.Position;
                    }
                }
                return pos;
            }

            return LastKnownPlayerPos;
        }

        /// <summary>Returns frequently visited positions (habit learning).</summary>
        public List<Vector3> GetHabitPositions(int topN = 3)
        {
            // Cluster entries and return top N most-repeated zones
            var positions = new List<Vector3>();
            var usedIndices = new HashSet<int>();

            for (int i = 0; i < _sightingHistory.Count && positions.Count < topN; i++)
            {
                if (usedIndices.Contains(i)) continue;
                positions.Add(_sightingHistory[i].Position);
                usedIndices.Add(i);
            }
            return positions;
        }

        private struct MemoryEntry
        {
            public Vector3 Position;
            public float   Time;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // VISION SENSOR MODULE
    // ─────────────────────────────────────────────────────────────────────────

    public class VisionSensorModule : MonoBehaviour, ISensorModule
    {
        [SerializeField] private float _visionRange    = 18f;
        [SerializeField] private float _visionAngle    = 90f;
        [SerializeField] private LayerMask _blockingLayers;
        [SerializeField] private LayerMask _playerLayer;

        public bool PlayerVisible { get; private set; }
        public Vector3 LastSeenPosition { get; private set; }

        public void Tick(EnemyActor actor)
        {
            PlayerVisible = false;
            var players = Physics.OverlapSphere(transform.position, _visionRange, _playerLayer);
            foreach (var col in players)
            {
                var dir = col.transform.position - transform.position;
                if (Vector3.Angle(transform.forward, dir) > _visionAngle * 0.5f) continue;

                if (!Physics.Raycast(transform.position, dir.normalized, dir.magnitude, _blockingLayers))
                {
                    PlayerVisible    = true;
                    LastSeenPosition = col.transform.position;
                    actor.MemoryModule?.RecordSighting(LastSeenPosition, Time.time);
                    AIManager.Instance?.BroadcastPlayerSeen(LastSeenPosition, actor.EnemyId);
                    return;
                }
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HEARING SENSOR MODULE
    // ─────────────────────────────────────────────────────────────────────────

    public class HearingSensorModule : MonoBehaviour, ISensorModule
    {
        [SerializeField] private float _hearingMultiplier = 1f;

        private void OnEnable()  => EventBus.Subscribe<PlayerNoiseEvent>(OnNoise);
        private void OnDisable() => EventBus.Unsubscribe<PlayerNoiseEvent>(OnNoise);

        private EnemyActor _actor;
        private void Awake() => _actor = GetComponent<EnemyActor>();

        private void OnNoise(PlayerNoiseEvent evt)
        {
            if (_actor == null) return;
            float scaledRadius = evt.Radius * _hearingMultiplier;
            float dist = Vector3.Distance(transform.position, evt.Origin);
            if (dist <= scaledRadius)
                _actor.MemoryModule?.RecordNoise(evt.Origin, Time.time);
        }

        public void Tick(EnemyActor actor) { /* Reactive via event subscription */ }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ENEMY PRESENCE MODULE
    // ─────────────────────────────────────────────────────────────────────────

    public class EnemyPresenceModule : MonoBehaviour, IPresenceModule
    {
        [SerializeField] private GameObject _visuals;

        public bool IsVisible { get; private set; }

        public void Show()
        {
            if (_visuals) _visuals.SetActive(true);
            IsVisible = true;
        }

        public void Hide()
        {
            if (_visuals) _visuals.SetActive(false);
            IsVisible = false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ENEMY ACTION EXECUTOR
    // ─────────────────────────────────────────────────────────────────────────

    public class EnemyActionExecutor : MonoBehaviour, IActionExecutor
    {
        public void ExecuteAction(EnemyAction action, EnemyActor actor)
        {
            if (action == null || actor == null) return;
            action.Execute(actor);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ENEMY ACTION BASE
    // ─────────────────────────────────────────────────────────────────────────

    public abstract class EnemyAction : ScriptableObject
    {
        [SerializeField] protected float _cooldown = 3f;
        private float _lastUsed = -999f;

        public bool IsReady => Time.time >= _lastUsed + _cooldown;

        public void Execute(EnemyActor actor)
        {
            if (!IsReady) return;
            _lastUsed = Time.time;
            OnExecute(actor);
        }

        protected abstract void OnExecute(EnemyActor actor);
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Eidolon.AI
{
    using Core;

    // ─────────────────────────────────────────────────────────────────────────
    //  BLACKBOARD  – key-value store per enemy, with optional shared keys
    // ─────────────────────────────────────────────────────────────────────────
    public class Blackboard
    {
        private readonly Dictionary<string, object> _local  = new();
        private static  readonly Dictionary<string, object> _shared = new();   // cross-monster

        // ── Read ───────────────────────────────────────────────────────────────
        public T Get<T>(string key, T defaultVal = default)
        {
            if (_local.TryGetValue(key, out var v) && v is T t) return t;
            return defaultVal;
        }

        public T GetShared<T>(string key, T defaultVal = default)
        {
            if (_shared.TryGetValue(key, out var v) && v is T t) return t;
            return defaultVal;
        }

        public bool Has(string key) => _local.ContainsKey(key);

        // ── Write ──────────────────────────────────────────────────────────────
        public void Set<T>(string key, T value)   => _local[key]  = value;
        public void SetShared<T>(string key, T value) => _shared[key] = value;

        // ── Clear shared (scene change) ────────────────────────────────────────
        public static void ClearShared() => _shared.Clear();

        // ── Common keys (prevents magic-string bugs) ───────────────────────────
        public static class Keys
        {
            // Local
            public const string CurrentState        = "CurrentState";
            public const string PlayerLastKnownPos  = "PlayerLastKnownPos";
            public const string PlayerLastKnownRoom = "PlayerLastKnownRoom";
            public const string PlayerConfidence    = "PlayerConfidence";    // 0–1
            public const string AttentionLevel      = "AttentionLevel";      // 0–1
            public const string HungerLevel         = "HungerLevel";
            public const string FamiliarityLevel    = "FamiliarityLevel";
            public const string PresenceCooldown    = "PresenceCooldown";
            public const string CurrentPath         = "CurrentPath";
            public const string PathIndex           = "PathIndex";
            public const string TrapCount           = "TrapCount";
            public const string LastActionTime      = "LastActionTime";
            public const string ActionLock          = "ActionLock";

            // Shared (partial information only)
            public const string SharedNoiseZone     = "Shared_NoiseZone";
            public const string SharedCommonRoute   = "Shared_CommonRoute";
            public const string SharedChaseActive   = "Shared_ChaseActive";
            public const string SharedChaseRoom     = "Shared_ChaseRoom";
            public const string SharedPlayerInjured = "Shared_PlayerInjured";
            public const string SharedPanicTendency = "Shared_PanicTendency";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  UTILITY BRAIN  – scores all available actions and executes the best
    // ─────────────────────────────────────────────────────────────────────────
    public class UtilityBrain
    {
        private readonly List<IAIAction>  _actions   = new();
        private IAIAction                 _current;
        private float                     _tickInterval;
        private float                     _tickTimer;

        public IAIAction CurrentAction => _current;

        public UtilityBrain(float tickInterval)
        {
            _tickInterval = tickInterval;
        }

        // ── Action registry ────────────────────────────────────────────────────
        public void RegisterAction(IAIAction action) => _actions.Add(action);
        public void ClearActions()                   => _actions.Clear();

        // ── Tick (call from MonoBehaviour.Update) ──────────────────────────────
        public void Tick(float dt, Blackboard bb)
        {
            _tickTimer += dt;
            if (_tickTimer < _tickInterval) return;
            _tickTimer = 0f;

            // Complete check on current action
            if (_current != null && _current.IsComplete(bb))
                _current = null;

            Evaluate(bb);
        }

        // ── Evaluation ────────────────────────────────────────────────────────
        private void Evaluate(Blackboard bb)
        {
            IAIAction best      = null;
            float     bestScore = float.MinValue;

            foreach (var action in _actions)
            {
                float score = action.ScoreAction(bb);
                if (score > bestScore)
                {
                    bestScore = score;
                    best      = action;
                }
            }

            if (best == null) return;

            // Only switch if materially better (hysteresis – prevents jittering)
            bool shouldSwitch = _current == null
                             || best != _current
                             && bestScore > ScoreCurrentAction(bb) + 0.1f;

            if (shouldSwitch)
            {
                _current?.Interrupt();
                _current = best;
                _current.Execute(bb);
            }
        }

        private float ScoreCurrentAction(Blackboard bb)
            => _current != null ? _current.ScoreAction(bb) : float.MinValue;

        // ── Force an action (external override) ───────────────────────────────
        public void ForceAction(IAIAction action, Blackboard bb)
        {
            _current?.Interrupt();
            _current = action;
            _current.Execute(bb);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  SENSOR MODULE
    // ─────────────────────────────────────────────────────────────────────────
    public class SensorModule : ISensor
    {
        private readonly Transform _sensorOrigin;
        private readonly float     _hearingRadius;
        private readonly float     _visionRange;
        private readonly float     _visionAngle;
        private readonly LayerMask _obstacleMask;

        public bool    HasDetected    { get; private set; }
        public float   ConfidenceScore { get; private set; }
        public Vector3 LastKnownPos   { get; private set; }

        private Transform _playerTransform;

        public SensorModule(Transform origin, float hearing, float visionRange,
                            float visionAngle, LayerMask obstacles)
        {
            _sensorOrigin  = origin;
            _hearingRadius = hearing;
            _visionRange   = visionRange;
            _visionAngle   = visionAngle;
            _obstacleMask  = obstacles;
        }

        public void Tick(float dt)
        {
            if (_playerTransform == null)
            {
                var go = GameObject.FindWithTag("Player");
                if (go) _playerTransform = go.transform;
            }
            if (_playerTransform == null) { HasDetected = false; return; }

            bool vision  = CheckVision();
            bool hearing = CheckHearing();

            HasDetected = vision || hearing;

            if (HasDetected)
            {
                LastKnownPos    = _playerTransform.position;
                ConfidenceScore = vision ? 1f : 0.6f;
            }
            else
            {
                ConfidenceScore = Mathf.MoveTowards(ConfidenceScore, 0f, dt * 0.1f);
            }
        }

        private bool CheckVision()
        {
            Vector3 dir = _playerTransform.position - _sensorOrigin.position;
            if (dir.magnitude > _visionRange) return false;
            if (Vector3.Angle(_sensorOrigin.forward, dir.normalized) > _visionAngle * 0.5f) return false;
            return !Physics.Raycast(_sensorOrigin.position, dir.normalized,
                                    dir.magnitude, _obstacleMask);
        }

        private bool CheckHearing()
        {
            var noise = WorldStateManager.Instance?.GetRecentNoise(1.5f);
            if (noise == null) return false;
            foreach (var n in noise)
            {
                float dist = Vector3.Distance(_sensorOrigin.position, n.Origin);
                float effectiveRadius = _hearingRadius * (int)n.Level * 0.4f;
                if (dist <= effectiveRadius) return true;
            }
            return false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  MEMORY MODULE
    // ─────────────────────────────────────────────────────────────────────────
    public class MemoryModule
    {
        private readonly int    _maxEntries;
        private readonly float  _decayRate;          // seconds until forgotten
        private readonly List<MemoryEntry> _entries = new();

        public MemoryModule(int maxEntries = 16, float decayRate = 120f)
        {
            _maxEntries = maxEntries;
            _decayRate  = decayRate;
        }

        public void Remember(string key, object value)
        {
            var existing = _entries.Find(e => e.Key == key);
            if (existing != null)
            {
                existing.Value     = value;
                existing.Timestamp = Time.time;
                return;
            }
            if (_entries.Count >= _maxEntries) _entries.RemoveAt(0);
            _entries.Add(new MemoryEntry(key, value, Time.time));
        }

        public bool TryRecall<T>(string key, out T result)
        {
            var entry = _entries.Find(e => e.Key == key);
            if (entry != null && Time.time - entry.Timestamp < _decayRate && entry.Value is T v)
            {
                result = v;
                return true;
            }
            result = default;
            return false;
        }

        public void Decay()
        {
            _entries.RemoveAll(e => Time.time - e.Timestamp >= _decayRate);
        }

        public IReadOnlyList<MemoryEntry> Entries => _entries;

        private class MemoryEntry
        {
            public string Key;
            public object Value;
            public float  Timestamp;
            public MemoryEntry(string k, object v, float t) { Key = k; Value = v; Timestamp = t; }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  MOVEMENT MODULE
    // ─────────────────────────────────────────────────────────────────────────
    public class MovementModule
    {
        private readonly UnityEngine.AI.NavMeshAgent _agent;
        private string _currentTargetRoom;

        public bool IsMoving => _agent != null && _agent.velocity.magnitude > 0.1f;
        public bool HasReachedDestination
            => _agent != null && !_agent.pathPending
               && _agent.remainingDistance <= _agent.stoppingDistance;

        public MovementModule(UnityEngine.AI.NavMeshAgent agent) => _agent = agent;

        public void MoveTo(Vector3 pos)
        {
            if (_agent != null) _agent.SetDestination(pos);
        }

        public void MoveTo(string roomId)
        {
            _currentTargetRoom = roomId;
            var graph = World.FacilityGraphManager.Instance?.Graph;
            if (graph == null) return;
            var node = graph.GetNode(roomId);
            if (node == null) return;
            // Room node's world position is looked up via FacilityGraphManager
            var worldPos = World.FacilityGraphManager.Instance.GetRoomWorldPos(roomId);
            MoveTo(worldPos);
        }

        public void SetSpeed(float speed) { if (_agent) _agent.speed = speed; }
        public void Stop()                { if (_agent) _agent.ResetPath(); }

        public string CurrentTargetRoom => _currentTargetRoom;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  PRESENCE MODULE  – controls how visible/audible the enemy is
    // ─────────────────────────────────────────────────────────────────────────
    public class PresenceModule
    {
        private float _cooldown;

        public bool  IsVisible          { get; private set; } = false;
        public bool  IsAudible          { get; private set; } = false;
        public float LastPresenceTime   { get; private set; } = -999f;

        public bool CooldownReady => Time.time >= _cooldown;

        public void ShowPresence(float duration, float cooldownDuration)
        {
            IsVisible       = true;
            LastPresenceTime = Time.time;
            _cooldown       = Time.time + cooldownDuration;
        }

        public void HidePresence()
        {
            IsVisible = false;
            IsAudible = false;
        }

        public void SetAudible(bool on) => IsAudible = on;
    }
}

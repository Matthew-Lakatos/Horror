using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Eidolon.Actors
{
    using Core;
    using AI;

    /// <summary>
    /// Universal enemy shell. Uniqueness is entirely in the EnemyProfile SO and
    /// the action set registered by the specialised controller (BigEController, etc.).
    /// This class holds no monster-specific logic.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class EnemyActor : MonoBehaviour, IEntity
    {
        // ── Inspector ──────────────────────────────────────────────────────────
        [SerializeField] private Data.EnemyProfile _profile;
        [SerializeField] private LayerMask         _obstacleMask;

        // ── IEntity ────────────────────────────────────────────────────────────
        public string    EntityId  => _profile != null ? _profile.EnemyId : "Unknown";
        public Transform Transform => transform;
        public bool      IsAlive   { get; private set; } = true;

        // ── Modules ───────────────────────────────────────────────────────────
        public Blackboard      BB       { get; private set; }
        public UtilityBrain    Brain    { get; private set; }
        public SensorModule    Sensor   { get; private set; }
        public MemoryModule    Memory   { get; private set; }
        public MovementModule  Movement { get; private set; }
        public PresenceModule  Presence { get; private set; }

        public EnemyState CurrentState { get; private set; } = EnemyState.Idle;
        public Data.EnemyProfile Profile => _profile;

        private NavMeshAgent _agent;

        // ── Lifecycle ──────────────────────────────────────────────────────────
        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            BB       = new Blackboard();
            Brain    = new UtilityBrain(_profile != null ? _profile.DecisionTickRate : 0.5f);
            Sensor   = new SensorModule(transform,
                                        _profile?.HearingRadius   ?? 12f,
                                        _profile?.VisionRange     ?? 15f,
                                        _profile?.VisionAngle     ?? 80f,
                                        _obstacleMask);
            Memory   = new MemoryModule(_profile?.MemoryCapacity ?? 16,
                                        _profile?.MemoryDecayTime ?? 120f);
            Movement = new MovementModule(_agent);
            Presence = new PresenceModule();
        }

        private void Start()
        {
            if (_agent && _profile)
                _agent.speed = _profile.BaseSpeed;

            // Register with AIManager
            AIManager.Instance?.RegisterEnemy(this);

            // Allow specialised controllers (BigEController etc.) to register actions
            SendMessage("OnRegisterActions", SendMessageOptions.DontRequireReceiver);
        }

        private void OnDestroy()
        {
            AIManager.Instance?.UnregisterEnemy(this);
        }

        private void Update()
        {
            if (!IsAlive) return;

            Sensor.Tick(Time.deltaTime);
            Memory.Decay();

            // Update blackboard from sensor
            if (Sensor.HasDetected)
            {
                BB.Set(Blackboard.Keys.PlayerLastKnownPos,  Sensor.LastKnownPos);
                BB.Set(Blackboard.Keys.PlayerConfidence,    Sensor.ConfidenceScore);

                var wsm = WorldStateManager.Instance;
                if (wsm != null)
                    BB.Set(Blackboard.Keys.PlayerLastKnownRoom, wsm.PlayerCurrentRoomId);
            }

            Brain.Tick(Time.deltaTime, BB);
        }

        // ── State management ───────────────────────────────────────────────────
        public void SetState(EnemyState state)
        {
            if (state == CurrentState) return;
            var prev = CurrentState;
            CurrentState = state;
            BB.Set(Blackboard.Keys.CurrentState, state);
            EventBus.Publish(new EvEnemyStateChanged(EntityId, prev, state));
        }

        // ── Action registration ────────────────────────────────────────────────
        public void RegisterAction(IAIAction action) => Brain.RegisterAction(action);

        // ── Death ──────────────────────────────────────────────────────────────
        public void OnDeath()
        {
            IsAlive = false;
            SetState(EnemyState.Dormant);
            _agent.enabled = false;
        }

        // ── Phase gating ──────────────────────────────────────────────────────
        /// <summary>True if this enemy should be active in the current game phase.</summary>
        public bool IsActiveInCurrentPhase()
        {
            if (_profile == null) return true;
            var phase = GameDirector.Instance?.CurrentPhase ?? GamePhase.EarlyGame;
            return phase >= _profile.ActivationPhase;
        }
    }
}

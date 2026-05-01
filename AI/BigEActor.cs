using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Eidolon.Core;
using Eidolon.Data;

namespace Eidolon.AI
{
    /// <summary>
    /// Big E — The Apex Watcher. Psychological predator.
    /// Internal variables: Attention, Confidence, Hunger, Familiarity, PresenceCooldown.
    /// Behaviour split: 70% observe / 20% manipulate / 10% attack.
    /// Seen often, kills rarely. Goal: player fears what it might do next.
    /// </summary>
    public class BigEActor : EnemyActor
    {
        // ─── Big E Variables (inspector-visible for debug) ──────────────────

        [Header("Big E Variables")]
        [SerializeField, Range(0f, 1f)] private float _attention   = 0f;
        [SerializeField, Range(0f, 1f)] private float _confidence  = 0f;
        [SerializeField, Range(0f, 1f)] private float _hunger      = 0f;
        [SerializeField, Range(0f, 1f)] private float _familiarity = 0f;

        [Header("Attention Settings")]
        [SerializeField] private float _attentionDecayRate    = 0.01f;
        [SerializeField] private float _attentionGainOnNoise  = 0.15f;
        [SerializeField] private float _attentionGainOnSight  = 0.3f;

        [Header("Hunger Settings")]
        [SerializeField] private float _hungerGrowthRate      = 0.005f;
        [SerializeField] private float _hungerOnEvade         = 0.1f;
        [SerializeField] private float _hungerOnAttack        = -0.4f;
        [SerializeField] private float _hungerBoldThreshold   = 0.65f;

        [Header("Familiarity")]
        [SerializeField] private float _familiarityGainPerSighting = 0.05f;
        [SerializeField] private float _familiarityMaxInterceptBias = 0.8f;

        [Header("Chase Settings")]
        [SerializeField] private float _chaseDuration         = 12f;
        [SerializeField] private float _chaseSpeed            = 5.5f;
        [SerializeField] private float _patrolSpeed           = 1.8f;
        [SerializeField] private float _withdrawDuration      = 20f;

        [Header("Observation Positions")]
        [SerializeField] private List<Transform> _watchPositions = new List<Transform>();

        [Header("References")]
        [SerializeField] private Transform _playerTransform;
        [SerializeField] private AudioClip _presenceAmbientClip;
        [SerializeField] private AudioClip _chaseStingClip;
        [SerializeField] private AudioClip _withdrawClip;
        [SerializeField] private AudioClip _attackClip;

        // ─── State Machine ──────────────────────────────────────────────────

        private BigEState _state = BigEState.Dormant;
        private float     _stateTimer;
        private bool      _isWithdrawn;
        private float     _withdrawExpiry;

        // Intercept route tracking (habit learning)
        private readonly List<Vector3> _knownHabitPositions = new List<Vector3>();

        // ─── Properties (exposed for DebugOverlay) ──────────────────────────

        public float Attention   => _attention;
        public float Confidence  => _confidence;
        public float Hunger      => _hunger;
        public float Familiarity => _familiarity;
        public BigEState State   => _state;

        // ─── Lifecycle ───────────────────────────────────────────────────────

        protected override void Awake()
        {
            base.Awake();
            _playerTransform = GameObject.FindWithTag("Player")?.transform;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<PlayerNoiseEvent>(OnPlayerNoise);
            EventBus.Subscribe<PlayerDamagedEvent>(OnPlayerDamaged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<PlayerNoiseEvent>(OnPlayerNoise);
            EventBus.Unsubscribe<PlayerDamagedEvent>(OnPlayerDamaged);
        }

        // ─── Tick (called by AIManager on medium interval) ───────────────────

        public override void Tick()
        {
            if (!IsPhaseActive || IsDormant) return;

            UpdateVariables();
            UpdateHabitLearning();
            TickStateMachine();
        }

        // ─── Variable Update ─────────────────────────────────────────────────

        private void UpdateVariables()
        {
            // Attention: decays over time, rises on cues
            var vision = SensorModule as VisionSensorModule;
            if (vision != null && vision.PlayerVisible)
            {
                _attention  = Mathf.Min(1f, _attention + _attentionGainOnSight * Time.deltaTime * 20f);
                _confidence = Mathf.Min(1f, _confidence + 0.04f);
                MemoryModule?.RecordSighting(vision.LastSeenPosition, Time.time);
                _familiarity = Mathf.Min(1f, _familiarity + _familiarityGainPerSighting);
            }
            else
            {
                _attention  = Mathf.Max(0f, _attention  - _attentionDecayRate);
                _confidence = Mathf.Max(0f, _confidence - 0.005f);
            }

            // Hunger: grows passively, resets on successful attack
            if (!IsChasing)
                _hunger = Mathf.Min(1f, _hunger + _hungerGrowthRate);
        }

        private void UpdateHabitLearning()
        {
            var habits = (MemoryModule as EnemyMemoryModule)?.GetHabitPositions(5);
            if (habits != null)
            {
                _knownHabitPositions.Clear();
                _knownHabitPositions.AddRange(habits);
            }
        }

        // ─── State Machine ───────────────────────────────────────────────────

        private void TickStateMachine()
        {
            _stateTimer += Time.deltaTime;

            // Withdraw check
            if (_isWithdrawn)
            {
                if (Time.time >= _withdrawExpiry)
                {
                    _isWithdrawn = false;
                    TransitionState(BigEState.Observing);
                }
                return;
            }

            switch (_state)
            {
                case BigEState.Dormant:
                    if (_attention > 0.2f || GameDirector.Instance?.CurrentTension >= TensionState.Suspicion)
                        TransitionState(BigEState.Observing);
                    break;

                case BigEState.Observing:
                    TickObserving();
                    break;

                case BigEState.Shadowing:
                    TickShadowing();
                    break;

                case BigEState.Intercepting:
                    TickIntercepting();
                    break;

                case BigEState.Inspecting:
                    TickInspecting();
                    break;

                case BigEState.Chasing:
                    TickChasing();
                    break;
            }
        }

        // ─── Observe (70%) ──────────────────────────────────────────────────

        private void TickObserving()
        {
            if (!CanAppear()) return;

            // Move to a watch position visible to player
            var watchPos = GetBestWatchPosition();
            if (watchPos != Vector3.zero)
            {
                MovementModule?.SetDestination(watchPos);
                if (_stateTimer > 4f) // stand still after reaching
                    MovementModule?.Stop();
            }

            // Slow head-turn effect: face player
            if (_playerTransform != null)
            {
                var dir = _playerTransform.position - transform.position;
                dir.y = 0;
                if (dir != Vector3.zero)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 0.4f * Time.deltaTime);
            }

            // Escalate if high attention / hunger
            if (_attention > 0.7f && _hunger > 0.5f)
                TransitionState(BigEState.Shadowing);
            else if (_hunger >= _hungerBoldThreshold)
                TransitionState(BigEState.Intercepting);
            else if (_stateTimer > 15f)
                TransitionState(BigEState.Dormant);
        }

        // ─── Shadow (move through parallel rooms) ───────────────────────────

        private void TickShadowing()
        {
            // Follow player's general path but through adjacent routes
            if (MemoryModule != null)
            {
                var bestGuess = (MemoryModule as EnemyMemoryModule)?.GetBestGuessPosition() ?? Vector3.zero;
                if (bestGuess != Vector3.zero)
                {
                    // Aim for intercept point slightly ahead of player
                    var offset = (bestGuess - transform.position).normalized * 3f;
                    MovementModule?.SetDestination(bestGuess + offset);
                    MovementModule.Speed = _patrolSpeed;
                }
            }

            if (_confidence > 0.6f && _hunger > 0.4f)
                TransitionState(BigEState.Intercepting);

            if (_stateTimer > 20f)
                TransitionState(BigEState.Observing);
        }

        // ─── Intercept (manipulate — 20%) ────────────────────────────────────

        private void TickIntercepting()
        {
            // Move to block a habitual route
            var intercept = GetInterceptPosition();
            if (intercept != Vector3.zero)
            {
                MovementModule?.SetDestination(intercept);
                MovementModule.Speed = _patrolSpeed * 1.3f;
            }

            // Stand motionless in a familiar space
            if (_stateTimer > 8f)
            {
                MovementModule?.Stop();
                // Fake corridor pressure — just presence, no attack
            }

            if ((SensorModule as VisionSensorModule)?.PlayerVisible == true && _hunger > 0.8f)
            {
                var req = new FairnessRequest
                {
                    ThreatType           = FairnessThreatType.LethalAttack,
                    PlayerHadWarning     = _attention > 0.5f,
                    RiskIsInferable      = _familiarity > 0.3f,
                    CounterplayAvailable = true,
                    ForceUnfair          = false
                };
                if (FairnessValidator.Instance.Validate(req, out _))
                    TransitionState(BigEState.Chasing);
            }

            if (_stateTimer > 25f)
                TransitionState(BigEState.Observing);
        }

        // ─── Inspect (enters room slowly) ────────────────────────────────────

        private void TickInspecting()
        {
            if (MemoryModule?.LastKnownPlayerPos != null)
                MovementModule?.SetDestination(MemoryModule.LastKnownPlayerPos);
            MovementModule.Speed = _patrolSpeed * 0.6f;

            if ((SensorModule as VisionSensorModule)?.PlayerVisible == true)
                TransitionState(BigEState.Chasing);

            if (_stateTimer > 12f)
                TransitionState(BigEState.Shadowing);
        }

        // ─── Chase (10% — rare, intelligent) ─────────────────────────────────

        private void TickChasing()
        {
            if (_playerTransform == null)
            {
                TransitionState(BigEState.Observing);
                return;
            }

            MovementModule?.SetDestination(_playerTransform.position);
            MovementModule.Speed = _chaseSpeed;
            IsChasing = true;

            // Check attack range
            float dist = Vector3.Distance(transform.position, _playerTransform.position);
            if (dist < 1.5f)
            {
                ExecuteAttack();
                return;
            }

            // Lose player check
            if (!(SensorModule as VisionSensorModule)?.PlayerVisible ?? false)
            {
                if (_stateTimer > _chaseDuration)
                    Withdraw();
            }
        }

        // ─── Attack ──────────────────────────────────────────────────────────

        private void ExecuteAttack()
        {
            PlayClip(_attackClip);
            _hunger      = Mathf.Max(0f, _hunger + _hungerOnAttack);
            IsChasing    = false;

            EventBus.Publish(new PlayerDamagedEvent
            {
                Amount   = 35f,
                Source   = DamageSource.BigE,
                HitPoint = _playerTransform != null ? _playerTransform.position : transform.position
            });

            EventBus.Publish(new EnemyDetectedPlayerEvent
            {
                EnemyId           = EnemyId,
                Type              = EnemyType.BigE,
                LastKnownPosition = _playerTransform != null ? _playerTransform.position : transform.position
            });

            Withdraw();
        }

        // ─── Withdraw (retreat after major encounter) ────────────────────────

        private void Withdraw()
        {
            PlayClip(_withdrawClip);
            _isWithdrawn   = true;
            _withdrawExpiry = Time.time + _withdrawDuration;
            IsChasing      = false;
            PresenceModule?.Hide();
            SetPresenceCooldown();
            TransitionState(BigEState.Dormant);
            EventBus.Publish(new EnemyLostPlayerEvent { EnemyId = EnemyId, Type = EnemyType.BigE });
        }

        // ─── Transition ──────────────────────────────────────────────────────

        private void TransitionState(BigEState next)
        {
            _state      = next;
            _stateTimer = 0f;
            MovementModule.Speed = _patrolSpeed;

            if (next == BigEState.Dormant || next == BigEState.Observing)
                IsChasing = false;

            if (next != BigEState.Dormant && CanAppear())
                PresenceModule?.Show();
        }

        // ─── Helpers ────────────────────────────────────────────────────────

        private Vector3 GetBestWatchPosition()
        {
            if (_watchPositions.Count == 0) return Vector3.zero;
            // Prefer positions with line-of-sight to player
            foreach (var pos in _watchPositions)
            {
                if (_playerTransform == null) break;
                var dir = _playerTransform.position - pos.position;
                if (!Physics.Raycast(pos.position, dir.normalized, dir.magnitude))
                    return pos.position;
            }
            return _watchPositions[Random.Range(0, _watchPositions.Count)].position;
        }

        private Vector3 GetInterceptPosition()
        {
            // Bias toward habit positions scaled by familiarity
            if (_knownHabitPositions.Count == 0) return Vector3.zero;
            float biasStrength = Mathf.Lerp(0f, _familiarityMaxInterceptBias, _familiarity);
            if (Random.value < biasStrength)
                return _knownHabitPositions[Random.Range(0, _knownHabitPositions.Count)];
            return _knownHabitPositions[0];
        }

        // ─── Event Handlers ──────────────────────────────────────────────────

        private void OnPlayerNoise(PlayerNoiseEvent evt)
        {
            float dist = Vector3.Distance(transform.position, evt.Origin);
            if (dist < evt.Radius * 1.5f)
                _attention = Mathf.Min(1f, _attention + _attentionGainOnNoise);
        }

        private void OnPlayerDamaged(PlayerDamagedEvent evt)
        {
            // Player being hurt elsewhere → Big E's hunger lessens (satisfied vicariously)
            if (evt.Source != DamageSource.BigE)
                _hunger = Mathf.Max(0f, _hunger - 0.05f);
        }

        private void PlayClip(AudioClip clip)
        {
            var source = GetComponent<AudioSource>();
            if (source != null && clip != null)
                source.PlayOneShot(clip);
        }

        // ─── Save/Load ───────────────────────────────────────────────────────

        public BigESaveData ExportState() => new BigESaveData
        {
            Attention   = _attention,
            Confidence  = _confidence,
            Hunger      = _hunger,
            Familiarity = _familiarity
        };

        public void ImportState(BigESaveData data)
        {
            _attention   = data.Attention;
            _confidence  = data.Confidence;
            _hunger      = data.Hunger;
            _familiarity = data.Familiarity;
        }
    }

    // ─── Big E State ─────────────────────────────────────────────────────────

    public enum BigEState
    {
        Dormant, Observing, Shadowing, Intercepting, Inspecting, Chasing
    }

    [System.Serializable]
    public class BigESaveData
    {
        public float Attention;
        public float Confidence;
        public float Hunger;
        public float Familiarity;
    }
}

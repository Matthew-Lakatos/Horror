using System.Collections;
using UnityEngine;
using Eidolon.Core;
using Eidolon.World;

namespace Eidolon.AI
{
    // ─────────────────────────────────────────────────────────────────────────
    // HOUND — Execution Pressure
    // Fast, noise-triggered, terrifying payoff threat. Weak search patience.
    // ─────────────────────────────────────────────────────────────────────────

    public class HoundActor : EnemyActor
    {
        [Header("Hound Settings")]
        [SerializeField] private float _pursuitSpeed    = 8f;
        [SerializeField] private float _patrolSpeed     = 2f;
        [SerializeField] private float _searchTimeout   = 6f;
        [SerializeField] private float _attackDamage    = 45f;
        [SerializeField] private float _attackRange     = 1.2f;
        [SerializeField] private float _decoyRedirectDuration = 5f;

        [Header("Audio")]
        [SerializeField] private AudioClip _alertClip;
        [SerializeField] private AudioClip _pursuitClip;
        [SerializeField] private AudioClip _loseClip;

        private HoundState _state = HoundState.Patrolling;
        private float      _stateTimer;
        private Transform  _playerTransform;
        private AudioSource _audioSource;

        protected override void Awake()
        {
            base.Awake();
            _playerTransform = GameObject.FindWithTag("Player")?.transform;
            _audioSource     = GetComponent<AudioSource>();
        }

        private void OnEnable()  => EventBus.Subscribe<PlayerNoiseEvent>(OnNoise);
        private void OnDisable() => EventBus.Unsubscribe<PlayerNoiseEvent>(OnNoise);

        public override void Tick()
        {
            if (!IsPhaseActive || IsDormant) return;
            _stateTimer += Time.deltaTime;

            switch (_state)
            {
                case HoundState.Patrolling: TickPatrol();  break;
                case HoundState.Alerted:    TickAlerted(); break;
                case HoundState.Pursuing:   TickPursue();  break;
                case HoundState.Searching:  TickSearch();  break;
            }
        }

        private void TickPatrol()
        {
            MovementModule.Speed = _patrolSpeed;
            // Idle patrol handled by NavMesh wander (no dedicated patrol logic needed)
        }

        private void TickAlerted()
        {
            MovementModule.Speed = _patrolSpeed * 1.5f;
            if (MemoryModule != null)
                MovementModule?.SetDestination(MemoryModule.LastKnownPlayerPos);

            if ((SensorModule as VisionSensorModule)?.PlayerVisible == true)
                TransitionHound(HoundState.Pursuing);
            else if (_stateTimer > 4f)
                TransitionHound(HoundState.Searching);
        }

        private void TickPursue()
        {
            if (_playerTransform == null) return;
            MovementModule?.SetDestination(_playerTransform.position);
            MovementModule.Speed = _pursuitSpeed;
            IsChasing = true;

            float dist = Vector3.Distance(transform.position, _playerTransform.position);
            if (dist < _attackRange)
            {
                ExecuteAttack();
                return;
            }

            if (!(SensorModule as VisionSensorModule)?.PlayerVisible ?? false)
                TransitionHound(HoundState.Searching);
        }

        private void TickSearch()
        {
            // Hound has weak search patience — gives up quickly
            if (_stateTimer > _searchTimeout)
            {
                PlayClip(_loseClip);
                IsChasing = false;
                TransitionHound(HoundState.Patrolling);
            }
        }

        private void ExecuteAttack()
        {
            EventBus.Publish(new PlayerDamagedEvent
            {
                Amount   = _attackDamage,
                Source   = DamageSource.Hound,
                HitPoint = _playerTransform.position
            });
            IsChasing = false;
            TransitionHound(HoundState.Patrolling);
        }

        private void TransitionHound(HoundState next)
        {
            _state = next; _stateTimer = 0f;
            if (next == HoundState.Pursuing) PlayClip(_pursuitClip);
            if (next == HoundState.Alerted)  PlayClip(_alertClip);
        }

        private void OnNoise(PlayerNoiseEvent evt)
        {
            float dist = Vector3.Distance(transform.position, evt.Origin);
            if (dist <= evt.Radius)
            {
                MemoryModule?.RecordNoise(evt.Origin, Time.time);
                if (_state == HoundState.Patrolling || _state == HoundState.Searching)
                    TransitionHound(HoundState.Alerted);
            }
        }

        private void PlayClip(AudioClip clip)
        {
            if (_audioSource && clip) _audioSource.PlayOneShot(clip);
        }
    }

    public enum HoundState { Patrolling, Alerted, Pursuing, Searching }

    // ─────────────────────────────────────────────────────────────────────────
    // TRAPPER — Strategic Punisher
    // Deploys bear traps + noise traps on hot routes. Shares route intel.
    // ─────────────────────────────────────────────────────────────────────────

    public class TrapperActor : EnemyActor
    {
        [Header("Trapper Settings")]
        [SerializeField] private GameObject _bearTrapPrefab;
        [SerializeField] private GameObject _noiseTrapPrefab;
        [SerializeField] private int        _maxActiveTraps    = 5;
        [SerializeField] private float      _trapPlacementRange = 25f;
        [SerializeField] private float      _placementCooldown  = 20f;
        [SerializeField] private int        _hotRouteSampleCount = 3;

        private int   _activeTraps;
        private float _lastPlacedTime = -999f;

        public override void Tick()
        {
            if (!IsPhaseActive || IsDormant) return;
            if (Time.time - _lastPlacedTime < _placementCooldown) return;
            if (_activeTraps >= _maxActiveTraps) return;

            TryPlaceTrap();
        }

        private void TryPlaceTrap()
        {
            var hotRooms = WorldStateManager.Instance?.GetMostVisitedRooms(_hotRouteSampleCount);
            if (hotRooms == null || hotRooms.Count == 0) return;

            var targetRoomId = hotRooms[Random.Range(0, hotRooms.Count)];
            var room = WorldStateManager.Instance?.GetRoom(targetRoomId);
            if (room == null) return;

            bool isBearTrap = Random.value > 0.4f;
            var prefab = isBearTrap ? _bearTrapPrefab : _noiseTrapPrefab;
            if (prefab == null) return;

            Vector3 spawnPos = room.transform.position + Random.insideUnitSphere * 2f;
            spawnPos.y = room.transform.position.y;

            var instance = Instantiate(prefab, spawnPos, Quaternion.identity);
            var trap = instance.GetComponent<TrapInstance>();
            if (trap != null)
            {
                trap.OnTrapTriggered += () => _activeTraps--;
                _activeTraps++;
            }

            _lastPlacedTime = Time.time;
            Debug.Log($"[Trapper] Placed {(isBearTrap ? "bear" : "noise")} trap in {targetRoomId}");
        }
    }

    // ─── Trap Instance ───────────────────────────────────────────────────────

    public class TrapInstance : MonoBehaviour
    {
        [Header("Trap Settings")]
        [SerializeField] private TrapType  _trapType;
        [SerializeField] private float     _damageAmount      = 25f;
        [SerializeField] private float     _immobilizeDuration = 1.5f;
        [SerializeField] private float     _noiseRadius        = 15f;

        [Header("Audio")]
        [SerializeField] private AudioClip _snapClip;
        [SerializeField] private AudioClip _noiseBurstClip;

        public System.Action OnTrapTriggered;
        private bool _triggered;
        private AudioSource _audioSource;

        private void Awake() => _audioSource = GetComponent<AudioSource>();

        private void OnTriggerEnter(Collider other)
        {
            if (_triggered || !other.CompareTag("Player")) return;
            _triggered = true;

            OnTrapTriggered?.Invoke();
            TriggerEffect();
            Destroy(gameObject, 0.5f);
        }

        private void TriggerEffect()
        {
            PlayClip(_trapType == TrapType.Bear ? _snapClip : _noiseBurstClip);

            if (_trapType == TrapType.Bear)
            {
                // Full trauma bundle
                EventBus.Publish(new PlayerDamagedEvent { Amount = _damageAmount, Source = DamageSource.Trapper });

                EventBus.Publish(new ConditionAppliedEvent { ConditionType = ConditionType.Limp,            Duration = 30f,  Magnitude = 0.6f });
                EventBus.Publish(new ConditionAppliedEvent { ConditionType = ConditionType.Bleed,           Duration = 20f,  Magnitude = 0.5f });
                EventBus.Publish(new ConditionAppliedEvent { ConditionType = ConditionType.Tinnitus,        Duration = 5f,   Magnitude = 0.7f });
                EventBus.Publish(new ConditionAppliedEvent { ConditionType = ConditionType.LouderFootsteps, Duration = 30f,  Magnitude = 0.8f });

                EventBus.Publish(new PlayerNoiseEvent
                {
                    Origin = transform.position,
                    Radius = _noiseRadius,
                    Type   = NoiseType.Trap
                });
            }
            else // Noise trap
            {
                EventBus.Publish(new PlayerNoiseEvent
                {
                    Origin = transform.position,
                    Radius = _noiseRadius * 1.5f,
                    Type   = NoiseType.Trap
                });
            }
        }

        private void PlayClip(AudioClip clip)
        {
            if (_audioSource && clip) _audioSource.PlayOneShot(clip);
        }
    }

    public enum TrapType { Bear, Noise, Barricade }

    // ─────────────────────────────────────────────────────────────────────────
    // MIMIC — Trust Destroyer
    // Plays false sounds to destroy certainty. Rare, believable lies only.
    // ─────────────────────────────────────────────────────────────────────────

    public class MimicActor : EnemyActor
    {
        [Header("Mimic Audio Pool")]
        [SerializeField] private AudioClip[] _footstepSounds;
        [SerializeField] private AudioClip[] _doorSounds;
        [SerializeField] private AudioClip[] _alarmSounds;
        [SerializeField] private AudioClip[] _healingSounds;
        [SerializeField] private AudioClip[] _monsterCueSounds;

        [Header("Settings")]
        [SerializeField] private float _mimicCooldown  = 18f;
        [SerializeField] private float _maxRange        = 30f;
        [SerializeField] private int   _maxLiesPerPhase = 3;

        private float _lastMimicTime = -999f;
        private int   _liesThisPhase;
        private AudioSource _audioSource;
        private Transform   _playerTransform;

        protected override void Awake()
        {
            base.Awake();
            _audioSource     = GetComponent<AudioSource>();
            _playerTransform = GameObject.FindWithTag("Player")?.transform;
        }

        public override void Tick()
        {
            if (!IsPhaseActive || IsDormant) return;
            if (_liesThisPhase >= _maxLiesPerPhase) return;
            if (Time.time - _lastMimicTime < _mimicCooldown) return;
            if (_playerTransform == null) return;

            float dist = Vector3.Distance(transform.position, _playerTransform.position);
            if (dist > _maxRange) return;

            PlayMimicSound();
        }

        private void PlayMimicSound()
        {
            var category = (MimicCategory)Random.Range(0, 5);
            AudioClip[] pool = category switch
            {
                MimicCategory.Footstep   => _footstepSounds,
                MimicCategory.Door       => _doorSounds,
                MimicCategory.Alarm      => _alarmSounds,
                MimicCategory.Healing    => _healingSounds,
                MimicCategory.MonsterCue => _monsterCueSounds,
                _ => _footstepSounds
            };

            if (pool == null || pool.Length == 0) return;

            var clip = pool[Random.Range(0, pool.Length)];
            if (clip == null) return;

            _audioSource.PlayOneShot(clip);
            _lastMimicTime = Time.time;
            _liesThisPhase++;

            Debug.Log($"[Mimic] Playing false {category} sound");
        }

        private void OnEnable() => EventBus.Subscribe<GamePhaseChangedEvent>(OnPhaseChanged);
        private void OnDisable() => EventBus.Unsubscribe<GamePhaseChangedEvent>(OnPhaseChanged);
        private void OnPhaseChanged(GamePhaseChangedEvent _) => _liesThisPhase = 0;
    }

    public enum MimicCategory { Footstep, Door, Alarm, Healing, MonsterCue }

    // ─────────────────────────────────────────────────────────────────────────
    // NURSE — Weakness Hunter
    // Recovery denial. Hunts injured players. Patrols med areas.
    // ─────────────────────────────────────────────────────────────────────────

    public class NurseActor : EnemyActor
    {
        [Header("Nurse Settings")]
        [SerializeField] private float _normalSpeed      = 2f;
        [SerializeField] private float _injuredHuntSpeed = 3.2f;
        [SerializeField] private float _attackDamage     = 20f;
        [SerializeField] private float _attackRange      = 1.5f;

        [Header("Med Area Patrol")]
        [SerializeField] private Transform[] _medAreaPatrolPoints;

        private int       _patrolIndex;
        private Transform _playerTransform;
        private bool      _isHuntingInjured;

        protected override void Awake()
        {
            base.Awake();
            _playerTransform = GameObject.FindWithTag("Player")?.transform;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<ConditionAppliedEvent>(OnConditionApplied);
            EventBus.Subscribe<PlayerHealedEvent>(OnPlayerHealed);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<ConditionAppliedEvent>(OnConditionApplied);
            EventBus.Unsubscribe<PlayerHealedEvent>(OnPlayerHealed);
        }

        public override void Tick()
        {
            if (!IsPhaseActive || IsDormant) return;

            if (_isHuntingInjured)
                HuntInjured();
            else
                PatrolMedAreas();
        }

        private void HuntInjured()
        {
            if (_playerTransform == null) return;
            MovementModule?.SetDestination(_playerTransform.position);
            MovementModule.Speed = _injuredHuntSpeed;
            IsChasing = true;

            if (Vector3.Distance(transform.position, _playerTransform.position) < _attackRange)
            {
                EventBus.Publish(new PlayerDamagedEvent
                {
                    Amount   = _attackDamage,
                    Source   = DamageSource.Nurse,
                    HitPoint = _playerTransform.position
                });
                _isHuntingInjured = false;
                IsChasing = false;
            }
        }

        private void PatrolMedAreas()
        {
            if (_medAreaPatrolPoints == null || _medAreaPatrolPoints.Length == 0) return;
            MovementModule.Speed = _normalSpeed;
            var target = _medAreaPatrolPoints[_patrolIndex].position;
            MovementModule?.SetDestination(target);

            var nav = MovementModule as NavMeshMovementModule;
            if (nav != null && nav.HasReachedDestination())
                _patrolIndex = (_patrolIndex + 1) % _medAreaPatrolPoints.Length;
        }

        private void OnConditionApplied(ConditionAppliedEvent evt)
        {
            if (evt.ConditionType == ConditionType.Bleed || evt.ConditionType == ConditionType.Limp)
            {
                AIManager.Instance.SharedBlackboard.PlayerIsInjured = true;
                _isHuntingInjured = true;
            }
        }

        private void OnPlayerHealed(PlayerHealedEvent evt)
        {
            AIManager.Instance.SharedBlackboard.PlayerIsInjured = false;
            _isHuntingInjured = false;
            IsChasing = false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CHILD UNIT — Emotional Manipulator
    // Moral discomfort / hesitation trap. Ambiguous. Leads toward danger.
    // Use sparingly. Active only in LateGame phase.
    // ─────────────────────────────────────────────────────────────────────────

    public class ChildUnitActor : EnemyActor
    {
        [Header("Child Unit Settings")]
        [SerializeField] private float _lureDistance       = 12f;
        [SerializeField] private float _vanishRange        = 3f;
        [SerializeField] private float _dangerZoneRadius   = 5f;
        [SerializeField] private Transform _dangerTarget;

        [Header("Audio")]
        [SerializeField] private AudioClip _cryingLoop;
        [SerializeField] private AudioClip _footstepClip;

        private bool _isLeading;
        private AudioSource _audioSource;
        private Transform   _playerTransform;

        protected override void Awake()
        {
            base.Awake();
            _audioSource     = GetComponent<AudioSource>();
            _playerTransform = GameObject.FindWithTag("Player")?.transform;
        }

        public override void Tick()
        {
            if (!IsPhaseActive || IsDormant) return;

            float dist = _playerTransform != null
                ? Vector3.Distance(transform.position, _playerTransform.position)
                : float.MaxValue;

            if (dist < _vanishRange)
            {
                // Vanish when approached
                PresenceModule?.Hide();
                IsDormant = true;
                return;
            }

            if (!_isLeading && dist < _lureDistance)
            {
                _isLeading = true;
                PlayCrying();
            }

            if (_isLeading && _dangerTarget != null)
            {
                // Move toward danger zone, drawing player after
                MovementModule?.SetDestination(_dangerTarget.position);
                MovementModule.Speed = 1.5f;
            }
        }

        private void PlayCrying()
        {
            if (_audioSource && _cryingLoop && !_audioSource.isPlaying)
            {
                _audioSource.clip = _cryingLoop;
                _audioSource.loop = true;
                _audioSource.Play();
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HOLLOW MAN — Existential Centerpiece
    // Reality collapse dread. Appears in reflections. Impossible placement.
    // Extremely rare. Overuse is strictly forbidden.
    // ─────────────────────────────────────────────────────────────────────────

    public class HollowManActor : EnemyActor
    {
        [Header("Hollow Man Settings")]
        [SerializeField] private float _maxAppearancesPerSession = 3;
        [SerializeField] private float _minAppearanceInterval    = 120f;
        [SerializeField] private float _stareAtPlayerDelay       = 8f;

        [Header("Reflection References")]
        [SerializeField] private Transform[] _reflectionSurfaces;

        [Header("Audio")]
        [SerializeField] private AudioClip _presenceAmbient;

        private int   _appearanceCount;
        private float _lastAppearanceTime = -999f;
        private bool  _facingPlayer;
        private float _appearTimer;
        private Transform _playerTransform;

        protected override void Awake()
        {
            base.Awake();
            _playerTransform = GameObject.FindWithTag("Player")?.transform;
            PresenceModule?.Hide();
        }

        public override void Tick()
        {
            if (!IsPhaseActive || IsDormant) return;
            if (_appearanceCount >= _maxAppearancesPerSession) return;
            if (Time.time - _lastAppearanceTime < _minAppearanceInterval) return;
            if (GameDirector.Instance?.CurrentPhase != GamePhase.LateGame) return;

            // Validate with FairnessValidator
            if (!FairnessValidator.Instance.QuickValidate(
                    FairnessThreatType.PerceptionOverload,
                    hadWarning: true,
                    hasCounterplay: true)) return;

            StartCoroutine(AppearSequence());
        }

        private IEnumerator AppearSequence()
        {
            _lastAppearanceTime = Time.time;
            _appearanceCount++;

            // Place behind player or in a reflection
            if (_reflectionSurfaces != null && _reflectionSurfaces.Length > 0)
            {
                var reflSurface = _reflectionSurfaces[Random.Range(0, _reflectionSurfaces.Length)];
                transform.position = reflSurface.position;
            }

            PresenceModule?.Show();
            _facingPlayer = false;
            _appearTimer  = 0f;

            // Face wrong direction first
            if (_playerTransform != null)
            {
                var awayDir = transform.position - _playerTransform.position;
                transform.rotation = Quaternion.LookRotation(awayDir.normalized);
            }

            // After delay, slowly turn toward player
            yield return new WaitForSeconds(_stareAtPlayerDelay);
            _facingPlayer = true;

            if (_playerTransform != null)
            {
                var toPlayer = _playerTransform.position - transform.position;
                transform.rotation = Quaternion.LookRotation(toPlayer.normalized);
            }

            // Emit perception event (blackout/fear spike)
            EventBus.Publish(new PerceptionEffectEvent
            {
                EffectType = PerceptionEffectType.Hallucination,
                Intensity  = 0.9f,
                Duration   = 2f
            });

            yield return new WaitForSeconds(3f);
            PresenceModule?.Hide();
        }
    }
}

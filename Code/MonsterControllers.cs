using System.Collections;
using UnityEngine;

namespace Eidolon.Actors
{
    using Core;
    using AI;

    // ═════════════════════════════════════════════════════════════════════════
    //  HOUND  – fast, noise-triggered execution threat
    // ═════════════════════════════════════════════════════════════════════════
    [RequireComponent(typeof(EnemyActor))]
    public class HoundController : MonoBehaviour
    {
        [SerializeField] private float _noiseActivationThreshold = 0.6f;   // Loud+
        [SerializeField] private float _searchPatience           = 20f;

        private EnemyActor _actor;

        private void Awake() => _actor = GetComponent<EnemyActor>();
        private void Start()
        {
            _actor.RegisterAction(new Hound_IdleAction(_actor));
            _actor.RegisterAction(new Hound_ChaseAction(_actor, _searchPatience));
        }

        private void OnEnable()  => EventBus.Subscribe<EvPlayerNoise>(OnNoise);
        private void OnDisable() => EventBus.Unsubscribe<EvPlayerNoise>(OnNoise);

        private void OnNoise(EvPlayerNoise evt)
        {
            if (!_actor.IsAlive || !_actor.IsActiveInCurrentPhase()) return;
            if ((int)evt.Level < (int)NoiseLevel.Loud) return;

            float dist = Vector3.Distance(transform.position, evt.Origin);
            if (dist > _actor.Profile.HearingRadius) return;

            // Check fairness
            var req = new DangerousEventRequest
            {
                EventName         = "Hound_Chase",
                Severity          = EventSeverity.Severe,
                HadWarning        = true,
                RiskWasInferable  = true,      // player made noise
                CounterplayExists = true,      // run, hide, decoy
                RoomId            = WorldStateManager.Instance?.PlayerCurrentRoomId,
                PlayerStressLevel = PerceptionManager.Instance?.StressLevel ?? 0f,
                SourceEntityId    = "Hound"
            };
            var report = FairnessValidator.Instance?.Validate(req);
            if (report?.WasSuppressed ?? false) return;

            _actor.BB.Set(Blackboard.Keys.PlayerLastKnownPos, evt.Origin);
            _actor.Brain.ForceAction(new Hound_ChaseAction(_actor, _searchPatience), _actor.BB);
            GameDirector.Instance?.EscalateTo(TensionState.Panic);
        }
    }

    public class Hound_IdleAction : IAIAction
    {
        private readonly EnemyActor _actor;
        public string ActionName => "Hound_Idle";
        public Hound_IdleAction(EnemyActor a) => _actor = a;
        public float ScoreAction(Blackboard bb) => 0.05f;
        public void Execute(Blackboard bb) => _actor.SetState(EnemyState.Patrolling);
        public bool IsComplete(Blackboard bb) => false;
        public void Interrupt() { }
    }

    public class Hound_ChaseAction : IAIAction
    {
        private readonly EnemyActor _actor;
        private readonly float      _patience;
        private float               _lostTimer;
        private bool                _complete;

        public string ActionName => "Hound_Chase";
        public Hound_ChaseAction(EnemyActor a, float patience) { _actor = a; _patience = patience; }

        public float ScoreAction(Blackboard bb) => _complete ? 0f : 0.9f;

        public void Execute(Blackboard bb)
        {
            _actor.SetState(EnemyState.Chasing);
            _actor.Movement.SetSpeed(_actor.Profile.ChaseSpeed);
            _lostTimer = 0f;
            _complete  = false;
            var pos = bb.Get<Vector3>(Blackboard.Keys.PlayerLastKnownPos);
            _actor.Movement.MoveTo(pos);
        }

        public bool IsComplete(Blackboard bb)
        {
            float conf = bb.Get<float>(Blackboard.Keys.PlayerConfidence);
            if (conf < 0.1f)
            {
                _lostTimer += Time.deltaTime;
                if (_lostTimer >= _patience) { _complete = true; return true; }
            }
            else
            {
                _lostTimer = 0f;
                var pos = bb.Get<Vector3>(Blackboard.Keys.PlayerLastKnownPos);
                _actor.Movement.MoveTo(pos);
            }
            return false;
        }

        public void Interrupt()
        {
            _complete = true;
            _actor.Movement.SetSpeed(_actor.Profile.BaseSpeed);
            _actor.SetState(EnemyState.Patrolling);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  TRAPPER  – strategic trap-layer, punishes routine
    // ═════════════════════════════════════════════════════════════════════════
    [RequireComponent(typeof(EnemyActor))]
    public class TrapperController : MonoBehaviour
    {
        [Header("Trap Settings")]
        [SerializeField] private GameObject _bearTrapPrefab;
        [SerializeField] private int        _maxActiveTraps     = 4;
        [SerializeField] private float      _trapLayCooldown    = 30f;
        [SerializeField] private float      _routeLearnThreshold = 4;

        private EnemyActor _actor;
        private float      _trapTimer;
        private int        _activeTrapCount;

        private void Awake() => _actor = GetComponent<EnemyActor>();

        private void Update()
        {
            if (!_actor.IsAlive || !_actor.IsActiveInCurrentPhase()) return;

            _trapTimer += Time.deltaTime;
            if (_trapTimer >= _trapLayCooldown && _activeTrapCount < _maxActiveTraps)
            {
                _trapTimer = 0f;
                TryLayTrap();
            }
        }

        private void TryLayTrap()
        {
            var wsm       = WorldStateManager.Instance;
            var topRoutes = wsm?.GetTopRoutes(3);
            if (topRoutes == null || topRoutes.Count == 0) return;

            // Pick the hottest route's destination room
            var parts = topRoutes[0].Split('>');
            if (parts.Length != 2) return;

            string targetRoom = parts[1];
            int routeCount = wsm.GetRouteCount(parts[0], parts[1]);
            if (routeCount < _routeLearnThreshold) return;

            // Move to target room and place trap
            _actor.SetState(EnemyState.Patrolling);
            _actor.Movement.MoveTo(targetRoom);
            StartCoroutine(LayTrapWhenArrived(targetRoom));

            // Share route intel on blackboard
            _actor.BB.SetShared(Blackboard.Keys.SharedCommonRoute, targetRoom);
        }

        private IEnumerator LayTrapWhenArrived(string roomId)
        {
            yield return new WaitUntil(() => _actor.Movement.HasReachedDestination);

            if (_bearTrapPrefab != null)
            {
                var trap = Instantiate(_bearTrapPrefab, transform.position + transform.forward, Quaternion.identity);
                var tc   = trap.GetComponent<BearTrapComponent>();
                if (tc) tc.OnTrapTriggered += OnTrapHit;
                _activeTrapCount++;
            }
        }

        private void OnTrapHit(GameObject victim)
        {
            _activeTrapCount = Mathf.Max(0, _activeTrapCount - 1);

            var player = victim.GetComponent<PlayerController>();
            if (player == null) return;

            // Apply trauma bundle
            var cm = ConditionManager.Instance;
            player.TakeDamage(25f, "BearTrap");
            cm?.Apply(ConditionType.Immobilised,  1f,  3f,  "BearTrap");
            cm?.Apply(ConditionType.Limp,         0.7f, 90f, "BearTrap");
            cm?.Apply(ConditionType.LoudFootsteps, 0.8f, 90f, "BearTrap");
            cm?.Apply(ConditionType.Tinnitus,      0.6f, 15f, "BearTrap");
            cm?.Apply(ConditionType.Bleed,         0.4f, 30f, "BearTrap");
            cm?.Apply(ConditionType.Blur,          0.7f,  5f, "BearTrap");

            EventBus.Publish(new EvPlayerNoise(victim.transform.position, NoiseLevel.VeryLoud));
            GameDirector.Instance?.EscalateTo(TensionState.Panic);
        }
    }

    // Simple placeholder – actual trap mesh/trigger belongs in a Prefab
    public class BearTrapComponent : MonoBehaviour
    {
        public event System.Action<GameObject> OnTrapTriggered;
        private bool _triggered;

        private void OnTriggerEnter(Collider other)
        {
            if (_triggered) return;
            if (!other.CompareTag("Player")) return;
            _triggered = true;
            OnTrapTriggered?.Invoke(other.gameObject);
            // Disable collider, play snap audio, show trap animation
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  MIMIC  – audio deception / trust destroyer
    // ═════════════════════════════════════════════════════════════════════════
    [RequireComponent(typeof(EnemyActor))]
    public class MimicController : MonoBehaviour
    {
        [Header("Mimic Settings")]
        [SerializeField] private float _deceptionInterval    = 60f;
        [SerializeField] private float _deceptionIntervalMin = 40f;
        [SerializeField] private float _deceptionIntervalMax = 120f;

        private EnemyActor _actor;
        private float      _timer;
        private float      _nextDeceptionAt;

        private readonly string[] _mimicableAudioEvents = new[]
        {
            "audio_footsteps_player",
            "audio_door_open",
            "audio_alarm_brief",
            "audio_heal_station",
            "audio_hound_growl",
            "audio_door_close"
        };

        private void Awake() => _actor = GetComponent<EnemyActor>();

        private void Start() => ScheduleNextDeception();

        private void Update()
        {
            if (!_actor.IsAlive || !_actor.IsActiveInCurrentPhase()) return;

            _timer += Time.deltaTime;
            if (_timer >= _nextDeceptionAt)
            {
                _timer = 0f;
                TryDeceive();
                ScheduleNextDeception();
            }
        }

        private void ScheduleNextDeception()
            => _nextDeceptionAt = Random.Range(_deceptionIntervalMin, _deceptionIntervalMax);

        private void TryDeceive()
        {
            // Choose a believable mimic near the player
            string audioId = _mimicableAudioEvents[Random.Range(0, _mimicableAudioEvents.Length)];

            // Position near player but not visible
            var playerPos = FindObjectOfType<PlayerController>()?.transform.position ?? transform.position;
            Vector3 emitPos = playerPos + Random.insideUnitSphere * 8f;
            emitPos.y = playerPos.y;

            AudioManager.Instance?.PlayAtPosition(audioId, emitPos);
            EventBus.Publish(new EvPerceptionEvent(PerceptionEventType.FalseMonsterCue, 0.7f));
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  NURSE  – recovery denial, weakness hunter
    // ═════════════════════════════════════════════════════════════════════════
    [RequireComponent(typeof(EnemyActor))]
    public class NurseController : MonoBehaviour
    {
        [SerializeField] private float _huntInjuredSpeedBonus = 1.4f;
        [SerializeField] private float _medStationDisableTime = 60f;

        private EnemyActor _actor;
        private bool       _huntingInjured;

        private void Awake() => _actor = GetComponent<EnemyActor>();

        private void Start()
        {
            _actor.RegisterAction(new Nurse_PatrolMedAction(_actor));
            _actor.RegisterAction(new Nurse_HuntAction(_actor, this));
        }

        private void OnEnable()  => EventBus.Subscribe<EvConditionApplied>(OnConditionApplied);
        private void OnDisable() => EventBus.Unsubscribe<EvConditionApplied>(OnConditionApplied);

        private void OnConditionApplied(EvConditionApplied evt)
        {
            if (evt.Type is ConditionType.Bleed or ConditionType.Limp)
            {
                _huntingInjured = true;
                _actor.Movement.SetSpeed(_actor.Profile.ChaseSpeed * _huntInjuredSpeedBonus);
                _actor.BB.SetShared(Blackboard.Keys.SharedPlayerInjured, true);
            }
        }

        public bool IsHuntingInjured => _huntingInjured;
        public void ClearHunt() { _huntingInjured = false; _actor.Movement.SetSpeed(_actor.Profile.BaseSpeed); }

        public float MedStationDisableTime => _medStationDisableTime;
    }

    public class Nurse_PatrolMedAction : IAIAction
    {
        private readonly EnemyActor _actor;
        public string ActionName => "Nurse_PatrolMed";
        public Nurse_PatrolMedAction(EnemyActor a) => _actor = a;

        public float ScoreAction(Blackboard bb)
        {
            bool injured = bb.GetShared<bool>(Blackboard.Keys.SharedPlayerInjured);
            return injured ? 0.1f : 0.4f;   // patrol med when player fine
        }

        public void Execute(Blackboard bb)
        {
            _actor.SetState(EnemyState.Patrolling);
            // Find nearest medical room
            var graph = World.FacilityGraphManager.Instance?.Graph;
            if (graph == null) return;
            foreach (var node in graph.AllNodes)
                if (node.Type == RoomType.Medical && node.MedStationActive)
                {
                    _actor.Movement.MoveTo(node.RoomId);
                    return;
                }
        }

        public bool IsComplete(Blackboard bb) => _actor.Movement.HasReachedDestination;
        public void Interrupt() { }
    }

    public class Nurse_HuntAction : IAIAction
    {
        private readonly EnemyActor     _actor;
        private readonly NurseController _nurse;
        public string ActionName => "Nurse_Hunt";
        public Nurse_HuntAction(EnemyActor a, NurseController n) { _actor = a; _nurse = n; }

        public float ScoreAction(Blackboard bb) => _nurse.IsHuntingInjured ? 0.85f : 0f;

        public void Execute(Blackboard bb)
        {
            _actor.SetState(EnemyState.Hunting);
            var pos = bb.Get<Vector3>(Blackboard.Keys.PlayerLastKnownPos);
            _actor.Movement.MoveTo(pos);

            // Disable nearest med station
            var graph = World.FacilityGraphManager.Instance?.Graph;
            if (graph == null) return;
            string playerRoom = WorldStateManager.Instance?.PlayerCurrentRoomId;
            if (playerRoom == null) return;

            foreach (var node in graph.AllNodes)
                if (node.Type == RoomType.Medical && node.MedStationActive)
                {
                    FacilityBrain.Instance?.TryDisableStation(node.RoomId, _nurse.MedStationDisableTime);
                    break;
                }
        }

        public bool IsComplete(Blackboard bb) => !_nurse.IsHuntingInjured;
        public void Interrupt() => _nurse.ClearHunt();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  CHILD UNIT  – emotional manipulation, temptation trap
    // ═════════════════════════════════════════════════════════════════════════
    [RequireComponent(typeof(EnemyActor))]
    public class ChildUnitController : MonoBehaviour
    {
        [SerializeField] private float _appearanceInterval  = 180f;
        [SerializeField] private float _disappearRange      = 3f;     // vanishes when approached

        private EnemyActor _actor;
        private float      _timer;
        private bool       _visible;
        private Renderer[] _renderers;

        private void Awake()
        {
            _actor     = GetComponent<EnemyActor>();
            _renderers = GetComponentsInChildren<Renderer>();
            SetVisible(false);
        }

        private void Update()
        {
            if (!_actor.IsActiveInCurrentPhase()) return;

            _timer += Time.deltaTime;
            if (!_visible && _timer >= _appearanceInterval)
            {
                _timer = 0f;
                TryAppear();
            }

            if (_visible)
            {
                var player = FindObjectOfType<PlayerController>();
                if (player != null)
                {
                    float dist = Vector3.Distance(transform.position, player.transform.position);
                    if (dist <= _disappearRange)
                    {
                        Vanish();
                        // Teleport to trap position ahead
                        TeleportNearDanger(player);
                    }
                }
            }
        }

        private void TryAppear()
        {
            // Only appear in a visible-but-distant position
            var player = FindObjectOfType<PlayerController>();
            if (player == null) return;

            Vector3 pos = player.transform.position + player.transform.forward * 18f;
            transform.position = pos;
            SetVisible(true);
            _visible = true;

            // Play crying audio at distance
            AudioManager.Instance?.PlayAtPosition("audio_child_crying", pos);
            GameDirector.Instance?.EscalateTo(TensionState.Suspicion);
        }

        private void Vanish()
        {
            SetVisible(false);
            _visible = false;
        }

        private void TeleportNearDanger(PlayerController player)
        {
            // Reappear near a Hound's position, or near a trap
            var hound = FindObjectOfType<HoundController>();
            if (hound != null)
            {
                Vector3 dangerPos = hound.transform.position + Random.insideUnitSphere * 5f;
                dangerPos.y = hound.transform.position.y;
                transform.position = dangerPos;
                SetVisible(true);
                _visible = true;
            }
        }

        private void SetVisible(bool on)
        {
            foreach (var r in _renderers) r.enabled = on;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  HOLLOW MAN  – existential dread, reflection entity
    // ═════════════════════════════════════════════════════════════════════════
    [RequireComponent(typeof(EnemyActor))]
    public class HollowManController : MonoBehaviour
    {
        [SerializeField] private float _minimumIntervalBetweenAppearances = 300f;

        private EnemyActor _actor;
        private float      _lastAppearance = -9999f;
        private bool       _facesPlayer;

        private void Awake() => _actor = GetComponent<EnemyActor>();

        private void OnEnable()  => EventBus.Subscribe<EvRoomEntered>(OnRoomEntered);
        private void OnDisable() => EventBus.Unsubscribe<EvRoomEntered>(OnRoomEntered);

        private void OnRoomEntered(EvRoomEntered evt)
        {
            if (!_actor.IsActiveInCurrentPhase()) return;
            if (Time.time - _lastAppearance < _minimumIntervalBetweenAppearances) return;

            // Hollow Man appears in reflective surfaces of safe rooms – maximum violation
            var graph = World.FacilityGraphManager.Instance?.Graph;
            var room  = graph?.GetNode(evt.RoomId);
            if (room == null) return;

            // Only appear in safe rooms for maximum dread (late game only)
            if (!room.IsSafeRoom) return;
            if (GameDirector.Instance?.CurrentPhase != GamePhase.LateGame) return;

            _lastAppearance = Time.time;
            StartCoroutine(AppearSequence(evt.RoomId));
        }

        private IEnumerator AppearSequence(string roomId)
        {
            // Phase 1: appears in reflection facing wrong direction
            _facesPlayer = false;
            EventBus.Publish(new EvPerceptionEvent(PerceptionEventType.ShadowFigure, 0.9f));
            AudioManager.Instance?.PlayAtPosition("audio_hollow_hum",
                FindObjectOfType<PlayerController>()?.transform.position ?? Vector3.zero);

            yield return new WaitForSeconds(8f);

            // Phase 2: now faces player
            _facesPlayer = true;
            EventBus.Publish(new EvPerceptionEvent(PerceptionEventType.PeripheralSighting, 1f));

            yield return new WaitForSeconds(5f);

            // Phase 3: blackout relocation
            EventBus.Publish(new EvPerceptionEvent(PerceptionEventType.UIDistortion, 1f));
            yield return new WaitForSeconds(0.5f);

            // Reposition – now somewhere in the player's next room
            GameDirector.Instance?.EscalateTo(TensionState.Panic);
        }
    }
}

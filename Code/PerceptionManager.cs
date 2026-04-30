using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Eidolon.Core
{
    /// <summary>
    /// Manages the player's hidden stress / perception-reliability values.
    /// No visible sanity meter. Effects are emergent and diegetic.
    ///
    /// Stress rises with: danger proximity, failed stealth, trap hits, Big E sightings.
    /// Stress falls with:  safe room time, recovery phase, healing.
    ///
    /// High stress unlocks perception events: false audio, peripheral sightings,
    /// label corruption, UI distortion. All effects are pooled and rate-limited.
    /// </summary>
    public class PerceptionManager : MonoBehaviour
    {
        public static PerceptionManager Instance { get; private set; }

        // ── Inspector ──────────────────────────────────────────────────────────
        [Header("Stress")]
        [SerializeField] private float _stressDecayRate      = 0.01f;   // per second in safe state
        [SerializeField] private float _stressDecayRateSafe  = 0.04f;   // per second in safe room
        [SerializeField] private float _stressGainOnDamage   = 0.15f;
        [SerializeField] private float _stressGainOnChase    = 0.02f;   // per second in chase
        [SerializeField] private float _stressGainOnSighting = 0.08f;   // Big E visible

        [Header("Perception Event Thresholds")]
        [SerializeField] private float _peripheralThreshold    = 0.30f;
        [SerializeField] private float _falseAudioThreshold    = 0.45f;
        [SerializeField] private float _labelCorruptThreshold  = 0.60f;
        [SerializeField] private float _uiDistortThreshold     = 0.70f;
        [SerializeField] private float _hallucinationThreshold = 0.85f;

        [Header("Event Rate Limiting")]
        [SerializeField] private float _minTimeBetweenEvents   = 25f;
        [SerializeField] private float _minTimeBetweenStrong   = 90f;

        // ── State ──────────────────────────────────────────────────────────────
        public float StressLevel           { get; private set; } = 0f;   // 0–1
        public float PerceptionReliability => 1f - StressLevel * 0.6f;   // 1 = fully reliable

        private float _lastEventTime     = -9999f;
        private float _lastStrongEvent   = -9999f;
        private bool  _inSafeRoom        = false;
        private bool  _chaseActive       = false;
        private bool  _bigEVisible       = false;

        // Object pool for hallucination GameObjects
        private readonly Queue<GameObject> _hallucinationPool = new();

        [Header("Hallucination Prefabs")]
        [SerializeField] private GameObject[] _shadowFigurePrefabs;
        [SerializeField] private int           _poolSize = 4;

        // ── Lifecycle ──────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            // Pre-warm pool
            for (int i = 0; i < _poolSize; i++)
            {
                if (_shadowFigurePrefabs == null || _shadowFigurePrefabs.Length == 0) break;
                var prefab = _shadowFigurePrefabs[Random.Range(0, _shadowFigurePrefabs.Length)];
                var go     = Instantiate(prefab);
                go.SetActive(false);
                _hallucinationPool.Enqueue(go);
            }
        }

        private void OnEnable()
        {
            EventBus.Subscribe<EvPlayerDamaged>    (OnDamaged);
            EventBus.Subscribe<EvEnemyStateChanged>(OnEnemyStateChanged);
            EventBus.Subscribe<EvTensionChanged>   (OnTensionChanged);
            EventBus.Subscribe<EvRoomEntered>      (OnRoomEntered);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<EvPlayerDamaged>    (OnDamaged);
            EventBus.Unsubscribe<EvEnemyStateChanged>(OnEnemyStateChanged);
            EventBus.Unsubscribe<EvTensionChanged>   (OnTensionChanged);
            EventBus.Unsubscribe<EvRoomEntered>      (OnRoomEntered);
        }

        private void Update()
        {
            UpdateStress();
            TryFirePerceptionEvent();
        }

        // ── Stress evolution ───────────────────────────────────────────────────
        private void UpdateStress()
        {
            float dt = Time.deltaTime;

            // Decay
            float decay = _inSafeRoom ? _stressDecayRateSafe : _stressDecayRate;
            StressLevel = Mathf.Max(0f, StressLevel - decay * dt);

            // Gain
            if (_chaseActive)
                AddStress(_stressGainOnChase * dt);

            if (_bigEVisible)
                AddStress(_stressGainOnSighting * dt);

            // Room heat pressure also raises stress
            var playerRoom = WorldStateManager.Instance?.PlayerCurrentRoomId;
            if (playerRoom != null)
            {
                var graph = World.FacilityGraphManager.Instance?.Graph;
                var node  = graph?.GetNode(playerRoom);
                if (node != null && node.IsOverheated)
                    AddStress(node.HeatPressure * 0.005f * dt);
            }
        }

        public void AddStress(float amount)
            => StressLevel = Mathf.Clamp01(StressLevel + amount);

        public void ReduceStress(float amount)
            => StressLevel = Mathf.Max(0f, StressLevel - amount);

        // ── Perception event firing ────────────────────────────────────────────
        private void TryFirePerceptionEvent()
        {
            if (Time.time - _lastEventTime < _minTimeBetweenEvents) return;

            // Weighted chance to fire rises with stress
            float roll = Random.value;
            if (roll > StressLevel * 0.015f) return;   // very sparse by default

            PerceptionEventType type = ChooseEventType();
            float intensity = Mathf.Lerp(0.3f, 1f, StressLevel);

            var evtRequest = new DangerousEventRequest
            {
                EventName         = $"Perception_{type}",
                Severity          = EventSeverity.Minor,
                HadWarning        = true,
                RiskWasInferable  = true,
                CounterplayExists = true,
                PlayerStressLevel = StressLevel
            };

            // Even minor perception events go through fairness
            var report = FairnessValidator.Instance?.Validate(evtRequest);
            if (report?.WasSuppressed ?? false) return;

            FireEvent(type, intensity);
            _lastEventTime = Time.time;
        }

        private PerceptionEventType ChooseEventType()
        {
            // Gate events behind stress thresholds
            var candidates = new List<PerceptionEventType>();

            if (StressLevel >= _peripheralThreshold)
            {
                candidates.Add(PerceptionEventType.PeripheralSighting);
                candidates.Add(PerceptionEventType.FalseFootstep);
            }
            if (StressLevel >= _falseAudioThreshold)
            {
                candidates.Add(PerceptionEventType.DelayedSound);
                candidates.Add(PerceptionEventType.FalseMonsterCue);
            }
            if (StressLevel >= _labelCorruptThreshold)
                candidates.Add(PerceptionEventType.WrongRoomLabel);

            if (StressLevel >= _uiDistortThreshold)
                candidates.Add(PerceptionEventType.UIDistortion);

            if (StressLevel >= _hallucinationThreshold &&
                Time.time - _lastStrongEvent >= _minTimeBetweenStrong)
            {
                candidates.Add(PerceptionEventType.EntityHallucination);
                candidates.Add(PerceptionEventType.ShadowFigure);
            }

            if (candidates.Count == 0) return PerceptionEventType.FalseFootstep;
            return candidates[Random.Range(0, candidates.Count)];
        }

        private void FireEvent(PerceptionEventType type, float intensity)
        {
            EventBus.Publish(new EvPerceptionEvent(type, intensity));

            bool isStrong = type is PerceptionEventType.EntityHallucination
                                 or PerceptionEventType.ShadowFigure
                                 or PerceptionEventType.UIDistortion;
            if (isStrong) _lastStrongEvent = Time.time;

            // Spawn visual for spatial events
            switch (type)
            {
                case PerceptionEventType.PeripheralSighting:
                case PerceptionEventType.ShadowFigure:
                case PerceptionEventType.EntityHallucination:
                    SpawnHallucination(intensity, type == PerceptionEventType.EntityHallucination);
                    break;

                case PerceptionEventType.FalseFootstep:
                    PlayFalseFootstep();
                    break;

                case PerceptionEventType.FalseMonsterCue:
                    PlayFalseMonsterCue();
                    break;
            }
        }

        // ── Spatial effects ────────────────────────────────────────────────────
        private void SpawnHallucination(float intensity, bool solid)
        {
            if (_hallucinationPool.Count == 0) return;

            var go = _hallucinationPool.Dequeue();
            go.SetActive(true);

            var player = FindObjectOfType<Actors.PlayerController>();
            if (player == null) { ReturnToPool(go); return; }

            // Place in peripheral vision – 70–120° off centre
            float   angle   = (Random.value > 0.5f ? 1f : -1f) * Random.Range(70f, 120f);
            Vector3 dir     = Quaternion.Euler(0, angle, 0) * player.Transform.forward;
            Vector3 spawnPos = player.Transform.position + dir * Random.Range(8f, 18f);
            spawnPos.y = player.Transform.position.y;

            go.transform.position = spawnPos;
            go.transform.LookAt(player.Transform.position);

            float duration = solid ? Random.Range(4f, 10f) : Random.Range(1.5f, 4f);
            StartCoroutine(ReturnHallucinationAfter(go, duration));
        }

        private IEnumerator ReturnHallucinationAfter(GameObject go, float delay)
        {
            yield return new WaitForSeconds(delay);
            ReturnToPool(go);
        }

        private void ReturnToPool(GameObject go)
        {
            go.SetActive(false);
            _hallucinationPool.Enqueue(go);
        }

        private void PlayFalseFootstep()
        {
            var player = FindObjectOfType<Actors.PlayerController>();
            if (player == null) return;
            Vector3 pos = player.Transform.position + Random.insideUnitSphere * 10f;
            pos.y = player.Transform.position.y;
            AudioManager.Instance?.PlayAtPosition("audio_footstep_false", pos);
        }

        private void PlayFalseMonsterCue()
        {
            var player = FindObjectOfType<Actors.PlayerController>();
            if (player == null) return;
            string[] cues = { "audio_hound_distant", "audio_machinery_bang", "audio_door_slam" };
            Vector3 pos   = player.Transform.position + Random.insideUnitSphere * 15f;
            pos.y = player.Transform.position.y;
            AudioManager.Instance?.PlayAtPosition(cues[Random.Range(0, cues.Length)], pos);
        }

        // ── Event handlers ─────────────────────────────────────────────────────
        private void OnDamaged(EvPlayerDamaged evt)
            => AddStress(_stressGainOnDamage * (evt.Amount / 30f));

        private void OnEnemyStateChanged(EvEnemyStateChanged evt)
        {
            if (evt.Next == EnemyState.Chasing && evt.EnemyId == "BigE")
                _bigEVisible = true;
            else if (evt.Prev == EnemyState.Chasing && evt.EnemyId == "BigE")
            {
                _bigEVisible = false;
                _chaseActive = false;
            }

            _chaseActive = evt.Next == EnemyState.Chasing;
        }

        private void OnTensionChanged(EvTensionChanged evt)
        {
            if (evt.Next == TensionState.Recovery)
                ReduceStress(0.1f);
        }

        private void OnRoomEntered(EvRoomEntered evt)
        {
            var graph = World.FacilityGraphManager.Instance?.Graph;
            var node  = graph?.GetNode(evt.RoomId);
            _inSafeRoom = node?.IsSafeRoom ?? false;
        }
    }
}

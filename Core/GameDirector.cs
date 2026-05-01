using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Eidolon.Data;

namespace Eidolon.Core
{
    /// <summary>
    /// Controls overall game pacing through a tension state machine.
    /// Manages encounter frequency, AI aggression, and pressure cycles.
    /// Cycle: calm → suspicion → escalation → close call → relief → doubt → rebuild.
    /// </summary>
    public class GameDirector : MonoBehaviour
    {
        public static GameDirector Instance { get; private set; }

        [Header("Phase Thresholds")]
        [SerializeField] private float _midGameObjectiveProgress = 0.35f;
        [SerializeField] private float _lateGameObjectiveProgress = 0.70f;

        [Header("Tension Timing")]
        [SerializeField] private float _minCalmDuration   = 45f;
        [SerializeField] private float _maxCalmDuration   = 90f;
        [SerializeField] private float _minPressureDuration = 30f;
        [SerializeField] private float _maxPressureDuration = 60f;
        [SerializeField] private float _recoveryDuration  = 20f;
        [SerializeField] private float _panicMaxDuration  = 25f;

        [Header("Tension Parameters")]
        [SerializeField] private TensionProfile _calmProfile;
        [SerializeField] private TensionProfile _suspicionProfile;
        [SerializeField] private TensionProfile _pressureProfile;
        [SerializeField] private TensionProfile _panicProfile;
        [SerializeField] private TensionProfile _recoveryProfile;

        [Header("Difficulty")]
        [SerializeField] private DifficultySettings _difficultySettings;

        // ─── State ──────────────────────────────────────────────────────────

        private TensionState _currentTension = TensionState.Calm;
        private GamePhase    _currentPhase   = GamePhase.EarlyGame;
        private float        _tensionTimer;
        private float        _tensionTargetDuration;
        private float        _objectiveProgress; // 0-1

        // Current active parameters (interpolated from profiles)
        public float EncounterFrequencyMultiplier { get; private set; } = 1f;
        public float AIAggressionMultiplier       { get; private set; } = 0f;
        public float FalseCueRate                 { get; private set; } = 0f;
        public float RoomHostility                { get; private set; } = 0f;
        public float SoundtrackIntensity          { get; private set; } = 0f;
        public float SafeRoomReliability          { get; private set; } = 1f;
        public float PerceptionInstability        { get; private set; } = 0f;

        public TensionState CurrentTension => _currentTension;
        public GamePhase    CurrentPhase   => _currentPhase;
        public float        ObjectiveProgress => _objectiveProgress;

        // ─── Lifecycle ──────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<ObjectiveCompletedEvent>(OnObjectiveCompleted);
            EventBus.Subscribe<PlayerDamagedEvent>(OnPlayerDamaged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<ObjectiveCompletedEvent>(OnObjectiveCompleted);
            EventBus.Unsubscribe<PlayerDamagedEvent>(OnPlayerDamaged);
        }

        private void Start()
        {
            TransitionToTension(TensionState.Calm);
        }

        private void Update()
        {
            _tensionTimer += Time.deltaTime;
            TickTensionCycle();
            ApplyDifficultyScaling();
        }

        // ─── Tension Cycle ──────────────────────────────────────────────────

        private void TickTensionCycle()
        {
            if (_tensionTimer < _tensionTargetDuration) return;

            TensionState next = _currentTension switch
            {
                TensionState.Calm       => TensionState.Suspicion,
                TensionState.Suspicion  => TensionState.Pressure,
                TensionState.Pressure   => TensionState.Panic,
                TensionState.Panic      => TensionState.Recovery,
                TensionState.Recovery   => TensionState.Calm,
                _ => TensionState.Calm
            };
            TransitionToTension(next);
        }

        public void TransitionToTension(TensionState next)
        {
            if (_currentTension == next) return;

            var previous = _currentTension;
            _currentTension = next;
            _tensionTimer = 0f;

            _tensionTargetDuration = next switch
            {
                TensionState.Calm      => UnityEngine.Random.Range(_minCalmDuration, _maxCalmDuration),
                TensionState.Suspicion => UnityEngine.Random.Range(15f, 30f),
                TensionState.Pressure  => UnityEngine.Random.Range(_minPressureDuration, _maxPressureDuration),
                TensionState.Panic     => _panicMaxDuration,
                TensionState.Recovery  => _recoveryDuration,
                _ => 30f
            };

            ApplyTensionProfile(next);

            EventBus.Publish(new TensionStateChangedEvent
            {
                Previous = previous,
                Current  = next
            });

            Debug.Log($"[GameDirector] Tension: {previous} → {next} (duration: {_tensionTargetDuration:F1}s)");
        }

        private void ApplyTensionProfile(TensionState state)
        {
            var profile = state switch
            {
                TensionState.Calm      => _calmProfile,
                TensionState.Suspicion => _suspicionProfile,
                TensionState.Pressure  => _pressureProfile,
                TensionState.Panic     => _panicProfile,
                TensionState.Recovery  => _recoveryProfile,
                _ => _calmProfile
            };

            if (profile == null) return;

            EncounterFrequencyMultiplier = profile.EncounterFrequencyMultiplier;
            AIAggressionMultiplier       = profile.AIAggressionMultiplier;
            FalseCueRate                 = profile.FalseCueRate;
            RoomHostility                = profile.RoomHostility;
            SoundtrackIntensity          = profile.SoundtrackIntensity;
            SafeRoomReliability          = profile.SafeRoomReliability;
            PerceptionInstability        = profile.PerceptionInstability;
        }

        // ─── Phase Progression ──────────────────────────────────────────────

        public void UpdateObjectiveProgress(float progress)
        {
            _objectiveProgress = Mathf.Clamp01(progress);

            var newPhase = _currentPhase;
            if (_objectiveProgress >= _lateGameObjectiveProgress)
                newPhase = GamePhase.LateGame;
            else if (_objectiveProgress >= _midGameObjectiveProgress)
                newPhase = GamePhase.MidGame;

            if (newPhase != _currentPhase)
            {
                var prev = _currentPhase;
                _currentPhase = newPhase;
                EventBus.Publish(new GamePhaseChangedEvent { Previous = prev, Current = _currentPhase });
                Debug.Log($"[GameDirector] Phase: {prev} → {_currentPhase}");
            }
        }

        // ─── External Triggers ──────────────────────────────────────────────

        /// <summary>Force immediate jump to panic (e.g. player spotted by BigE).</summary>
        public void ForcePanic()
        {
            TransitionToTension(TensionState.Panic);
        }

        /// <summary>Force recovery window (e.g. player reaches safe room after chase).</summary>
        public void ForceRecovery()
        {
            TransitionToTension(TensionState.Recovery);
        }

        // ─── Difficulty Scaling ─────────────────────────────────────────────

        private void ApplyDifficultyScaling()
        {
            if (_difficultySettings == null) return;
            float scaledAggression = AIAggressionMultiplier * _difficultySettings.AggressionScale;
            AIAggressionMultiplier = Mathf.Clamp01(scaledAggression);
        }

        // ─── Event Handlers ─────────────────────────────────────────────────

        private void OnObjectiveCompleted(ObjectiveCompletedEvent evt)
        {
            if (ObjectiveManager.Instance != null)
                UpdateObjectiveProgress(ObjectiveManager.Instance.NormalisedProgress);
        }

        private void OnPlayerDamaged(PlayerDamagedEvent evt)
        {
            // Being damaged during Calm bumps tension up
            if (_currentTension == TensionState.Calm || _currentTension == TensionState.Suspicion)
                TransitionToTension(TensionState.Pressure);
        }

        // ─── Queries ────────────────────────────────────────────────────────

        public bool IsMonsterTypeActiveThisPhase(EnemyType type)
        {
            return _currentPhase switch
            {
                GamePhase.EarlyGame => type == EnemyType.BigE || type == EnemyType.FacilityBrain
                                    || type == EnemyType.Hound || type == EnemyType.Mimic,
                GamePhase.MidGame   => type != EnemyType.ChildUnit && type != EnemyType.HollowMan,
                GamePhase.LateGame  => true,
                _ => false
            };
        }

        public bool IsInSafeWindow()
            => _currentTension == TensionState.Recovery || _currentTension == TensionState.Calm;
    }
}

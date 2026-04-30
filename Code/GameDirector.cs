using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Eidolon.Core
{
    /// <summary>
    /// Orchestrates all high-level pacing: tension cycles, phase transitions,
    /// encounter gating, and cross-system coordination.
    /// Never directly controls monsters – issues commands through AIManager and FacilityBrain.
    /// </summary>
    public class GameDirector : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────────
        public static GameDirector Instance { get; private set; }

        // ── Inspector config ──────────────────────────────────────────────────
        [Header("Phase Thresholds (0-1 objective progress)")]
        [SerializeField] private float _midGameThreshold  = 0.35f;
        [SerializeField] private float _lateGameThreshold = 0.70f;

        [Header("Tension Durations (seconds)")]
        [SerializeField] private float _minCalmDuration      = 45f;
        [SerializeField] private float _maxCalmDuration      = 90f;
        [SerializeField] private float _suspicionDuration    = 25f;
        [SerializeField] private float _pressureDuration     = 40f;
        [SerializeField] private float _panicDuration        = 20f;
        [SerializeField] private float _recoveryDuration     = 35f;

        [Header("Encounter Gating")]
        [SerializeField] private float _minTimeBetweenMajorEncounters = 120f;
        [SerializeField] private float _minTimeBetweenDeaths          = 180f;

        // ── State ─────────────────────────────────────────────────────────────
        public TensionState CurrentTension { get; private set; } = TensionState.Calm;
        public GamePhase    CurrentPhase   { get; private set; } = GamePhase.EarlyGame;

        private float _phaseTimer;
        private float _tensionTimer;
        private float _lastMajorEncounterTime = -9999f;
        private float _lastDeathTime          = -9999f;
        private float _calmDurationTarget;

        private readonly List<IPacingObserver> _pacingObservers = new List<IPacingObserver>();

        // ── Lifecycle ─────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            EventBus.Subscribe<EvPlayerDied>(OnPlayerDied);
            EventBus.Subscribe<EvObjectiveCompleted>(OnObjectiveCompleted);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<EvPlayerDied>(OnPlayerDied);
            EventBus.Unsubscribe<EvObjectiveCompleted>(OnObjectiveCompleted);
        }

        private void Start()
        {
            _calmDurationTarget = UnityEngine.Random.Range(_minCalmDuration, _maxCalmDuration);
            SetTension(TensionState.Calm);
        }

        private void Update()
        {
            _tensionTimer += Time.deltaTime;
            TickTensionCycle();
        }

        // ── Tension FSM ───────────────────────────────────────────────────────
        private void TickTensionCycle()
        {
            switch (CurrentTension)
            {
                case TensionState.Calm:
                    if (_tensionTimer >= _calmDurationTarget)
                        SetTension(TensionState.Suspicion);
                    break;

                case TensionState.Suspicion:
                    if (_tensionTimer >= _suspicionDuration)
                        SetTension(TensionState.Pressure);
                    break;

                case TensionState.Pressure:
                    if (_tensionTimer >= _pressureDuration)
                        SetTension(TensionState.Panic);
                    break;

                case TensionState.Panic:
                    if (_tensionTimer >= _panicDuration)
                        SetTension(TensionState.Recovery);
                    break;

                case TensionState.Recovery:
                    if (_tensionTimer >= _recoveryDuration)
                    {
                        _calmDurationTarget = UnityEngine.Random.Range(_minCalmDuration, _maxCalmDuration);
                        SetTension(TensionState.Calm);
                    }
                    break;
            }
        }

        private void SetTension(TensionState next)
        {
            var prev = CurrentTension;
            CurrentTension = next;
            _tensionTimer  = 0f;

            foreach (var obs in _pacingObservers)
                obs.OnTensionChanged(prev, next);

            EventBus.Publish(new EvTensionChanged(prev, next));

            if (DebugOverlayManager.Instance != null)
                DebugOverlayManager.Instance.SetTension(next);
        }

        // ── External API ──────────────────────────────────────────────────────

        /// <summary>Force tension – e.g. when a chase starts externally.</summary>
        public void EscalateTo(TensionState target)
        {
            if ((int)target > (int)CurrentTension)
                SetTension(target);
        }

        /// <summary>Called by AIManager when a major encounter fires.</summary>
        public void RegisterMajorEncounter()
        {
            _lastMajorEncounterTime = Time.time;
        }

        /// <summary>True if enough time has elapsed for another major encounter.</summary>
        public bool MajorEncounterAllowed()
        {
            return Time.time - _lastMajorEncounterTime >= _minTimeBetweenMajorEncounters;
        }

        /// <summary>True if death punishment gating allows a lethal attempt.</summary>
        public bool LethalAttemptAllowed()
        {
            return Time.time - _lastDeathTime >= _minTimeBetweenDeaths;
        }

        // ── Observers ─────────────────────────────────────────────────────────
        public void AddObserver(IPacingObserver obs)    => _pacingObservers.Add(obs);
        public void RemoveObserver(IPacingObserver obs) => _pacingObservers.Remove(obs);

        // ── Phase management ──────────────────────────────────────────────────
        public void CheckPhaseTransition(float objectiveProgress01)
        {
            GamePhase desired = CurrentPhase;

            if      (objectiveProgress01 >= _lateGameThreshold) desired = GamePhase.LateGame;
            else if (objectiveProgress01 >= _midGameThreshold)  desired = GamePhase.MidGame;

            if (desired != CurrentPhase)
            {
                CurrentPhase = desired;
                EventBus.Publish(new EvGamePhaseChanged(desired));
                AIManager.Instance?.OnPhaseChanged(desired);
            }
        }

        // ── Event handlers ────────────────────────────────────────────────────
        private void OnPlayerDied(EvPlayerDied _)
        {
            _lastDeathTime = Time.time;
            SetTension(TensionState.Recovery);
        }

        private void OnObjectiveCompleted(EvObjectiveCompleted _)
        {
            float progress = ObjectiveManager.Instance != null
                ? ObjectiveManager.Instance.GetTotalProgress()
                : 0f;
            CheckPhaseTransition(progress);
        }

        // ── Tension query helpers (used by AI systems) ────────────────────────
        public float GetTensionFloat()
        {
            return CurrentTension switch
            {
                TensionState.Calm      => 0f,
                TensionState.Suspicion => 0.25f,
                TensionState.Pressure  => 0.55f,
                TensionState.Panic     => 0.90f,
                TensionState.Recovery  => 0.15f,
                _                      => 0f
            };
        }

        public bool IsInRelaxedState()
            => CurrentTension is TensionState.Calm or TensionState.Recovery;
    }
}

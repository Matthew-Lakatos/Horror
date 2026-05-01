using System.Collections.Generic;
using UnityEngine;

namespace Eidolon.Core
{
    /// <summary>
    /// Mandatory fairness gate. ALL dangerous events must pass before execution.
    /// Checks: prior warning, inferable risk, counterplay availability, recent similar event,
    /// and reasonable-player subjective fairness.
    /// Returns a verdict with optional mitigation strategy.
    /// </summary>
    public class FairnessValidator : MonoBehaviour
    {
        public static FairnessValidator Instance { get; private set; }

        [Header("Cooldown Settings")]
        [SerializeField] private float _deathPunishmentCooldown    = 90f;
        [SerializeField] private float _trapCooldownSameRoom       = 45f;
        [SerializeField] private float _chaseAfterDamageCooldown   = 30f;
        [SerializeField] private float _panicSpikeMinInterval      = 20f;

        // Tracks last time each FairnessThreatType was triggered
        private readonly Dictionary<FairnessThreatType, float> _lastTriggerTime
            = new Dictionary<FairnessThreatType, float>();

        // How many suppressions have occurred (for debug overlay)
        private int _totalSuppressionsThisSession;
        public  int TotalSuppressions => _totalSuppressionsThisSession;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        // ─── Primary Validation Gate ─────────────────────────────────────────

        /// <summary>
        /// Evaluates whether a dangerous event should proceed.
        /// Returns true if the event is FAIR to execute.
        /// </summary>
        public bool Validate(FairnessRequest request, out FairnessVerdict verdict)
        {
            verdict = new FairnessVerdict { ShouldProceed = true, Mitigation = FairnessMitigation.None };

            // 1. Was there a warning?
            if (!request.PlayerHadWarning)
            {
                verdict = Suppress(FairnessMitigation.ConvertToWarning,
                    $"No warning before {request.ThreatType}");
                return false;
            }

            // 2. Could the risk be inferred from environment/prior knowledge?
            if (!request.RiskIsInferable)
            {
                verdict = Suppress(FairnessMitigation.Soften,
                    $"Risk not inferable for {request.ThreatType}");
                return false;
            }

            // 3. Does the player have some counterplay available?
            if (!request.CounterplayAvailable)
            {
                verdict = Suppress(FairnessMitigation.Delay,
                    $"No counterplay for {request.ThreatType}");
                return false;
            }

            // 4. Was a similar event too recent?
            if (_lastTriggerTime.TryGetValue(request.ThreatType, out float lastTime))
            {
                float cooldown = GetCooldownFor(request.ThreatType);
                float elapsed = Time.time - lastTime;
                if (elapsed < cooldown)
                {
                    verdict = Suppress(FairnessMitigation.Delay,
                        $"{request.ThreatType} cooldown: {cooldown - elapsed:F1}s remaining");
                    return false;
                }
            }

            // 5. Subjective fairness check (designer override flag)
            if (request.ForceUnfair)
            {
                verdict = Suppress(FairnessMitigation.Reroute,
                    $"Designer flagged {request.ThreatType} as unfair in current context");
                return false;
            }

            // PASSED — record trigger time
            _lastTriggerTime[request.ThreatType] = Time.time;
            return true;
        }

        /// <summary>
        /// Convenience overload for simple checks with just type and warning status.
        /// </summary>
        public bool QuickValidate(FairnessThreatType type, bool hadWarning, bool hasCounterplay)
        {
            var request = new FairnessRequest
            {
                ThreatType         = type,
                PlayerHadWarning   = hadWarning,
                RiskIsInferable    = true,
                CounterplayAvailable = hasCounterplay,
                ForceUnfair        = false
            };
            return Validate(request, out _);
        }

        // ─── Safe Room Override ──────────────────────────────────────────────

        /// <summary>
        /// Validates whether a safe room can be compromised.
        /// Late game: max 2 memorable corruptions total.
        /// </summary>
        private int _safeRoomBreachCount;
        private const int MaxSafeRoomBreaches = 2;

        public bool CanBreachSafeRoom()
        {
            if (GameDirector.Instance == null) return false;
            if (GameDirector.Instance.CurrentPhase != GamePhase.LateGame) return false;
            if (_safeRoomBreachCount >= MaxSafeRoomBreaches) return false;

            _safeRoomBreachCount++;
            return true;
        }

        // ─── Helpers ────────────────────────────────────────────────────────

        private FairnessVerdict Suppress(FairnessMitigation mitigation, string reason)
        {
            _totalSuppressionsThisSession++;
            Debug.Log($"[FairnessValidator] SUPPRESSED — {reason} | Mitigation: {mitigation}");
            EventBus.Publish(new FairnessSuppressedEvent { Reason = reason, Mitigation = mitigation });
            return new FairnessVerdict { ShouldProceed = false, Mitigation = mitigation };
        }

        private float GetCooldownFor(FairnessThreatType type) => type switch
        {
            FairnessThreatType.LethalAttack      => _deathPunishmentCooldown,
            FairnessThreatType.TrapInSameRoom    => _trapCooldownSameRoom,
            FairnessThreatType.ChaseAfterDamage  => _chaseAfterDamageCooldown,
            FairnessThreatType.PanicSpike        => _panicSpikeMinInterval,
            FairnessThreatType.SafeRoomBreach    => float.MaxValue, // controlled by CanBreachSafeRoom
            _ => 15f
        };

        public void ResetCooldown(FairnessThreatType type)
        {
            if (_lastTriggerTime.ContainsKey(type))
                _lastTriggerTime.Remove(type);
        }
    }

    // ─── Supporting Types ────────────────────────────────────────────────────

    public enum FairnessThreatType
    {
        LethalAttack, TrapInSameRoom, ChaseAfterDamage, PanicSpike,
        SafeRoomBreach, GeometryShift, PerceptionOverload
    }

    public enum FairnessMitigation
    {
        None, Suppress, ConvertToWarning, Soften, Delay, Reroute
    }

    public struct FairnessRequest
    {
        public FairnessThreatType ThreatType;
        public bool               PlayerHadWarning;
        public bool               RiskIsInferable;
        public bool               CounterplayAvailable;
        public bool               ForceUnfair;
    }

    public struct FairnessVerdict
    {
        public bool              ShouldProceed;
        public FairnessMitigation Mitigation;
    }

    public struct FairnessSuppressedEvent
    {
        public string            Reason;
        public FairnessMitigation Mitigation;
    }
}

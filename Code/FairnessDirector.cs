using System.Collections.Generic;
using UnityEngine;

namespace Eidolon.Core
{
    /// <summary>
    /// Runs before every dangerous event. Enforces the seven fairness checks.
    /// Any suppressed event is logged and optionally converted to a warning.
    /// </summary>
    public class FairnessValidator : MonoBehaviour
    {
        public static FairnessValidator Instance { get; private set; }

        // ── Config ─────────────────────────────────────────────────────────────
        [Header("Cooldowns")]
        [SerializeField] private float _recentDeathWindow    = 90f;   // seconds
        [SerializeField] private float _recentDamageWindow   = 30f;
        [SerializeField] private float _sameRoomPunishWindow = 60f;

        // ── State ──────────────────────────────────────────────────────────────
        private float _lastDeathTime   = -9999f;
        private float _lastDamageTime  = -9999f;
        private readonly Dictionary<string, float> _lastPunishByRoom = new();
        private readonly List<FairnessReport> _recentReports         = new();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<EvPlayerDied>   (e => _lastDeathTime  = Time.time);
            EventBus.Subscribe<EvPlayerDamaged>(e => _lastDamageTime = Time.time);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<EvPlayerDied>   (e => _lastDeathTime  = Time.time);
            EventBus.Unsubscribe<EvPlayerDamaged>(e => _lastDamageTime = Time.time);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  MAIN VALIDATION  – call before firing any dangerous event
        // ─────────────────────────────────────────────────────────────────────
        public FairnessReport Validate(DangerousEventRequest request)
        {
            var report = new FairnessReport(request);

            // Rule 1 – Was there a warning?
            if (!request.HadWarning && request.Severity >= EventSeverity.Lethal)
            {
                report.Suppress(FairnessRule.NoWarning,
                    "Lethal event fired without prior warning.");
                return Finalise(report);
            }

            // Rule 2 – Could risk be inferred from context?
            if (!request.RiskWasInferable && request.Severity >= EventSeverity.Severe)
            {
                report.Suppress(FairnessRule.UninferableRisk,
                    "Severe event in a context with no prior telegraphing.");
                return Finalise(report);
            }

            // Rule 3 – Is there counterplay?
            if (!request.CounterplayExists)
            {
                report.Downgrade(FairnessRule.NoCounterplay,
                    "Downgrading to non-lethal – no counterplay option existed.");
            }

            // Rule 4 – Recent punishment in same room?
            if (request.RoomId != null &&
                _lastPunishByRoom.TryGetValue(request.RoomId, out float roomTime) &&
                Time.time - roomTime < _sameRoomPunishWindow)
            {
                report.Suppress(FairnessRule.RepeatRoomPunish,
                    $"Room {request.RoomId} punished too recently.");
                return Finalise(report);
            }

            // Rule 5 – Recent death window?
            if (Time.time - _lastDeathTime < _recentDeathWindow &&
                request.Severity >= EventSeverity.Lethal)
            {
                report.Suppress(FairnessRule.TooSoonAfterDeath,
                    "Lethal event within recent-death window.");
                return Finalise(report);
            }

            // Rule 6 – Recent damage window (stacking punishment)?
            if (Time.time - _lastDamageTime < _recentDamageWindow &&
                request.Severity >= EventSeverity.Severe)
            {
                report.Downgrade(FairnessRule.StackedPunishment,
                    "Reducing severity – player already damaged recently.");
            }

            // Rule 7 – Would reasonable players call this unfair? (heuristic)
            if (request.PlayerStressLevel > 0.85f && request.Severity >= EventSeverity.Lethal)
            {
                report.Downgrade(FairnessRule.HighStressLethal,
                    "Player stress very high – converting lethal to severe.");
            }

            return Finalise(report);
        }

        private FairnessReport Finalise(FairnessReport report)
        {
            if (!report.WasSuppressed && report.EffectiveSeverity >= EventSeverity.Moderate)
            {
                var req = report.Request;
                if (req.RoomId != null)
                    _lastPunishByRoom[req.RoomId] = Time.time;
            }

            _recentReports.Add(report);
            if (_recentReports.Count > 100) _recentReports.RemoveAt(0);

            DebugOverlayManager.Instance?.LogFairnessReport(report);
            return report;
        }

        // ── Query ──────────────────────────────────────────────────────────────
        public IReadOnlyList<FairnessReport> RecentReports => _recentReports;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  DATA STRUCTURES
    // ─────────────────────────────────────────────────────────────────────────

    public enum EventSeverity { Minor, Moderate, Severe, Lethal }
    public enum FairnessRule
    {
        NoWarning, UninferableRisk, NoCounterplay,
        RepeatRoomPunish, TooSoonAfterDeath, StackedPunishment, HighStressLethal
    }
    public enum FairnessVerdict { Allow, Downgrade, Suppress }

    public class DangerousEventRequest
    {
        public string      EventName;
        public EventSeverity Severity;
        public bool        HadWarning;
        public bool        RiskWasInferable;
        public bool        CounterplayExists;
        public string      RoomId;
        public float       PlayerStressLevel;   // 0–1, from PerceptionManager
        public string      SourceEntityId;
    }

    public class FairnessReport
    {
        public readonly DangerousEventRequest Request;
        public FairnessVerdict  Verdict         { get; private set; } = FairnessVerdict.Allow;
        public EventSeverity    EffectiveSeverity { get; private set; }
        public FairnessRule     TriggeredRule    { get; private set; }
        public string           Reason           { get; private set; }
        public bool             WasSuppressed    => Verdict == FairnessVerdict.Suppress;
        public bool             WasDowngraded    => Verdict == FairnessVerdict.Downgrade;

        public FairnessReport(DangerousEventRequest req)
        {
            Request          = req;
            EffectiveSeverity = req.Severity;
        }

        public void Suppress(FairnessRule rule, string reason)
        {
            Verdict       = FairnessVerdict.Suppress;
            TriggeredRule = rule;
            Reason        = reason;
        }

        public void Downgrade(FairnessRule rule, string reason)
        {
            if (Verdict == FairnessVerdict.Suppress) return; // already suppressed
            Verdict           = FairnessVerdict.Downgrade;
            TriggeredRule     = rule;
            Reason            = reason;
            EffectiveSeverity = (EventSeverity)Mathf.Max(0, (int)EffectiveSeverity - 1);
        }
    }
}

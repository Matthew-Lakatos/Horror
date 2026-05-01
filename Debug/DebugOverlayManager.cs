using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Eidolon.Core;
using Eidolon.AI;
using Eidolon.Actors;
using Eidolon.Conditions;

namespace Eidolon.Debug
{
    /// <summary>
    /// Runtime debug overlay displaying all mandatory debug fields:
    /// monster state/utility scores, route heatmaps, fairness suppressions,
    /// room danger ratings, player noise radius, tension phase,
    /// Big E variables (Attention, Confidence, Hunger, Familiarity).
    /// Toggle with F1 in editor/development builds only.
    /// </summary>
    public class DebugOverlayManager : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Canvas    _overlayCanvas;
        [SerializeField] private Text      _leftColumnText;
        [SerializeField] private Text      _rightColumnText;
        [SerializeField] private bool      _enableInBuilds = false;

        private bool  _visible;
        private float _refreshRate = 0.2f;
        private float _refreshTimer;
        private readonly StringBuilder _sb = new StringBuilder(2048);

        // ─── Lifecycle ───────────────────────────────────────────────────────

        private void Awake()
        {
#if !UNITY_EDITOR
            if (!_enableInBuilds)
            {
                gameObject.SetActive(false);
                return;
            }
#endif
            if (_overlayCanvas) _overlayCanvas.enabled = false;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1))
                ToggleVisibility();

            if (!_visible) return;

            _refreshTimer += Time.deltaTime;
            if (_refreshTimer >= _refreshRate)
            {
                _refreshTimer = 0f;
                RefreshDisplay();
            }
        }

        private void ToggleVisibility()
        {
            _visible = !_visible;
            if (_overlayCanvas) _overlayCanvas.enabled = _visible;
        }

        // ─── Refresh ────────────────────────────────────────────────────────

        private void RefreshDisplay()
        {
            if (_leftColumnText  != null) _leftColumnText.text  = BuildLeftColumn();
            if (_rightColumnText != null) _rightColumnText.text = BuildRightColumn();
        }

        // ─── Left Column — Director / Player / Facility ──────────────────────

        private string BuildLeftColumn()
        {
            _sb.Clear();

            // ── Director ──
            var dir = GameDirector.Instance;
            _sb.AppendLine("<b>=== DIRECTOR ===</b>");
            if (dir != null)
            {
                _sb.AppendLine($"Phase:       {dir.CurrentPhase}");
                _sb.AppendLine($"Tension:     {dir.CurrentTension}");
                _sb.AppendLine($"ObjProgress: {dir.ObjectiveProgress:P0}");
                _sb.AppendLine($"Aggression:  {dir.AIAggressionMultiplier:F2}");
                _sb.AppendLine($"Encounter×:  {dir.EncounterFrequencyMultiplier:F2}");
                _sb.AppendLine($"Perception:  {dir.PerceptionInstability:F2}");
            }

            _sb.AppendLine();

            // ── Player ──
            var player = PlayerController.Instance;
            _sb.AppendLine("<b>=== PLAYER ===</b>");
            if (player != null)
            {
                _sb.AppendLine($"Health:      {player.Health:F0}");
                _sb.AppendLine($"Stamina:     {player.Stamina:F0}");
                _sb.AppendLine($"Battery:     {player.Battery:F0}");
                _sb.AppendLine($"HiddenStress:{player.HiddenStress:F2}");
                _sb.AppendLine($"Trauma:      {player.TraumaLevel:F2}");
                _sb.AppendLine($"Moving:      {player.IsMoving}");
                _sb.AppendLine($"Sprinting:   {player.IsSprinting}");
                _sb.AppendLine($"Crouching:   {player.IsCrouching}");

                // Noise radius indicator
                float noiseR = player.IsSprinting ? 10f : player.IsCrouching ? 1.5f : 5f;
                _sb.AppendLine($"Noise Radius:{noiseR:F1}m");
            }

            _sb.AppendLine();

            // ── Conditions ──
            var cond = ConditionManager.Instance;
            _sb.AppendLine("<b>=== CONDITIONS ===</b>");
            if (cond != null)
            {
                foreach (ConditionType t in System.Enum.GetValues(typeof(ConditionType)))
                {
                    if (cond.HasCondition(t))
                        _sb.AppendLine($"  {t}: {cond.GetTotalMagnitude(t):F2}");
                }
                if (!HasAnyCondition(cond)) _sb.AppendLine("  (none)");
            }

            _sb.AppendLine();

            // ── Facility ──
            var brain = FacilityBrain.Instance;
            _sb.AppendLine("<b>=== FACILITY BRAIN ===</b>");
            if (brain != null)
                _sb.AppendLine($"Hostility:   {brain.HostilityLevel:F2}");

            // ── Fairness ──
            var fair = FairnessValidator.Instance;
            _sb.AppendLine();
            _sb.AppendLine("<b>=== FAIRNESS ===</b>");
            if (fair != null)
                _sb.AppendLine($"Suppressions:{fair.TotalSuppressions}");

            return _sb.ToString();
        }

        // ─── Right Column — Monsters ─────────────────────────────────────────

        private string BuildRightColumn()
        {
            _sb.Clear();

            var aiMgr = AIManager.Instance;
            if (aiMgr == null) return "AI Manager unavailable";

            // ── Big E ──
            _sb.AppendLine("<b>=== BIG E ===</b>");
            var bigEs = aiMgr.GetEnemiesOfType(EnemyType.BigE);
            if (bigEs.Count > 0 && bigEs[0] is BigEActor bigE)
            {
                _sb.AppendLine($"State:       {bigE.State}");
                _sb.AppendLine($"Attention:   {DrawBar(bigE.Attention)}  {bigE.Attention:F2}");
                _sb.AppendLine($"Confidence:  {DrawBar(bigE.Confidence)} {bigE.Confidence:F2}");
                _sb.AppendLine($"Hunger:      {DrawBar(bigE.Hunger)}     {bigE.Hunger:F2}");
                _sb.AppendLine($"Familiarity: {DrawBar(bigE.Familiarity)}{bigE.Familiarity:F2}");
                _sb.AppendLine($"Chasing:     {bigE.IsChasing}");
            }
            else _sb.AppendLine("  Not active");

            _sb.AppendLine();

            // ── Hound ──
            _sb.AppendLine("<b>=== HOUND ===</b>");
            BuildEnemyLine(aiMgr.GetEnemiesOfType(EnemyType.Hound));

            // ── Trapper ──
            _sb.AppendLine("<b>=== TRAPPER ===</b>");
            BuildEnemyLine(aiMgr.GetEnemiesOfType(EnemyType.Trapper));

            // ── Mimic ──
            _sb.AppendLine("<b>=== MIMIC ===</b>");
            BuildEnemyLine(aiMgr.GetEnemiesOfType(EnemyType.Mimic));

            // ── Nurse ──
            _sb.AppendLine("<b>=== NURSE ===</b>");
            BuildEnemyLine(aiMgr.GetEnemiesOfType(EnemyType.Nurse));

            // ── Shared Blackboard ──
            _sb.AppendLine();
            _sb.AppendLine("<b>=== SHARED BLACKBOARD ===</b>");
            var bb = aiMgr.SharedBlackboard;
            if (bb != null)
            {
                _sb.AppendLine($"LastKnownPos:  {bb.LastKnownPlayerPosition:F0}");
                _sb.AppendLine($"LastSeenAge:   {(Time.time - bb.LastSeenTime):F1}s ago");
                _sb.AppendLine($"LastNoiseAge:  {(Time.time - bb.LastNoiseTime):F1}s ago");
                _sb.AppendLine($"PlayerInjured: {bb.PlayerIsInjured}");
                _sb.AppendLine($"PlayerPanic:   {bb.PlayerInPanic}");
            }

            // ── Hot Rooms ──
            _sb.AppendLine();
            _sb.AppendLine("<b>=== HEATMAP TOP 3 ===</b>");
            var hot = WorldStateManager.Instance?.GetMostVisitedRooms(3);
            if (hot != null)
                foreach (var r in hot)
                    _sb.AppendLine($"  {r}: {WorldStateManager.Instance?.GetVisitCount(r)} visits");

            return _sb.ToString();
        }

        // ─── Helpers ────────────────────────────────────────────────────────

        private void BuildEnemyLine(System.Collections.Generic.List<EnemyActor> list)
        {
            if (list.Count == 0) { _sb.AppendLine("  Not active"); return; }
            foreach (var e in list)
                if (e != null)
                    _sb.AppendLine($"  {e.name} | Chasing:{e.IsChasing} | Phase:{e.IsPhaseActive}");
        }

        private string DrawBar(float value, int width = 8)
        {
            int filled = Mathf.RoundToInt(value * width);
            string bar = "[";
            for (int i = 0; i < width; i++) bar += i < filled ? "█" : "░";
            bar += "]";
            return bar;
        }

        private bool HasAnyCondition(ConditionManager cond)
        {
            foreach (ConditionType t in System.Enum.GetValues(typeof(ConditionType)))
                if (cond.HasCondition(t)) return true;
            return false;
        }
    }
}

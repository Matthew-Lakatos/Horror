using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Eidolon.Core
{
    using Actors;

    /// <summary>
    /// In-editor / development overlay. Activated with F1.
    /// Draws runtime state for: monster variables, tension phase, noise radius,
    /// fairness suppressions, route heatmap, room danger, Big E variables.
    /// Zero runtime cost when disabled.
    /// </summary>
    public class DebugOverlayManager : MonoBehaviour
    {
        public static DebugOverlayManager Instance { get; private set; }

        // ── Inspector ──────────────────────────────────────────────────────────
        [Header("Overlay Settings")]
        [SerializeField] private KeyCode        _toggleKey       = KeyCode.F1;
        [SerializeField] private GUISkin        _skin;
        [SerializeField] private float          _panelWidth      = 400f;
        [SerializeField] private Color          _headerColor     = Color.cyan;
        [SerializeField] private Color          _warningColor    = Color.yellow;
        [SerializeField] private Color          _criticalColor   = Color.red;
        [SerializeField] private bool           _enableInBuild   = false;

        // ── State ──────────────────────────────────────────────────────────────
        private bool _visible = false;

        // Cached data (written by other systems, read by OnGUI)
        private TensionState        _tension;
        private readonly List<string> _fairnessLog   = new();
        private const int MaxFairnessLog = 20;

        // ── Lifecycle ──────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

#if !UNITY_EDITOR
            if (!_enableInBuild) { enabled = false; return; }
#endif
        }

        private void Update()
        {
            if (Input.GetKeyDown(_toggleKey))
                _visible = !_visible;
        }

        // ── Data setters (called by other systems) ─────────────────────────────
        public void SetTension(TensionState t) => _tension = t;

        public void LogFairnessReport(FairnessReport report)
        {
            if (report.WasSuppressed || report.WasDowngraded)
            {
                string entry = $"[{Time.time:F1}] {report.Request.EventName} → {report.Verdict} ({report.TriggeredRule})";
                _fairnessLog.Add(entry);
                if (_fairnessLog.Count > MaxFairnessLog)
                    _fairnessLog.RemoveAt(0);
            }
        }

        // ── OnGUI ──────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            if (!_visible) return;
            if (_skin != null) GUI.skin = _skin;

            float x       = 10f;
            float y       = 10f;
            float spacing = 18f;
            float w       = _panelWidth;

            // Background
            GUI.Box(new Rect(x - 5, y - 5, w + 10, Screen.height - 20), "");

            // ── Header ─────────────────────────────────────────────────────────
            DrawLabel(ref x, ref y, w, "═══ EIDOLON DEBUG OVERLAY ═══", _headerColor);
            DrawLabel(ref x, ref y, w, $"F1 to toggle | Time: {Time.time:F1}s");
            y += spacing * 0.5f;

            // ── Game Director ──────────────────────────────────────────────────
            DrawLabel(ref x, ref y, w, "── DIRECTOR ──", _headerColor);
            var gd = GameDirector.Instance;
            if (gd != null)
            {
                Color tc = _tension switch
                {
                    TensionState.Panic    => _criticalColor,
                    TensionState.Pressure => _warningColor,
                    _                     => Color.white
                };
                DrawLabel(ref x, ref y, w, $"Tension: {_tension}", tc);
                DrawLabel(ref x, ref y, w, $"Phase:   {gd.CurrentPhase}");
                DrawLabel(ref x, ref y, w, $"TensionF: {gd.GetTensionFloat():F2}");
            }
            y += spacing * 0.5f;

            // ── Player ─────────────────────────────────────────────────────────
            DrawLabel(ref x, ref y, w, "── PLAYER ──", _headerColor);
            var player = FindObjectOfType<PlayerController>();
            if (player != null)
            {
                DrawLabel(ref x, ref y, w, $"Health:  {player.Health:F1} / Stamina: {player.Stamina:F1}");
                DrawLabel(ref x, ref y, w, $"Battery: {player.Battery:F1}");
                DrawLabel(ref x, ref y, w, $"Room:    {player.CurrentRoomId ?? "?"}");
                DrawLabel(ref x, ref y, w, $"Crouch:  {player.IsCrouching} | Sprint: {player.IsSprinting}");
                DrawLabel(ref x, ref y, w, $"NoiseR:  {player.CurrentNoiseRadius:F1}m");

                var pm = PerceptionManager.Instance;
                if (pm != null)
                {
                    Color stressColor = pm.StressLevel > 0.7f ? _criticalColor
                                      : pm.StressLevel > 0.4f ? _warningColor : Color.white;
                    DrawLabel(ref x, ref y, w, $"Stress:  {pm.StressLevel:F2} | Reliability: {pm.PerceptionReliability:F2}", stressColor);
                }
            }
            y += spacing * 0.5f;

            // ── Conditions ─────────────────────────────────────────────────────
            DrawLabel(ref x, ref y, w, "── CONDITIONS ──", _headerColor);
            var cm = ConditionManager.Instance;
            if (cm != null)
            {
                if (cm.Active.Count == 0)
                    DrawLabel(ref x, ref y, w, "None");
                else
                    foreach (var c in cm.Active)
                        DrawLabel(ref x, ref y, w, $"  {c.Type} mag:{c.Magnitude:F2}", _warningColor);
            }
            y += spacing * 0.5f;

            // ── Big E ──────────────────────────────────────────────────────────
            DrawLabel(ref x, ref y, w, "── BIG E ──", _headerColor);
            var bigEActor = AIManager.Instance?.GetEnemy(MonsterType.BigE);
            if (bigEActor != null)
            {
                DrawLabel(ref x, ref y, w, $"State:       {bigEActor.CurrentState}");
                var bb = bigEActor.BB;
                DrawLabel(ref x, ref y, w, $"Attention:   {bb.Get<float>(AI.Blackboard.Keys.AttentionLevel):F2}");
                DrawLabel(ref x, ref y, w, $"Confidence:  {bb.Get<float>(AI.Blackboard.Keys.PlayerConfidence):F2}");
                DrawLabel(ref x, ref y, w, $"Hunger:      {bb.Get<float>(AI.Blackboard.Keys.HungerLevel):F2}",
                    bb.Get<float>(AI.Blackboard.Keys.HungerLevel) > 0.7f ? _criticalColor : Color.white);
                DrawLabel(ref x, ref y, w, $"Familiarity: {bb.Get<float>(AI.Blackboard.Keys.FamiliarityLevel):F2}");

                var bigEc = bigEActor.GetComponent<BigEController>();
                if (bigEc != null)
                    DrawLabel(ref x, ref y, w, $"Withdrawn:   {bigEc.IsWithdrawn}");

                DrawLabel(ref x, ref y, w, $"Action:      {bigEActor.Brain.CurrentAction?.ActionName ?? "None"}");
            }
            else DrawLabel(ref x, ref y, w, "Not active");
            y += spacing * 0.5f;

            // ── All Enemies ────────────────────────────────────────────────────
            DrawLabel(ref x, ref y, w, "── ENEMIES ──", _headerColor);
            var am = AIManager.Instance;
            if (am != null)
            {
                foreach (var e in am.ActiveEnemies)
                {
                    Color ec = e.CurrentState == EnemyState.Chasing ? _criticalColor
                             : e.CurrentState == EnemyState.Hunting  ? _warningColor
                             : Color.white;
                    DrawLabel(ref x, ref y, w, $"  {e.EntityId,-18} {e.CurrentState}", ec);
                }
            }
            y += spacing * 0.5f;

            // ── Facility Brain ─────────────────────────────────────────────────
            DrawLabel(ref x, ref y, w, "── FACILITY BRAIN ──", _headerColor);
            var fb = World.FacilityBrain.Instance;
            if (fb != null)
            {
                Color hc = fb.GlobalHostility > 0.7f ? _criticalColor
                         : fb.GlobalHostility > 0.4f ? _warningColor : Color.white;
                DrawLabel(ref x, ref y, w, $"Hostility: {fb.GlobalHostility:F2}", hc);
            }
            y += spacing * 0.5f;

            // ── Route Heatmap ──────────────────────────────────────────────────
            DrawLabel(ref x, ref y, w, "── TOP ROUTES ──", _headerColor);
            var wsm = WorldStateManager.Instance;
            if (wsm != null)
            {
                var routes = wsm.GetTopRoutes(5);
                foreach (var r in routes)
                {
                    var parts = r.Split('>');
                    if (parts.Length == 2)
                    {
                        int count = wsm.GetRouteCount(parts[0], parts[1]);
                        DrawLabel(ref x, ref y, w, $"  {r}  ×{count}");
                    }
                }
            }
            y += spacing * 0.5f;

            // ── Fairness Log ───────────────────────────────────────────────────
            DrawLabel(ref x, ref y, w, "── FAIRNESS SUPPRESSIONS ──", _headerColor);
            foreach (var entry in _fairnessLog)
                DrawLabel(ref x, ref y, w, entry, _warningColor);

            y += spacing * 0.5f;

            // ── Objectives ─────────────────────────────────────────────────────
            DrawLabel(ref x, ref y, w, "── OBJECTIVES ──", _headerColor);
            var om = ObjectiveManager.Instance;
            if (om != null)
            {
                DrawLabel(ref x, ref y, w, $"Total Progress: {om.GetTotalProgress() * 100:F0}%");
                foreach (var p in om.Primaries)
                {
                    Color oc = p.IsComplete ? Color.green : Color.white;
                    DrawLabel(ref x, ref y, w, $"  [P] {p.DisplayName} {p.Progress*100:F0}%", oc);
                }
            }
        }

        // ── GUI helpers ────────────────────────────────────────────────────────
        private static void DrawLabel(ref float x, ref float y, float w, string text, Color? col = null)
        {
            Color prev = GUI.color;
            if (col.HasValue) GUI.color = col.Value;
            GUI.Label(new Rect(x, y, w, 20f), text);
            GUI.color = prev;
            y += 18f;
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Eidolon.Core;

namespace Eidolon.Conditions
{
    /// <summary>
    /// Central manager for all ConditionEffect instances applied to the player.
    /// Handles stacking rules, duration management, and visual/gameplay payloads.
    /// One unified system — no per-condition scripts scattered elsewhere.
    /// </summary>
    public class ConditionManager : MonoBehaviour
    {
        public static ConditionManager Instance { get; private set; }

        [Header("Visual Payload Components")]
        [SerializeField] private UnityEngine.Rendering.Volume _postProcessVolume;
        [SerializeField] private UnityEngine.UI.Image         _vignetteOverlay;
        [SerializeField] private UnityEngine.UI.Image         _bleedScreenFlash;
        [SerializeField] private Canvas                       _uiCanvas;

        // Active conditions: type → list of active instances
        private readonly Dictionary<ConditionType, List<ConditionInstance>> _activeConditions
            = new Dictionary<ConditionType, List<ConditionInstance>>();

        // ─── Lifecycle ───────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            foreach (ConditionType t in System.Enum.GetValues(typeof(ConditionType)))
                _activeConditions[t] = new List<ConditionInstance>();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<ConditionAppliedEvent>(OnConditionApplied);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<ConditionAppliedEvent>(OnConditionApplied);
        }

        private void Update()
        {
            TickConditions();
            ApplyVisualPayloads();
        }

        // ─── Apply ───────────────────────────────────────────────────────────

        private void ApplyCondition(ConditionType type, float duration, float magnitude, StackRule stackRule)
        {
            var list = _activeConditions[type];

            switch (stackRule)
            {
                case StackRule.Replace:
                    list.Clear();
                    list.Add(new ConditionInstance(type, duration, magnitude));
                    break;

                case StackRule.Extend:
                    if (list.Count > 0)
                    {
                        list[0].RemainingTime = Mathf.Max(list[0].RemainingTime, duration);
                        list[0].Magnitude     = Mathf.Max(list[0].Magnitude, magnitude);
                    }
                    else list.Add(new ConditionInstance(type, duration, magnitude));
                    break;

                case StackRule.Stack:
                    if (list.Count < GetMaxStack(type))
                        list.Add(new ConditionInstance(type, duration, magnitude));
                    break;

                case StackRule.Ignore:
                    if (list.Count == 0)
                        list.Add(new ConditionInstance(type, duration, magnitude));
                    break;
            }

            Debug.Log($"[ConditionManager] Applied {type} — duration: {duration}s, magnitude: {magnitude:F2}");
            StartCoroutine(RunPayloadCoroutine(type, magnitude));
        }

        // ─── Tick ────────────────────────────────────────────────────────────

        private void TickConditions()
        {
            foreach (var list in _activeConditions.Values)
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    list[i].RemainingTime -= Time.deltaTime;
                    if (list[i].RemainingTime <= 0f)
                        list.RemoveAt(i);
                }
            }
        }

        // ─── Visual Payloads ─────────────────────────────────────────────────

        private void ApplyVisualPayloads()
        {
            // Blur (chromatic shift / post-process)
            float blurIntensity = GetTotalMagnitude(ConditionType.Blur);
            float tremor        = GetTotalMagnitude(ConditionType.Tremor);
            float chromaticShift= GetTotalMagnitude(ConditionType.ChromaticShift);

            // These would drive post-processing volume parameters at runtime
            // Implementation depends on project's URP/HDRP post-process setup
        }

        private IEnumerator RunPayloadCoroutine(ConditionType type, float magnitude)
        {
            switch (type)
            {
                case ConditionType.Bleed:
                    // Flash red overlay briefly
                    if (_bleedScreenFlash != null)
                    {
                        _bleedScreenFlash.color = new Color(1f, 0f, 0f, 0.3f * magnitude);
                        yield return new WaitForSeconds(0.3f);
                        _bleedScreenFlash.color = Color.clear;
                    }
                    break;

                case ConditionType.Tinnitus:
                    // Tinnitus handled in AudioManager via event — no visual payload
                    EventBus.Publish(new ConditionAppliedEvent
                    {
                        ConditionType = ConditionType.Tinnitus,
                        Duration      = 0f, // signal to AudioManager only
                        Magnitude     = magnitude
                    });
                    break;

                case ConditionType.PanicBreathing:
                    // Breathing audio cue — handled by AudioManager
                    break;
            }
        }

        // ─── Query Helpers ───────────────────────────────────────────────────

        public bool HasCondition(ConditionType type)
            => _activeConditions[type].Count > 0;

        public float GetTotalMagnitude(ConditionType type)
        {
            float total = 0f;
            foreach (var c in _activeConditions[type])
                total += c.Magnitude;
            return total;
        }

        public float GetSpeedMultiplier()
        {
            float mult = 1f;
            if (HasCondition(ConditionType.Limp))           mult *= (1f - GetTotalMagnitude(ConditionType.Limp) * 0.5f);
            if (HasCondition(ConditionType.StaminaDrain))   mult *= 0.85f;
            return Mathf.Clamp(mult, 0.2f, 1f);
        }

        public bool HasInputLag()    => HasCondition(ConditionType.InputLag);
        public bool HasTunnelVision()=> HasCondition(ConditionType.TunnelVision);
        public bool HasMuffledAudio()=> HasCondition(ConditionType.MuffledHearing);

        private int GetMaxStack(ConditionType type) => type switch
        {
            ConditionType.Bleed   => 3,
            ConditionType.Tremor  => 2,
            _ => 1
        };

        // ─── Event Handler ───────────────────────────────────────────────────

        private void OnConditionApplied(ConditionAppliedEvent evt)
        {
            var stackRule = GetDefaultStackRule(evt.ConditionType);
            ApplyCondition(evt.ConditionType, evt.Duration, evt.Magnitude, stackRule);
        }

        private StackRule GetDefaultStackRule(ConditionType type) => type switch
        {
            ConditionType.Bleed            => StackRule.Stack,
            ConditionType.Tinnitus         => StackRule.Replace,
            ConditionType.InputLag         => StackRule.Ignore,   // rare/light use only
            ConditionType.LouderFootsteps  => StackRule.Extend,
            _ => StackRule.Replace
        };

        // ─── Clear ───────────────────────────────────────────────────────────

        public void ClearAll()
        {
            foreach (var list in _activeConditions.Values)
                list.Clear();
        }

        public void ClearCondition(ConditionType type)
            => _activeConditions[type].Clear();

        // ─── Save/Load ───────────────────────────────────────────────────────

        public List<ConditionSaveEntry> ExportConditions()
        {
            var result = new List<ConditionSaveEntry>();
            foreach (var kv in _activeConditions)
                foreach (var inst in kv.Value)
                    result.Add(new ConditionSaveEntry
                    {
                        Type          = kv.Key,
                        RemainingTime = inst.RemainingTime,
                        Magnitude     = inst.Magnitude
                    });
            return result;
        }

        public void ImportConditions(List<ConditionSaveEntry> data)
        {
            ClearAll();
            foreach (var entry in data)
                ApplyCondition(entry.Type, entry.RemainingTime, entry.Magnitude, GetDefaultStackRule(entry.Type));
        }
    }

    // ─── Supporting Types ────────────────────────────────────────────────────

    public class ConditionInstance
    {
        public ConditionType Type;
        public float         RemainingTime;
        public float         Magnitude;

        public ConditionInstance(ConditionType type, float duration, float magnitude)
        {
            Type          = type;
            RemainingTime = duration;
            Magnitude     = magnitude;
        }
    }

    public enum StackRule { Replace, Extend, Stack, Ignore }

    [System.Serializable]
    public class ConditionSaveEntry
    {
        public ConditionType Type;
        public float         RemainingTime;
        public float         Magnitude;
    }
}

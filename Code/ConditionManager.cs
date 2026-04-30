using System;
using System.Collections.Generic;
using UnityEngine;

namespace Eidolon.Core
{
    // ─────────────────────────────────────────────────────────────────────────
    //  CONDITION EFFECT – one active runtime status
    // ─────────────────────────────────────────────────────────────────────────
    [System.Serializable]
    public class ConditionEffect : IConditionEffect
    {
        public string        EffectId      { get; private set; }
        public ConditionType Type;
        public float         Magnitude;        // 0–1
        public float         Duration;         // seconds; -1 = permanent
        public bool          Stackable;
        public string        SourceId;

        private float _elapsed;

        public bool IsExpired => Duration >= 0f && _elapsed >= Duration;

        public ConditionEffect(ConditionType type, float magnitude, float duration,
                               string sourceId, bool stackable = false)
        {
            EffectId  = $"{type}_{sourceId}_{Time.time}";
            Type      = type;
            Magnitude = Mathf.Clamp01(magnitude);
            Duration  = duration;
            SourceId  = sourceId;
            Stackable = stackable;
        }

        public void OnApply(Actors.PlayerController player)
        {
            EventBus.Publish(new EvConditionApplied(Type, Magnitude));
        }

        public void OnTick(float dt, Actors.PlayerController player)
        {
            _elapsed += dt;
            ApplyRuntimeEffects(dt, player);
        }

        public void OnRemove(Actors.PlayerController player)
        {
            EventBus.Publish(new EvConditionRemoved(Type));
        }

        private void ApplyRuntimeEffects(float dt, Actors.PlayerController player)
        {
            switch (Type)
            {
                case ConditionType.StaminaDrain:
                    player.DrainStamina(Magnitude * 8f * dt);
                    break;
                case ConditionType.Bleed:
                    player.TakeDamage(Magnitude * 2f * dt, "Bleed");
                    break;
                case ConditionType.LoudFootsteps:
                    // noise multiplier handled in PlayerController noise calculation
                    break;
                // Visual/audio effects handled by ConditionVisualRouter
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  CONDITION MANAGER
    // ─────────────────────────────────────────────────────────────────────────
    public class ConditionManager : MonoBehaviour, ISaveable
    {
        public static ConditionManager Instance { get; private set; }

        private Actors.PlayerController _player;
        private readonly List<ConditionEffect> _active = new();
        private readonly List<ConditionEffect> _toRemove = new();

        // ── Active query ───────────────────────────────────────────────────────
        public bool Has(ConditionType type)
            => _active.Exists(c => c.Type == type);

        public float GetMagnitude(ConditionType type)
        {
            float total = 0f;
            foreach (var c in _active)
                if (c.Type == type) total += c.Magnitude;
            return Mathf.Clamp01(total);
        }

        public IReadOnlyList<ConditionEffect> Active => _active;

        // ── Lifecycle ──────────────────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            _player = FindObjectOfType<Actors.PlayerController>();
        }

        private void Update()
        {
            if (_player == null) return;

            _toRemove.Clear();
            foreach (var c in _active)
            {
                c.OnTick(Time.deltaTime, _player);
                if (c.IsExpired) _toRemove.Add(c);
            }
            foreach (var c in _toRemove) Remove(c);
        }

        // ── Apply ──────────────────────────────────────────────────────────────
        public void Apply(ConditionEffect effect)
        {
            if (!effect.Stackable)
            {
                var existing = _active.Find(c => c.Type == effect.Type);
                if (existing != null)
                {
                    // refresh magnitude if higher
                    if (effect.Magnitude >= existing.Magnitude)
                    {
                        Remove(existing);
                    }
                    else return;
                }
            }

            _active.Add(effect);
            effect.OnApply(_player);
        }

        public void Apply(ConditionType type, float magnitude, float duration,
                          string sourceId, bool stackable = false)
            => Apply(new ConditionEffect(type, magnitude, duration, sourceId, stackable));

        // ── Remove ─────────────────────────────────────────────────────────────
        public void Remove(ConditionEffect effect)
        {
            if (_active.Remove(effect))
                effect.OnRemove(_player);
        }

        public void RemoveByType(ConditionType type)
        {
            _toRemove.Clear();
            foreach (var c in _active)
                if (c.Type == type) _toRemove.Add(c);
            foreach (var c in _toRemove) Remove(c);
        }

        public void RemoveBySource(string sourceId)
        {
            _toRemove.Clear();
            foreach (var c in _active)
                if (c.SourceId == sourceId) _toRemove.Add(c);
            foreach (var c in _toRemove) Remove(c);
        }

        public void ClearAll()
        {
            _toRemove.AddRange(_active);
            foreach (var c in _toRemove) Remove(c);
        }

        // ── ISaveable ──────────────────────────────────────────────────────────
        public string SaveKey => "Conditions";

        public object CaptureState()
        {
            var list = new List<ConditionSaveEntry>();
            foreach (var c in _active)
                list.Add(new ConditionSaveEntry
                {
                    Type      = c.Type,
                    Magnitude = c.Magnitude,
                    Duration  = c.Duration,
                    SourceId  = c.SourceId,
                    Stackable = c.Stackable
                });
            return list;
        }

        public void RestoreState(object raw)
        {
            if (raw is not List<ConditionSaveEntry> entries) return;
            ClearAll();
            foreach (var e in entries)
                Apply(e.Type, e.Magnitude, e.Duration, e.SourceId, e.Stackable);
        }
    }

    [System.Serializable]
    public class ConditionSaveEntry
    {
        public ConditionType Type;
        public float         Magnitude;
        public float         Duration;
        public string        SourceId;
        public bool          Stackable;
    }
}

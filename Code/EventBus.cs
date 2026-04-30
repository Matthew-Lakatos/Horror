using System;
using System.Collections.Generic;
using UnityEngine;

namespace Eidolon.Core
{
    /// <summary>
    /// Type-safe, zero-dependency event bus.
    /// Prefer this over UnityEvents or direct references for cross-system communication.
    /// </summary>
    public static class EventBus
    {
        // ── Internal registry ─────────────────────────────────────────────────
        private static readonly Dictionary<Type, List<Delegate>> _subscribers
            = new Dictionary<Type, List<Delegate>>();

        // ── Subscribe ─────────────────────────────────────────────────────────
        public static void Subscribe<T>(Action<T> handler)
        {
            var type = typeof(T);
            if (!_subscribers.TryGetValue(type, out var list))
            {
                list = new List<Delegate>();
                _subscribers[type] = list;
            }
            if (!list.Contains(handler))
                list.Add(handler);
        }

        // ── Unsubscribe ───────────────────────────────────────────────────────
        public static void Unsubscribe<T>(Action<T> handler)
        {
            var type = typeof(T);
            if (_subscribers.TryGetValue(type, out var list))
                list.Remove(handler);
        }

        // ── Publish ───────────────────────────────────────────────────────────
        public static void Publish<T>(T evt)
        {
            var type = typeof(T);
            if (!_subscribers.TryGetValue(type, out var list)) return;

            // iterate a copy – handlers may unsubscribe during dispatch
            var snapshot = new List<Delegate>(list);
            foreach (var d in snapshot)
            {
                try   { ((Action<T>)d).Invoke(evt); }
                catch (Exception ex)
                {
                    Debug.LogError($"[EventBus] Handler threw on {type.Name}: {ex}");
                }
            }
        }

        // ── Clear (use only between scenes) ───────────────────────────────────
        public static void ClearAll() => _subscribers.Clear();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  EVENT STRUCTS  (plain data, no behaviour)
    // ─────────────────────────────────────────────────────────────────────────

    public readonly struct EvTensionChanged
    {
        public readonly TensionState Prev;
        public readonly TensionState Next;
        public EvTensionChanged(TensionState p, TensionState n) { Prev = p; Next = n; }
    }

    public readonly struct EvPlayerNoise
    {
        public readonly Vector3 Origin;
        public readonly NoiseLevel Level;
        public EvPlayerNoise(Vector3 o, NoiseLevel l) { Origin = o; Level = l; }
    }

    public readonly struct EvPlayerDamaged
    {
        public readonly float Amount;
        public readonly string SourceId;
        public EvPlayerDamaged(float a, string s) { Amount = a; SourceId = s; }
    }

    public readonly struct EvPlayerDied { }

    public readonly struct EvConditionApplied
    {
        public readonly ConditionType Type;
        public readonly float Magnitude;
        public EvConditionApplied(ConditionType t, float m) { Type = t; Magnitude = m; }
    }

    public readonly struct EvConditionRemoved
    {
        public readonly ConditionType Type;
        public EvConditionRemoved(ConditionType t) { Type = t; }
    }

    public readonly struct EvRoomEntered
    {
        public readonly string RoomId;
        public EvRoomEntered(string r) { RoomId = r; }
    }

    public readonly struct EvObjectiveCompleted
    {
        public readonly string ObjectiveId;
        public EvObjectiveCompleted(string id) { ObjectiveId = id; }
    }

    public readonly struct EvObjectiveActivated
    {
        public readonly string ObjectiveId;
        public EvObjectiveActivated(string id) { ObjectiveId = id; }
    }

    public readonly struct EvFacilityActionTriggered
    {
        public readonly FacilityAction Action;
        public readonly string RoomId;
        public EvFacilityActionTriggered(FacilityAction a, string r) { Action = a; RoomId = r; }
    }

    public readonly struct EvPerceptionEvent
    {
        public readonly PerceptionEventType Type;
        public readonly float Intensity;    // 0–1
        public EvPerceptionEvent(PerceptionEventType t, float i) { Type = t; Intensity = i; }
    }

    public readonly struct EvEnemyStateChanged
    {
        public readonly string EnemyId;
        public readonly EnemyState Prev;
        public readonly EnemyState Next;
        public EvEnemyStateChanged(string id, EnemyState p, EnemyState n)
            { EnemyId = id; Prev = p; Next = n; }
    }

    public readonly struct EvGamePhaseChanged
    {
        public readonly GamePhase Phase;
        public EvGamePhaseChanged(GamePhase p) { Phase = p; }
    }

    public readonly struct EvSaveRequested { }
    public readonly struct EvLoadCompleted { }
}

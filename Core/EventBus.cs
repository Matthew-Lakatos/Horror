using System;
using System.Collections.Generic;
using UnityEngine;

namespace Eidolon.Core
{
    /// <summary>
    /// Type-safe, decoupled event bus for all inter-system communication.
    /// Subscribe, Unsubscribe, and Publish typed events without direct references.
    /// </summary>
    public static class EventBus
    {
        private static readonly Dictionary<Type, List<Delegate>> _handlers = new Dictionary<Type, List<Delegate>>();
        private static readonly List<(Type type, Delegate handler)> _pendingRemoval = new List<(Type, Delegate)>();
        private static bool _isDispatching;

        public static void Subscribe<T>(Action<T> handler) where T : struct
        {
            var type = typeof(T);
            if (!_handlers.ContainsKey(type))
                _handlers[type] = new List<Delegate>();

            if (!_handlers[type].Contains(handler))
                _handlers[type].Add(handler);
        }

        public static void Unsubscribe<T>(Action<T> handler) where T : struct
        {
            var type = typeof(T);
            if (_isDispatching)
            {
                _pendingRemoval.Add((type, handler));
                return;
            }
            if (_handlers.ContainsKey(type))
                _handlers[type].Remove(handler);
        }

        public static void Publish<T>(T eventData) where T : struct
        {
            var type = typeof(T);
            if (!_handlers.ContainsKey(type)) return;

            _isDispatching = true;
            var handlers = _handlers[type];
            for (int i = handlers.Count - 1; i >= 0; i--)
            {
                try
                {
                    (handlers[i] as Action<T>)?.Invoke(eventData);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[EventBus] Exception in handler for {type.Name}: {ex}");
                }
            }
            _isDispatching = false;

            // Flush pending removals
            foreach (var (t, h) in _pendingRemoval)
                if (_handlers.ContainsKey(t)) _handlers[t].Remove(h);
            _pendingRemoval.Clear();
        }

        public static void Clear()
        {
            _handlers.Clear();
            _pendingRemoval.Clear();
            _isDispatching = false;
        }
    }

    // ─── Event Structs ────────────────────────────────────────────────────────

    public struct PlayerNoiseEvent
    {
        public Vector3 Origin;
        public float Radius;
        public NoiseType Type;
    }

    public struct TensionStateChangedEvent
    {
        public TensionState Previous;
        public TensionState Current;
    }

    public struct PlayerDamagedEvent
    {
        public float Amount;
        public DamageSource Source;
        public Vector3 HitPoint;
    }

    public struct PlayerHealedEvent
    {
        public float Amount;
    }

    public struct RoomStateChangedEvent
    {
        public string RoomId;
        public RoomStateFlag ChangedFlag;
    }

    public struct DoorStateChangedEvent
    {
        public string DoorId;
        public bool IsLocked;
    }

    public struct ObjectiveCompletedEvent
    {
        public string ObjectiveId;
    }

    public struct ObjectiveActivatedEvent
    {
        public string ObjectiveId;
    }

    public struct EnemyDetectedPlayerEvent
    {
        public string EnemyId;
        public EnemyType Type;
        public Vector3 LastKnownPosition;
    }

    public struct EnemyLostPlayerEvent
    {
        public string EnemyId;
        public EnemyType Type;
    }

    public struct FacilityBrainActionEvent
    {
        public FacilityActionType ActionType;
        public string TargetId;
    }

    public struct PerceptionEffectEvent
    {
        public PerceptionEffectType EffectType;
        public float Intensity;
        public float Duration;
    }

    public struct ConditionAppliedEvent
    {
        public ConditionType ConditionType;
        public float Duration;
        public float Magnitude;
    }

    public struct SaveRequestEvent
    {
        public string Slot;
    }

    public struct GamePhaseChangedEvent
    {
        public GamePhase Previous;
        public GamePhase Current;
    }

    // ─── Enumerations ─────────────────────────────────────────────────────────

    public enum TensionState { Calm, Suspicion, Pressure, Panic, Recovery }
    public enum GamePhase    { EarlyGame, MidGame, LateGame }
    public enum NoiseType    { Footstep, Sprint, Wrench, Collision, Decoy, Scream, Trap }
    public enum DamageSource { BearTrap, RustyNail, BigE, Hound, Trapper, Nurse, StructuralFail, Environment }
    public enum EnemyType    { BigE, Hound, Trapper, Mimic, Nurse, ChildUnit, HollowMan, FacilityBrain }
    public enum RoomStateFlag{ Locked, Lit, Fogged, Heated, Visited, Safe, GeometryVariant, LabelCorrupted }
    public enum FacilityActionType { LockDoor, UnlockDoor, DimLights, RaiseFog, RaiseHeat, TriggerMachinery,
                                     SlowLift, DisableMedStation, SwapGeometry, CorruptLabel, ActivateSiren,
                                     FloodSteam, DisableFloodlights, CloseSkybridge, BlockTunnel }
    public enum PerceptionEffectType { PeripheralFigure, FalseFootstep, RoomLabelFlicker, UIDistort,
                                        ShadowFigure, DelayedSound, Tremor, ChromaticShift, IntrusiveText,
                                        FalseMonsterCue, VisualLag, Hallucination }
    public enum ConditionType { Blur, Tremor, Limp, Bleed, StaminaDrain, Tinnitus, TunnelVision,
                                 MuffledHearing, SlowedInteraction, LouderFootsteps, PanicBreathing,
                                 ChromaticShift, InputLag }
    public enum DifficultyLevel { Easy, Normal, Hard, Impossible }

    // ─── New Event: Key Item Acquired ────────────────────────────────────────
    // Fired when the player picks up any key item (keycard, token, elevator pass).
    // ElevatorController and FloorLockSystem listen to this.
 
    public struct KeyItemAcquiredEvent
    {
        public KeyItemType ItemType;
        public string      ItemId;     // matches KeyItem.ItemId for dedup
    }
 
    public struct ElevatorCalledEvent
    {
        public string ElevatorId;
        public int    TargetFloor;
        public bool   WasGranted;   // false = denied (no pass, floor locked)
    }
 
    // ─── New Enum: Key Item Types ────────────────────────────────────────────
 
    public enum KeyItemType
    {
        SecurityKeycard,    // Unlocks upper mezzanine floor button
        UtilityKey,         // Unlocks basement floor button
        OverrideToken,      // Opens specific locked terminal/door
        ElevatorPass,       // Required to use elevator at all (optional discovery)
        PhoneCharger        // Optional — enables Phone device
    }
}

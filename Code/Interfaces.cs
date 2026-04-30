using System.Collections.Generic;
using UnityEngine;

namespace Eidolon.Core
{
    // ─────────────────────────────────────────────────────────────────────────
    //  ENTITY
    // ─────────────────────────────────────────────────────────────────────────
    public interface IEntity
    {
        string EntityId       { get; }
        Transform Transform   { get; }
        bool IsAlive          { get; }
        void OnDeath();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  SENSOR
    // ─────────────────────────────────────────────────────────────────────────
    public interface ISensor
    {
        void Tick(float dt);
        bool HasDetected       { get; }
        float ConfidenceScore  { get; }   // 0–1
        Vector3 LastKnownPos   { get; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  AI ACTION
    // ─────────────────────────────────────────────────────────────────────────
    public interface IAIAction
    {
        string ActionName      { get; }
        float ScoreAction(AI.Blackboard bb);
        void  Execute(AI.Blackboard bb);
        bool  IsComplete(AI.Blackboard bb);
        void  Interrupt();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  CONDITION EFFECT
    // ─────────────────────────────────────────────────────────────────────────
    public interface IConditionEffect
    {
        string EffectId        { get; }
        bool   IsExpired       { get; }
        void   OnApply(PlayerController player);
        void   OnTick(float dt, PlayerController player);
        void   OnRemove(PlayerController player);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  OBJECTIVE
    // ─────────────────────────────────────────────────────────────────────────
    public interface IObjective
    {
        string ObjectiveId    { get; }
        string DisplayName    { get; }
        bool   IsComplete     { get; }
        bool   IsActive       { get; }
        void   Activate();
        void   Tick(float dt);
        float  Progress       { get; }   // 0–1
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  PACING OBSERVER – anything that cares about tension phase changes
    // ─────────────────────────────────────────────────────────────────────────
    public interface IPacingObserver
    {
        void OnTensionChanged(TensionState prev, TensionState next);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  SAVEABLE
    // ─────────────────────────────────────────────────────────────────────────
    public interface ISaveable
    {
        string SaveKey         { get; }
        object CaptureState();
        void   RestoreState(object state);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  FACILITY INTERACTIVE (doors, panels, lifts)
    // ─────────────────────────────────────────────────────────────────────────
    public interface IFacilityInteractive
    {
        string InteractiveId  { get; }
        bool   CanInteract    { get; }
        void   Interact(PlayerController player);
        void   ForceState(bool open);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  MOVEMENT PROVIDER
    // ─────────────────────────────────────────────────────────────────────────
    public interface IMovementProvider
    {
        float Speed            { get; }
        bool  IsGrounded       { get; }
        void  ApplySpeedModifier(float multiplier, string sourceId);
        void  RemoveSpeedModifier(string sourceId);
    }
}

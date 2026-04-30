namespace Eidolon.Core
{
    // ─────────────────────────────────────────────────────────────────────────
    //  PACING / TENSION
    // ─────────────────────────────────────────────────────────────────────────
    public enum TensionState
    {
        Calm        = 0,
        Suspicion   = 1,
        Pressure    = 2,
        Panic       = 3,
        Recovery    = 4
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  ROOM TYPES
    // ─────────────────────────────────────────────────────────────────────────
    public enum RoomType
    {
        Corridor,
        Lab,
        Storage,
        Safe,
        Medical,
        Control,
        Server,
        Maintenance,
        Lift,
        SealedSector,
        Exterior
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  CONDITION TYPES
    // ─────────────────────────────────────────────────────────────────────────
    public enum ConditionType
    {
        Blur,
        Tremor,
        Limp,
        Bleed,
        StaminaDrain,
        Tinnitus,
        TunnelVision,
        MuffledHearing,
        SlowedInteraction,
        InputLag,
        LoudFootsteps,
        PanicBreathing,
        ChromaticShift,
        Immobilised
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  ENEMY STATES
    // ─────────────────────────────────────────────────────────────────────────
    public enum EnemyState
    {
        Idle,
        Patrolling,
        Investigating,
        Watching,
        Stalking,
        Hunting,
        Chasing,
        Retreating,
        Dormant
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  MONSTER IDs
    // ─────────────────────────────────────────────────────────────────────────
    public enum MonsterType
    {
        BigE,
        IndustrialMachine,
        Trapper,
        Hound,
        Mimic,
        Nurse,
        ChildUnit,
        HollowMan
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  FACILITY ACTIONS
    // ─────────────────────────────────────────────────────────────────────────
    public enum FacilityAction
    {
        LockDoor,
        DimLights,
        FlickerLights,
        RaiseHeat,
        VentFog,
        TriggerMachinery,
        SlowLift,
        DisableStation,
        RerouteAccess,
        ShiftGeometry,
        MislabelRoom,
        SubtlePropChange,
        RestoreNormal
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  PERCEPTION EVENTS
    // ─────────────────────────────────────────────────────────────────────────
    public enum PerceptionEventType
    {
        PeripheralSighting,
        FalseFootstep,
        WrongRoomLabel,
        UIDistortion,
        ShadowFigure,
        DelayedSound,
        BlurPulse,
        ChromaticFlash,
        IntrusiveText,
        FalseMonsterCue,
        EntityHallucination,
        Tremor
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  OBJECTIVE TYPES
    // ─────────────────────────────────────────────────────────────────────────
    public enum ObjectiveType
    {
        Primary,
        Secondary,
        Optional
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  DIFFICULTY
    // ─────────────────────────────────────────────────────────────────────────
    public enum DifficultyLevel
    {
        Easy,
        Normal,
        Hard,
        Impossible
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GAME PHASE (progression)
    // ─────────────────────────────────────────────────────────────────────────
    public enum GamePhase
    {
        EarlyGame,
        MidGame,
        LateGame
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  NOISE LEVEL
    // ─────────────────────────────────────────────────────────────────────────
    public enum NoiseLevel
    {
        Silent,
        Quiet,
        Moderate,
        Loud,
        VeryLoud
    }
}

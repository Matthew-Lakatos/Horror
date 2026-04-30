# EIDOLON INDUSTRIAL COGNITIVE FACILITY
## Unity C# Architecture Reference  |  Version 2.0 – Final Canon

---

## FOLDER → FILE MAP

```
Scripts/
├── Core/
│   ├── Interfaces.cs           – IEntity, ISensor, IAIAction, IConditionEffect,
│   │                             IObjective, IPacingObserver, ISaveable,
│   │                             IFacilityInteractive, IMovementProvider
│   ├── Enums.cs                – All global enumerations
│   ├── EventBus.cs             – Type-safe static event bus + all event structs
│   ├── GameDirector.cs         – Pacing FSM, tension cycles, phase transitions
│   ├── FairnessValidator.cs    – Pre-event safety checks (MANDATORY)
│   ├── WorldStateManager.cs    – Room visits, route heatmap, noise history
│   ├── AIManager.cs            – Enemy registry, encounter budget, shared BB sync
│   └── ObjectiveManager.cs     – Layered objective stack, progress tracking
│
├── Actors/
│   ├── PlayerController.cs     – FP movement, health, stamina, flashlight,
│   │                             inventory, noise emission, speed modifiers
│   ├── EnemyActor.cs           – Universal enemy shell (modules + UtilityBrain)
│   ├── BigEController.cs       – Apex Watcher + all Big E actions
│   └── MonsterControllers.cs   – Hound, Trapper, Mimic, Nurse,
│                                 ChildUnit, HollowMan + their actions
│
├── AI/
│   └── AICore.cs               – Blackboard, UtilityBrain, SensorModule,
│                                 MemoryModule, MovementModule, PresenceModule
│
├── World/
│   ├── FacilityGraph.cs        – RoomNodeData SO, RoomNode, RoomEdge,
│   │                             FacilityGraph (BFS pathfinding)
│   ├── FacilityBrain.cs        – Strategic environment antagonist
│   ├── FacilityGraphManager.cs – Scene bridge: graph↔world positions,
│   │                             RoomAnchor, RoomVolume, FacilityBrain partial
│   ├── Interactables.cs        – ResourceManager, HealStation, PickableItem,
│   │                             DoorInteractive, FuseOverride, DecoyThrower
│   └── LoreManager.cs          – LoreEntry SO, LoreTerminal, LoreManager
│
├── Conditions/
│   └── ConditionManager.cs     – ConditionEffect, ConditionManager
│
├── Perception/
│   └── PerceptionManager.cs    – Hidden stress, perception events, hallucination pool
│
├── Objectives/
│   └── ObjectiveManager.cs     – (see Core/ObjectiveManager.cs – same file)
│
├── Audio/
│   └── AudioManager.cs         – Positional audio pool, occlusion, music layers,
│                                 AudioClipRegistry SO
│
├── UI/
│   ├── ConditionVisualRouter.cs – Post-processing driver (blur, tremor,
│   │                              vignette, chromatic, lens distortion)
│   └── LoreManager.cs          – LoreDisplayUI, HUDManager (co-located)
│
├── Save/
│   └── SaveManager.cs          – Binary serialisation, auto-save,
│                                 AutoRegisterSaveable helper
│
├── Debug/
│   └── DebugOverlayManager.cs  – F1 runtime overlay (all systems)
│
└── Data/
    ├── EnemyProfile.cs         – EnemyProfile ScriptableObject
    └── DifficultySettings.cs   – DifficultySettings SO + DifficultyManager
```

---

## SCRIPTABLEOBJECT ASSETS TO CREATE IN EDITOR

| Asset Class         | Path Suggestion                     | Notes                                  |
|---------------------|-------------------------------------|----------------------------------------|
| RoomNodeData        | Data/Rooms/Room_*.asset             | One per room. Wire DefaultExits.       |
| EnemyProfile        | Data/Enemies/Profile_BigE.asset     | One per monster type.                  |
| DifficultySettings  | Data/Difficulty/Easy/Normal/…       | Can use presets or custom.             |
| AudioClipRegistry   | Data/Audio/AudioRegistry.asset      | All clip IDs mapped here.              |
| LoreEntry           | Data/Lore/Log_*.asset               | Contradictions wired by EntryId.       |

---

## WIRING GUIDE – Scene Setup

### Manager GameObjects (all DontDestroyOnLoad or Scene root)
```
[Managers]
  ├── GameDirector
  ├── FairnessValidator
  ├── WorldStateManager
  ├── AIManager
  ├── ConditionManager
  ├── PerceptionManager
  ├── ObjectiveManager
  ├── AudioManager        ← assign Volume/AudioSource refs
  ├── SaveManager
  ├── DebugOverlayManager
  └── DifficultyManager

[World]
  ├── FacilityGraphManager ← assign RoomNodeData[], RoomAnchor[]
  └── FacilityBrain        ← assign FacilityGraphManager ref

[Player]
  └── PlayerController
       └── Camera Root
            └── Flashlight (Light)
       └── DecoyThrower (child)

[Enemies]            (one per monster)
  └── BigE_Prefab
       ├── EnemyActor        ← assign EnemyProfile SO
       ├── BigEController
       └── NavMeshAgent

[Rooms]              (per room)
  ├── RoomAnchor            ← set RoomId
  ├── RoomVolume (Trigger)  ← set RoomId
  ├── HealStation?
  ├── LoreTerminal?
  ├── DoorInteractive?
  └── FuseOverride?

[UI]
  ├── ConditionVisualRouter ← assign Volume, CameraRoot, HUD refs
  ├── HUDManager            ← assign sliders, labels
  └── LoreDisplayUI         ← assign panel, text refs
```

---

## SYSTEM INTEGRATION MAP

```
Player action
    │
    ├─► EvPlayerNoise ──────────► AIManager ──► Enemy Blackboards
    │                                        └► Hound wake
    │
    ├─► EvRoomEntered ─────────► WorldStateManager (visit count, heatmap)
    │                          └► FacilityBrain (strategic tick context)
    │                          └► PerceptionManager (safe room flag)
    │                          └► HUDManager (room label update)
    │
    ├─► EvPlayerDamaged ───────► FairnessValidator (death window)
    │                          └► PerceptionManager (stress += )
    │
    └─► EvPlayerDied ──────────► GameDirector (recovery phase, death window)
                               └► SaveManager (checkpoint)

GameDirector
    └─► EvTensionChanged ──────► AudioManager (music layers)
                               └► PerceptionManager (stress decay rate)
                               └─ All IPacingObserver implementors

FacilityBrain (strategic tick)
    ├─► EvFacilityActionTriggered ► DoorInteractive (lock)
    │                              └► HealStation (disable)
    └─► Graph mutations (edge blocking, room state)

Enemy detects player
    └─► UtilityBrain scores actions
         └─► FairnessValidator.Validate() BEFORE any lethal event
              ├─ Suppress → abort
              ├─ Downgrade → reduce severity
              └─ Allow → execute action + GameDirector.RegisterMajorEncounter()
```

---

## BIG E STATE FLOW

```
Dormant
  │ (phase unlocked, attention rises)
  ▼
Patrolling  ←──────────────────────────────────────────┐
  │                                                     │
  ├─[attention > 0.3] ──► Watching (Observe / Motionless)
  │                              │
  ├─[familiarity rises] ─► Stalking (Shadow / Intercept)
  │                              │
  ├─[room entered] ──────► Investigating (InspectRoom)
  │                              │
  ├─[hunger > 0.85,      ─► Chasing (Chase – rare)
  │  FairnessValidator.Allow]    │
  │                              │ (lost player / encounter over)
  └──────────────────────────────┤
                                 │
                          Retreating (Withdraw)
                                 │ (cooldown expires)
                                 └─► Patrolling
```

---

## ENEMY PHASE GATE TABLE

| Monster          | ActivationPhase  | Notes                                |
|------------------|------------------|--------------------------------------|
| BigE             | EarlyGame        | Distant only in early, enters mid.   |
| IndustrialMachine| EarlyGame        | Subtle early, aggressive late.       |
| Hound            | EarlyGame        | Triggered by noise from day one.     |
| Mimic            | EarlyGame        | Sparse lies from start.              |
| Trapper          | MidGame          | Needs route data to learn first.     |
| Nurse            | MidGame          | Activates when player first bleeds.  |
| ChildUnit        | LateGame         | Sparingly – max 1-2 appearances.     |
| HollowMan        | LateGame         | Extremely rare. 300s minimum gap.    |

---

## FAIRNESS RULES REFERENCE

| Rule                  | When triggered                              | Default response       |
|-----------------------|---------------------------------------------|------------------------|
| NoWarning             | Lethal event, no prior observation          | Suppress               |
| UninferableRisk       | Severe event, no telegraphing context       | Suppress               |
| NoCounterplay         | Any event, no player option exists          | Downgrade              |
| RepeatRoomPunish      | Same room punished within 60s              | Suppress               |
| TooSoonAfterDeath     | Lethal within 90s of last death            | Suppress               |
| StackedPunishment     | Severe within 30s of last damage           | Downgrade              |
| HighStressLethal      | Stress > 0.85 + lethal event               | Downgrade              |

---

## CONDITION EFFECT CROSS-REFERENCE

| ConditionType     | Visual Effect             | Gameplay Effect                    | Source             |
|-------------------|---------------------------|------------------------------------|---------------------|
| Blur              | DOF defocus               | Vision impaired                    | Big E presence      |
| Tremor            | Camera shake              | Aim disturbed                      | Bear trap, stress   |
| Limp              | Speed mod 0.6x            | Slower movement                    | Bear trap           |
| Bleed             | –                         | 2×mag hp/sec drain                 | Bear trap, wounds   |
| StaminaDrain      | –                         | 8×mag stamina/sec drain            | Heat, conditions    |
| Tinnitus          | High pitched SFX          | Masks audio cues                   | Bear trap, panic    |
| TunnelVision      | Heavy vignette            | Peripheral vision reduced          | High stress         |
| MuffledHearing    | Low-pass audio            | Harder to hear enemies             | –                   |
| SlowedInteraction | UI prompt delay           | Longer interact times              | –                   |
| InputLag          | timeScale 0.9x (brief)    | Subtle response delay              | RARE – high stress  |
| LoudFootsteps     | –                         | 1.6x noise radius                  | Bear trap, limp     |
| PanicBreathing    | Vignette pulse            | Audible breathing to enemies       | Chase, trauma       |
| ChromaticShift    | CA aberration             | Visual discomfort                  | Perception events   |
| Immobilised       | –                         | Zero movement for duration         | Bear trap (3s)      |

---

## DIFFICULTY SCALING MODEL

No health sponge difficulty. Intelligence and pressure scale, not numbers.

| Variable                     | Easy | Normal | Hard  | Impossible |
|------------------------------|------|--------|-------|------------|
| FamiliarityGainMultiplier    | 0.5x | 1.0x   | 1.6x  | 2.5x       |
| HungerGrowthMultiplier       | 0.6x | 1.0x   | 1.5x  | 2.5x       |
| MajorEncounterCooldown (s)   | 180  | 120    | 80    | 45         |
| TensionDurationMultiplier    | 1.4x | 1.0x   | 0.7x  | 0.5x       |
| WarningWindowSeconds         | 14   | 8      | 5     | 2          |
| TrapRouteLearnThreshold      | 6    | 4      | 3     | 2          |
| HealStationsDisabled         | No   | No     | No    | YES        |
| MonsterSynergyWeight         | 0.2  | 0.5    | 0.8   | 1.0        |

---

## SAVE SYSTEM – WHAT IS PERSISTED

- Objective completion states + progress floats
- Room visit counts + last visit timestamps
- Route heatmap (A→B transition counts)
- Player: health, stamina, battery, inventory item IDs
- Active conditions + remaining durations
- AI learned habits (Big E Familiarity, route familiarity)
- GameDirector: phase, tension state
- FacilityBrain: global hostility, active manipulations
- Lore: read entry ID set
- WorldStateManager: danger ratings per room

---

## DESIGN CONSTRAINTS ENFORCED BY CODE

1. **At least one viable path always exists** – FacilityBrain.TryLockDoor
   simulates blocking and verifies graph viability before committing.

2. **Safe rooms never repeatedly violated** – FacilityBrain.SafeToManipulate
   blocks manipulation of IsSafeRoom nodes in Early/Mid game.

3. **No unavoidable deaths** – All lethal events pass FairnessValidator.Validate().

4. **No permanent maximum tension** – GameDirector FSM always transitions
   through Recovery after Panic before returning to Calm.

5. **No hive mind** – AIManager syncs only 4 shared blackboard keys,
   all with natural staleness. Enemies make independent mistakes.

6. **No softlock from resources** – ResourceManager.EnsureMinimumResources
   injects emergency caches if critical items are fully depleted.

7. **Hollow Man max 1 per 300s, ChildUnit max 2 appearances per run** –
   Enforced by per-controller minimum interval timers.
```

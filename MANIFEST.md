# EIDOLON — Complete File & Class Manifest
# Unity C# Production Architecture
# Generated against: Ultimate Master Prompt V2 + Campus Expansion Addendum

===========================================================================
## FOLDER STRUCTURE
===========================================================================

Scripts/
├── Core/
│   ├── EventBus.cs
│   ├── GameDirector.cs
│   ├── FairnessValidator.cs
│   ├── WorldStateManager.cs
│   ├── AIManager.cs
│   ├── FacilityBrain.cs
│   └── ResourceManager.cs
│
├── AI/
│   ├── EnemyActor.cs
│   ├── UtilityBrain.cs
│   ├── BigEActor.cs
│   └── MonsterRoster.cs
│
├── Actors/
│   ├── PlayerController.cs
│   └── WrenchTool.cs
│
├── World/
│   ├── RoomNode.cs
│   ├── WorldObjects.cs        (DoorController, LiftController, MedStation,
│   │                           StructuralFloor, RustyNailHazard)
│   ├── CampusConnectors.cs    (SkybridgeController, YardZone,
│   │                           MaintenanceTunnelZone)
│   └── ItemCache.cs           (embedded in ResourceManager.cs)
│
├── Conditions/
│   └── ConditionManager.cs
│
├── Objectives/
│   └── ObjectiveManager.cs
│
├── Audio/
│   └── AudioManager.cs
│
├── Perception/
│   └── PerceptionManager.cs
│
├── Save/
│   └── SaveManager.cs
│
├── UI/
│   └── GameHUD.cs             (GameHUD, IntroSequenceController, LoreLogViewer)
│
├── Data/
│   └── ScriptableObjects.cs   (ThreatProfile, TensionProfile, DifficultySettings,
│                               AudioProfile, LoreLogData)
│
└── Debug/
    └── DebugOverlayManager.cs

===========================================================================
## CLASS REGISTRY
===========================================================================

### Core Systems (Scripts/Core/)

| Class                  | File                    | Purpose                                        |
|------------------------|-------------------------|------------------------------------------------|
| EventBus (static)      | EventBus.cs             | Type-safe global pub/sub messaging             |
| GameDirector           | GameDirector.cs         | Tension state machine, phase management        |
| FairnessValidator      | FairnessValidator.cs    | Pre-event fairness gate (MANDATORY)            |
| WorldStateManager      | WorldStateManager.cs    | Room graph, BFS pathfinding, visit heatmap     |
| AIManager              | AIManager.cs            | Enemy registry, typed tick loops               |
| FacilityBrain          | FacilityBrain.cs        | Environmental antagonist controller            |
| ResourceManager        | ResourceManager.cs      | Inventory, caches, lore logs                   |
| ObjectiveManager       | ObjectiveManager.cs     | Layered objective system (Primary/Secondary)   |
| SaveManager            | SaveManager.cs          | JSON save/load, full state persistence         |

### Event Structs (in EventBus.cs)

PlayerNoiseEvent, TensionStateChangedEvent, PlayerDamagedEvent,
PlayerHealedEvent, RoomStateChangedEvent, DoorStateChangedEvent,
ObjectiveCompletedEvent, ObjectiveActivatedEvent, EnemyDetectedPlayerEvent,
EnemyLostPlayerEvent, FacilityBrainActionEvent, PerceptionEffectEvent,
ConditionAppliedEvent, SaveRequestEvent, GamePhaseChangedEvent,
BatteryReplacedEvent, FairnessSuppressedEvent

### Enumerations (in EventBus.cs)

TensionState, GamePhase, NoiseType, DamageSource, EnemyType,
RoomStateFlag, FacilityActionType, PerceptionEffectType, ConditionType,
DifficultyLevel

### AI Systems (Scripts/AI/)

| Class                     | File              | Purpose                                       |
|---------------------------|-------------------|-----------------------------------------------|
| EnemyActor (base)         | EnemyActor.cs     | Composition root: sensor/move/memory/utility  |
| SharedBlackboard          | EnemyActor.cs     | Partial intel sharing (NOT hive mind)         |
| NavMeshMovementModule     | EnemyActor.cs     | IMovementModule via NavMeshAgent              |
| EnemyMemoryModule         | EnemyActor.cs     | IMemoryModule, habit position tracking        |
| VisionSensorModule        | EnemyActor.cs     | ISensorModule, FOV + raycast                  |
| HearingSensorModule       | EnemyActor.cs     | ISensorModule, noise event reactive           |
| EnemyPresenceModule       | EnemyActor.cs     | IPresenceModule, show/hide visuals            |
| EnemyActionExecutor       | EnemyActor.cs     | IActionExecutor pipeline                      |
| EnemyAction (abstract)    | EnemyActor.cs     | ScriptableObject action base with cooldown    |
| UtilityBrain              | UtilityBrain.cs   | Utility AI scorer/decision loop               |
| UtilityScorer (abstract)  | UtilityBrain.cs   | Pluggable scoring SO base                     |
| PlayerVisibleScorer       | UtilityBrain.cs   | SO scorer: is player visible?                 |
| DistanceToPlayerScorer    | UtilityBrain.cs   | SO scorer: proximity curve                    |
| TensionStateScorer        | UtilityBrain.cs   | SO scorer: tension state match                |
| PlayerInjuredScorer       | UtilityBrain.cs   | SO scorer: player injured bonus               |
| IsChasingScorer           | UtilityBrain.cs   | SO scorer: chase mode bonus                   |
| HasRecentNoiseScorer      | UtilityBrain.cs   | SO scorer: recent noise bonus                 |
| GamePhaseScorer           | UtilityBrain.cs   | SO scorer: phase gate                         |
| BigEActor                 | BigEActor.cs      | Apex Watcher: Attn/Conf/Hunger/Fam system     |
| HoundActor                | MonsterRoster.cs  | Noise-triggered fast pursuit                  |
| TrapperActor              | MonsterRoster.cs  | Strategic trap placement on hot routes        |
| TrapInstance              | MonsterRoster.cs  | Bear/noise trap with full trauma bundle       |
| MimicActor                | MonsterRoster.cs  | False audio cues, trust destruction           |
| NurseActor                | MonsterRoster.cs  | Injury hunter, med area patrol                |
| ChildUnitActor            | MonsterRoster.cs  | Emotional lure, moral discomfort              |
| HollowManActor            | MonsterRoster.cs  | Reflection-only, late game dread              |

### Interfaces (all in EnemyActor.cs)

ISensorModule, IMovementModule, IMemoryModule, IPresenceModule,
IActionExecutor, IWrenchInteractable (WrenchTool.cs)

### Actors (Scripts/Actors/)

| Class            | File                  | Purpose                                          |
|------------------|-----------------------|--------------------------------------------------|
| PlayerController | PlayerController.cs   | FPS: move/crouch/sprint, stamina, health, noise  |
| WrenchTool       | WrenchTool.cs         | Counter-agency tool: force doors, disable traps  |
| WrenchableDoor   | WrenchTool.cs         | DoorController + IWrenchInteractable             |
| MaintenancePanel | WrenchTool.cs         | Panel access point, reveals routes               |
| WrenchDisarmable | WrenchTool.cs         | TrapInstance + IWrenchInteractable               |

### World (Scripts/World/)

| Class                  | File                | Purpose                                        |
|------------------------|---------------------|------------------------------------------------|
| RoomNode               | RoomNode.cs         | Room graph node, all state flags               |
| DoorController         | WorldObjects.cs     | Lock/unlock, FacilityBrain override            |
| LiftController         | WorldObjects.cs     | Elevator, FacilityBrain slow debuff            |
| MedStation             | WorldObjects.cs     | Healing station, Nurse/Brain can disable       |
| StructuralFloor        | WorldObjects.cs     | Telegraphed weak floor, noise + stumble        |
| RustyNailHazard        | WorldObjects.cs     | Rare hazard: damage + bleed + limp             |
| SkybridgeController    | CampusConnectors.cs | Elevated connector, mid-cross lock, Big E spot |
| YardZone               | CampusConnectors.cs | Outdoor traversal, steam/siren/floodlights     |
| MaintenanceTunnelZone  | CampusConnectors.cs | Underground routes, claustrophobic acoustics   |
| ItemCache              | ResourceManager.cs  | World resource pickup                          |
| LoreLogPickup          | ResourceManager.cs  | Lore discovery trigger                         |

### Conditions (Scripts/Conditions/)

| Class               | File                 | Purpose                                    |
|---------------------|----------------------|--------------------------------------------|
| ConditionManager    | ConditionManager.cs  | Unified modular status effect system       |
| ConditionInstance   | ConditionManager.cs  | Runtime instance: type/magnitude/remaining |

### Perception (Scripts/Perception/)

| Class             | File                 | Purpose                                    |
|-------------------|----------------------|--------------------------------------------|
| PerceptionManager | PerceptionManager.cs | Hidden stress → visual/audio effect driver |

### Audio (Scripts/Audio/)

| Class        | File           | Purpose                                          |
|--------------|----------------|--------------------------------------------------|
| AudioManager | AudioManager.cs| Positional audio, occlusion, chase layers, dread |

### Data / ScriptableObjects (Scripts/Data/)

| Class            | File                | Menu Path                            |
|------------------|---------------------|--------------------------------------|
| ThreatProfile    | ScriptableObjects.cs| Eidolon/AI/ThreatProfile             |
| TensionProfile   | ScriptableObjects.cs| Eidolon/Director/TensionProfile      |
| DifficultySettings| ScriptableObjects.cs| Eidolon/Director/DifficultySettings  |
| AudioProfile     | ScriptableObjects.cs| Eidolon/Audio/AudioProfile           |
| LoreLogData      | ScriptableObjects.cs| Eidolon/Lore/LoreLog                 |
| ObjectiveData    | ObjectiveManager.cs | Eidolon/Objectives/ObjectiveData     |

### UI (Scripts/UI/)

| Class                   | File        | Purpose                               |
|-------------------------|-------------|---------------------------------------|
| GameHUD                 | GameHUD.cs  | Status bars, inventory, objective HUD |
| IntroSequenceController | GameHUD.cs  | 2-4 min intro: room→email→search→fade |
| LoreLogViewer           | GameHUD.cs  | Pausable in-game log reader           |

### Debug (Scripts/Debug/)

| Class                | File                   | Purpose                               |
|----------------------|------------------------|---------------------------------------|
| DebugOverlayManager  | DebugOverlayManager.cs | F1 runtime overlay, all required info |

### Save Data (serialised structs, embedded in respective files)

EidolonSaveData, PlayerSaveData, BigESaveData, RoomSaveData,
ObjectiveSaveEntry, ConditionSaveEntry, HeatmapEntry,
WrenchSaveData, ResourceSaveData

===========================================================================
## UNITY SCENE SETUP CHECKLIST
===========================================================================

### Required GameObjects (one per scene):

 [ ] GameDirector              — attach: GameDirector.cs, DifficultySettings SO
 [ ] FairnessValidator         — attach: FairnessValidator.cs
 [ ] WorldStateManager         — attach: WorldStateManager.cs, all RoomNode refs
 [ ] AIManager                 — attach: AIManager.cs
 [ ] FacilityBrain             — attach: FacilityBrain.cs
 [ ] ResourceManager           — attach: ResourceManager.cs
 [ ] ObjectiveManager          — attach: ObjectiveManager.cs, all ObjectiveData SOs
 [ ] SaveManager               — attach: SaveManager.cs
 [ ] AudioManager              — attach: AudioManager.cs, AudioSource children
 [ ] PerceptionManager         — attach: PerceptionManager.cs
 [ ] ConditionManager          — attach: ConditionManager.cs
 [ ] DebugOverlayManager       — attach: DebugOverlayManager.cs (editor only)

### Player GameObject:

 [ ] PlayerController          — requires CharacterController
 [ ] WrenchTool                — child or on same GO
 [ ] Camera child              — for look system
 [ ] Flashlight (Light)        — referenced in PlayerController

### Enemy GameObjects (per enemy):

 [ ] BigEActor                 — requires NavMeshAgent
     └── VisionSensorModule    — child or component
     └── HearingSensorModule   — component
     └── EnemyMemoryModule     — component
     └── UtilityBrain          — component (UtilityActionEntries in inspector)
     └── EnemyPresenceModule   — component (visuals reference)
     └── EnemyActionExecutor   — component
     └── NavMeshMovementModule — component

 [ ] Same pattern for: HoundActor, TrapperActor, MimicActor, NurseActor,
                        ChildUnitActor, HollowManActor

### Per-Room:

 [ ] RoomNode.cs on a trigger collider volume per room
     — set Building, RoomType, Exits, GeometryVariants

### ScriptableObjects to Create:

 [ ] TensionProfile × 5        (Calm, Suspicion, Pressure, Panic, Recovery)
 [ ] ThreatProfile × 7         (BigE, Hound, Trapper, Mimic, Nurse, Child, Hollow)
 [ ] DifficultySettings × 4    (Easy, Normal, Hard, Impossible)
 [ ] ObjectiveData × N         (all primary + secondary + optional directives)
 [ ] AudioProfile × N          (per room type)
 [ ] LoreLogData × N           (all lore entries)

===========================================================================
## DESIGN SPEC COMPLIANCE CHECKLIST
===========================================================================

 [x] Tension state machine: Calm→Suspicion→Pressure→Panic→Recovery cycle
 [x] FairnessValidator mandatory before ALL dangerous events
 [x] Big E: Attention / Confidence / Hunger / Familiarity variables
 [x] Big E: 70% observe / 20% manipulate / 10% attack split
 [x] Big E: Withdraw after major encounter, reappear later
 [x] FacilityBrain: always preserves at least one viable path (LockDoorSafely)
 [x] FacilityBrain: learns hot routes via heatmap, targets them
 [x] Geometry horror: CycleGeometryVariant, Skybridge wrong-exit
 [x] Phase-gated monster activation (Early/Mid/Late)
 [x] No visible sanity meter — hidden stress/trauma drives effects
 [x] PerceptionManager: rare memorable events > constant spam rule enforced
 [x] ConditionManager: one unified modular system (no per-condition scripts)
 [x] Bear trap: full trauma bundle (damage + limp + bleed + tinnitus + loud steps)
 [x] Wrench: noise cost + stamina drain + optional durability
 [x] Structural floor: visually telegraphed, no hidden cheap deaths
 [x] Rusty nail: rare hazard, never hidden attrition, always visible
 [x] Safe room: breachable max 2 times, late game only, controlled by FairnessValidator
 [x] Softlock prevention: ResourceManager.EnsureMinimumViability()
 [x] Hound: fast, noise-triggered, weak search patience
 [x] Mimic: rare believable lies only, max per phase
 [x] Nurse: hunts injured, patrols med areas, Impossible = med disabled
 [x] Child Unit: active late game only, vanishes on approach
 [x] Hollow Man: max 3 appearances per session, reflection surfaces
 [x] Campus: BuildingA/B/C, Yard, Skybridge, Tunnel all implemented
 [x] Debug overlay: F1 toggle, all mandatory fields shown
 [x] Save persists: objectives, rooms, conditions, AI habits, heatmap, hostility
 [x] Intro sequence: room→email→payout→Gongle search→laptop close→breath→line
 [x] Difficulty: Easy/Normal/Hard/Impossible (no health sponge, intelligence scaling)
 [x] Composition over inheritance: all systems use interfaces + components
 [x] ScriptableObjects for all data: ThreatProfile, TensionProfile, DifficultySettings
 [x] Event-driven messaging: EventBus (zero direct references between systems)
 [x] Object pooling: PerceptionManager figure pool, AudioManager positional pool
 [x] Tick-based AI: AIManager typed coroutine loops (no Update() abuse)
 [x] NavMesh pathfinding: NavMeshMovementModule on all enemies
 [x] Blackboard: SharedBlackboard (partial intel, not hive mind)
 [x] Audio-first: positional, occlusion, silence, chase layers, dread tones
 [x] Lore delivery: LoreLogData SO + LoreLogPickup world object + LoreLogViewer UI

===========================================================================
## NAMESPACE MAP
===========================================================================

Eidolon.Core          → EventBus, GameDirector, FairnessValidator,
                         WorldStateManager, AIManager, FacilityBrain,
                         ObjectiveManager, SaveManager, ResourceManager

Eidolon.AI            → EnemyActor, SharedBlackboard, all modules,
                         UtilityBrain, all Scorers, BigEActor,
                         HoundActor, TrapperActor, MimicActor, NurseActor,
                         ChildUnitActor, HollowManActor

Eidolon.Actors        → PlayerController, WrenchTool, WrenchableDoor,
                         MaintenancePanel

Eidolon.World         → RoomNode, DoorController, LiftController, MedStation,
                         StructuralFloor, RustyNailHazard, SkybridgeController,
                         YardZone, MaintenanceTunnelZone, ItemCache, LoreLogPickup

Eidolon.Conditions    → ConditionManager, ConditionInstance

Eidolon.Perception    → PerceptionManager

Eidolon.Audio         → AudioManager

Eidolon.Objectives    → ObjectiveManager, ObjectiveData, ObjectiveInstance

Eidolon.Save          → SaveManager, EidolonSaveData (all save structs)

Eidolon.UI            → GameHUD, IntroSequenceController, LoreLogViewer

Eidolon.Data          → ThreatProfile, TensionProfile, DifficultySettings,
                         AudioProfile, LoreLogData

Eidolon.Debug         → DebugOverlayManager

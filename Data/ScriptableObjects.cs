using UnityEngine;

namespace Eidolon.Data
{
    // ─────────────────────────────────────────────────────────────────────────
    // THREAT PROFILE — Per-enemy data config
    // Uniqueness of each enemy comes from this SO, not from hardcoded scripts.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ScriptableObject defining all tunable parameters for a given enemy type.
    /// Swap profiles to change behaviour. No code changes needed.
    /// </summary>
    [CreateAssetMenu(menuName = "Eidolon/AI/ThreatProfile", fileName = "ThreatProfile_New")]
    public class ThreatProfile : ScriptableObject
    {
        [Header("Identity")]
        public string   EnemyName;
        public Core.EnemyType EnemyType;

        [Header("Presence")]
        [Tooltip("Seconds before this enemy can appear again after hiding.")]
        public float PresenceCooldownDuration = 30f;

        [Tooltip("Maximum concurrent visible instances of this enemy.")]
        public int   MaxSimultaneousInstances = 1;

        [Header("Movement")]
        public float PatrolSpeed   = 2f;
        public float ChaseSpeed    = 5.5f;
        public float CrouchedSpeed = 1.2f;

        [Header("Sensor Ranges")]
        public float VisionRange       = 18f;
        public float VisionAngle       = 90f;
        public float HearingMultiplier = 1f;

        [Header("Decision Timing")]
        public float MinReactionDelay = 0.3f;
        public float MaxReactionDelay = 1.2f;

        [Header("Aggression")]
        [Range(0f, 1f)] public float BaseAggressionBias   = 0.5f;
        [Range(0f, 1f)] public float HungerEscalationRate = 0.005f;

        [Header("Memory")]
        public int   MaxMemoryEntries = 8;
        public float SightingMemoryDuration = 30f;
        public float NoiseMemoryDuration    = 15f;

        [Header("Encounter Pacing")]
        public float MinEncounterInterval   = 60f;
        public float MaxEncounterInterval   = 180f;

        [Header("Damage")]
        public float AttackDamage   = 30f;
        public float AttackRange    = 1.5f;
        public float AttackCooldown = 5f;

        [Header("Fairness")]
        [Tooltip("Always requires the FairnessValidator to pass before attacking.")]
        public bool  RequiresFairnessCheck = true;
        [Tooltip("Minimum warning time the player must have received before this enemy can deal lethal damage.")]
        public float MinWarningTimeRequired = 3f;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TENSION PROFILE — Per-state pacing parameters
    // ─────────────────────────────────────────────────────────────────────────

    [CreateAssetMenu(menuName = "Eidolon/Director/TensionProfile", fileName = "TensionProfile_Calm")]
    public class TensionProfile : ScriptableObject
    {
        [Header("Identity")]
        public Core.TensionState State;

        [Header("Encounter")]
        [Range(0f, 2f)]
        [Tooltip("Multiplier on base encounter frequency. 0 = no encounters, 2 = double frequency.")]
        public float EncounterFrequencyMultiplier = 1f;

        [Range(0f, 1f)]
        [Tooltip("How aggressively AI acts. 0 = observe only, 1 = full attack mode.")]
        public float AIAggressionMultiplier = 0.2f;

        [Header("Perception")]
        [Range(0f, 1f)]
        [Tooltip("Rate at which false audio/visual cues are generated.")]
        public float FalseCueRate = 0.05f;

        [Range(0f, 1f)]
        [Tooltip("How unstable the player's perception is (drives PerceptionManager effects).")]
        public float PerceptionInstability = 0f;

        [Header("Environment")]
        [Range(0f, 1f)]
        [Tooltip("Danger contribution FacilityBrain adds to rooms in this state.")]
        public float RoomHostility = 0.1f;

        [Header("Audio")]
        [Range(0f, 1f)]
        [Tooltip("Target volume for dread score layer.")]
        public float SoundtrackIntensity = 0.1f;

        [Header("Safety")]
        [Range(0f, 1f)]
        [Tooltip("1 = safe rooms fully reliable, 0 = no safe rooms.")]
        public float SafeRoomReliability = 1f;

        [Tooltip("Objective urgency text shown on HUD.")]
        public string ObjectiveUrgencyLabel = "";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DIFFICULTY SETTINGS
    // ─────────────────────────────────────────────────────────────────────────

    [CreateAssetMenu(menuName = "Eidolon/Director/DifficultySettings", fileName = "DifficultySettings_Normal")]
    public class DifficultySettings : ScriptableObject
    {
        [Header("Identity")]
        public Core.DifficultyLevel Level;
        public string DisplayName;

        [Header("AI Scaling")]
        [Range(0.5f, 2f)]
        [Tooltip("Multiplier on AI aggression. Easy < 1, Hard > 1.")]
        public float AggressionScale = 1f;

        [Range(0.5f, 2f)]
        [Tooltip("How fast enemy memory/habit learning occurs.")]
        public float LearningRateScale = 1f;

        [Range(0.5f, 2f)]
        [Tooltip("Scales warning time given before dangerous events.")]
        public float WarningTimeScale = 1f;

        [Header("Trapper")]
        [Range(0.3f, 2f)]
        [Tooltip("How aggressively Trapper places traps on hot routes.")]
        public float TrapFrequencyScale = 1f;

        [Header("Mimic")]
        [Range(0.3f, 2f)]
        [Tooltip("How often Mimic produces false cues.")]
        public float MimicFrequencyScale = 1f;

        [Header("Facility Brain")]
        [Range(0.5f, 2f)]
        [Tooltip("How quickly FacilityBrain escalates hostility.")]
        public float FacilityEscalationScale = 1f;

        [Header("Safe Rooms")]
        [Tooltip("On Impossible, med stations are deactivated.")]
        public bool HealingCentresDeactivated = false;

        [Header("Recovery")]
        [Range(0.3f, 2f)]
        [Tooltip("Shorter recovery windows on higher difficulties.")]
        public float RecoveryWindowScale = 1f;

        [Header("Pacing")]
        [Range(0.3f, 1.5f)]
        [Tooltip("Scales how long pressure cycles last. Easy = longer calm.")]
        public float PressureDurationScale = 1f;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AUDIO PROFILE — Per-room audio settings
    // ─────────────────────────────────────────────────────────────────────────

    [CreateAssetMenu(menuName = "Eidolon/Audio/AudioProfile", fileName = "AudioProfile_New")]
    public class AudioProfile : ScriptableObject
    {
        [Header("Identity")]
        public string ProfileName;

        [Header("Ambience")]
        public Audio.BuildingAmbienceType AmbienceType = Audio.BuildingAmbienceType.Interior;
        public AudioClip RoomAmbienceClip;
        [Range(0f, 1f)] public float AmbienceVolume = 0.5f;

        [Header("Acoustics")]
        [Tooltip("Simulates claustrophobic acoustics — higher reverb in tight spaces.")]
        [Range(0f, 1f)] public float ReverbStrength = 0.3f;

        [Tooltip("Occlusion amount for sounds passing through this room's walls.")]
        [Range(0f, 1f)] public float OcclusionStrength = 0.5f;

        [Header("Machinery")]
        public bool HasMachineryAmbience = false;
        public AudioClip MachineryLoopClip;
        [Range(0f, 1f)] public float MachineryVolume = 0.3f;

        [Header("Dread")]
        [Tooltip("Constant dread contribution from this room's audio design.")]
        [Range(0f, 1f)] public float BaseDreadContribution = 0f;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // LORE LOG DATA
    // ─────────────────────────────────────────────────────────────────────────

    [CreateAssetMenu(menuName = "Eidolon/Lore/LoreLog", fileName = "LoreLog_New")]
    public class LoreLogData : ScriptableObject
    {
        [Header("Identity")]
        public string LogId;
        public LoreLogType LogType;

        [Header("Content")]
        public string Title;
        [TextArea(3, 12)] public string Body;
        public string Author;
        public string Timestamp;

        [Header("Discovery")]
        public string DiscoveryRoomId;
        public bool   IsRedacted;
        [TextArea(1, 4)] public string RedactedVersion;
    }

    public enum LoreLogType
    {
        ResearchNote, EmployeeMessage, InspectorLog,
        CCTVCaption, ContradictoryReport, TestChamberLog, SealedDocument
    }
}

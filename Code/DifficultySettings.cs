using UnityEngine;

namespace Eidolon.Data
{
    using Core;

    // ─────────────────────────────────────────────────────────────────────────
    //  DIFFICULTY SETTINGS SO
    // ─────────────────────────────────────────────────────────────────────────
    [CreateAssetMenu(menuName = "Eidolon/Difficulty Settings", fileName = "DifficultySettings")]
    public class DifficultySettings : ScriptableObject
    {
        [Header("Identity")]
        public DifficultyLevel Level;

        [Header("AI Learning")]
        [Tooltip("How quickly Big E's Familiarity grows.")]
        public float FamiliarityGainMultiplier      = 1f;
        [Tooltip("How quickly Big E's Hunger rises without interaction.")]
        public float HungerGrowthMultiplier         = 1f;
        [Tooltip("Seconds between successive major encounters.")]
        public float MajorEncounterCooldownOverride = 120f;

        [Header("Pressure Windows")]
        [Tooltip("Multiplier on GameDirector tension duration. <1 = faster escalation.")]
        public float TensionDurationMultiplier      = 1f;
        [Tooltip("How many warning seconds before dangerous events.")]
        public float WarningWindowSeconds           = 8f;

        [Header("Deception")]
        [Tooltip("Mimic deception frequency multiplier.")]
        public float MimicFrequencyMultiplier       = 1f;
        [Tooltip("Probability multiplier on perception event firing.")]
        public float PerceptionEventMultiplier      = 1f;

        [Header("Trap Aggression")]
        [Tooltip("How early Trapper begins placing traps (visits threshold).")]
        public int   TrapRouteLearnThreshold        = 4;

        [Header("Synergy")]
        [Tooltip("How strongly monsters coordinate via shared blackboard.")]
        [Range(0f, 1f)]
        public float MonsterSynergyWeight           = 0.5f;

        [Header("Resources")]
        public bool  HealStationsDisabled           = false;
        [Tooltip("Multiplier on heal station heal amount.")]
        public float HealAmountMultiplier           = 1f;

        // ── Presets ────────────────────────────────────────────────────────────
        public static DifficultySettings Easy()
        {
            var s = CreateInstance<DifficultySettings>();
            s.Level                            = DifficultyLevel.Easy;
            s.FamiliarityGainMultiplier        = 0.5f;
            s.HungerGrowthMultiplier           = 0.6f;
            s.MajorEncounterCooldownOverride   = 180f;
            s.TensionDurationMultiplier        = 1.4f;
            s.WarningWindowSeconds             = 14f;
            s.MimicFrequencyMultiplier         = 0.5f;
            s.PerceptionEventMultiplier        = 0.5f;
            s.TrapRouteLearnThreshold          = 6;
            s.MonsterSynergyWeight             = 0.2f;
            s.HealAmountMultiplier             = 1.3f;
            return s;
        }

        public static DifficultySettings Normal()
        {
            var s = CreateInstance<DifficultySettings>();
            s.Level                            = DifficultyLevel.Normal;
            s.FamiliarityGainMultiplier        = 1f;
            s.HungerGrowthMultiplier           = 1f;
            s.MajorEncounterCooldownOverride   = 120f;
            s.TensionDurationMultiplier        = 1f;
            s.WarningWindowSeconds             = 8f;
            s.MimicFrequencyMultiplier         = 1f;
            s.PerceptionEventMultiplier        = 1f;
            s.TrapRouteLearnThreshold          = 4;
            s.MonsterSynergyWeight             = 0.5f;
            s.HealAmountMultiplier             = 1f;
            return s;
        }

        public static DifficultySettings Hard()
        {
            var s = CreateInstance<DifficultySettings>();
            s.Level                            = DifficultyLevel.Hard;
            s.FamiliarityGainMultiplier        = 1.6f;
            s.HungerGrowthMultiplier           = 1.5f;
            s.MajorEncounterCooldownOverride   = 80f;
            s.TensionDurationMultiplier        = 0.7f;
            s.WarningWindowSeconds             = 5f;
            s.MimicFrequencyMultiplier         = 1.6f;
            s.PerceptionEventMultiplier        = 1.5f;
            s.TrapRouteLearnThreshold          = 3;
            s.MonsterSynergyWeight             = 0.8f;
            s.HealAmountMultiplier             = 0.8f;
            return s;
        }

        public static DifficultySettings Impossible()
        {
            var s = CreateInstance<DifficultySettings>();
            s.Level                            = DifficultyLevel.Impossible;
            s.FamiliarityGainMultiplier        = 2.5f;
            s.HungerGrowthMultiplier           = 2.5f;
            s.MajorEncounterCooldownOverride   = 45f;
            s.TensionDurationMultiplier        = 0.5f;
            s.WarningWindowSeconds             = 2f;
            s.MimicFrequencyMultiplier         = 2.5f;
            s.PerceptionEventMultiplier        = 2f;
            s.TrapRouteLearnThreshold          = 2;
            s.MonsterSynergyWeight             = 1f;
            s.HealStationsDisabled             = true;
            s.HealAmountMultiplier             = 0f;
            return s;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  DIFFICULTY MANAGER
    // ─────────────────────────────────────────────────────────────────────────
    public class DifficultyManager : MonoBehaviour
    {
        public static DifficultyManager Instance { get; private set; }

        [SerializeField] private DifficultySettings _settings;

        public DifficultySettings Settings => _settings;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            if (_settings == null)
                _settings = DifficultySettings.Normal();

            ApplySettings();
        }

        public void SetDifficulty(DifficultyLevel level)
        {
            _settings = level switch
            {
                DifficultyLevel.Easy       => DifficultySettings.Easy(),
                DifficultyLevel.Hard       => DifficultySettings.Hard(),
                DifficultyLevel.Impossible => DifficultySettings.Impossible(),
                _                          => DifficultySettings.Normal()
            };
            ApplySettings();
        }

        private void ApplySettings()
        {
            if (_settings == null) return;

            // Heal stations
            if (_settings.HealStationsDisabled)
            {
                foreach (var hs in FindObjectsOfType<World.HealStation>())
                    hs.ForceState(false);
            }

            // Synergy weight propagated via AIManager accessor
            // BigE variables propagated via BigEController Start()
            // (All other multipliers are queried by each system at runtime via DifficultyManager.Instance.Settings)
        }
    }
}

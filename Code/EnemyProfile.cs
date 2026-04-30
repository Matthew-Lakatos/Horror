using UnityEngine;

namespace Eidolon.Data
{
    using Core;

    [CreateAssetMenu(menuName = "Eidolon/Enemy Profile", fileName = "EnemyProfile")]
    public class EnemyProfile : ScriptableObject
    {
        [Header("Identity")]
        public string      EnemyId;
        public MonsterType Type;
        public string      DisplayName;

        [Header("Phase Gate")]
        [Tooltip("This enemy only activates at or after this game phase.")]
        public GamePhase   ActivationPhase = GamePhase.EarlyGame;

        [Header("Sensing")]
        public float       HearingRadius    = 12f;
        public float       VisionRange      = 15f;
        public float       VisionAngle      = 80f;

        [Header("Movement")]
        public float       BaseSpeed        = 3.5f;
        public float       ChaseSpeed       = 5.5f;
        public float       StalkSpeed       = 1.8f;
        public float       StoppingDistance = 1.5f;

        [Header("Decision")]
        public float       DecisionTickRate = 0.5f;

        [Header("Memory")]
        public int         MemoryCapacity   = 16;
        public float       MemoryDecayTime  = 120f;

        [Header("Presence")]
        [Tooltip("How long between visible appearances to keep tension without overexposure.")]
        public float       PresenceCooldown = 45f;

        [Header("Lethality")]
        [Range(0f, 1f)]
        public float       LethalityBase    = 0.3f;    // chance of kill on contact vs stun/chase
        [Tooltip("How much damage on hit if non-lethal.")]
        public float       ContactDamage    = 30f;

        [Header("Utility Weights – override per monster")]
        [Tooltip("Relative urge to observe the player vs act.")]
        [Range(0f, 2f)]
        public float       ObserveWeight    = 1f;
        [Range(0f, 2f)]
        public float       StalksWeight     = 1f;
        [Range(0f, 2f)]
        public float       ChaseWeight      = 1f;
        [Range(0f, 2f)]
        public float       PatrolWeight     = 1f;

        [Header("Cooldowns")]
        public float       AttackCooldown   = 90f;
        public float       RetreatCooldown  = 120f;

        [Header("Audio")]
        public string      AmbientSoundId;
        public string      AlertSoundId;
        public string      ChaseSoundId;
    }
}

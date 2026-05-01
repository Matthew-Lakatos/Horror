using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using Eidolon.Core;
using Eidolon.Actors;

namespace Eidolon.Perception
{
    /// <summary>
    /// Manages the hidden stress/perception reliability system.
    /// No visible sanity meter. Effects driven by TraumaLevel and HiddenStress.
    /// Uses object pooling for hallucination/effect instances.
    /// Rules: rare memorable events > constant spam. Create hesitation, not disable play.
    /// </summary>
    public class PerceptionManager : MonoBehaviour
    {
        public static PerceptionManager Instance { get; private set; }

        [Header("Effect Thresholds (TraumaLevel 0-1)")]
        [SerializeField] private float _mildThreshold   = 0.2f;
        [SerializeField] private float _mediumThreshold = 0.5f;
        [SerializeField] private float _strongThreshold = 0.75f;

        [Header("Effect Cooldowns")]
        [SerializeField] private float _peripheralCooldown   = 12f;
        [SerializeField] private float _falseFootstepCooldown = 8f;
        [SerializeField] private float _labelFlickerCooldown  = 20f;
        [SerializeField] private float _hallucCooldown        = 60f;
        [SerializeField] private float _uiDistortCooldown     = 15f;
        [SerializeField] private float _intrusiveTextCooldown = 30f;
        [SerializeField] private float _maxEffectsPerMinute   = 4f;

        [Header("References")]
        [SerializeField] private GameObject       _peripheralFigurePrefab;
        [SerializeField] private GameObject       _shadowFigurePrefab;
        [SerializeField] private AudioSource      _falseAudioSource;
        [SerializeField] private AudioClip[]      _falseFootstepClips;
        [SerializeField] private UnityEngine.UI.Text _intrusiveTextUI;
        [SerializeField] private string[]         _intrusiveMessages;

        // ─── Runtime ────────────────────────────────────────────────────────

        private readonly Dictionary<PerceptionEffectType, float> _cooldowns
            = new Dictionary<PerceptionEffectType, float>();

        private float _effectsThisMinute;
        private float _minuteTimer;

        // Object pool for peripheral figures
        private ObjectPool<GameObject> _figurePool;

        // ─── Lifecycle ───────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _figurePool = new ObjectPool<GameObject>(
                createFunc:      () => Instantiate(_peripheralFigurePrefab),
                actionOnGet:     g  => g.SetActive(true),
                actionOnRelease: g  => g.SetActive(false),
                actionOnDestroy: Destroy,
                maxSize: 4
            );
        }

        private void OnEnable()  => EventBus.Subscribe<PerceptionEffectEvent>(OnExternalEffect);
        private void OnDisable() => EventBus.Unsubscribe<PerceptionEffectEvent>(OnExternalEffect);

        private void Update()
        {
            _minuteTimer += Time.deltaTime;
            if (_minuteTimer >= 60f) { _minuteTimer = 0f; _effectsThisMinute = 0f; }

            EvaluatePassiveEffects();
        }

        // ─── Passive Effect Evaluation ───────────────────────────────────────

        private void EvaluatePassiveEffects()
        {
            var player = PlayerController.Instance;
            if (player == null) return;

            float trauma = player.TraumaLevel;
            float stress = player.HiddenStress;

            // Budget check
            if (_effectsThisMinute >= _maxEffectsPerMinute) return;

            // ── Mild (trauma > 0.2) ───────────────────────────────────────
            if (trauma > _mildThreshold)
            {
                if (Random.value < 0.015f && CanTrigger(PerceptionEffectType.FalseFootstep, _falseFootstepCooldown))
                    TriggerFalseFootstep();

                if (Random.value < 0.005f && CanTrigger(PerceptionEffectType.RoomLabelFlicker, _labelFlickerCooldown))
                    TriggerLabelFlicker();
            }

            // ── Medium (trauma > 0.5) ─────────────────────────────────────
            if (trauma > _mediumThreshold)
            {
                if (Random.value < 0.008f && CanTrigger(PerceptionEffectType.PeripheralFigure, _peripheralCooldown))
                    StartCoroutine(SpawnPeripheralFigure(stress));

                if (Random.value < 0.006f && CanTrigger(PerceptionEffectType.ShadowFigure, _peripheralCooldown))
                    StartCoroutine(SpawnShadowFigure());
            }

            // ── Strong (trauma > 0.75) ────────────────────────────────────
            if (trauma > _strongThreshold)
            {
                if (Random.value < 0.004f && CanTrigger(PerceptionEffectType.UIDistort, _uiDistortCooldown))
                    StartCoroutine(UIDistortion(1.2f));

                if (Random.value < 0.003f && CanTrigger(PerceptionEffectType.IntrusiveText, _intrusiveTextCooldown))
                    StartCoroutine(ShowIntrusiveText());

                if (Random.value < 0.002f && CanTrigger(PerceptionEffectType.Hallucination, _hallucCooldown))
                    StartCoroutine(FullHallucination());
            }
        }

        // ─── Effect Implementations ──────────────────────────────────────────

        private void TriggerFalseFootstep()
        {
            RecordEffect(PerceptionEffectType.FalseFootstep);
            if (_falseAudioSource == null || _falseFootstepClips.Length == 0) return;
            var clip = _falseFootstepClips[Random.Range(0, _falseFootstepClips.Length)];
            _falseAudioSource.PlayOneShot(clip);
            Debug.Log("[Perception] False footstep triggered");
        }

        private void TriggerLabelFlicker()
        {
            RecordEffect(PerceptionEffectType.RoomLabelFlicker);
            EventBus.Publish(new PerceptionEffectEvent
            {
                EffectType = PerceptionEffectType.RoomLabelFlicker,
                Intensity  = 0.5f,
                Duration   = 1.5f
            });
        }

        private IEnumerator SpawnPeripheralFigure(float stress)
        {
            RecordEffect(PerceptionEffectType.PeripheralFigure);

            var player = PlayerController.Instance;
            if (player == null || _peripheralFigurePrefab == null) yield break;

            // Spawn at edge of player's peripheral vision
            float angle  = Random.value > 0.5f ? 70f : -70f;
            var   offset = Quaternion.Euler(0f, angle, 0f) * player.transform.forward * 8f;
            var   pos    = player.transform.position + offset;

            var figure = _figurePool.Get();
            figure.transform.position = pos;

            // Figure exists briefly then disappears
            float duration = Mathf.Lerp(0.3f, 1.2f, stress);
            yield return new WaitForSeconds(duration);
            _figurePool.Release(figure);
        }

        private IEnumerator SpawnShadowFigure()
        {
            RecordEffect(PerceptionEffectType.ShadowFigure);
            if (_shadowFigurePrefab == null) yield break;

            var player = PlayerController.Instance;
            if (player == null) yield break;

            var pos    = player.transform.position + player.transform.forward * 12f;
            var figure = Instantiate(_shadowFigurePrefab, pos, Quaternion.identity);

            yield return new WaitForSeconds(2f);
            Destroy(figure);
        }

        private IEnumerator UIDistortion(float duration)
        {
            RecordEffect(PerceptionEffectType.UIDistort);
            // Post-process: chromatic aberration, distortion
            EventBus.Publish(new PerceptionEffectEvent
            {
                EffectType = PerceptionEffectType.UIDistort,
                Intensity  = 0.7f,
                Duration   = duration
            });
            yield return new WaitForSeconds(duration);
            EventBus.Publish(new PerceptionEffectEvent
            {
                EffectType = PerceptionEffectType.UIDistort,
                Intensity  = 0f,
                Duration   = 0f
            });
        }

        private IEnumerator ShowIntrusiveText()
        {
            RecordEffect(PerceptionEffectType.IntrusiveText);
            if (_intrusiveTextUI == null || _intrusiveMessages.Length == 0) yield break;

            _intrusiveTextUI.text    = _intrusiveMessages[Random.Range(0, _intrusiveMessages.Length)];
            _intrusiveTextUI.enabled = true;

            yield return new WaitForSeconds(1.8f);

            _intrusiveTextUI.enabled = false;
        }

        private IEnumerator FullHallucination()
        {
            RecordEffect(PerceptionEffectType.Hallucination);
            EventBus.Publish(new PerceptionEffectEvent
            {
                EffectType = PerceptionEffectType.Hallucination,
                Intensity  = 1f,
                Duration   = 2.5f
            });
            yield return null;
        }

        // ─── External Event Handling ─────────────────────────────────────────

        private void OnExternalEffect(PerceptionEffectEvent evt)
        {
            switch (evt.EffectType)
            {
                case PerceptionEffectType.Hallucination:
                    StartCoroutine(UIDistortion(evt.Duration));
                    break;
                case PerceptionEffectType.ChromaticShift:
                    StartCoroutine(ChromaticShiftCoroutine(evt.Intensity, evt.Duration));
                    break;
                case PerceptionEffectType.DelayedSound:
                    StartCoroutine(DelayedSoundCue(evt.Duration));
                    break;
            }
        }

        private IEnumerator ChromaticShiftCoroutine(float intensity, float duration)
        {
            // Signal post-process volume
            EventBus.Publish(new ConditionAppliedEvent
            {
                ConditionType = Core.ConditionType.ChromaticShift,
                Duration      = duration,
                Magnitude     = intensity
            });
            yield return null;
        }

        private IEnumerator DelayedSoundCue(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (_falseAudioSource && _falseFootstepClips.Length > 0)
                _falseAudioSource.PlayOneShot(_falseFootstepClips[Random.Range(0, _falseFootstepClips.Length)]);
        }

        // ─── Helpers ────────────────────────────────────────────────────────

        private bool CanTrigger(PerceptionEffectType type, float cooldown)
        {
            if (!_cooldowns.TryGetValue(type, out float expiry)) return true;
            return Time.time >= expiry;
        }

        private void RecordEffect(PerceptionEffectType type)
        {
            float cd = type switch
            {
                PerceptionEffectType.PeripheralFigure   => _peripheralCooldown,
                PerceptionEffectType.FalseFootstep       => _falseFootstepCooldown,
                PerceptionEffectType.RoomLabelFlicker    => _labelFlickerCooldown,
                PerceptionEffectType.Hallucination       => _hallucCooldown,
                PerceptionEffectType.UIDistort           => _uiDistortCooldown,
                PerceptionEffectType.IntrusiveText       => _intrusiveTextCooldown,
                _ => 10f
            };
            _cooldowns[type]   = Time.time + cd;
            _effectsThisMinute++;
        }
    }
}

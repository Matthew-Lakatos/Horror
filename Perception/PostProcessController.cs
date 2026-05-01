using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal; // Change to HighDefinition if using HDRP
using Eidolon.Core;
using Eidolon.Actors;

namespace Eidolon.Perception
{
    /// <summary>
    /// Drives URP post-process Volume parameters from ConditionManager
    /// and PlayerController stress/trauma values each frame.
    /// </summary>
    public class PostProcessController : MonoBehaviour
    {
        [SerializeField] private Volume _volume;

        private ChromaticAberration _chromatic;
        private Vignette            _vignette;
        private LensDistortion      _lensDistortion;

        private void Awake()
        {
            if (_volume == null) _volume = GetComponent<Volume>();

            _volume.profile.TryGet(out _chromatic);
            _volume.profile.TryGet(out _vignette);
            _volume.profile.TryGet(out _lensDistortion);
        }

        private void OnEnable()
        {
            EventBus.Subscribe<PerceptionEffectEvent>(OnPerceptionEffect);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<PerceptionEffectEvent>(OnPerceptionEffect);
        }

        private void Update()
        {
            var player   = PlayerController.Instance;
            var condMgr  = Conditions.ConditionManager.Instance;
            if (player == null || condMgr == null) return;

            float trauma = player.TraumaLevel;
            float stress = player.HiddenStress;

            // Chromatic aberration — driven by trauma + ChromaticShift condition
            float chromaTarget = Mathf.Clamp01(trauma * 0.4f
                + condMgr.GetTotalMagnitude(ConditionType.ChromaticShift));
            if (_chromatic != null)
                _chromatic.intensity.value = Mathf.Lerp(
                    _chromatic.intensity.value, chromaTarget, 3f * Time.deltaTime);

            // Vignette — driven by stress
            if (_vignette != null)
                _vignette.intensity.value = Mathf.Lerp(
                    _vignette.intensity.value,
                    Mathf.Lerp(0.2f, 0.55f, stress),
                    2f * Time.deltaTime);

            // Lens distortion — driven by Blur condition
            float blurTarget = condMgr.GetTotalMagnitude(ConditionType.Blur) * -0.3f;
            if (_lensDistortion != null)
                _lensDistortion.intensity.value = Mathf.Lerp(
                    _lensDistortion.intensity.value, blurTarget, 4f * Time.deltaTime);
        }

        private void OnPerceptionEffect(PerceptionEffectEvent evt)
        {
            if (evt.EffectType == PerceptionEffectType.ChromaticShift)
                StartCoroutine(ChromaticSpike(evt.Intensity, evt.Duration));
        }

        private System.Collections.IEnumerator ChromaticSpike(float intensity, float duration)
        {
            if (_chromatic == null) yield break;
            _chromatic.intensity.value = intensity;
            yield return new WaitForSeconds(duration);
            // Update() will smoothly bring it back down
        }
    }
}

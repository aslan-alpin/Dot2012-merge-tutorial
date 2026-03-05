using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VRCombat.Player
{
    [DisallowMultipleComponent]
    public class DamageVignetteFeedback : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("How strong the red vignette should be on hit.")]
        float m_FlashIntensity = 0.45f;

        [SerializeField]
        [Tooltip("Duration in seconds for the vignette fade-out.")]
        float m_FadeDuration = 0.22f;

        [SerializeField]
        Color m_VignetteColor = new Color(1f, 0f, 0f, 1f);

        Volume m_RuntimeVolume;
        Vignette m_Vignette;
        Coroutine m_FlashRoutine;

        void Awake()
        {
            EnsureVignetteOverride();
        }

        void OnDestroy()
        {
            if (m_RuntimeVolume != null)
                Destroy(m_RuntimeVolume.gameObject);
        }

        public void PlayFlash()
        {
            EnsureVignetteOverride();
            if (m_Vignette == null)
                return;

            if (m_FlashRoutine != null)
                StopCoroutine(m_FlashRoutine);

            m_FlashRoutine = StartCoroutine(FlashRoutine());
        }

        void EnsureVignetteOverride()
        {
            if (m_Vignette != null)
                return;

            var volumeObject = new GameObject("Damage Vignette Volume");
            m_RuntimeVolume = volumeObject.AddComponent<Volume>();
            m_RuntimeVolume.isGlobal = true;
            m_RuntimeVolume.priority = 200f;
            m_RuntimeVolume.weight = 1f;
            m_RuntimeVolume.sharedProfile = ScriptableObject.CreateInstance<VolumeProfile>();

            if (!m_RuntimeVolume.sharedProfile.TryGet(out m_Vignette))
                m_Vignette = m_RuntimeVolume.sharedProfile.Add<Vignette>(true);

            m_Vignette.active = true;
            m_Vignette.intensity.overrideState = true;
            m_Vignette.intensity.value = 0f;
            m_Vignette.smoothness.overrideState = true;
            m_Vignette.smoothness.value = 1f;
            m_Vignette.rounded.overrideState = true;
            m_Vignette.rounded.value = true;
            m_Vignette.color.overrideState = true;
            m_Vignette.color.value = m_VignetteColor;
        }

        IEnumerator FlashRoutine()
        {
            m_Vignette.intensity.value = m_FlashIntensity;

            var elapsed = 0f;
            while (elapsed < m_FadeDuration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / Mathf.Max(m_FadeDuration, 0.0001f));
                m_Vignette.intensity.value = Mathf.Lerp(m_FlashIntensity, 0f, t);
                yield return null;
            }

            m_Vignette.intensity.value = 0f;
            m_FlashRoutine = null;
        }
    }
}

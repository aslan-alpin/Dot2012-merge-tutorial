using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace VRCombat.UI
{
    [DisallowMultipleComponent]
    public class CombatHUDRuntime : MonoBehaviour
    {
        Text m_HealthText;
        Text m_WaveText;
        Text m_BannerText;
        GameObject m_DeathPanel;
        Text m_DeathText;
        Button m_RestartButton;
        GameObject m_PausePanel;
        Slider m_MovementVignetteSlider;
        Text m_MovementVignetteValueText;
        Button m_MenuResumeButton;
        Button m_MenuRestartButton;
        Button m_MenuQuitButton;
        Image m_FadeOverlay;
        Coroutine m_BannerRoutine;
        Coroutine m_FadeRoutine;
        Camera m_ViewCamera;
        Transform m_ViewTransform;
        Transform m_CanvasTransform;
        Action<float> m_OnMovementVignetteChanged;
        Action m_OnResumeRequested;

        [SerializeField]
        float m_HudDistance = 1.05f;

        [SerializeField]
        float m_HudVerticalOffset = -0.1f;

        [SerializeField]
        float m_HudScale = 0.00145f;

        public bool IsPauseMenuVisible => m_PausePanel != null && m_PausePanel.activeSelf;

        public void Initialize(
            Action restartAction,
            Action quitAction,
            Action resumeAction,
            Action<float> movementVignetteChangedAction,
            float initialMovementVignetteStrength,
            Camera viewCamera,
            Transform viewTransform)
        {
            m_OnMovementVignetteChanged = movementVignetteChangedAction;
            m_OnResumeRequested = resumeAction;
            m_ViewCamera = viewCamera;
            m_ViewTransform = viewTransform;
            BuildUi(restartAction, quitAction);
            SetMovementVignetteStrength(initialMovementVignetteStrength, notify: false);
        }

        public void SetViewAnchor(Camera viewCamera, Transform viewTransform)
        {
            m_ViewCamera = viewCamera;
            m_ViewTransform = viewTransform;
            RefreshCanvasCamera();
            LateUpdate();
        }

        void LateUpdate()
        {
            if (m_ViewTransform == null || m_CanvasTransform == null)
                return;

            var forward = Vector3.ProjectOnPlane(m_ViewTransform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f)
                forward = m_ViewTransform.forward;

            var targetPosition = m_ViewTransform.position + forward * m_HudDistance + Vector3.up * m_HudVerticalOffset;
            var targetRotation = Quaternion.LookRotation(forward, Vector3.up);

            m_CanvasTransform.SetPositionAndRotation(targetPosition, targetRotation);
        }

        public void SetHealth(float currentHealth, float maxHealth)
        {
            if (m_HealthText == null)
                return;

            m_HealthText.text = $"HP: {Mathf.CeilToInt(currentHealth)}/{Mathf.CeilToInt(maxHealth)}";
        }

        public void SetWaveInfo(int currentWave, int enemiesRemaining, float flowRate)
        {
            if (m_WaveText == null)
                return;

            m_WaveText.text = $"Wave {currentWave} | Remaining: {enemiesRemaining} | Flow: {flowRate:0.00}/s";
        }

        public void ShowBanner(string message, float seconds)
        {
            if (m_BannerText == null)
                return;

            if (m_BannerRoutine != null)
                StopCoroutine(m_BannerRoutine);

            m_BannerRoutine = StartCoroutine(BannerRoutine(message, seconds));
        }

        public void ShowDeathPanel(int currentWave)
        {
            if (m_DeathPanel == null || m_DeathText == null)
                return;

            m_DeathText.text = $"You died on Wave {currentWave}";
            m_DeathPanel.SetActive(true);
        }

        public void HideDeathPanel()
        {
            if (m_DeathPanel != null)
                m_DeathPanel.SetActive(false);
        }

        public void SetPauseMenuVisible(bool visible)
        {
            if (m_PausePanel == null)
                return;

            m_PausePanel.SetActive(visible);
        }

        public void SetMovementVignetteStrength(float value, bool notify)
        {
            var clampedValue = Mathf.Clamp01(value);
            if (m_MovementVignetteSlider != null)
            {
                if (notify)
                {
                    if (!Mathf.Approximately(m_MovementVignetteSlider.value, clampedValue))
                        m_MovementVignetteSlider.value = clampedValue;
                }
                else
                    m_MovementVignetteSlider.SetValueWithoutNotify(clampedValue);
            }

            if (m_MovementVignetteValueText != null)
                m_MovementVignetteValueText.text = $"{Mathf.RoundToInt(clampedValue * 100f)}%";

            if (notify)
                m_OnMovementVignetteChanged?.Invoke(clampedValue);
        }

        public void FadeToBlack(float duration)
        {
            if (m_FadeOverlay == null)
                return;

            if (m_FadeRoutine != null)
                StopCoroutine(m_FadeRoutine);

            m_FadeRoutine = StartCoroutine(FadeOverlayRoutine(Mathf.Max(0.01f, duration)));
        }

        public void ResetForRestart()
        {
            if (m_BannerRoutine != null)
            {
                StopCoroutine(m_BannerRoutine);
                m_BannerRoutine = null;
            }

            if (m_FadeRoutine != null)
            {
                StopCoroutine(m_FadeRoutine);
                m_FadeRoutine = null;
            }

            if (m_BannerText != null)
                m_BannerText.enabled = false;

            HideDeathPanel();
            SetPauseMenuVisible(false);
            SetFadeOverlayAlpha(0f);
        }

        IEnumerator BannerRoutine(string message, float seconds)
        {
            m_BannerText.text = message;
            m_BannerText.enabled = true;
            yield return new WaitForSecondsRealtime(seconds);
            m_BannerText.enabled = false;
            m_BannerRoutine = null;
        }

        IEnumerator FadeOverlayRoutine(float duration)
        {
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                SetFadeOverlayAlpha(Mathf.Lerp(0f, 0.92f, t));
                yield return null;
            }

            SetFadeOverlayAlpha(0.92f);
            m_FadeRoutine = null;
        }

        void BuildUi(Action restartAction, Action quitAction)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            var canvasObject = new GameObject("Combat HUD Canvas");
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = m_ViewCamera;
            canvas.sortingOrder = 200;

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.matchWidthOrHeight = 0.5f;

            canvasObject.AddComponent<GraphicRaycaster>();
            canvasObject.AddComponent<TrackedDeviceGraphicRaycaster>();

            var canvasRect = canvas.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(1400f, 700f);
            m_CanvasTransform = canvasObject.transform;
            m_CanvasTransform.localScale = Vector3.one * m_HudScale;
            LateUpdate();

            m_HealthText = CreateText("HealthText", canvasObject.transform, font, 38, TextAnchor.UpperLeft);
            var healthRect = m_HealthText.rectTransform;
            healthRect.anchorMin = new Vector2(0.5f, 1f);
            healthRect.anchorMax = new Vector2(0.5f, 1f);
            healthRect.pivot = new Vector2(0.5f, 1f);
            healthRect.anchoredPosition = new Vector2(-250f, -20f);
            healthRect.sizeDelta = new Vector2(460f, 70f);

            m_WaveText = CreateText("WaveText", canvasObject.transform, font, 32, TextAnchor.UpperLeft);
            var waveRect = m_WaveText.rectTransform;
            waveRect.anchorMin = new Vector2(0.5f, 1f);
            waveRect.anchorMax = new Vector2(0.5f, 1f);
            waveRect.pivot = new Vector2(0.5f, 1f);
            waveRect.anchoredPosition = new Vector2(260f, -20f);
            waveRect.sizeDelta = new Vector2(780f, 70f);

            m_BannerText = CreateText("BannerText", canvasObject.transform, font, 52, TextAnchor.MiddleCenter);
            m_BannerText.enabled = false;
            var bannerRect = m_BannerText.rectTransform;
            bannerRect.anchorMin = new Vector2(0.5f, 0.5f);
            bannerRect.anchorMax = new Vector2(0.5f, 0.5f);
            bannerRect.pivot = new Vector2(0.5f, 0.5f);
            bannerRect.anchoredPosition = new Vector2(0f, 65f);
            bannerRect.sizeDelta = new Vector2(1280f, 180f);

            var fadeObject = new GameObject("FadeOverlay", typeof(RectTransform), typeof(Image));
            fadeObject.transform.SetParent(canvasObject.transform, false);
            m_FadeOverlay = fadeObject.GetComponent<Image>();
            m_FadeOverlay.raycastTarget = false;
            m_FadeOverlay.color = new Color(0f, 0f, 0f, 0f);

            var fadeRect = m_FadeOverlay.rectTransform;
            fadeRect.anchorMin = Vector2.zero;
            fadeRect.anchorMax = Vector2.one;
            fadeRect.offsetMin = Vector2.zero;
            fadeRect.offsetMax = Vector2.zero;
            m_FadeOverlay.transform.SetAsFirstSibling();

            m_DeathPanel = new GameObject("DeathPanel", typeof(RectTransform), typeof(Image));
            m_DeathPanel.transform.SetParent(canvasObject.transform, false);
            var deathPanelRect = m_DeathPanel.GetComponent<RectTransform>();
            deathPanelRect.anchorMin = new Vector2(0.5f, 0.5f);
            deathPanelRect.anchorMax = new Vector2(0.5f, 0.5f);
            deathPanelRect.pivot = new Vector2(0.5f, 0.5f);
            deathPanelRect.sizeDelta = new Vector2(720f, 360f);

            var panelImage = m_DeathPanel.GetComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.82f);

            m_DeathText = CreateText("DeathText", m_DeathPanel.transform, font, 52, TextAnchor.MiddleCenter);
            var deathTextRect = m_DeathText.rectTransform;
            deathTextRect.anchorMin = new Vector2(0.5f, 0.74f);
            deathTextRect.anchorMax = new Vector2(0.5f, 0.74f);
            deathTextRect.pivot = new Vector2(0.5f, 0.5f);
            deathTextRect.sizeDelta = new Vector2(640f, 150f);

            var buttonObject = new GameObject("RestartButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(m_DeathPanel.transform, false);
            var buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.5f, 0.3f);
            buttonRect.anchorMax = new Vector2(0.5f, 0.3f);
            buttonRect.pivot = new Vector2(0.5f, 0.5f);
            buttonRect.sizeDelta = new Vector2(300f, 96f);

            var buttonImage = buttonObject.GetComponent<Image>();
            buttonImage.color = new Color(0.85f, 0.15f, 0.15f, 0.95f);

            m_RestartButton = buttonObject.GetComponent<Button>();
            m_RestartButton.targetGraphic = buttonImage;
            m_RestartButton.onClick.AddListener(() => restartAction?.Invoke());

            var buttonText = CreateText("RestartButtonText", buttonObject.transform, font, 42, TextAnchor.MiddleCenter);
            buttonText.text = "Restart";
            var buttonTextRect = buttonText.rectTransform;
            buttonTextRect.anchorMin = Vector2.zero;
            buttonTextRect.anchorMax = Vector2.one;
            buttonTextRect.offsetMin = Vector2.zero;
            buttonTextRect.offsetMax = Vector2.zero;

            m_PausePanel = new GameObject("PausePanel", typeof(RectTransform), typeof(Image));
            m_PausePanel.transform.SetParent(canvasObject.transform, false);
            var pausePanelRect = m_PausePanel.GetComponent<RectTransform>();
            pausePanelRect.anchorMin = new Vector2(0.5f, 0.5f);
            pausePanelRect.anchorMax = new Vector2(0.5f, 0.5f);
            pausePanelRect.pivot = new Vector2(0.5f, 0.5f);
            pausePanelRect.sizeDelta = new Vector2(760f, 460f);

            var pausePanelImage = m_PausePanel.GetComponent<Image>();
            pausePanelImage.color = new Color(0f, 0f, 0f, 0.88f);

            var pauseTitle = CreateText("PauseTitle", m_PausePanel.transform, font, 54, TextAnchor.MiddleCenter);
            pauseTitle.text = "Menu";
            var pauseTitleRect = pauseTitle.rectTransform;
            pauseTitleRect.anchorMin = new Vector2(0.5f, 0.88f);
            pauseTitleRect.anchorMax = new Vector2(0.5f, 0.88f);
            pauseTitleRect.pivot = new Vector2(0.5f, 0.5f);
            pauseTitleRect.sizeDelta = new Vector2(500f, 80f);

            var vignetteLabel = CreateText("MovementVignetteLabel", m_PausePanel.transform, font, 34, TextAnchor.MiddleLeft);
            vignetteLabel.text = "Movement Vignette";
            var vignetteLabelRect = vignetteLabel.rectTransform;
            vignetteLabelRect.anchorMin = new Vector2(0.5f, 0.66f);
            vignetteLabelRect.anchorMax = new Vector2(0.5f, 0.66f);
            vignetteLabelRect.pivot = new Vector2(0.5f, 0.5f);
            vignetteLabelRect.anchoredPosition = new Vector2(-110f, 0f);
            vignetteLabelRect.sizeDelta = new Vector2(420f, 64f);

            m_MovementVignetteValueText = CreateText("MovementVignetteValue", m_PausePanel.transform, font, 32, TextAnchor.MiddleRight);
            var movementVignetteValueRect = m_MovementVignetteValueText.rectTransform;
            movementVignetteValueRect.anchorMin = new Vector2(0.5f, 0.66f);
            movementVignetteValueRect.anchorMax = new Vector2(0.5f, 0.66f);
            movementVignetteValueRect.pivot = new Vector2(0.5f, 0.5f);
            movementVignetteValueRect.anchoredPosition = new Vector2(210f, 0f);
            movementVignetteValueRect.sizeDelta = new Vector2(180f, 64f);

            var sliderObject = new GameObject("MovementVignetteSlider", typeof(RectTransform), typeof(Slider));
            sliderObject.transform.SetParent(m_PausePanel.transform, false);
            var sliderRect = sliderObject.GetComponent<RectTransform>();
            sliderRect.anchorMin = new Vector2(0.5f, 0.52f);
            sliderRect.anchorMax = new Vector2(0.5f, 0.52f);
            sliderRect.pivot = new Vector2(0.5f, 0.5f);
            sliderRect.sizeDelta = new Vector2(560f, 44f);

            var sliderBackground = new GameObject("Background", typeof(RectTransform), typeof(Image));
            sliderBackground.transform.SetParent(sliderObject.transform, false);
            var sliderBackgroundRect = sliderBackground.GetComponent<RectTransform>();
            sliderBackgroundRect.anchorMin = Vector2.zero;
            sliderBackgroundRect.anchorMax = Vector2.one;
            sliderBackgroundRect.offsetMin = Vector2.zero;
            sliderBackgroundRect.offsetMax = Vector2.zero;
            var sliderBackgroundImage = sliderBackground.GetComponent<Image>();
            sliderBackgroundImage.color = new Color(0.16f, 0.16f, 0.16f, 0.95f);

            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(sliderObject.transform, false);
            var fillAreaRect = fillArea.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0f, 0f);
            fillAreaRect.anchorMax = new Vector2(1f, 1f);
            fillAreaRect.offsetMin = new Vector2(12f, 8f);
            fillAreaRect.offsetMax = new Vector2(-12f, -8f);

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(fillArea.transform, false);
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            var fillImage = fill.GetComponent<Image>();
            fillImage.color = new Color(0.23f, 0.74f, 0.36f, 1f);

            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(sliderObject.transform, false);
            var handleAreaRect = handleArea.GetComponent<RectTransform>();
            handleAreaRect.anchorMin = new Vector2(0f, 0f);
            handleAreaRect.anchorMax = new Vector2(1f, 1f);
            handleAreaRect.offsetMin = new Vector2(8f, 0f);
            handleAreaRect.offsetMax = new Vector2(-8f, 0f);

            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(handleArea.transform, false);
            var handleRect = handle.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(28f, 44f);
            var handleImage = handle.GetComponent<Image>();
            handleImage.color = new Color(0.96f, 0.96f, 0.96f, 1f);

            m_MovementVignetteSlider = sliderObject.GetComponent<Slider>();
            m_MovementVignetteSlider.fillRect = fillRect;
            m_MovementVignetteSlider.handleRect = handleRect;
            m_MovementVignetteSlider.targetGraphic = handleImage;
            m_MovementVignetteSlider.direction = Slider.Direction.LeftToRight;
            m_MovementVignetteSlider.minValue = 0f;
            m_MovementVignetteSlider.maxValue = 1f;
            m_MovementVignetteSlider.wholeNumbers = false;
            m_MovementVignetteSlider.onValueChanged.AddListener(value => SetMovementVignetteStrength(value, notify: true));

            m_MenuResumeButton = CreateButton(
                "ResumeButton",
                m_PausePanel.transform,
                font,
                "Resume",
                new Vector2(0f, -52f),
                new Vector2(300f, 84f),
                new Color(0.2f, 0.58f, 0.27f, 0.95f));
            m_MenuResumeButton.onClick.AddListener(() => m_OnResumeRequested?.Invoke());

            m_MenuRestartButton = CreateButton(
                "MenuRestartButton",
                m_PausePanel.transform,
                font,
                "Restart",
                new Vector2(0f, -152f),
                new Vector2(300f, 84f),
                new Color(0.85f, 0.15f, 0.15f, 0.95f));
            m_MenuRestartButton.onClick.AddListener(() => restartAction?.Invoke());

            m_MenuQuitButton = CreateButton(
                "QuitButton",
                m_PausePanel.transform,
                font,
                "Quit",
                new Vector2(0f, -252f),
                new Vector2(300f, 84f),
                new Color(0.18f, 0.22f, 0.26f, 0.96f));
            m_MenuQuitButton.onClick.AddListener(() => quitAction?.Invoke());

            m_DeathPanel.SetActive(false);
            m_PausePanel.SetActive(false);
        }

        void RefreshCanvasCamera()
        {
            if (m_CanvasTransform == null)
                return;

            var canvas = m_CanvasTransform.GetComponent<Canvas>();
            if (canvas == null)
                return;

            canvas.worldCamera = m_ViewCamera;
        }

        void SetFadeOverlayAlpha(float alpha)
        {
            if (m_FadeOverlay == null)
                return;

            var color = m_FadeOverlay.color;
            color.a = Mathf.Clamp01(alpha);
            m_FadeOverlay.color = color;
        }

        static Text CreateText(string name, Transform parent, Font font, int fontSize, TextAnchor anchor)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);

            var text = textObject.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = Color.white;

            return text;
        }

        static Button CreateButton(
            string name,
            Transform parent,
            Font font,
            string text,
            Vector2 anchoredPosition,
            Vector2 size,
            Color backgroundColor)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            var buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
            buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.pivot = new Vector2(0.5f, 0.5f);
            buttonRect.anchoredPosition = anchoredPosition;
            buttonRect.sizeDelta = size;

            var buttonImage = buttonObject.GetComponent<Image>();
            buttonImage.color = backgroundColor;

            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = buttonImage;

            var buttonText = CreateText($"{name}Text", buttonObject.transform, font, 36, TextAnchor.MiddleCenter);
            buttonText.text = text;
            var buttonTextRect = buttonText.rectTransform;
            buttonTextRect.anchorMin = Vector2.zero;
            buttonTextRect.anchorMax = Vector2.one;
            buttonTextRect.offsetMin = Vector2.zero;
            buttonTextRect.offsetMax = Vector2.zero;

            return button;
        }
    }
}

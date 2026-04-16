using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace VRCombat.UI
{
    public readonly struct CombatHudUpgradeChoice
    {
        public CombatHudUpgradeChoice(string title, string description, string artResourcePath)
        {
            Title = title;
            Description = description;
            ArtResourcePath = artResourcePath;
        }

        public string Title { get; }
        public string Description { get; }
        public string ArtResourcePath { get; }
    }

    [DisallowMultipleComponent]
    public class CombatHUDRuntime : MonoBehaviour
    {
        Text m_HealthText;
        Text m_WaveText;
        Text m_XpText;
        Text m_SelectedSpellText;
        Text m_BannerText;
        GameObject m_DeathPanel;
        Text m_DeathText;
        Button m_RestartButton;
        GameObject m_PausePanel;
        GameObject m_UpgradePanel;
        Slider m_MovementVignetteSlider;
        Text m_MovementVignetteValueText;
        Button m_MenuResumeButton;
        Button m_MenuRestartButton;
        Button m_MenuQuitButton;
        readonly Button[] m_UpgradeButtons = new Button[3];
        readonly Image[] m_UpgradeArtImages = new Image[3];
        readonly Text[] m_UpgradeTitleTexts = new Text[3];
        readonly Text[] m_UpgradeDescriptionTexts = new Text[3];
        Image m_FadeOverlay;
        Coroutine m_BannerRoutine;
        Coroutine m_FadeRoutine;
        Coroutine m_UpgradeUnlockRoutine;
        Camera m_ViewCamera;
        Transform m_ViewTransform;
        Transform m_CanvasTransform;
        readonly Dictionary<int, Material> m_AlwaysOnTopMaterialCache = new Dictionary<int, Material>();
        Action<float> m_OnMovementVignetteChanged;
        Action m_OnResumeRequested;
        Action<int> m_OnUpgradeSelected;
        float m_UpgradeButtonsUnlockAtRealtime;

        [SerializeField]
        float m_HudDistance = 1.05f;

        [SerializeField]
        float m_HudVerticalOffset = -0.1f;

        [SerializeField]
        float m_HudScale = 0.00145f;

        [SerializeField]
        float m_UpgradeSelectionArmDelaySeconds = 0.45f;

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

        public void SetXpInfo(int level, int currentXp, int nextLevelXp)
        {
            if (m_XpText == null)
                return;

            m_XpText.text = $"Level {level} | XP: {currentXp}/{nextLevelXp}";
        }

        public void SetSelectedSpell(string selectedSpellText)
        {
            if (m_SelectedSpellText == null)
                return;

            m_SelectedSpellText.text = string.IsNullOrWhiteSpace(selectedSpellText)
                ? "Spell: None"
                : selectedSpellText;
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

        public void ShowUpgradeChoices(CombatHudUpgradeChoice[] choices, Action<int> onSelected)
        {
            if (m_UpgradePanel == null || choices == null || choices.Length == 0)
                return;

            if (m_UpgradeUnlockRoutine != null)
            {
                StopCoroutine(m_UpgradeUnlockRoutine);
                m_UpgradeUnlockRoutine = null;
            }

            m_OnUpgradeSelected = onSelected;
            m_UpgradePanel.SetActive(true);
            m_UpgradeButtonsUnlockAtRealtime = Time.realtimeSinceStartup + Mathf.Max(0f, m_UpgradeSelectionArmDelaySeconds);

            for (var i = 0; i < m_UpgradeButtons.Length; i++)
            {
                var isActive = i < choices.Length;
                if (m_UpgradeButtons[i] != null)
                {
                    m_UpgradeButtons[i].gameObject.SetActive(isActive);
                    m_UpgradeButtons[i].interactable = false;
                }

                if (!isActive)
                    continue;

                var choice = choices[i];
                if (m_UpgradeTitleTexts[i] != null)
                    m_UpgradeTitleTexts[i].text = choice.Title;
                if (m_UpgradeDescriptionTexts[i] != null)
                    m_UpgradeDescriptionTexts[i].text = choice.Description;
                if (m_UpgradeArtImages[i] != null)
                    m_UpgradeArtImages[i].sprite = LoadSprite(choice.ArtResourcePath);
            }

            m_UpgradeUnlockRoutine = StartCoroutine(EnableUpgradeButtonsAfterDelayRealtime(choices.Length));
        }

        public void HideUpgradeChoices()
        {
            if (m_UpgradeUnlockRoutine != null)
            {
                StopCoroutine(m_UpgradeUnlockRoutine);
                m_UpgradeUnlockRoutine = null;
            }

            m_OnUpgradeSelected = null;
            if (m_UpgradePanel != null)
                m_UpgradePanel.SetActive(false);
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

            if (m_UpgradeUnlockRoutine != null)
            {
                StopCoroutine(m_UpgradeUnlockRoutine);
                m_UpgradeUnlockRoutine = null;
            }

            if (m_BannerText != null)
                m_BannerText.enabled = false;

            HideDeathPanel();
            SetPauseMenuVisible(false);
            HideUpgradeChoices();
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

        IEnumerator EnableUpgradeButtonsAfterDelayRealtime(int choiceCount)
        {
            var remainingDelay = Mathf.Max(0f, m_UpgradeButtonsUnlockAtRealtime - Time.realtimeSinceStartup);
            if (remainingDelay > 0f)
                yield return new WaitForSecondsRealtime(remainingDelay);

            for (var i = 0; i < m_UpgradeButtons.Length; i++)
            {
                if (m_UpgradeButtons[i] != null && i < choiceCount && m_UpgradeButtons[i].gameObject.activeSelf)
                    m_UpgradeButtons[i].interactable = true;
            }

            m_UpgradeUnlockRoutine = null;
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
            canvas.overrideSorting = true;
            canvas.sortingOrder = 200;

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.matchWidthOrHeight = 0.5f;

            canvasObject.AddComponent<GraphicRaycaster>();
            canvasObject.AddComponent<TrackedDeviceGraphicRaycaster>();

            var canvasRect = canvas.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(1400f, 760f);
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

            m_XpText = CreateText("XpText", canvasObject.transform, font, 28, TextAnchor.UpperLeft);
            var xpRect = m_XpText.rectTransform;
            xpRect.anchorMin = new Vector2(0.5f, 1f);
            xpRect.anchorMax = new Vector2(0.5f, 1f);
            xpRect.pivot = new Vector2(0.5f, 1f);
            xpRect.anchoredPosition = new Vector2(-250f, -72f);
            xpRect.sizeDelta = new Vector2(500f, 54f);

            m_SelectedSpellText = CreateText("SelectedSpellText", canvasObject.transform, font, 28, TextAnchor.UpperLeft);
            var selectedSpellRect = m_SelectedSpellText.rectTransform;
            selectedSpellRect.anchorMin = new Vector2(0.5f, 1f);
            selectedSpellRect.anchorMax = new Vector2(0.5f, 1f);
            selectedSpellRect.pivot = new Vector2(0.5f, 1f);
            selectedSpellRect.anchoredPosition = new Vector2(260f, -72f);
            selectedSpellRect.sizeDelta = new Vector2(780f, 54f);

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
            pausePanelRect.anchorMin = Vector2.zero;
            pausePanelRect.anchorMax = Vector2.one;
            pausePanelRect.offsetMin = Vector2.zero;
            pausePanelRect.offsetMax = Vector2.zero;

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

            BuildUpgradePanel(canvasObject.transform, font);
            ApplyAlwaysOnTopMaterials(canvasObject.transform, font);

            m_DeathPanel.SetActive(false);
            m_PausePanel.SetActive(false);
            m_UpgradePanel.SetActive(false);
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

        void ApplyAlwaysOnTopMaterials(Transform root, Font font)
        {
            if (root == null)
                return;

            var graphics = root.GetComponentsInChildren<Graphic>(true);
            for (var i = 0; i < graphics.Length; i++)
            {
                var graphic = graphics[i];
                if (graphic == null)
                    continue;

                var sourceMaterial = graphic.material;
                if (graphic is Text textGraphic)
                {
                    sourceMaterial = textGraphic.material != null
                        ? textGraphic.material
                        : textGraphic.font != null
                            ? textGraphic.font.material
                            : font != null ? font.material : null;
                }

                var alwaysOnTopMaterial = GetOrCreateAlwaysOnTopMaterial(sourceMaterial, graphic is Text);
                if (alwaysOnTopMaterial == null)
                    continue;

                if (graphic is Text text)
                    text.material = alwaysOnTopMaterial;
                else
                    graphic.material = alwaysOnTopMaterial;
            }
        }

        Material GetOrCreateAlwaysOnTopMaterial(Material sourceMaterial, bool isText)
        {
            if (sourceMaterial == null)
            {
                var fallbackShader = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
                if (fallbackShader == null)
                    return null;

                sourceMaterial = new Material(fallbackShader)
                {
                    name = isText ? "Runtime HUD Text Base" : "Runtime HUD Graphic Base"
                };
            }

            var cacheKey = sourceMaterial.GetInstanceID();
            if (m_AlwaysOnTopMaterialCache.TryGetValue(cacheKey, out var cachedMaterial) && cachedMaterial != null)
                return cachedMaterial;

            var material = new Material(sourceMaterial)
            {
                name = $"{sourceMaterial.name} AlwaysOnTop",
                renderQueue = 4000
            };
            if (material.HasProperty("_ZTest"))
                material.SetInt("_ZTest", (int)CompareFunction.Always);
            else
                material.SetInt("_ZTest", (int)CompareFunction.Always);

            m_AlwaysOnTopMaterialCache[cacheKey] = material;
            return material;
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

        void BuildUpgradePanel(Transform parent, Font font)
        {
            m_UpgradePanel = new GameObject("UpgradePanel", typeof(RectTransform), typeof(Image));
            m_UpgradePanel.transform.SetParent(parent, false);
            var panelRect = m_UpgradePanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            var panelImage = m_UpgradePanel.GetComponent<Image>();
            panelImage.color = new Color(0.02f, 0.02f, 0.02f, 0.94f);

            var titleText = CreateText("UpgradePanelTitle", m_UpgradePanel.transform, font, 54, TextAnchor.MiddleCenter);
            titleText.text = "Choose an Upgrade";
            var titleRect = titleText.rectTransform;
            titleRect.anchorMin = new Vector2(0.5f, 0.85f);
            titleRect.anchorMax = new Vector2(0.5f, 0.85f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.sizeDelta = new Vector2(900f, 80f);

            for (var i = 0; i < m_UpgradeButtons.Length; i++)
            {
                var buttonObject = new GameObject($"UpgradeChoice{i + 1}", typeof(RectTransform), typeof(Image), typeof(Button));
                buttonObject.transform.SetParent(m_UpgradePanel.transform, false);

                var buttonRect = buttonObject.GetComponent<RectTransform>();
                buttonRect.anchorMin = new Vector2(0.5f, 0.45f);
                buttonRect.anchorMax = new Vector2(0.5f, 0.45f);
                buttonRect.pivot = new Vector2(0.5f, 0.5f);
                buttonRect.sizeDelta = new Vector2(320f, 460f);
                buttonRect.anchoredPosition = new Vector2((i - 1) * 350f, 0f);

                var buttonImage = buttonObject.GetComponent<Image>();
                buttonImage.color = new Color(0.08f, 0.08f, 0.08f, 0.06f);

                var button = buttonObject.GetComponent<Button>();
                var capturedIndex = i;
                button.onClick.AddListener(() =>
                {
                    if (Time.realtimeSinceStartup < m_UpgradeButtonsUnlockAtRealtime)
                        return;

                    m_OnUpgradeSelected?.Invoke(capturedIndex);
                });
                button.targetGraphic = buttonImage;
                m_UpgradeButtons[i] = button;

                var artObject = new GameObject($"UpgradeArt{i + 1}", typeof(RectTransform), typeof(Image));
                artObject.transform.SetParent(buttonObject.transform, false);
                var artRect = artObject.GetComponent<RectTransform>();
                artRect.anchorMin = new Vector2(0.5f, 0.5f);
                artRect.anchorMax = new Vector2(0.5f, 0.5f);
                artRect.pivot = new Vector2(0.5f, 0.5f);
                artRect.anchoredPosition = Vector2.zero;
                artRect.sizeDelta = new Vector2(320f, 460f);
                var artImage = artObject.GetComponent<Image>();
                artImage.preserveAspect = false;
                artImage.raycastTarget = false;
                artObject.transform.localScale = new Vector3(2f, 1f, 1f);
                m_UpgradeArtImages[i] = artImage;

                var choiceTitle = CreateText($"UpgradeTitle{i + 1}", buttonObject.transform, font, 32, TextAnchor.UpperCenter);
                var choiceTitleRect = choiceTitle.rectTransform;
                choiceTitleRect.anchorMin = new Vector2(0.5f, 0f);
                choiceTitleRect.anchorMax = new Vector2(0.5f, 0f);
                choiceTitleRect.pivot = new Vector2(0.5f, 0f);
                choiceTitleRect.anchoredPosition = new Vector2(0f, 120f);
                choiceTitleRect.sizeDelta = new Vector2(470f, 56f);
                m_UpgradeTitleTexts[i] = choiceTitle;

                var descriptionText = CreateText($"UpgradeDescription{i + 1}", buttonObject.transform, font, 22, TextAnchor.UpperCenter);
                var descriptionRect = descriptionText.rectTransform;
                descriptionRect.anchorMin = new Vector2(0.5f, 0f);
                descriptionRect.anchorMax = new Vector2(0.5f, 0f);
                descriptionRect.pivot = new Vector2(0.5f, 0f);
                descriptionRect.anchoredPosition = new Vector2(0f, 28f);
                descriptionRect.sizeDelta = new Vector2(470f, 84f);
                m_UpgradeDescriptionTexts[i] = descriptionText;
            }
        }

        static Sprite LoadSprite(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
                return null;

            var texture = Resources.Load<Texture2D>(resourcePath);
            if (texture == null)
                return null;

            return Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f);
        }
    }
}

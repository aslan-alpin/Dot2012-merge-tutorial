using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using VRCombat.Core;

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
        GameObject m_DebugPanel;
        GameObject m_VictoryPanel;
        GameObject m_UpgradePanel;
        Slider m_MovementVignetteSlider;
        Text m_MovementVignetteValueText;
        Text m_DebugWaveValueText;
        Button m_MenuResumeButton;
        Button m_MenuRespawnButton;
        Button m_MenuQuitButton;
        Button m_DebugHotspotButton;
        Button m_VictoryRestartButton;
        Button m_VictoryContinueButton;
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
        Action m_OnContinueEndlessRequested;
        Action<UpgradeKind> m_OnDebugGrantUpgradeRequested;
        Action<WeaponKind> m_OnDebugGrantWeaponRequested;
        Action<SpellKind> m_OnDebugUnlockSpellRequested;
        Action m_OnDebugGrantAllWeaponsRequested;
        Action m_OnDebugKillAllEnemiesRequested;
        Action m_OnDebugSpawnMahmutCanKovanCardRequested;
        Action<int> m_OnDebugSetWaveRequested;
        Action<int> m_OnUpgradeSelected;
        int m_DebugWaveNumber = 15;
        int m_DebugHotspotClickCount;
        float m_LastDebugHotspotClickTime = -100f;
        float m_UpgradeButtonsUnlockAtRealtime;

        const int DebugHotspotClickThreshold = 3;
        const float DebugHotspotClickWindowSeconds = 1.6f;

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
            Action continueEndlessAction,
            Action<float> movementVignetteChangedAction,
            float initialMovementVignetteStrength,
            Camera viewCamera,
            Transform viewTransform,
            Action<UpgradeKind> debugGrantUpgradeAction = null,
            Action<WeaponKind> debugGrantWeaponAction = null,
            Action<SpellKind> debugUnlockSpellAction = null,
            Action debugGrantAllWeaponsAction = null,
            Action debugKillAllEnemiesAction = null,
            Action<int> debugSetWaveAction = null,
            Action debugSpawnMahmutCanKovanCardAction = null)
        {
            m_OnMovementVignetteChanged = movementVignetteChangedAction;
            m_OnResumeRequested = resumeAction;
            m_OnContinueEndlessRequested = continueEndlessAction;
            m_OnDebugGrantUpgradeRequested = debugGrantUpgradeAction;
            m_OnDebugGrantWeaponRequested = debugGrantWeaponAction;
            m_OnDebugUnlockSpellRequested = debugUnlockSpellAction;
            m_OnDebugGrantAllWeaponsRequested = debugGrantAllWeaponsAction;
            m_OnDebugKillAllEnemiesRequested = debugKillAllEnemiesAction;
            m_OnDebugSetWaveRequested = debugSetWaveAction;
            m_OnDebugSpawnMahmutCanKovanCardRequested = debugSpawnMahmutCanKovanCardAction;
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

        public void ShowVictoryPanel()
        {
            if (m_VictoryPanel == null)
                return;

            HideDeathPanel();
            SetPauseMenuVisible(false);
            HideUpgradeChoices();
            m_VictoryPanel.SetActive(true);
            m_VictoryPanel.transform.SetAsLastSibling();
            if (m_VictoryRestartButton != null)
                m_VictoryRestartButton.interactable = true;
            if (m_VictoryContinueButton != null)
                m_VictoryContinueButton.interactable = true;

            EventSystem.current?.SetSelectedGameObject(m_VictoryContinueButton != null
                ? m_VictoryContinueButton.gameObject
                : m_VictoryRestartButton != null ? m_VictoryRestartButton.gameObject : null);
        }

        public void HideVictoryPanel()
        {
            if (m_VictoryPanel != null)
                m_VictoryPanel.SetActive(false);
        }

        public void SetPauseMenuVisible(bool visible)
        {
            if (m_PausePanel == null)
                return;

            m_PausePanel.SetActive(visible);
            if (!visible)
            {
                SetDebugPanelVisible(false);
                m_DebugHotspotClickCount = 0;
                return;
            }

            m_PausePanel.transform.SetAsLastSibling();
            if (m_MenuResumeButton != null)
                m_MenuResumeButton.interactable = true;
            if (m_MenuRespawnButton != null)
                m_MenuRespawnButton.interactable = true;
            if (m_MenuQuitButton != null)
                m_MenuQuitButton.interactable = true;

            FocusPauseMenu();
        }

        public void ToggleDebugPanel()
        {
            if (m_DebugPanel == null || !IsPauseMenuVisible)
                return;

            SetDebugPanelVisible(!m_DebugPanel.activeSelf);
        }

        public void SetDebugPanelVisible(bool visible)
        {
            if (m_DebugPanel == null)
                return;

            m_DebugPanel.SetActive(visible);
            if (!visible)
                return;

            m_DebugPanel.transform.SetAsLastSibling();
            UpdateDebugWaveText();
        }

        public void FocusPauseMenu()
        {
            if (m_PausePanel == null || !m_PausePanel.activeSelf || m_MenuResumeButton == null)
                return;

            EventSystem.current?.SetSelectedGameObject(m_MenuResumeButton.gameObject);
        }

        public void AdjustPauseMenuVignette(float delta)
        {
            if (m_MovementVignetteSlider == null || Mathf.Approximately(delta, 0f))
                return;

            SetMovementVignetteStrength(m_MovementVignetteSlider.value + delta, notify: true);
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
            m_UpgradePanel.transform.SetAsLastSibling();
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
            EventSystem.current?.SetSelectedGameObject(m_UpgradeButtons[0] != null ? m_UpgradeButtons[0].gameObject : null);
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
            HideVictoryPanel();
            SetPauseMenuVisible(false);
            SetDebugPanelVisible(false);
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
            EnsureEventSystem();

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
            pauseTitle.text = "Paused";
            var pauseTitleRect = pauseTitle.rectTransform;
            pauseTitleRect.anchorMin = new Vector2(0.5f, 0.88f);
            pauseTitleRect.anchorMax = new Vector2(0.5f, 0.88f);
            pauseTitleRect.pivot = new Vector2(0.5f, 0.5f);
            pauseTitleRect.sizeDelta = new Vector2(500f, 80f);

            m_DebugHotspotButton = CreateInvisibleButton(
                "DebugTitleHotspot",
                m_PausePanel.transform,
                new Vector2(0f, 288f),
                new Vector2(520f, 96f));
            m_DebugHotspotButton.onClick.AddListener(RegisterDebugHotspotClick);

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

            m_MenuRespawnButton = CreateButton(
                "RestartRunButton",
                m_PausePanel.transform,
                font,
                "Restart",
                new Vector2(0f, -152f),
                new Vector2(300f, 84f),
                new Color(0.85f, 0.15f, 0.15f, 0.95f));
            m_MenuRespawnButton.onClick.AddListener(() => restartAction?.Invoke());

            m_MenuQuitButton = CreateButton(
                "QuitButton",
                m_PausePanel.transform,
                font,
                "Quit",
                new Vector2(0f, -252f),
                new Vector2(300f, 84f),
                new Color(0.18f, 0.22f, 0.26f, 0.96f));
            m_MenuQuitButton.onClick.AddListener(() => quitAction?.Invoke());

            BuildDebugPanel(m_PausePanel.transform, font);
            if (m_DebugHotspotButton != null)
                m_DebugHotspotButton.transform.SetAsLastSibling();
            BuildVictoryPanel(canvasObject.transform, font, restartAction);
            BuildUpgradePanel(canvasObject.transform, font);
            ApplyAlwaysOnTopMaterials(canvasObject.transform, font);

            m_DeathPanel.SetActive(false);
            m_PausePanel.SetActive(false);
            m_DebugPanel.SetActive(false);
            m_VictoryPanel.SetActive(false);
            m_UpgradePanel.SetActive(false);
        }

        static void EnsureEventSystem()
        {
            var eventSystem = FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
            if (eventSystem == null)
            {
                var eventSystemObject = new GameObject("EventSystem");
                eventSystem = eventSystemObject.AddComponent<EventSystem>();
            }

            eventSystem.gameObject.SetActive(true);
            var xrInputModule = eventSystem.GetComponent<XRUIInputModule>();
            if (xrInputModule == null)
                xrInputModule = eventSystem.gameObject.AddComponent<XRUIInputModule>();

            xrInputModule.enabled = true;

            var inputSystemModule = eventSystem.GetComponent<InputSystemUIInputModule>();
            if (inputSystemModule == null)
                inputSystemModule = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();

            inputSystemModule.enabled = true;
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

        static Button CreateInvisibleButton(
            string name,
            Transform parent,
            Vector2 anchoredPosition,
            Vector2 size)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            var rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.001f);
            image.raycastTarget = true;

            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            return button;
        }

        void RegisterDebugHotspotClick()
        {
            var now = Time.realtimeSinceStartup;
            if (now - m_LastDebugHotspotClickTime > DebugHotspotClickWindowSeconds)
                m_DebugHotspotClickCount = 0;

            m_LastDebugHotspotClickTime = now;
            m_DebugHotspotClickCount++;
            if (m_DebugHotspotClickCount < DebugHotspotClickThreshold)
                return;

            m_DebugHotspotClickCount = 0;
            ToggleDebugPanel();
            ShowBanner("Debug menu", 0.8f);
        }

        void BuildDebugPanel(Transform parent, Font font)
        {
            m_DebugPanel = new GameObject("DebugPanel", typeof(RectTransform), typeof(Image));
            m_DebugPanel.transform.SetParent(parent, false);
            var panelRect = m_DebugPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = new Vector2(120f, -20f);
            panelRect.sizeDelta = new Vector2(650f, 660f);

            var panelImage = m_DebugPanel.GetComponent<Image>();
            panelImage.color = new Color(0.015f, 0.02f, 0.025f, 0.96f);

            var title = CreateText("DebugTitle", m_DebugPanel.transform, font, 34, TextAnchor.MiddleCenter);
            title.text = "Debug";
            var titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0.5f, 0.5f);
            titleRect.anchorMax = new Vector2(0.5f, 0.5f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.anchoredPosition = new Vector2(0f, 290f);
            titleRect.sizeDelta = new Vector2(560f, 48f);

            m_DebugWaveValueText = CreateText("DebugWaveValue", m_DebugPanel.transform, font, 25, TextAnchor.MiddleCenter);
            var waveRect = m_DebugWaveValueText.rectTransform;
            waveRect.anchorMin = new Vector2(0.5f, 0.5f);
            waveRect.anchorMax = new Vector2(0.5f, 0.5f);
            waveRect.pivot = new Vector2(0.5f, 0.5f);
            waveRect.anchoredPosition = new Vector2(0f, 248f);
            waveRect.sizeDelta = new Vector2(560f, 42f);
            UpdateDebugWaveText();

            CreateDebugButton("DebugWaveDownButton", m_DebugPanel.transform, font, "- Wave", new Vector2(-190f, 204f), () => AdjustDebugWaveNumber(-1));
            CreateDebugButton("DebugSetWaveButton", m_DebugPanel.transform, font, "Set Wave", new Vector2(0f, 204f), () => m_OnDebugSetWaveRequested?.Invoke(m_DebugWaveNumber));
            CreateDebugButton("DebugWaveUpButton", m_DebugPanel.transform, font, "+ Wave", new Vector2(190f, 204f), () => AdjustDebugWaveNumber(1));
            CreateDebugButton("DebugKillEnemiesButton", m_DebugPanel.transform, font, "Kill All Enemies", new Vector2(0f, 158f), () => m_OnDebugKillAllEnemiesRequested?.Invoke(), width: 250f);

            var loadoutTitle = CreateText("DebugLoadoutTitle", m_DebugPanel.transform, font, 23, TextAnchor.MiddleCenter);
            loadoutTitle.text = "Loadout";
            var loadoutTitleRect = loadoutTitle.rectTransform;
            loadoutTitleRect.anchorMin = new Vector2(0.5f, 0.5f);
            loadoutTitleRect.anchorMax = new Vector2(0.5f, 0.5f);
            loadoutTitleRect.pivot = new Vector2(0.5f, 0.5f);
            loadoutTitleRect.anchoredPosition = new Vector2(0f, 112f);
            loadoutTitleRect.sizeDelta = new Vector2(560f, 32f);

            var loadoutButtons = new (string Label, Action Action)[]
            {
                ("All Weapons", () => m_OnDebugGrantAllWeaponsRequested?.Invoke()),
                ("Chain", () => m_OnDebugGrantWeaponRequested?.Invoke(WeaponKind.Chain)),
                ("Daggers", () => m_OnDebugGrantWeaponRequested?.Invoke(WeaponKind.Dagger)),
                ("Flintlock", () => m_OnDebugGrantWeaponRequested?.Invoke(WeaponKind.Flintlock)),
                ("Mace", () => m_OnDebugGrantWeaponRequested?.Invoke(WeaponKind.Mace)),
                ("Shield", () => m_OnDebugGrantWeaponRequested?.Invoke(WeaponKind.Shield)),
                ("Spear", () => m_OnDebugGrantWeaponRequested?.Invoke(WeaponKind.Spear)),
                ("Sword", () => m_OnDebugGrantWeaponRequested?.Invoke(WeaponKind.Sword)),
                ("Fireball", () => m_OnDebugUnlockSpellRequested?.Invoke(SpellKind.Fireball)),
                ("Frost", () => m_OnDebugUnlockSpellRequested?.Invoke(SpellKind.Frost)),
                ("Lightning", () => m_OnDebugUnlockSpellRequested?.Invoke(SpellKind.Lightning))
            };

            CreateDebugButtonGrid(loadoutButtons, m_DebugPanel.transform, font, startY: 73f, rowStep: 38f);

            var upgradesTitle = CreateText("DebugUpgradesTitle", m_DebugPanel.transform, font, 23, TextAnchor.MiddleCenter);
            upgradesTitle.text = "Upgrades";
            var upgradesTitleRect = upgradesTitle.rectTransform;
            upgradesTitleRect.anchorMin = new Vector2(0.5f, 0.5f);
            upgradesTitleRect.anchorMax = new Vector2(0.5f, 0.5f);
            upgradesTitleRect.pivot = new Vector2(0.5f, 0.5f);
            upgradesTitleRect.anchoredPosition = new Vector2(0f, -62f);
            upgradesTitleRect.sizeDelta = new Vector2(560f, 32f);

            var upgrades = RunCatalog.Upgrades;
            var upgradeButtons = new (string Label, Action Action)[upgrades.Count];
            for (var i = 0; i < upgrades.Count; i++)
            {
                var definition = upgrades[i];
                upgradeButtons[i] = (definition.DisplayName, () => m_OnDebugGrantUpgradeRequested?.Invoke(definition.Kind));
            }

            CreateDebugButtonGrid(upgradeButtons, m_DebugPanel.transform, font, startY: -102f, rowStep: 38f);
            CreateDebugButton(
                "DebugMahmutCanKovanButton",
                m_DebugPanel.transform,
                font,
                "MahmutCanKovan",
                new Vector2(0f, -282f),
                () => m_OnDebugSpawnMahmutCanKovanCardRequested?.Invoke(),
                width: 300f);
        }

        void CreateDebugButtonGrid(
            IReadOnlyList<(string Label, Action Action)> buttons,
            Transform parent,
            Font font,
            float startY,
            float rowStep)
        {
            const int columns = 4;
            var xPositions = new[] { -225f, -75f, 75f, 225f };
            for (var i = 0; i < buttons.Count; i++)
            {
                var button = buttons[i];
                var column = i % columns;
                var row = i / columns;
                CreateDebugButton(
                    $"DebugButton{i}_{button.Label.Replace(" ", string.Empty)}",
                    parent,
                    font,
                    button.Label,
                    new Vector2(xPositions[column], startY - row * rowStep),
                    button.Action,
                    width: 138f);
            }
        }

        Button CreateDebugButton(
            string name,
            Transform parent,
            Font font,
            string label,
            Vector2 anchoredPosition,
            Action action,
            float width = 150f)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            var buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
            buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.pivot = new Vector2(0.5f, 0.5f);
            buttonRect.anchoredPosition = anchoredPosition;
            buttonRect.sizeDelta = new Vector2(width, 32f);

            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.12f, 0.17f, 0.2f, 0.98f);

            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => action?.Invoke());

            var buttonText = CreateText($"{name}Text", buttonObject.transform, font, 18, TextAnchor.MiddleCenter);
            buttonText.text = label;
            var textRect = buttonText.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(4f, 0f);
            textRect.offsetMax = new Vector2(-4f, 0f);
            return button;
        }

        void AdjustDebugWaveNumber(int delta)
        {
            m_DebugWaveNumber = Mathf.Clamp(m_DebugWaveNumber + delta, 1, 999);
            UpdateDebugWaveText();
        }

        void UpdateDebugWaveText()
        {
            if (m_DebugWaveValueText != null)
                m_DebugWaveValueText.text = $"Wave {m_DebugWaveNumber}";
        }

        void BuildVictoryPanel(Transform parent, Font font, Action restartAction)
        {
            m_VictoryPanel = new GameObject("VictoryPanel", typeof(RectTransform), typeof(Image));
            m_VictoryPanel.transform.SetParent(parent, false);
            var panelRect = m_VictoryPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            var panelImage = m_VictoryPanel.GetComponent<Image>();
            panelImage.color = new Color(0f, 0f, 0f, 0.9f);

            var titleText = CreateText("VictoryTitle", m_VictoryPanel.transform, font, 64, TextAnchor.MiddleCenter);
            titleText.text = "You won";
            var titleRect = titleText.rectTransform;
            titleRect.anchorMin = new Vector2(0.5f, 0.72f);
            titleRect.anchorMax = new Vector2(0.5f, 0.72f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.sizeDelta = new Vector2(720f, 100f);

            var subtitleText = CreateText("VictorySubtitle", m_VictoryPanel.transform, font, 34, TextAnchor.MiddleCenter);
            subtitleText.text = "The boss is defeated.";
            var subtitleRect = subtitleText.rectTransform;
            subtitleRect.anchorMin = new Vector2(0.5f, 0.59f);
            subtitleRect.anchorMax = new Vector2(0.5f, 0.59f);
            subtitleRect.pivot = new Vector2(0.5f, 0.5f);
            subtitleRect.sizeDelta = new Vector2(760f, 70f);

            m_VictoryRestartButton = CreateButton(
                "VictoryRestartButton",
                m_VictoryPanel.transform,
                font,
                "Restart",
                new Vector2(-190f, -85f),
                new Vector2(330f, 88f),
                new Color(0.85f, 0.15f, 0.15f, 0.95f));
            m_VictoryRestartButton.onClick.AddListener(() => restartAction?.Invoke());

            m_VictoryContinueButton = CreateButton(
                "VictoryContinueEndlessButton",
                m_VictoryPanel.transform,
                font,
                "Continue Endless",
                new Vector2(190f, -85f),
                new Vector2(390f, 88f),
                new Color(0.2f, 0.58f, 0.27f, 0.95f));
            m_VictoryContinueButton.onClick.AddListener(() => m_OnContinueEndlessRequested?.Invoke());
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

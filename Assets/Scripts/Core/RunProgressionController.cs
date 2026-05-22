using System;
using System.Collections.Generic;
using UnityEngine;
using VRCombat.UI;

namespace VRCombat.Core
{
    [DisallowMultipleComponent]
    public sealed class RunProgressionController : MonoBehaviour
    {
        readonly Dictionary<UpgradeKind, int> m_UpgradeStacks = new Dictionary<UpgradeKind, int>();
        readonly HashSet<WeaponKind> m_OwnedWeapons = new HashSet<WeaponKind>();
        readonly List<SpellKind> m_UnlockedSpells = new List<SpellKind>();
        readonly List<CardDefinition> m_RunCardPool = new List<CardDefinition>();
        readonly List<UpgradeDefinition> m_CurrentUpgradeChoices = new List<UpgradeDefinition>(3);

        CombatHUDRuntime m_Hud;
        int m_Level = 1;
        int m_CurrentXp;
        int m_NextLevelXp = 100;
        int m_DrawCardIndex;
        int m_PendingUpgradeChoices;
        int m_SelectedSpellIndex = -1;
        bool m_IsChoosingUpgrade;
        bool m_MahmutCanKovanActive;

        public event Action ProgressionChanged;
        public event Action<SpellKind> SelectedSpellChanged;

        public int Level => m_Level;
        public int CurrentXp => m_CurrentXp;
        public int NextLevelXp => m_NextLevelXp;
        public bool IsUpgradeSelectionActive => m_IsChoosingUpgrade;
        public bool HasMahmutCanKovanEasterEgg => m_MahmutCanKovanActive;
        public bool SpellCooldownsDisabled => m_MahmutCanKovanActive;
        public bool FlintlockReloadsDisabled => m_MahmutCanKovanActive;
        public SpellKind SelectedSpellKind => m_SelectedSpellIndex >= 0 && m_SelectedSpellIndex < m_UnlockedSpells.Count
            ? m_UnlockedSpells[m_SelectedSpellIndex]
            : SpellKind.None;

        public void Initialize(CombatHUDRuntime hud)
        {
            m_Hud = hud;
            RefreshHud();
        }

        public void ResetRun()
        {
            m_Level = 1;
            m_CurrentXp = 0;
            m_NextLevelXp = 100;
            m_DrawCardIndex = 0;
            m_PendingUpgradeChoices = 0;
            m_SelectedSpellIndex = -1;
            m_IsChoosingUpgrade = false;
            m_MahmutCanKovanActive = false;
            m_UpgradeStacks.Clear();
            m_OwnedWeapons.Clear();
            m_UnlockedSpells.Clear();
            m_RunCardPool.Clear();
            RebuildRunCardPool();
            m_CurrentUpgradeChoices.Clear();
            m_Hud?.HideUpgradeChoices();
            RefreshHud();
            RaiseProgressionChanged();
        }

        public void RegisterWeaponAcquired(WeaponKind weaponKind)
        {
            if (weaponKind == WeaponKind.None)
                return;

            if (m_OwnedWeapons.Add(weaponKind))
                RaiseProgressionChanged();
        }

        public bool GrantUpgradeForDebug(UpgradeKind upgradeKind, int stackCount = 1)
        {
            if (!RunCatalog.TryGetUpgrade(upgradeKind, out _))
                return false;

            var stacksToAdd = Mathf.Max(1, stackCount);
            if (m_UpgradeStacks.TryGetValue(upgradeKind, out var currentStacks))
                m_UpgradeStacks[upgradeKind] = currentStacks + stacksToAdd;
            else
                m_UpgradeStacks[upgradeKind] = stacksToAdd;

            RefreshHud();
            RaiseProgressionChanged();
            return true;
        }

        public bool ActivateMahmutCanKovanEasterEgg()
        {
            if (m_MahmutCanKovanActive)
                return false;

            m_MahmutCanKovanActive = true;
            RaiseProgressionChanged();
            return true;
        }

        public bool HasWeapon(WeaponKind weaponKind)
        {
            return weaponKind != WeaponKind.None && m_OwnedWeapons.Contains(weaponKind);
        }

        public bool HasSpell(SpellKind spellKind)
        {
            return spellKind != SpellKind.None && m_UnlockedSpells.Contains(spellKind);
        }

        public bool UnlockSpell(SpellKind spellKind)
        {
            if (spellKind == SpellKind.None || m_UnlockedSpells.Contains(spellKind))
                return false;

            m_UnlockedSpells.Add(spellKind);
            if (m_SelectedSpellIndex < 0)
                m_SelectedSpellIndex = 0;

            RefreshHud();
            SelectedSpellChanged?.Invoke(SelectedSpellKind);
            RaiseProgressionChanged();
            return true;
        }

        public void CycleSelectedSpell(int direction)
        {
            if (m_UnlockedSpells.Count <= 1)
                return;

            var offset = direction >= 0 ? 1 : -1;
            m_SelectedSpellIndex = (m_SelectedSpellIndex + offset + m_UnlockedSpells.Count) % m_UnlockedSpells.Count;
            RefreshHud();
            SelectedSpellChanged?.Invoke(SelectedSpellKind);
        }

        public CardDefinition DrawNextCard()
        {
            if (m_RunCardPool.Count == 0)
                RebuildRunCardPool();

            if (m_DrawCardIndex < 0 || m_DrawCardIndex >= m_RunCardPool.Count)
            {
                RebuildRunCardPool();
                m_DrawCardIndex = 0;
            }

            if (m_RunCardPool.Count == 0)
                return null;

            var card = m_RunCardPool[m_DrawCardIndex];
            m_DrawCardIndex++;
            return card;
        }

        public void AwardXp(int amount)
        {
            if (amount <= 0)
                return;

            m_CurrentXp += amount;
            while (m_CurrentXp >= m_NextLevelXp)
            {
                m_CurrentXp -= m_NextLevelXp;
                m_Level++;
                m_NextLevelXp += 50;
                m_PendingUpgradeChoices++;
            }

            RefreshHud();
            RaiseProgressionChanged();

            if (!m_IsChoosingUpgrade)
                TryShowNextUpgradeChoice();
        }

        public int GetUpgradeStacks(UpgradeKind upgradeKind)
        {
            return m_UpgradeStacks.TryGetValue(upgradeKind, out var stacks) ? stacks : 0;
        }

        public bool HasShieldBashUnlocked()
        {
            return GetUpgradeStacks(UpgradeKind.ShieldKnockbackUnlock) > 0;
        }

        public float GetPlayerSpeedMultiplier()
        {
            var multiplier = 1f + GetUpgradeStacks(UpgradeKind.PlayerSpeed) * 0.1f;
            if (m_MahmutCanKovanActive)
                multiplier *= 2f;
            return multiplier;
        }

        public float GetShieldKnockbackDamageMultiplier()
        {
            var unlockRanks = GetUpgradeStacks(UpgradeKind.ShieldKnockbackUnlock);
            if (unlockRanks <= 0)
                return 0f;

            return 1f + Mathf.Max(0, unlockRanks - 1) * 0.25f;
        }

        public float GetWeaponDamageMultiplier(WeaponKind weaponKind)
        {
            var totalMultiplier = 1f;
            if (RunCatalog.IsMeleeWeapon(weaponKind))
                totalMultiplier *= 1f + GetUpgradeStacks(UpgradeKind.AllMeleeDamage) * 0.1f;

            totalMultiplier *= 1f + GetSpecificWeaponUpgradeStacks(weaponKind) * 0.15f;
            if (m_MahmutCanKovanActive && RunCatalog.IsMeleeWeapon(weaponKind))
                totalMultiplier *= 2f;
            return totalMultiplier;
        }

        public float GetSpellDamageMultiplier(SpellKind spellKind)
        {
            var multiplier = 1f + GetSpecificSpellUpgradeStacks(spellKind) * 0.15f;
            if (m_MahmutCanKovanActive)
                multiplier *= 2f;
            return multiplier;
        }

        void RebuildRunCardPool()
        {
            m_RunCardPool.Clear();
            var cards = RunCatalog.Cards;
            for (var i = 0; i < cards.Count; i++)
                m_RunCardPool.Add(cards[i]);

            for (var i = 0; i < m_RunCardPool.Count; i++)
            {
                var swapIndex = UnityEngine.Random.Range(i, m_RunCardPool.Count);
                (m_RunCardPool[i], m_RunCardPool[swapIndex]) = (m_RunCardPool[swapIndex], m_RunCardPool[i]);
            }
        }

        void TryShowNextUpgradeChoice()
        {
            if (m_IsChoosingUpgrade || m_PendingUpgradeChoices <= 0 || m_Hud == null)
                return;

            var candidates = BuildRelevantUpgradePool();
            if (candidates.Count == 0)
                return;

            m_CurrentUpgradeChoices.Clear();
            var availableChoices = new List<UpgradeDefinition>(candidates);
            while (m_CurrentUpgradeChoices.Count < 3 && availableChoices.Count > 0)
            {
                var choiceIndex = UnityEngine.Random.Range(0, availableChoices.Count);
                m_CurrentUpgradeChoices.Add(availableChoices[choiceIndex]);
                availableChoices.RemoveAt(choiceIndex);
            }

            while (m_CurrentUpgradeChoices.Count < 3 && candidates.Count > 0)
                m_CurrentUpgradeChoices.Add(candidates[UnityEngine.Random.Range(0, candidates.Count)]);

            if (m_CurrentUpgradeChoices.Count == 0)
                return;

            m_IsChoosingUpgrade = true;
            Time.timeScale = 0f;

            var viewModels = new CombatHudUpgradeChoice[m_CurrentUpgradeChoices.Count];
            for (var i = 0; i < m_CurrentUpgradeChoices.Count; i++)
            {
                var definition = m_CurrentUpgradeChoices[i];
                viewModels[i] = new CombatHudUpgradeChoice(
                    definition.DisplayName,
                    definition.Description,
                    definition.ArtResourcePath);
            }

            m_Hud.ShowUpgradeChoices(viewModels, HandleUpgradeSelected);
        }

        void HandleUpgradeSelected(int selectedIndex)
        {
            if (!m_IsChoosingUpgrade || selectedIndex < 0 || selectedIndex >= m_CurrentUpgradeChoices.Count)
                return;

            var selectedUpgrade = m_CurrentUpgradeChoices[selectedIndex];
            if (m_UpgradeStacks.TryGetValue(selectedUpgrade.Kind, out var stacks))
                m_UpgradeStacks[selectedUpgrade.Kind] = stacks + 1;
            else
                m_UpgradeStacks[selectedUpgrade.Kind] = 1;

            m_CurrentUpgradeChoices.Clear();
            m_PendingUpgradeChoices = Mathf.Max(0, m_PendingUpgradeChoices - 1);
            m_IsChoosingUpgrade = false;
            Time.timeScale = 1f;
            m_Hud?.HideUpgradeChoices();
            RefreshHud();
            RaiseProgressionChanged();

            if (!m_IsChoosingUpgrade)
                TryShowNextUpgradeChoice();
        }

        List<UpgradeDefinition> BuildRelevantUpgradePool()
        {
            var upgrades = RunCatalog.Upgrades;
            var relevantUpgrades = new List<UpgradeDefinition>(upgrades.Count);
            for (var i = 0; i < upgrades.Count; i++)
            {
                var definition = upgrades[i];
                if (!IsUpgradeRelevant(definition))
                    continue;

                relevantUpgrades.Add(definition);
            }

            return relevantUpgrades;
        }

        bool IsUpgradeRelevant(UpgradeDefinition definition)
        {
            switch (definition.Kind)
            {
                case UpgradeKind.PlayerSpeed:
                    return true;
                case UpgradeKind.AllMeleeDamage:
                    return HasAnyMeleeWeapon();
                case UpgradeKind.ChainDamage:
                case UpgradeKind.DaggerDamage:
                case UpgradeKind.FlintlockDamage:
                case UpgradeKind.MaceDamage:
                case UpgradeKind.ShieldKnockbackUnlock:
                case UpgradeKind.SpearDamage:
                case UpgradeKind.SwordDamage:
                    return HasWeapon(definition.WeaponKind);
                case UpgradeKind.FireDamage:
                case UpgradeKind.IceDamage:
                case UpgradeKind.LightningDamage:
                    return HasSpell(definition.SpellKind);
                default:
                    return false;
            }
        }

        bool HasAnyMeleeWeapon()
        {
            foreach (var weaponKind in m_OwnedWeapons)
            {
                if (RunCatalog.IsMeleeWeapon(weaponKind))
                    return true;
            }

            return false;
        }

        int GetSpecificWeaponUpgradeStacks(WeaponKind weaponKind)
        {
            return weaponKind switch
            {
                WeaponKind.Chain => GetUpgradeStacks(UpgradeKind.ChainDamage),
                WeaponKind.Dagger => GetUpgradeStacks(UpgradeKind.DaggerDamage),
                WeaponKind.Flintlock => GetUpgradeStacks(UpgradeKind.FlintlockDamage),
                WeaponKind.Mace => GetUpgradeStacks(UpgradeKind.MaceDamage),
                WeaponKind.Spear => GetUpgradeStacks(UpgradeKind.SpearDamage),
                WeaponKind.Sword => GetUpgradeStacks(UpgradeKind.SwordDamage),
                _ => 0
            };
        }

        int GetSpecificSpellUpgradeStacks(SpellKind spellKind)
        {
            return spellKind switch
            {
                SpellKind.Fireball => GetUpgradeStacks(UpgradeKind.FireDamage),
                SpellKind.Frost => GetUpgradeStacks(UpgradeKind.IceDamage),
                SpellKind.Lightning => GetUpgradeStacks(UpgradeKind.LightningDamage),
                _ => 0
            };
        }

        void RefreshHud()
        {
            m_Hud?.SetXpInfo(m_Level, m_CurrentXp, m_NextLevelXp);

            var selectedSpellName = "Spell: None";
            if (RunCatalog.TryGetSpell(SelectedSpellKind, out var spellDefinition))
                selectedSpellName = $"Spell: {spellDefinition.DisplayName}";

            m_Hud?.SetSelectedSpell(selectedSpellName);
        }

        void RaiseProgressionChanged()
        {
            ProgressionChanged?.Invoke();
        }
    }
}

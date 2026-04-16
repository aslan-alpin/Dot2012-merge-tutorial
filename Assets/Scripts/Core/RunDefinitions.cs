using System;
using System.Collections.Generic;

namespace VRCombat.Core
{
    public enum WeaponKind
    {
        None,
        Chain,
        Dagger,
        Flintlock,
        Mace,
        Shield,
        Spear,
        Sword
    }

    public enum SpellKind
    {
        None,
        Fireball,
        Frost,
        Lightning
    }

    public enum CardRewardType
    {
        Weapon,
        Spell
    }

    public enum UpgradeKind
    {
        AllMeleeDamage,
        ChainDamage,
        DaggerDamage,
        FireDamage,
        FlintlockDamage,
        IceDamage,
        LightningDamage,
        MaceDamage,
        PlayerSpeed,
        ShieldKnockbackUnlock,
        SpearDamage,
        SwordDamage
    }

    [Serializable]
    public sealed class CardDefinition
    {
        public CardDefinition(
            string id,
            string displayName,
            CardRewardType rewardType,
            string cardMeshName,
            string cardArtResourcePath,
            WeaponKind weaponKind = WeaponKind.None,
            SpellKind spellKind = SpellKind.None,
            params string[] cardMeshAliases)
        {
            Id = id;
            DisplayName = displayName;
            RewardType = rewardType;
            CardMeshName = cardMeshName;
            CardMeshNames = BuildMeshNames(cardMeshName, cardMeshAliases);
            CardArtResourcePath = cardArtResourcePath;
            WeaponKind = weaponKind;
            SpellKind = spellKind;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public CardRewardType RewardType { get; }
        public string CardMeshName { get; }
        public IReadOnlyList<string> CardMeshNames { get; }
        public string CardArtResourcePath { get; }
        public WeaponKind WeaponKind { get; }
        public SpellKind SpellKind { get; }

        static IReadOnlyList<string> BuildMeshNames(string primaryName, IReadOnlyList<string> aliases)
        {
            var meshNames = new List<string>();
            if (!string.IsNullOrWhiteSpace(primaryName))
                meshNames.Add(primaryName);

            if (aliases != null)
            {
                for (var i = 0; i < aliases.Count; i++)
                {
                    var alias = aliases[i];
                    if (string.IsNullOrWhiteSpace(alias) || meshNames.Contains(alias))
                        continue;

                    meshNames.Add(alias);
                }
            }

            return meshNames;
        }
    }

    [Serializable]
    public sealed class WeaponDefinition
    {
        public WeaponDefinition(
            WeaponKind kind,
            string displayName,
            float pickupLength,
            float rigidbodyMass,
            float rigidbodyLinearDamping,
            float rigidbodyAngularDamping,
            float baseDamage,
            float minSwingSpeed,
            float maxSwingSpeedForScaling,
            float hitCooldownSeconds,
            float proximityFallbackRadius,
            float damageRadiusMultiplier = 1f,
            float damageLengthMultiplier = 1f,
            float damageForwardBias = 0f,
            bool throwOnDetach = false,
            float throwArmSpeed = 0f,
            float throwImpactDamage = 0f,
            float heavySwingSpeed = 0f,
            float stunDuration = 0f,
            float impactImpulse = 0f,
            float sweepRadius = 0f,
            float sweepDamageMultiplier = 0f,
            float tipOnlyReach = 0f)
        {
            Kind = kind;
            DisplayName = displayName;
            PickupLength = pickupLength;
            RigidbodyMass = rigidbodyMass;
            RigidbodyLinearDamping = rigidbodyLinearDamping;
            RigidbodyAngularDamping = rigidbodyAngularDamping;
            BaseDamage = baseDamage;
            MinSwingSpeed = minSwingSpeed;
            MaxSwingSpeedForScaling = maxSwingSpeedForScaling;
            HitCooldownSeconds = hitCooldownSeconds;
            ProximityFallbackRadius = proximityFallbackRadius;
            DamageRadiusMultiplier = damageRadiusMultiplier;
            DamageLengthMultiplier = damageLengthMultiplier;
            DamageForwardBias = damageForwardBias;
            ThrowOnDetach = throwOnDetach;
            ThrowArmSpeed = throwArmSpeed;
            ThrowImpactDamage = throwImpactDamage;
            HeavySwingSpeed = heavySwingSpeed;
            StunDuration = stunDuration;
            ImpactImpulse = impactImpulse;
            SweepRadius = sweepRadius;
            SweepDamageMultiplier = sweepDamageMultiplier;
            TipOnlyReach = tipOnlyReach;
        }

        public WeaponKind Kind { get; }
        public string DisplayName { get; }
        public float PickupLength { get; }
        public float RigidbodyMass { get; }
        public float RigidbodyLinearDamping { get; }
        public float RigidbodyAngularDamping { get; }
        public float BaseDamage { get; }
        public float MinSwingSpeed { get; }
        public float MaxSwingSpeedForScaling { get; }
        public float HitCooldownSeconds { get; }
        public float ProximityFallbackRadius { get; }
        public float DamageRadiusMultiplier { get; }
        public float DamageLengthMultiplier { get; }
        public float DamageForwardBias { get; }
        public bool ThrowOnDetach { get; }
        public float ThrowArmSpeed { get; }
        public float ThrowImpactDamage { get; }
        public float HeavySwingSpeed { get; }
        public float StunDuration { get; }
        public float ImpactImpulse { get; }
        public float SweepRadius { get; }
        public float SweepDamageMultiplier { get; }
        public float TipOnlyReach { get; }
    }

    [Serializable]
    public sealed class SpellDefinition
    {
        public SpellDefinition(
            SpellKind kind,
            string displayName,
            float cooldownSeconds,
            float projectileSpeed,
            float lifetimeSeconds)
        {
            Kind = kind;
            DisplayName = displayName;
            CooldownSeconds = cooldownSeconds;
            ProjectileSpeed = projectileSpeed;
            LifetimeSeconds = lifetimeSeconds;
        }

        public SpellKind Kind { get; }
        public string DisplayName { get; }
        public float CooldownSeconds { get; }
        public float ProjectileSpeed { get; }
        public float LifetimeSeconds { get; }
    }

    [Serializable]
    public sealed class UpgradeDefinition
    {
        public UpgradeDefinition(
            UpgradeKind kind,
            string displayName,
            string description,
            string artResourcePath,
            WeaponKind weaponKind = WeaponKind.None,
            SpellKind spellKind = SpellKind.None)
        {
            Kind = kind;
            DisplayName = displayName;
            Description = description;
            ArtResourcePath = artResourcePath;
            WeaponKind = weaponKind;
            SpellKind = spellKind;
        }

        public UpgradeKind Kind { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public string ArtResourcePath { get; }
        public WeaponKind WeaponKind { get; }
        public SpellKind SpellKind { get; }
    }

    public static class RunCatalog
    {
        static readonly CardDefinition[] s_Cards =
        {
            new CardDefinition("daggers", "Daggers", CardRewardType.Weapon, "Daggers", "CombatModels/daggerscard", weaponKind: WeaponKind.Dagger),
            new CardDefinition("fireball", "Fireball", CardRewardType.Spell, "Fireball", "CombatModels/fireballcard", spellKind: SpellKind.Fireball),
            new CardDefinition("flintlock", "Flintlock", CardRewardType.Weapon, "Flintlock", "CombatModels/flintlockcard", weaponKind: WeaponKind.Flintlock),
            new CardDefinition("frost", "Frost", CardRewardType.Spell, "Frost", "CombatModels/icecard", spellKind: SpellKind.Frost, cardMeshAliases: new[] { "Ice" }),
            new CardDefinition("lightning", "Lightning", CardRewardType.Spell, "Lightning", "CombatModels/lightningcard", spellKind: SpellKind.Lightning),
            new CardDefinition("mace", "Mace", CardRewardType.Weapon, "Mace", "CombatModels/macecard", weaponKind: WeaponKind.Mace),
            new CardDefinition("shield", "Shield", CardRewardType.Weapon, "Shield", "CombatModels/shieldcard", weaponKind: WeaponKind.Shield),
            new CardDefinition("sword", "Sword", CardRewardType.Weapon, "Sword", "CombatModels/swordcard", weaponKind: WeaponKind.Sword),
            new CardDefinition("spear", "Spear", CardRewardType.Weapon, "Trident", "CombatModels/spearcard", weaponKind: WeaponKind.Spear, cardMeshAliases: new[] { "Spear" })
        };

        static readonly WeaponDefinition[] s_Weapons =
        {
            new WeaponDefinition(WeaponKind.Chain, "Chain", pickupLength: 0.92f, rigidbodyMass: 1.15f, rigidbodyLinearDamping: 0.12f, rigidbodyAngularDamping: 0.18f, baseDamage: 15f, minSwingSpeed: 0.55f, maxSwingSpeedForScaling: 9f, hitCooldownSeconds: 0.08f, proximityFallbackRadius: 0.1f),
            new WeaponDefinition(WeaponKind.Dagger, "Daggers", pickupLength: 0.6f, rigidbodyMass: 0.58f, rigidbodyLinearDamping: 0.08f, rigidbodyAngularDamping: 0.1f, baseDamage: 12f, minSwingSpeed: 0.35f, maxSwingSpeedForScaling: 6.2f, hitCooldownSeconds: 0.07f, proximityFallbackRadius: 0.1f, damageRadiusMultiplier: 0.82f, damageLengthMultiplier: 0.72f, throwOnDetach: true, throwArmSpeed: 2.8f, throwImpactDamage: 32f),
            new WeaponDefinition(WeaponKind.Flintlock, "Flintlock", pickupLength: 0.75f, rigidbodyMass: 1f, rigidbodyLinearDamping: 0.12f, rigidbodyAngularDamping: 0.14f, baseDamage: 0f, minSwingSpeed: 1f, maxSwingSpeedForScaling: 1f, hitCooldownSeconds: 0.2f, proximityFallbackRadius: 0f),
            new WeaponDefinition(WeaponKind.Mace, "Mace", pickupLength: 0.78f, rigidbodyMass: 1.95f, rigidbodyLinearDamping: 0.22f, rigidbodyAngularDamping: 0.28f, baseDamage: 19f, minSwingSpeed: 0.78f, maxSwingSpeedForScaling: 8.4f, hitCooldownSeconds: 0.1f, proximityFallbackRadius: 0.12f, damageRadiusMultiplier: 1.45f, damageLengthMultiplier: 0.38f, damageForwardBias: 0.18f, heavySwingSpeed: 1.35f, stunDuration: 0.45f, impactImpulse: 1.75f),
            new WeaponDefinition(WeaponKind.Shield, "Shield", pickupLength: 0.65f, rigidbodyMass: 2.5f, rigidbodyLinearDamping: 0.2f, rigidbodyAngularDamping: 0.22f, baseDamage: 13f, minSwingSpeed: 0.65f, maxSwingSpeedForScaling: 6f, hitCooldownSeconds: 0.1f, proximityFallbackRadius: 0.2f),
            new WeaponDefinition(WeaponKind.Spear, "Spear", pickupLength: 1.42f, rigidbodyMass: 1.3f, rigidbodyLinearDamping: 0.14f, rigidbodyAngularDamping: 0.17f, baseDamage: 18f, minSwingSpeed: 0.52f, maxSwingSpeedForScaling: 7.2f, hitCooldownSeconds: 0.1f, proximityFallbackRadius: 0.04f, damageRadiusMultiplier: 0.68f, damageLengthMultiplier: 0.22f, damageForwardBias: 0.36f, tipOnlyReach: 0.2f),
            new WeaponDefinition(WeaponKind.Sword, "Sword", pickupLength: 0.95f, rigidbodyMass: 1.08f, rigidbodyLinearDamping: 0.11f, rigidbodyAngularDamping: 0.13f, baseDamage: 15f, minSwingSpeed: 0.42f, maxSwingSpeedForScaling: 7.1f, hitCooldownSeconds: 0.08f, proximityFallbackRadius: 0.16f, damageRadiusMultiplier: 1.22f, damageLengthMultiplier: 1.15f, damageForwardBias: 0.06f, sweepRadius: 0.55f, sweepDamageMultiplier: 0.55f)
        };

        static readonly SpellDefinition[] s_Spells =
        {
            new SpellDefinition(SpellKind.Fireball, "Fireball", cooldownSeconds: 0.65f, projectileSpeed: 12f, lifetimeSeconds: 2.2f),
            new SpellDefinition(SpellKind.Frost, "Frost", cooldownSeconds: 0.5f, projectileSpeed: 14f, lifetimeSeconds: 1.6f),
            new SpellDefinition(SpellKind.Lightning, "Lightning", cooldownSeconds: 0.9f, projectileSpeed: 0f, lifetimeSeconds: 0.2f)
        };

        static readonly UpgradeDefinition[] s_Upgrades =
        {
            new UpgradeDefinition(UpgradeKind.AllMeleeDamage, "All Melee", "Melee hits deal 10% more damage.", "CombatModels/Ui/Allmeleedamage+"),
            new UpgradeDefinition(UpgradeKind.ChainDamage, "Chain Damage", "Chain swings deal 15% more damage.", "CombatModels/Ui/Chaindamage+", weaponKind: WeaponKind.Chain),
            new UpgradeDefinition(UpgradeKind.DaggerDamage, "Dagger Damage", "Daggers deal 15% more damage.", "CombatModels/Ui/Daggerdamage+", weaponKind: WeaponKind.Dagger),
            new UpgradeDefinition(UpgradeKind.FireDamage, "Fire Damage", "Fireball direct and burn damage increase by 15%.", "CombatModels/Ui/Firedamage+", spellKind: SpellKind.Fireball),
            new UpgradeDefinition(UpgradeKind.FlintlockDamage, "Flintlock Damage", "Flintlock shots deal 15% more damage.", "CombatModels/Ui/Flintlockdamage+", weaponKind: WeaponKind.Flintlock),
            new UpgradeDefinition(UpgradeKind.IceDamage, "Ice Damage", "Icicles deal 15% more damage.", "CombatModels/Ui/icedamage+", spellKind: SpellKind.Frost),
            new UpgradeDefinition(UpgradeKind.LightningDamage, "Lightning Damage", "Lightning strikes and arcs deal 15% more damage.", "CombatModels/Ui/Lightningdamage+", spellKind: SpellKind.Lightning),
            new UpgradeDefinition(UpgradeKind.MaceDamage, "Mace Damage", "Mace swings deal 15% more damage.", "CombatModels/Ui/Macedamage+", weaponKind: WeaponKind.Mace),
            new UpgradeDefinition(UpgradeKind.PlayerSpeed, "Player Speed", "Locomotion speed increases by 10%.", "CombatModels/Ui/Playerspeed+"),
            new UpgradeDefinition(UpgradeKind.ShieldKnockbackUnlock, "Shield Bash", "Unlock shield bash knockback. Extra ranks add 25% more bash force.", "CombatModels/Ui/Shieldknockbackunlock", weaponKind: WeaponKind.Shield),
            new UpgradeDefinition(UpgradeKind.SpearDamage, "Spear Damage", "Spear thrusts deal 15% more damage.", "CombatModels/Ui/Speardamage+", weaponKind: WeaponKind.Spear),
            new UpgradeDefinition(UpgradeKind.SwordDamage, "Sword Damage", "Sword swings deal 15% more damage.", "CombatModels/Ui/Sworddamage+", weaponKind: WeaponKind.Sword)
        };

        static readonly Dictionary<UpgradeKind, UpgradeDefinition> s_UpgradeByKind = BuildUpgradeLookup();
        static readonly Dictionary<SpellKind, SpellDefinition> s_SpellByKind = BuildSpellLookup();
        static readonly Dictionary<WeaponKind, CardDefinition> s_CardByWeaponKind = BuildWeaponCardLookup();
        static readonly Dictionary<SpellKind, CardDefinition> s_CardBySpellKind = BuildSpellCardLookup();
        static readonly Dictionary<WeaponKind, WeaponDefinition> s_WeaponByKind = BuildWeaponLookup();

        public static IReadOnlyList<CardDefinition> Cards => s_Cards;
        public static IReadOnlyList<UpgradeDefinition> Upgrades => s_Upgrades;

        public static bool IsMeleeWeapon(WeaponKind weaponKind)
        {
            return weaponKind == WeaponKind.Chain ||
                   weaponKind == WeaponKind.Dagger ||
                   weaponKind == WeaponKind.Mace ||
                   weaponKind == WeaponKind.Shield ||
                   weaponKind == WeaponKind.Spear ||
                   weaponKind == WeaponKind.Sword;
        }

        public static bool TryGetCardForWeapon(WeaponKind weaponKind, out CardDefinition definition)
        {
            return s_CardByWeaponKind.TryGetValue(weaponKind, out definition);
        }

        public static bool TryGetCardForSpell(SpellKind spellKind, out CardDefinition definition)
        {
            return s_CardBySpellKind.TryGetValue(spellKind, out definition);
        }

        public static bool TryGetSpell(SpellKind spellKind, out SpellDefinition definition)
        {
            return s_SpellByKind.TryGetValue(spellKind, out definition);
        }

        public static bool TryGetWeapon(WeaponKind weaponKind, out WeaponDefinition definition)
        {
            return s_WeaponByKind.TryGetValue(weaponKind, out definition);
        }

        public static bool TryGetUpgrade(UpgradeKind upgradeKind, out UpgradeDefinition definition)
        {
            return s_UpgradeByKind.TryGetValue(upgradeKind, out definition);
        }

        static Dictionary<UpgradeKind, UpgradeDefinition> BuildUpgradeLookup()
        {
            var lookup = new Dictionary<UpgradeKind, UpgradeDefinition>();
            for (var i = 0; i < s_Upgrades.Length; i++)
                lookup[s_Upgrades[i].Kind] = s_Upgrades[i];

            return lookup;
        }

        static Dictionary<SpellKind, SpellDefinition> BuildSpellLookup()
        {
            var lookup = new Dictionary<SpellKind, SpellDefinition>();
            for (var i = 0; i < s_Spells.Length; i++)
                lookup[s_Spells[i].Kind] = s_Spells[i];

            return lookup;
        }

        static Dictionary<WeaponKind, CardDefinition> BuildWeaponCardLookup()
        {
            var lookup = new Dictionary<WeaponKind, CardDefinition>();
            for (var i = 0; i < s_Cards.Length; i++)
            {
                var card = s_Cards[i];
                if (card.WeaponKind != WeaponKind.None)
                    lookup[card.WeaponKind] = card;
            }

            return lookup;
        }

        static Dictionary<WeaponKind, WeaponDefinition> BuildWeaponLookup()
        {
            var lookup = new Dictionary<WeaponKind, WeaponDefinition>();
            for (var i = 0; i < s_Weapons.Length; i++)
                lookup[s_Weapons[i].Kind] = s_Weapons[i];

            return lookup;
        }

        static Dictionary<SpellKind, CardDefinition> BuildSpellCardLookup()
        {
            var lookup = new Dictionary<SpellKind, CardDefinition>();
            for (var i = 0; i < s_Cards.Length; i++)
            {
                var card = s_Cards[i];
                if (card.SpellKind != SpellKind.None)
                    lookup[card.SpellKind] = card;
            }

            return lookup;
        }
    }
}

# Core Mechanics UML

These Mermaid diagrams are based on the current runtime code under `Assets/Scripts`.
They focus on presentation-level structure while keeping class and method names tied to the implementation.

## Source Map

- `Assets/Scripts/Core/VRCombatBootstrapper.cs`: run orchestration, wave loop, spawning, boss/victory, pause/restart, card rewards.
- `Assets/Scripts/Core/RunProgressionController.cs`: XP, level-up choices, owned weapons, unlocked spells, upgrade multipliers, MahmutCanKovan flags.
- `Assets/Scripts/Core/CardTableRuntime.cs`: physical card table, card pickup/drop activation.
- `Assets/Scripts/Enemies/CapsuleEnemy.cs`: enemy AI, rarity scaling, boss stats, health, contact damage, status effects, death.
- `Assets/Scripts/Player/PlayerSpellLoadout.cs`: selected spell, hand input, spell casting, projectile/status behavior.
- `Assets/Scripts/Combat/SwingDamageDealer.cs`: melee collision damage, swing-speed scaling, progression multipliers.
- `Assets/Scripts/Combat/FlintlockWeapon.cs`: held-hand firing, reload state, projectile/hitscan impact.
- `Assets/Scripts/Environment/ArenaOpeningEncounter.cs`: authored encounter gates, key carrier flow, wave resume.
- `Assets/Scripts/UI/CombatHUDRuntime.cs`: HUD panels for health, waves, upgrades, pause, debug, victory.

## 1. Runtime Component Class Diagram

```mermaid
classDiagram
    direction LR

    class VRCombatBootstrapper {
        +Start()
        -StartWaveLoop()
        +SpawnEnemyAt()
        +SpawnCardRewardWeapon()
        +ShowRuntimeBanner()
        +ActivateSpecialCard()
        -ContinueEndlessMode()
        -RestartRunRoutine()
    }

    class RunProgressionController {
        +ResetRun()
        +DrawNextCard()
        +AwardXp(amount)
        +RegisterWeaponAcquired(kind)
        +UnlockSpell(kind)
        +GrantUpgradeForDebug(kind)
        +ActivateMahmutCanKovanEasterEgg()
        +GetWeaponDamageMultiplier(kind)
        +GetSpellDamageMultiplier(kind)
        +GetPlayerSpeedMultiplier()
    }

    class CombatHUDRuntime {
        +SetHealth(current,max)
        +SetWaveInfo(wave,enemies,flow)
        +ShowUpgradeChoices()
        +SetPauseMenuVisible(visible)
        +ShowVictoryPanel()
        +ShowBanner(message,duration)
    }

    class CardTableRuntime {
        +MountCardChoices(first,second)
        +CreateLooseCard()
        +ClearCards()
    }

    class CardPickup {
        -OnSelectEntered()
        -OnSelectExited()
        -CommitChoice()
    }

    class ArenaOpeningEncounter {
        +BeginEncounter(bootstrapper)
        +PrepareLockedGate(bootstrapper)
        +SpawnConfiguredKeyAt()
        +ResetEncounter()
        +EncounterCompleted
    }

    class CapsuleEnemy {
        +SetRarity(rarity,keyPrefab)
        +SetPlayerTarget(camera,damageReceiver)
        +ApplyDamage(amount,hitPoint,source)
        +HandlePlayerContact(player)
        +ApplySlow(multiplier,duration,source)
        +ApplyBurn(dps,duration,source)
        +ApplyStun(duration)
        +Died
    }

    class PlayerDamageReceiver {
        +ReceiveDamage(amount)
        +RestoreFullHealth()
        +ForceKill()
        +ApplyKnockback(direction,full,zero)
        +HealthChanged
        +DamageTaken
        +Died
    }

    class PlayerSpellLoadout {
        +Configure()
        +SetSpellsEnabled(enabled)
        -TryCastSelectedSpell()
        -CastFireball()
        -CastFrostVolley()
        -CastLightning()
    }

    class SwingDamageDealer {
        +Configure()
        +ConfigureRuntimeModifiers()
        -TryDealDamage()
    }

    class FlintlockWeapon {
        +ConfigureRuntimeSetup()
        +SetMahmutCanKovanMode(enabled)
        +Reload()
        -Fire()
        -RefreshHeldDevice()
    }

    class IDamageable {
        <<interface>>
        +ApplyDamage(amount,hitPoint,source)
    }

    class IStatusEffectTarget {
        <<interface>>
        +ApplySlow(multiplier,duration,source)
        +ApplyBurn(dps,duration,source)
        +ApplyStun(duration)
    }

    VRCombatBootstrapper --> RunProgressionController : initializes and queries
    VRCombatBootstrapper --> CombatHUDRuntime : updates panels
    VRCombatBootstrapper --> CardTableRuntime : mounts reward choices
    VRCombatBootstrapper --> ArenaOpeningEncounter : starts or resumes waves
    VRCombatBootstrapper --> CapsuleEnemy : spawns and tracks
    VRCombatBootstrapper --> PlayerDamageReceiver : subscribes to death/damage
    VRCombatBootstrapper --> PlayerSpellLoadout : configures spell input
    CardTableRuntime --> CardPickup : creates
    CardPickup --> VRCombatBootstrapper : reward callbacks
    CardPickup --> RunProgressionController : weapons and spells
    SwingDamageDealer --> RunProgressionController : damage multiplier
    FlintlockWeapon --> RunProgressionController : flintlock multiplier/reloadless mode
    PlayerSpellLoadout --> RunProgressionController : selected spell and multipliers
    CapsuleEnemy ..|> IDamageable
    CapsuleEnemy ..|> IStatusEffectTarget
    SwingDamageDealer ..> IDamageable : damages
    PlayerSpellLoadout ..> IDamageable : spell hits
    PlayerSpellLoadout ..> IStatusEffectTarget : burn/slow
    CapsuleEnemy --> PlayerDamageReceiver : contact damage
```

## 2. Run And Wave Progression Sequence

```mermaid
sequenceDiagram
    autonumber
    participant B as VRCombatBootstrapper
    participant W as WristChainIntroController
    participant E as ArenaOpeningEncounter
    participant HUD as CombatHUDRuntime
    participant Enemy as CapsuleEnemy
    participant Prog as RunProgressionController

    B->>B: RestartRunRoutine(initialStartup)
    B->>Prog: ResetRun()
    B->>W: Begin(..., HandleWristChainBroken, HandleIntroChainsCompleted)
    W-->>B: HandleWristChainBroken(hand, position, rotation)
    B->>B: SpawnCardRewardWeapon(Chain, position, rotation)
    W-->>B: HandleIntroChainsCompleted()

    alt authored intro encounter exists
        B->>E: BeginEncounter(this)
        E-->>B: EncounterCompleted(encounter)
        B->>B: StartWaveLoop()
    else no encounter starts/resumes waves
        B->>B: StartWaveLoop()
    end

    loop each wave
        B->>HUD: SetWaveInfo(currentWave,totalEnemies,flowRate)
        B->>HUD: ShowBanner("Wave N starting")

        alt wave 15 and not endless
            B->>B: SpawnWaveBoss()
            B->>Enemy: SetRarity(Boss)
            B->>HUD: ShowBanner("Boss goblin has entered the arena")
        end

        loop normal enemy count
            B->>B: TryResolveWaveEnemySpawnPosition()
            B->>Enemy: SpawnEnemyAt(position, rarity)
            Enemy-->>B: Died(enemy)
            B->>Prog: AwardXp(EnemyXpReward)
        end

        B->>HUD: SetWaveInfo(currentWave, aliveNow, flowRate)

        alt wave 15 cleared
            B->>HUD: ShowVictoryPanel()
            B->>B: Time.timeScale = 0
            alt Continue Endless
                HUD-->>B: ContinueEndlessMode()
                B->>B: m_EndlessModeActive = true
                B->>B: currentWave = 16
            else Restart
                HUD-->>B: RestartRunRoutine(false)
            end
        else wave 5 or 10 milestone
            B->>E: PrepareLockedGate(this)
            B->>Enemy: SpawnMilestoneKeyCarrier()
            Enemy-->>B: HandleMilestoneKeyCarrierDied(enemy)
            B->>E: SpawnConfiguredKeyAt(this, enemy.position)
            E-->>B: EncounterCompleted(encounter)
            B->>B: resume next wave
        else regular clear
            B->>HUD: ShowBanner("Wave cleared")
            B->>B: currentWave++
        end
    end
```

## 3. Enemy Lifecycle State Diagram

```mermaid
stateDiagram-v2
    [*] --> Spawned
    Spawned --> Active: SpawnEnemyAt()

    Active --> Chasing: target assigned
    Chasing --> Avoiding: obstacle or enemy separation
    Avoiding --> Chasing: direction resolved

    Chasing --> RareJump: rarity == Rare and ability ready
    RareJump --> Chasing: landing detected

    Chasing --> ChargeWindup: rarity == Epic or Boss and ability ready
    ChargeWindup --> Charging: m_IsCharging = true
    Charging --> Chasing: charge duration ends

    Chasing --> Grabbed: XRGrabInteractable.selectEntered
    Charging --> Grabbed: XRGrabInteractable.selectEntered
    Grabbed --> Chasing: selectExited and resume delay elapsed

    Chasing --> Slowed: ApplySlow()
    Slowed --> Chasing: slow timer expires
    Chasing --> Burning: ApplyBurn()
    Burning --> Burning: UpdateStatusEffects tick damage
    Burning --> Chasing: burn timer expires

    Chasing --> Dying: ApplyDamage drops health to zero
    Charging --> Dying: ApplyDamage drops health to zero
    Grabbed --> Dying: ApplyDamage drops health to zero
    Burning --> Dying: burn damage kills

    Dying --> DeathCleanup: BeginDeathSequence()
    DeathCleanup --> [*]: Died event, key drop, despawn
```

## 4. Card Reward And Upgrade Flow

```mermaid
sequenceDiagram
    autonumber
    participant Prog as RunProgressionController
    participant HUD as CombatHUDRuntime
    participant Table as CardTableRuntime
    participant Card as CardPickup
    participant B as VRCombatBootstrapper
    participant Enemy as CapsuleEnemy
    participant Spell as PlayerSpellLoadout
    participant Weapon as Runtime Weapon

    Prog->>Prog: DrawNextCard()
    B->>Table: MountCardChoices(firstChoice, secondChoice, Prog, B)
    Table->>Card: Initialize(cardDefinition, Prog, B)
    Card->>Card: OnSelectEntered()
    Card->>Card: OnSelectExited()
    Card->>Card: CommitChoice()

    alt RewardType == Weapon
        Card->>Prog: RegisterWeaponAcquired(WeaponKind)
        Card->>B: SpawnCardRewardWeapon(WeaponKind, pose)
        B->>Weapon: ConfigureRuntimeModifiers(Prog)
        B->>HUD: ShowBanner("weapon acquired")
    else RewardType == Spell
        Card->>Prog: UnlockSpell(SpellKind)
        Prog->>HUD: SetSelectedSpell(...)
        Prog-->>B: ProgressionChanged
        B->>Spell: SetSpellsEnabled(true)
        B->>HUD: ShowBanner("spell unlocked")
    else RewardType == Special
        Card->>B: ActivateSpecialCard(MahmutCanKovan)
        B->>Prog: ActivateMahmutCanKovanEasterEgg()
        Prog-->>B: ProgressionChanged
        B->>B: ApplyPlayerSpeedMultiplier()
        B->>B: ApplyMahmutCanKovanRuntimeEffects()
    end

    Enemy-->>B: Died(enemy)
    B->>Prog: AwardXp(amount)
    Prog->>Prog: level up when XP reaches nextLevel
    Prog->>HUD: ShowUpgradeChoices(3 choices)
    HUD-->>Prog: HandleUpgradeSelected(index)
    Prog->>Prog: increment UpgradeKind stack
    Prog-->>B: ProgressionChanged
    B->>B: apply speed/flintlock/spell effects
```

## 5. Combat Damage Sequence

```mermaid
sequenceDiagram
    autonumber
    participant Player as Player Input/Rig
    participant Melee as SwingDamageDealer
    participant Gun as FlintlockWeapon
    participant Spell as PlayerSpellLoadout
    participant Enemy as CapsuleEnemy
    participant Prog as RunProgressionController
    participant HP as PlayerDamageReceiver
    participant HUD as CombatHUDRuntime
    participant B as VRCombatBootstrapper

    alt melee swing
        Player->>Melee: weapon collider moves fast enough
        Melee->>Prog: GetWeaponDamageMultiplier(WeaponKind)
        Melee->>Enemy: ApplyDamage(scaledDamage, hitPoint, weapon)
        Enemy->>Enemy: EmitBloodSplatter and ApplyDamageKnockback
    else flintlock shot
        Player->>Gun: trigger from held hand only
        Gun->>Gun: Fire()
        Gun->>Enemy: ApplyDamage(damageAmount * multiplier, impact, gun)
        Gun->>Gun: Reload() or stay loaded in MahmutCanKovan mode
    else spell cast
        Player->>Spell: trigger with empty hand
        Spell->>Prog: SelectedSpellKind and GetSpellDamageMultiplier()
        alt Fireball
            Spell->>Enemy: ApplyDamage(7 * multiplier)
            Spell->>Enemy: ApplyBurn(10 dps * multiplier, 5s)
        else Frost
            Spell->>Enemy: ApplyDamage(7 * multiplier)
            Spell->>Enemy: ApplySlow(0.45, 2.5s)
        else Lightning
            Spell->>Enemy: ApplyDamage(18 * multiplier)
            Spell->>Enemy: chain to up to 2 nearby enemies
        end
    end

    alt enemy health reaches zero
        Enemy-->>B: Died(enemy)
        B->>Prog: AwardXp(EnemyXpReward)
    end

    alt enemy touches player
        Enemy->>HP: ReceiveDamage(contactDamage)
        Enemy->>HP: ApplyKnockback(direction, fullHealth, zeroHealth)
        HP-->>HUD: HealthChanged(current,max)
        HP-->>B: DamageTaken(amount)
        alt player dies or hit limit reached
            HP-->>B: Died()
            B->>HUD: FadeToBlack()
            B->>HUD: Show death UI
        end
    end
```

## 6. Encounter And Milestone Gate Sequence

```mermaid
sequenceDiagram
    autonumber
    participant B as VRCombatBootstrapper
    participant E as ArenaOpeningEncounter
    participant Enemy as CapsuleEnemy
    participant Key as KeyItem
    participant Padlock as Padlock
    participant Gate as GateController
    participant HUD as CombatHUDRuntime

    alt encounter starts automatically or by trigger
        B->>E: BeginEncounter(this)
        E->>B: ReserveEncounterSpace(...)
        E->>E: PrepareLockedGateInternal()
        E->>B: SpawnEnemyAt(local spawn positions)
        E->>HUD: ShowBanner("Defeat the goblins...")
    end

    alt milestone after wave 5 or 10
        B->>E: PrepareLockedGate(this)
        B->>Enemy: SpawnMilestoneKeyCarrier()
        Enemy-->>B: Died(enemy)
        B->>E: SpawnConfiguredKeyAt(this, enemy.position)
    end

    Key->>Padlock: player strikes lock with key
    Padlock-->>E: unlocked callback
    E->>Gate: Open()
    E->>E: move platform/bridges if configured
    E-->>B: EncounterCompleted(this)

    alt StartsOrResumesWavesOnCompletion
        B->>B: StartWaveLoop() or resume deferred wave
    end
```

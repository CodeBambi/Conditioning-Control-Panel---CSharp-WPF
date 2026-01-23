# Bubble Drift - Jetpack Joyride Style Minigame

## Overview
A Jetpack Joyride-inspired endless runner minigame featuring the neon pink aesthetic. Player controls a girl in a glowing bubble, holding to rise and releasing to fall, dodging obstacles and collecting items.

---

## Architecture

### File Structure
```
ConditioningControlPanel/
├── Services/
│   └── BubbleDriftService.cs          # Game trigger & XP management
│
├── Games/
│   └── BubbleDrift/
│       ├── BubbleDriftWindow.xaml     # Game window UI
│       ├── BubbleDriftWindow.xaml.cs  # Game loop & logic
│       ├── BubbleDriftGameState.cs    # Score, health, distance state
│       ├── GameObjects/
│       │   ├── Player.cs              # Player entity with physics
│       │   ├── Obstacle.cs            # Obstacle base class
│       │   └── Collectible.cs         # Collectible base class
│       └── BubbleDriftHighScores.cs   # Personal best tracking
│
├── Models/
│   └── MinigameSettings.cs            # Global minigame settings
│   └── HapticSettings.cs              # (existing - add minigame props)
│
└── Assets/
    └── games/
        └── bubble-drift/
            ├── preview.png            # 600x400 tab preview image
            ├── player-bubble.png      # Player sprite
            ├── bubble-trail.png       # Trail particle
            ├── obstacles/
            │   ├── alarm-clock.png
            │   ├── homework.png
            │   ├── angry-face.png
            │   └── gear.png
            ├── collectibles/
            │   ├── heart.png
            │   ├── lipstick.png
            │   ├── candy.png
            │   └── lollipop.png
            └── background/
                ├── clouds.png
                └── gears-ground.png
```

---

## Minigames Tab Design

New tab in MainWindow with 2x2 grid of game cards:
- **Bubble Drift** - Active, playable
- **Coming Soon** x3 - Placeholder slots for future minigames

Each card contains:
- 600x400 preview image
- Game description
- High score / best distance display
- Play button

---

## Game Window Layout (~800x550)

```
┌─────────────────────────────────────────────────────────────────────┐
│ ☁️☁️☁️☁️☁️☁️☁️☁️ PARALLAX CLOUDS (slow) ☁️☁️☁️☁️☁️☁️☁️☁️ │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│      [PLAYER]  ←─────  ⏰  📕  😠  💄  ❤️  🍭  ←─── spawn        │
│        ⚪                                                            │
│       ╰bubbles                                                       │
│                                                                      │
├─────────────────────────────────────────────────────────────────────┤
│ ⚙️⚙️⚙️⚙️⚙️⚙️⚙️ PARALLAX GEARS (fast) ⚙️⚙️⚙️⚙️⚙️⚙️⚙️ │
├─────────────────────────────────────────────────────────────────────┤
│  SCORE: 1,250  │  DISTANCE: 847m  │  ❤️❤️❤️  │  [ESC: Pause]      │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Game Mechanics

### Physics Constants
```csharp
private const double Gravity = 0.5;           // Pixels per frame²
private const double ThrustForce = -1.2;      // Negative = upward
private const double MaxVelocity = 12.0;      // Terminal velocity
private const double InitialScrollSpeed = 4.0;
private const double MaxScrollSpeed = 12.0;
private const double SpeedIncreaseRate = 0.001; // Per frame
```

### Input
- **Hold** (Mouse/Spacebar) = Rise (thrust)
- **Release** = Fall (gravity)
- **ESC** = Pause

### Object Types

| Type | Sprite | Behavior | Effect |
|------|--------|----------|--------|
| 🔴 Alarm Clock | alarm-clock.png | Floats mid-screen | -1 HP, screen shake |
| 🔴 Homework | homework.png | Moves in sine wave | -1 HP |
| 🔴 Angry Face | angry-face.png | Tracks player slightly | -1 HP |
| 🔴 Gears | gear.png | Floor obstacles | Instant death |
| 🟢 Heart | heart.png | Floats gently | +1 HP (max 3) |
| 🟢 Lipstick | lipstick.png | Static | +50 score |
| 🟢 Candy | candy.png | Cluster spawn | +25 score |
| 🟡 Pink Orb | (special) | Rare | Invincibility 5s |

### Spawn Patterns
```csharp
enum ObstaclePattern
{
    Single,          // One obstacle at random Y
    VerticalPair,    // Two obstacles, gap in middle
    Diagonal,        // 3 obstacles in diagonal line
    Wave,            // Sine-wave positioned obstacles
    Wall,            // Multiple obstacles with one gap
}
```

### Difficulty Scaling
- Scroll speed increases over time (caps at MaxScrollSpeed)
- Obstacle density increases with distance
- Collectible frequency slightly decreases

---

## Haptics Integration

### New HapticSettings Properties
```csharp
// Minigame - Thrust (continuous while holding)
public bool MinigameThrustEnabled { get; set; } = true;
public double MinigameThrustIntensity { get; set; } = 0.6;
public VibrationMode MinigameThrustMode { get; set; } = VibrationMode.Constant;

// Minigame - Collectible pickup (pulse)
public bool MinigameCollectEnabled { get; set; } = true;
public double MinigameCollectIntensity { get; set; } = 0.8;
public VibrationMode MinigameCollectMode { get; set; } = VibrationMode.Pulse;

// Minigame - Obstacle hit (jolt)
public bool MinigameHitEnabled { get; set; } = true;
public double MinigameHitIntensity { get; set; } = 1.0;
public VibrationMode MinigameHitMode { get; set; } = VibrationMode.Earthquake;

// Minigame - Game over
public bool MinigameEndEnabled { get; set; } = true;
public double MinigameEndIntensity { get; set; } = 0.5;
public VibrationMode MinigameEndMode { get; set; } = VibrationMode.Heartbeat;
```

### Thrust Haptic Loop
```csharp
private bool _isThrusting;
private CancellationTokenSource? _thrustHapticCts;

private async void OnMouseDown(object sender, MouseButtonEventArgs e)
{
    _isThrusting = true;
    await StartThrustHapticsAsync();
}

private async void OnMouseUp(object sender, MouseButtonEventArgs e)
{
    _isThrusting = false;
    await StopThrustHapticsAsync();
}

private async Task StartThrustHapticsAsync()
{
    if (!App.Settings.Current.Haptic.MinigameThrustEnabled) return;
    if (App.Haptic == null || !App.Haptic.IsConnected) return;

    _thrustHapticCts = new CancellationTokenSource();
    var intensity = App.Settings.Current.Haptic.MinigameThrustIntensity
                  * App.Settings.Current.Haptic.GlobalIntensity;

    try
    {
        // Continuous vibration while thrusting
        while (!_thrustHapticCts.Token.IsCancellationRequested && _isThrusting)
        {
            await App.Haptic.VibrateAsync(intensity, 100); // 100ms pulses
            await Task.Delay(80, _thrustHapticCts.Token);  // Slight overlap
        }
    }
    catch (OperationCanceledException) { }
}

private async Task StopThrustHapticsAsync()
{
    _thrustHapticCts?.Cancel();
    if (App.Haptic?.IsConnected == true)
    {
        await App.Haptic.StopAsync();
    }
}
```

---

## XP & Achievements

### XP Rewards
```csharp
public void OnGameEnd(BubbleDriftGameState state)
{
    // Distance-based XP: 1 XP per 50 meters
    int distanceXp = (int)(state.DistanceMeters / 50);

    // Collectible bonus: 2 XP per collectible
    int collectibleXp = state.CollectiblesGathered * 2;

    // Survival bonus: 5 XP per 30 seconds survived
    int survivalXp = (int)(state.SurvivalTimeSeconds / 30) * 5;

    int totalXp = distanceXp + collectibleXp + survivalXp;

    App.Progression?.AddXP(totalXp, "Bubble Drift");
}
```

### Achievements
| Achievement | Condition |
|-------------|-----------|
| "First Flight" | Complete first Bubble Drift game |
| "Frequent Flyer" | Play Bubble Drift 10 times |
| "Mile High Club" | Travel 1000m in single run |
| "Marathon Runner" | Travel 5000m total |
| "Collector" | Gather 100 collectibles |
| "Untouchable" | Travel 500m without getting hit |
| "Speed Demon" | Reach max scroll speed |

---

## Implementation Phases

| Phase | Tasks | Complexity |
|-------|-------|------------|
| **1. Tab Infrastructure** | Add Minigames tab button, tab grid, 4 card placeholders, navigation | Low |
| **2. BubbleDriftService** | Game launcher, InteractionQueue registration, XP handler | Low |
| **3. Game Window Shell** | Window creation, Canvas setup, basic HUD, ESC to close | Medium |
| **4. Player & Physics** | Player sprite, thrust/gravity mechanics, boundary clamping | Medium |
| **5. Obstacles & Collectibles** | Spawning, movement, pooling, collision detection | Medium |
| **6. Haptics Integration** | Thrust vibration loop, hit/collect pulses, game over pattern | Medium |
| **7. Parallax Background** | Cloud layer, ground/gears layer, seamless scrolling | Low |
| **8. Polish** | Particles, screen shake, game over screen, high scores | Medium |
| **9. Assets** | Create/integrate all sprites from neon art style | Art task |

---

## Open Questions

1. **Window size** - Fixed (800x550) or percentage of MainWindow?
2. **Input method** - Mouse only, keyboard only, or both?
3. **Pause behavior** - ESC to pause menu or ESC to quit?
4. **Level gating** - Available immediately or unlock at certain level?
5. **High scores** - Local only or sync to leaderboard?

---

## Asset Reference

Based on the provided concept image:
- **Player**: Girl in glowing pink bubble with particle trail beneath
- **Art style**: Neon pink outlines on dark background
- **Obstacles**: Alarm clocks (angry), math homework, angry emoji faces, gears
- **Collectibles**: Lipstick, hearts, candy/lollipops
- **Background**: Purple cloudy sky (top), dark middle, gears/ground (bottom)

Colors match app theme:
- Primary pink: #FF69B4
- Dark background: #1A1A2E
- Panel background: #252542

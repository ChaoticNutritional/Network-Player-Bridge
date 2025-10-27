# Bridge Pattern Context Agent/Pawn system

A flexible multiplayer architecture pattern for Unity games using PurrNet that separates **player identity** from **controlled characters**, enabling dynamic possession, context-aware input binding, intended FSM-like gameplay state transitions.

## The Problem

Traditional multiplayer architectures tightly couple the player's network identity to their character. This creates several challenges:

### Rigid Player-Character Binding
When a player's identity is bound to a single character prefab, implementing common game features becomes difficult:
- **Death sequences**: Showing a death camera or corpse view requires hacky workarounds
- **Spectator modes**: Switching to spectate other players means destroying the player object
- **Vehicle/mount systems**: Possessing vehicles while maintaining player state is complex
- **Character selection**: Changing characters mid-game requires reconnection

### Input Context Problems
Different gameplay states need different input behaviors, but traditional systems force you to either:
- Write monolithic input handlers with complex state machines
- Duplicate input code across multiple character types
- Use brittle enable/disable patterns for input components

### State Management Complexity
Managing gameplay states (alive, dead, spectating, in vehicle) becomes tangled with network identity management, making code hard to maintain and extend.

## The Solution

**NetworkPlayerBridge** implements the Bridge pattern to decouple player identity from controlled entities:

```
PlayerAgent (Network Identity)
    -> possesses
IPawn (Character, Camera, Vehicle, etc.)
    -> declares
Input Context (Move, Jump, Primary, Look, etc.)
```

### Key Benefits

1. **FSM-Like State Transitions**: Seamlessly transition between gameplay states by possessing different pawns:
   ```
   Alive (CharacterPawn) → Death (OrbitCameraPawn) → Spectating (SpectatorPawn)
   ```

2. **Context-Aware Input**: Each pawn declares its own input needs. The same "Primary" button can mean:
   - Fire weapon (in CharacterPawn)
   - Cycle spectate target (in SpectatorPawn)
   - Toggle camera mode (in DeathCameraPawn)

3. **Network Identity Persistence**: PlayerAgent maintains ownership, stats, and network identity across all pawn transitions

4. **Clean Separation of Concerns**:
   - `PlayerAgent`: Network identity, ownership, RPC routing
   - `PawnBase`: Input syncing, possession lifecycle
   - `WizardPawn`/etc: Gameplay-specific behavior

## Architecture

### Core Components

#### `PlayerAgent` (Assets/_Developers/Jack/Player/PlayerAgent.cs:6)
The persistent network identity representing the player across all pawn transitions.

```csharp
public class PlayerAgent : PlayerIdentity<PlayerAgent>
{
    public void Possess(IPawn pawn)      // Transfer control to new pawn
    public void UnPossess(IPawn pawn)    // Release current pawn
    public PlayerID? PlayerId { get; }   // Network player identifier
}
```

**Responsibilities:**
- Maintains network ownership across state transitions
- Routes input binding to owned pawns only
- Handles server-authoritative ownership transfer
- Survives pawn destruction/spawning

#### `IPawn` Interface (Assets/_Developers/Jack/Player/IPawn.cs:5)
Defines any entity that can be possessed by a PlayerAgent.

```csharp
public interface IPawn
{
    NetworkIdentity Identity { get; }
    void OnPossessed(PlayerAgent agent);
    void OnUnpossessed();
}
```

**Use Cases:**
- Character pawns (alive player)
- Death cameras (orbit around corpse)
- Spectator cameras (free-fly or follow other players)
- Vehicles, turrets, drones

#### `IInputBindable` Interface (Assets/_Developers/Jack/Player/IBindInput.cs:3)
Allows pawns to declaratively specify their input needs.

```csharp
public interface IInputBindable
{
    void BindAgentInput(AgentInputBinder binder);
}
```

Example implementation:
```csharp
public void BindAgentInput(AgentInputBinder binder)
{
    // Same action names, different behaviors per pawn
    binder.Action("Primary",
        performed: ctx => CastFireball(),  // In WizardPawn
        // vs. CycleSpectateTarget()       // In SpectatorPawn
    );
}
```

#### `PawnBase` (Assets/_Developers/Jack/Player/PawnBase.cs:6)
Base implementation providing input syncing via PurrNet's `SyncInput<T>`.

```csharp
public abstract class PawnBase : NetworkBehaviour, IPawn
{
    protected SyncInput<Vector2> _move;
    protected SyncInput<Vector2> _lookDelta;
    protected SyncInput<bool> _jump;
    protected SyncInput<bool> _primary;

    protected virtual void HandlePossessed() { }
    protected virtual void HandleUnpossessed() { }
}
```

**Features:**
- Automatic owner→server input synchronization
- Server-authoritative input consumption
- Template method pattern for pawn-specific logic

#### `AgentInputBinder` (Assets/_Developers/Jack/Player/AgentInputBinder.cs:6)
Manages Unity Input System bindings with automatic cleanup.

```csharp
binder.Action("Jump",
    performed: ctx => _jump.value = true,
    canceled: ctx => _jump.value = false
);
```

**Features:**
- Automatic unbinding on unpossess
- Action name-based binding (decoupled from input map changes)
- Started/Performed/Canceled callbacks

#### `PawnSpawner` (Assets/_Developers/Jack/Player/PawnSpawner.cs:7)
Handles server-side pawn spawning with ownership management.

```csharp
public class PawnSpawner : PurrMonoBehaviour
{
    // Spawns pawns when players load scenes
    // Handles "orphaned" pawns (spawned before PlayerAgent ready)
    // Manages spawn points and ownership transfer
}
```

**Features:**
- Scene-based spawning via PurrNet's `ScenePlayersModule`
- Orphaned pawn registry (handles race conditions)
- Spawn point rotation

## Usage Examples

### Example 1: Death Sequence FSM

```csharp
// When character dies
public void OnCharacterDeath()
{
    // Spawn death camera orbiting corpse
    var deathCam = Instantiate(deathCameraPrefab, corpse.position, Quaternion.identity);
    playerAgent.Possess(deathCam);

    // After 3 seconds, switch to spectator
    StartCoroutine(SwitchToSpectatorAfterDelay(3f));
}

IEnumerator SwitchToSpectatorAfterDelay(float delay)
{
    yield return new WaitForSeconds(delay);

    var spectator = Instantiate(spectatorPrefab);
    playerAgent.Possess(spectator);  // Automatically unpossesses death camera
}
```

### Example 2: Context-Aware Input

```csharp
// WizardPawn.cs - "Primary" casts fireball
public class WizardPawn : PawnBase, IInputBindable
{
    public void BindAgentInput(AgentInputBinder binder)
    {
        binder.Action("Primary",
            performed: ctx => _primary.value = true);
    }

    protected override void HandlePossessed()
    {
        _primary.onChanged += down => {
            if (isServer && down) CastFireball_Server();
        };
    }
}

// SpectatorPawn.cs - "Primary" cycles target
public class SpectatorPawn : PawnBase, IInputBindable
{
    public void BindAgentInput(AgentInputBinder binder)
    {
        binder.Action("Primary",
            performed: ctx => CycleSpectateTarget());
    }
}
```

### Example 3: Custom Pawn Implementation

```csharp
[RequireComponent(typeof(NetworkTransform))]
public class VehiclePawn : PawnBase, IInputBindable
{
    [SerializeField] private SyncInput<float> _throttle = new();
    [SerializeField] private SyncInput<float> _steering = new();

    public void BindAgentInput(AgentInputBinder binder)
    {
        binder.Action("Move",
            performed: ctx => {
                var input = ctx.ReadValue<Vector2>();
                _throttle.value = input.y;
                _steering.value = input.x;
            });
    }

    protected override void HandlePossessed()
    {
        // Enable vehicle HUD, camera, etc.
    }

    protected override void HandleUnpossessed()
    {
        // Cleanup vehicle state
    }
}
```

## Setup

### 1. Input Action Asset
Create a Unity Input Action Asset with your game's core actions:
- Move (Vector2)
- Look (Vector2)
- Jump (Button)
- Primary (Button)
- Secondary (Button)
- etc.

Keep action names consistent across pawns for maximum reusability.

### 2. PlayerAgent Prefab
1. Create empty GameObject
2. Add `PlayerAgent` component
3. Assign Input Action Asset
4. Add to PurrNet's player prefab registry

### 3. Pawn Prefabs
1. Create GameObject for each pawn type
2. Add `NetworkIdentity` component
3. Add `NetworkTransform` component
4. Inherit from `PawnBase` or implement `IPawn` + `IInputBindable`
5. Implement `BindAgentInput()` and input handling

### 4. PawnSpawner Setup
1. Add `PawnSpawner` to scene
2. Assign pawn prefab to spawn
3. (Optional) Add spawn point transforms
4. Configure network rules (despawn on disconnect, etc.)

## Advanced Topics

### Possession Lifecycle

```
Client: playerAgent.Possess(pawn)
  ->
1. BindInputIfOwner() - Local input binding
2. Server_RequestPossess() - RPC to server
  ->
Server: GiveOwnership(pawn)
  ->
3. Observers_OnPossessionChanged() - Broadcast to all clients
  ->
All Clients:
  - pawn.OnPossessed(agent)
  - Input binding (owner only)
```

### Orphaned Pawn Handling

Race condition: Pawn spawns before PlayerAgent is ready

```csharp
// PawnSpawner registers orphan
_orphanedPawns[playerID] = pawn;

// Retries possession every 100ms (max 10 attempts)
StartCoroutine(RetryPossession(playerID, pawn));

// PlayerAgent checks for orphans on owner change
if (TryGetOrphanedPawn(PlayerId.Value, out var pawn))
    Possess(pawn);
```

### Extending PawnBase

Override lifecycle hooks for pawn-specific behavior:

```csharp
public class CustomPawn : PawnBase
{
    protected override void HandlePossessed()
    {
        // Setup pawn-specific subscriptions
        _jump.onChanged += HandleJump;
    }

    protected override void HandleUnpossessed()
    {
        // Cleanup (unsubscribe, disable, etc.)
        _jump.onChanged -= HandleJump;
    }
}
```

### Server-Authoritative Input

PawnBase uses PurrNet's `SyncInput<T>` for server authority:

```csharp
// Owner client: Set input value
_jump.value = true;

// Automatically synced to server

// Server: React to input
_jump.onChanged += pressed => {
    if (isServer && pressed && IsGrounded) // in progress testing
        Jump();
};
```

## API Reference

### PlayerAgent

| Method | Description |
|--------|-------------|
| `Possess(IPawn)` | Transfer control to new pawn, unpossessing current |
| `UnPossess(IPawn)` | Release control of pawn |
| `PlayerId` | Get network PlayerID (nullable) |

### IPawn

| Member | Description |
|--------|-------------|
| `Identity` | NetworkIdentity for ownership transfer |
| `OnPossessed(PlayerAgent)` | Called when agent takes control |
| `OnUnpossessed()` | Called when agent releases control |

### IInputBindable

| Method | Description |
|--------|-------------|
| `BindAgentInput(AgentInputBinder)` | Declare input action bindings |

### AgentInputBinder

| Method | Description |
|--------|-------------|
| `Action(name, started, performed, canceled)` | Bind callbacks to input action |
| `UnbindAll()` | Remove all bindings (automatic on unpossess) |

## Requirements
- [PurrNet](https://github.com/ShinyScythe/PurrNet) networking library
- Unity Input System package
- Unity Version (I made this in Unity 6.2)

---

## Roadmap / Future Improvements

Based on code comments:
- [ ] Extract camera/look system to separate component
- [ ] Extract movement to separate component
- [ ] Integrate game-level resource system
- [ ] Spectator pawn implementation with server replicated player camera feeds
- [ ] Orbit-cam pawn implementation with coroutine determined lifetime, operates only on target/client systems (explore non-replicated behavior further)

---

**Architecture inspired by Unreal Engine's Pawn/Controller system, adapted for Unity + with PurrNet**

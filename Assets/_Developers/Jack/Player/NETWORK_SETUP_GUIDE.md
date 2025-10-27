# Network Setup Guide - Fixing Socket Bind Error

## The Problem

You're getting this error on the client instance:
```
[UDPTransport] [B]Bind exception: Only one usage of each socket address (protocol/network address/port) is normally permitted.
```

### Root Cause
Your `NetworkManager` in the scene has `startServerFlags: -1` which auto-starts the server on **BOTH** instances:
- Host instance: Binds to port 7777 ✅
- Client instance: Tries to bind to port 7777 ❌ **CONFLICT!**

In peer-to-peer (client-host) mode:
- **Host** = Server + Client (listens on port AND plays the game)
- **Client** = Client only (connects to host, does NOT start a server)

---

## Quick Fix

### Step 1: Disable Auto-Start

In Unity, select the `NetworkManager` object in your scene and change:

```
Start Server Flags: None (0)
Start Client Flags: None (0)
```

This prevents automatic server startup on both instances.

### Step 2: Use Manual Start

You have two options:

#### Option A: Use Existing AutoStartUI
- **Host Player**: Click the "Server" button (or use the new Host button)
- **Other Players**: Click ONLY the "Client" button

#### Option B: Use SimpleNetworkStarter (Recommended)
I created `SimpleNetworkStarter.cs` for you:

1. Add the script to a UI GameObject in your scene
2. Create two buttons in your UI:
   - "Host Game" button → Assign to `hostButton` field
   - "Join Game" button → Assign to `clientButton` field
3. (Optional) Create a "Disconnect" button → Assign to `disconnectButton` field
4. (Optional) Add a Text component for status → Assign to `statusText` field

**Usage:**
- **Host Player**: Click "Host Game" (starts server + client)
- **Other Players**: Click "Join Game" (starts client only)

---

## How It Works

### Host Behavior
```csharp
NetworkManager.main.StartServer();  // Bind to port 7777 and listen
NetworkManager.main.StartClient();  // Connect to localhost:7777
```
The host runs both server and client in the same process.

### Client Behavior
```csharp
NetworkManager.main.StartClient();  // Connect to host's IP:7777
```
Clients only connect, they don't bind to any port for listening.

---

## Testing Checklist

1. **Disable auto-start flags** in NetworkManager inspector
2. **Build or run two instances:**
   - Instance 1 (Host): Click "Host Game"
   - Instance 2 (Client): Click "Join Game"
3. **Verify in console:**
   - Host should show: `Server state: Connected` and `Client state: Connected`
   - Client should show: `Client state: Connected`
4. **Each player should spawn their own pawn** (thanks to the fixes we made earlier!)
5. **Pawns should see and interact with each other**

---

## Advanced: Different Builds

If you want auto-start for different build types:

### Host Build
```
startServerFlags: All (-1)
startClientFlags: OnAwake | LocalHost (5)
```

### Client Build
```
startServerFlags: None (0)
startClientFlags: OnAwake | LocalHost (5)
```

You'd need to create separate build configurations or use scripting defines to swap these values.

---

## Summary

**The socket bind error happens because both instances tried to start a server.**

**Fix:** Disable auto-start and use buttons to manually choose Host or Client role.

The `SimpleNetworkStarter.cs` script makes this easy with clear "Host Game" and "Join Game" buttons.

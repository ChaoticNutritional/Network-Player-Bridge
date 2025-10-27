using System;
using UnityEngine;
using UnityEngine.InputSystem;
using PurrNet;

public class PlayerAgent : PlayerIdentity<PlayerAgent>
{
    [Header("Input")]
    [SerializeField] private InputActionAsset _inputActionAsset;

    private IPawn _currentPawn;
    private AgentInputBinder _inputBinder;
    public PlayerID? PlayerId => owner;


    protected override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer)
    {
        base.OnOwnerChanged(oldOwner, newOwner, asServer);

        // Server: check for orphaned pawns when agent gets owner
        if (asServer && PlayerId.HasValue && PawnSpawner.TryGetOrphanedPawn(PlayerId.Value, out var orphanedPawn))
        {
            orphanedPawn.Identity.GiveOwnership(PlayerId.Value);
            Observers_OnPossessionChanged(this, orphanedPawn.Identity, PlayerId.Value);
        }
    }


    public void Possess(IPawn pawn)
    {
        // No pawn, or same pawn, early return
        if (pawn == null || pawn == _currentPawn) return;

        // Bind input locally
        BindInputIfOwner(pawn);

        // Unpossess current pawn if needed
        if (_currentPawn != null)
            UnPossess(_currentPawn);

        // Request server to transfer ownership
        if (PlayerId.HasValue)
            Server_RequestPossess(pawn.Identity, PlayerId.Value);

        _currentPawn = pawn;
        _currentPawn.OnPossessed(this);
    }


    public void UnPossess(IPawn pawn)
    {
        if (pawn == null || pawn != _currentPawn) return;
        Server_RequestUnpossess(pawn.Identity);
        
        _inputBinder?.UnbindAll();
        _inputBinder = null;
        
        pawn.OnUnpossessed();
        _currentPawn = null;
    }
    
    
    // ----------------------- Server RPCs ----------------------- ///
    [ServerRpc(requireOwnership: false)]
    private void Server_RequestPossess(NetworkIdentity pawnIdentity, PlayerID newOwner)
    {
        pawnIdentity.GiveOwnership(newOwner); // transfer ownership per PurrNet
        Observers_OnPossessionChanged(this, pawnIdentity, newOwner);
    }

    [ServerRpc]
    private void Server_RequestUnpossess(NetworkIdentity pawnIdentity)
    {
        // Make it server-controlled by removing player ownership
        Observers_OnPossessionChanged(this, pawnIdentity, default);
    }

    [ObserversRpc(bufferLast: true, runLocally: true)]
    private void Observers_OnPossessionChanged(PlayerAgent agent, NetworkIdentity pawnIdentity, PlayerID newOwner)
    {
        // Only handle if this is OUR agent
        if (agent != this) return;

        // Validate pawnIdentity is not null
        if (pawnIdentity == null)
        {
            Debug.LogError("[PlayerAgent] Observers_OnPossessionChanged received null pawnIdentity!");
            return;
        }

        var pawn = pawnIdentity.GetComponent<IPawn>();
        if (pawn == null)
        {
            Debug.LogError($"[PlayerAgent] No IPawn component found on {pawnIdentity.name}");
            return;
        }

        _currentPawn = pawn;
        _currentPawn.OnPossessed(this);

        // Bind input on owning client
        BindInputIfOwner(pawn);
    }

    private void BindInputIfOwner(IPawn pawn)
    {
        if (isOwner && pawn is IInputBindable bindable)
        {
            _inputBinder?.UnbindAll(); // Clean up previous bindings
            _inputBinder = new AgentInputBinder(_inputActionAsset);
            bindable.BindAgentInput(_inputBinder);
        }
    }
}


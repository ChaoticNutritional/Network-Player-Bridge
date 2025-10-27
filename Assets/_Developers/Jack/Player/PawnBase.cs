using System;
using PurrNet;
using UnityEngine;
using UnityEngine.Assertions;

public abstract class PawnBase : NetworkBehaviour, IPawn
{
    protected PlayerAgent Agent;
    
    public NetworkIdentity Identity => this;
    
    public virtual void OnPossessed(PlayerAgent agent)
    {
        Agent = agent;
        
        HandlePossessed();
    }
    
    public virtual void OnUnpossessed()
    {
        HandleUnpossessed();
    }
    
    // ---- Input Sync (owner -> server) ---- //
    [SerializeField] protected SyncInput<Vector2> _move = new();
    [SerializeField] protected SyncInput<Vector2> _lookDelta = new();
    [SerializeField] protected SyncInput<bool>    _jump = new();
    [SerializeField] protected SyncInput<bool>    _primary = new();

    public void BindAgentInput(AgentInputBinder b)
    {
        // ACTION NAMES MUST MATCH INPUT MAP
        b.Action("Move",
            performed: ctx =>
            {
                if (!isOwner) return;                   
                _move.value = ctx.ReadValue<Vector2>();
            },
            canceled: ctx =>
            {
                if (!isOwner) return;
                _move.value = Vector2.zero;
            });

        b.Action("Jump",
            performed: ctx =>
            {
                if (!isOwner) return;
                _jump.value = true;
            },
            canceled: ctx =>
            {
                if (!isOwner) return;
                _jump.value = false;
            });

        b.Action("Primary",
            performed: ctx =>
            {
                if (!isOwner) return;
                _primary.value = true;              
            },
            canceled: ctx =>
            {
                if (!isOwner) return;
                _primary.value = false;             
            });
        
        b.Action("Look",
            performed: ctx =>
            {
                if (!isOwner) return;
                _lookDelta.value = ctx.ReadValue<Vector2>();
            });
    }

    /// <summary>
    ///  Breakout so that we may expand upon these without crowding Possessed()
    /// </summary>
    protected virtual void HandlePossessed() { }    // default no op
    /// <summary>
    /// Breakout so that we may expand upon these without crowding Unpossessed()
    /// </summary>
    protected virtual void HandleUnpossessed() { }  // default no op
}

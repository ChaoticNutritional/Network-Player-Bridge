using System.Collections.Generic;
using UnityEngine.InputSystem;
using System;
using UnityEngine;

public sealed class AgentInputBinder : IInputBinder, IDisposable
{
    private readonly InputActionAsset _map;
    private readonly List<Action> _boundActs = new();

    public AgentInputBinder(InputActionAsset iaAsset)
    {
        // no asset
        if (!iaAsset) return;
        _map = iaAsset;
        _map?.Enable();
    }

    public void Action(string actionName,
        Action<InputAction.CallbackContext> started   = null,
        Action<InputAction.CallbackContext> performed = null,
        Action<InputAction.CallbackContext> canceled  = null)
    {
        
        if (_map == null) return;
        var act = _map.FindAction(actionName, throwIfNotFound: false);
        if (act == null)
        {
            Debug.Log("Action not found");
            return;
        }

        if (started != null)   { act.started   += started;   _boundActs.Add(() => act.started   -= started); }
        if (performed != null) { act.performed += performed; _boundActs.Add(() => act.performed -= performed); }
        if (canceled != null)  { act.canceled  += canceled;  _boundActs.Add(() => act.canceled  -= canceled); }
    }

    public void UnbindAll()
    {
        for (int i = _boundActs.Count - 1; i >= 0; --i) _boundActs[i]();
        _boundActs.Clear();
    }

    public void Dispose() => UnbindAll();
}
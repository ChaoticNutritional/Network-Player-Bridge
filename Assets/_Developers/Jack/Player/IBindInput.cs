using UnityEngine.InputSystem;

public interface IInputBindable
{
    // pawn declares what actions it wants and how to handle them
    void BindAgentInput(AgentInputBinder binder);
}

public interface IInputBinder
{
    // subscribe handlers for an action by name
    void Action(string actionName,
        System.Action<InputAction.CallbackContext> started   = null,
        System.Action<InputAction.CallbackContext> performed = null,
        System.Action<InputAction.CallbackContext> canceled  = null);
    void UnbindAll();
}
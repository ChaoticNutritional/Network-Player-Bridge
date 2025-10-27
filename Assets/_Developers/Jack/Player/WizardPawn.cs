using PurrNet;
using System;
using System.Collections.Generic;
using UnityEngine;


[RequireComponent(typeof(NetworkTransform))]
public class WizardPawn : PawnBase, IInputBindable
{
    /// <summary>
    ///  note to self, break this out into separate component too...
    /// </summary>
    public float lookSensitivity = 2f;
    private float _rotX;
    private float _rotY;
    public float maxLookX = 80f;
    public float minLookx = -80f;
    private Vector2 localLook = Vector2.zero;
    
    [Header("Movement ## Offload this to other component soon ##")]
    public float moveForce = 50f;
    public float maxSpeed = 8f; //So I can clamp speed
    public float airControlMultiplier = 0.5f;
    public float groundDrag = 5f;
    public float airDrag = 0.1f;

    [SerializeField] private float _playerHeight = 5.0f;
    [SerializeField] protected SyncVar<bool> _isGrounded = new();
    
    
    [Header("Sprinting ## Offload this to new component for resource based sprinting ##")]
    public float sprintMultiplier = 1.5f;
    private bool isSprinting;
    private bool canSprint = true;
    [SerializeField] protected Rigidbody _rb = new Rigidbody();
    [SerializeField] protected float _jumpForce = 5f;



    // Assumptions
    // [SerializeField] private CharacterController _cc; // or Rigidbody
    // [SerializeField] private Animator _anim;
    public PlayerAgent _playerAgent;
    
    // ---- Casting ---- //
    [SerializeField] protected Transform _castOrigin;     // temporary, place to shoot projectile
    [SerializeField] protected GameObject _fireballPrefab;
    [SerializeField] protected float _fireballSpeed = 20f;
    [SerializeField] public Camera _camera;
    

    [SerializeField] private bool _jumpEdge; // local only


    //Resource Manager stuff
    //Resource manager stuff
    private PlayerID playerID;
    private int playerIndex;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    // Owner->Server input sync for "primary cast" edge
    // [SerializeField] private SyncInput<bool> _primaryTestAbility = new(defaultValue:false);
    protected override void OnSpawned()
    {
        playerID = GetComponent<NetworkIdentity>().localPlayerForced;
        ulong rawId = playerID.id.value;
        playerIndex = (int)rawId;
    }

    void HandleMove(Vector2 dir)
    {
        // If using SyncInput, do the motion on server... if predicting, mirror locally.
    }

    protected override void HandlePossessed()
    {
        // Safety check in case this ever becomes enabled?
        if (!isOwner)
        {
            _camera.enabled = false;
            return;
        }
        else
        {
            _camera.enabled = true;
        }
        
        // Server consumes synced inputs
        _jump.onChanged += pressed =>
        {
            if (isServer && pressed && IsGrounded)
            {
                Jump();
            }
        };

        _jump.onSentData += () =>
        {
            if (isOwner) _jumpEdge = false;
        };

        _primary.onChanged += down =>
        {
            if (isServer && down)
                CastFireball_Server();
        };

        _lookDelta.onChanged += (Vector2 delta) =>
        {
            localLook = delta;
            _lookDelta.value = Vector2.zero;
        };
    }

    private void OnLook()
    {
        float mouseY = localLook.y * lookSensitivity; 
        float mouseX = localLook.x * lookSensitivity;
        
        //Normal free look (not climbing) 
        _rotX -= mouseY;
        _rotX = Mathf.Clamp(_rotX, minLookx, maxLookX);
        
        _camera.transform.localRotation = Quaternion.Euler(_rotX, 0f, 0f);
        transform.rotation *= Quaternion.Euler(0, mouseX, 0);
    }

    protected override void HandleUnpossessed()
    {
        // Clean up any pawn-specific state here if needed
    }
    
    [ServerRpc(runLocally: true)]
    private bool IsGrounded => Physics.Raycast(transform.position, Vector3.down, _playerHeight + 0.1f);

    [ServerRpc(runLocally: true)]
    private void Jump()
    {
        _rb.AddForce(Vector3.up * _jumpForce, ForceMode.Impulse);
    }

    
    private void FixedUpdate()
    {
        OnLook();        
        
        // server-authoritative motion
        _rb.linearDamping = IsGrounded ? groundDrag : airDrag;
        
        // Original Movement Logic below  ---------------------
        
        //Transform move input into world space
        Vector3 forward = transform.forward * _move.value.y;
        Vector3 right = transform.right * _move.value.x;
        var moveDir = (forward + right).normalized;

        float control = IsGrounded ? 1f : airControlMultiplier;
        
        float currentForce = moveForce * (isSprinting ? sprintMultiplier : 1f);
        float currentMaxSpeed = maxSpeed * (isSprinting ? sprintMultiplier : 1f);

        float playerMoveForce = control * currentForce;

        _rb.AddForce(playerMoveForce * moveDir, ForceMode.Acceleration);

        // Clamp horizontal speed
        Vector3 flatVel = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
        if (flatVel.magnitude > currentMaxSpeed)
        {
            Vector3 limitedVel = flatVel.normalized * maxSpeed;
            _rb.linearVelocity = new Vector3(limitedVel.x, _rb.linearVelocity.y, limitedVel.z);
        }
    }
    
    // test func, this won't be how it works obv
    [ServerRpc(runLocally: true)]
    private void CastFireball_Server()
    {
        if (!_fireballPrefab || !_castOrigin) return;
        var go = Instantiate(_fireballPrefab, _castOrigin.position, _castOrigin.rotation);
        if (go.TryGetComponent<Rigidbody>(out var rb))
            rb.linearVelocity = _castOrigin.forward * _fireballSpeed;
    }
}

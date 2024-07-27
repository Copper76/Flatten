using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using static UnityEditor.Experimental.GraphView.GraphView;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(InputManager))]
public class PlayerMovementController : MonoBehaviour
{
    private InputManager _inputManager;
    private Vector2 _moveDirection = Vector2.zero;

    [Header("Camera")]
    [SerializeField] private Vector2 mouseSensitivity;

    private Transform _cameraTransform;

    [Header("Movement")]
    [SerializeField] private Vector2 arialTerminalVelocity;
    [SerializeField] private float groundTerminalVelocity;
    [SerializeField] private float groundMoveForce;
    [SerializeField] private float airMoveForce;
    [Range(0f, 10f)] [SerializeField] private float dampingStrength;
    [Range(0f, 1f)] [SerializeField] private float airDampingRatio;
    
    [Header("Jumping")]
    [SerializeField] private float initialJumpSpeed;
    [SerializeField] private float coyoteTime;
    [SerializeField] private float jumpBufferTime;
    [Range(0f, 1f)] [SerializeField] private float slopeJumpNormalBias;

    private bool _readyToJump = false;
    private float _coyoteTimeCounter;
    private float _jumpBufferCounter;
    
    [Header("Ground/Slope")]
    [SerializeField] private float groundDetectionRange;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float slopeDetectionRange;
    [SerializeField] private float maxSlopeAngle;

    [Header("Gun")]
    [SerializeField] private float weaponRange = 30.0f;
    [SerializeField] private float _fireRate = 0.5f;
    [SerializeField] private Camera flattenCamera;
    private float _fireRateCounter = 0.0f;
    [SerializeField] private Material baseMaterial;
    [SerializeField] private RenderTexture renderTexture;
    [SerializeField] private DecalProjector testProjector;
    [SerializeField] private LayerMask flattenLayerMask;
    [SerializeField] private int flattenLayer = 6;
    private LayerMask _noFlattenMask;
    private float _decalPriority = 0.0f;

    private bool _isGrounded;
    private bool _isOnSlope;
    private bool _isSteepSlope;
    private Vector3 _slopeNormal;

    private Rigidbody _rb;

    private Vector3 _moveForce;

    private Vector2 _lookDirection;
    private Vector2 _mouseCameraRotation = Vector2.zero;

    private const float Epsilon = 1e-3f;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _noFlattenMask = ~(1 << flattenLayer);
    }

    void Start()
    {
        InitializeComponents();
        LockCursor();
    }

    void FixedUpdate()
    {
        Move();
        Jump();
        Damping();
        EnforceTerminalVelocity();
    }

    private void Update()
    {
        DetectGround();
        DetectSlope();
        
        ReadInput();
        JumpCheck();
        Look();

        if (_fireRateCounter > 0.0f)
        {
            _fireRateCounter -= Time.deltaTime;
        }
        else
        {
            _fireRateCounter = 0.0f;
        }
    }

    private void InitializeComponents()
    {
        _inputManager = FindObjectOfType<InputManager>();
        _inputManager.playerInputActions.Player.Fire.performed += Fire;
        _cameraTransform = Camera.main?.transform;
    }

    private void LockCursor()
    {
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    private void UnlockCursor()
    {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.Confined;
    }

    private void ReadInput()
    {
        _moveDirection = _inputManager.playerInputActions.Player.Move.ReadValue<Vector2>();
    }

    private void Damping()
    {
        if (_moveDirection != Vector2.zero) return;

        Vector3 horizontalPlaneVelocity = new Vector3(_rb.velocity.x, 0f, _rb.velocity.z);
        Vector3 damping = -horizontalPlaneVelocity * dampingStrength * Time.fixedDeltaTime;
        if (!_isGrounded) damping *= airDampingRatio;
        _rb.AddForce(damping, ForceMode.VelocityChange);
    }

    private void DetectGround()
    {
        RaycastHit hit;
        if (Physics.Raycast(transform.position, Vector3.down, out hit, groundDetectionRange, groundLayer))
        {
            _isGrounded = true;
        }
        else
        {
            _isGrounded = false;
        }
        _isGrounded = Physics.Raycast(transform.position, Vector3.down, groundDetectionRange, groundLayer);
        Debug.DrawRay(transform.position, Vector3.down * groundDetectionRange, Color.green, Time.fixedDeltaTime);
    }

    private void DetectSlope()
    {
        RaycastHit hit;
        if (Physics.Raycast(transform.position, Vector3.down, out hit, slopeDetectionRange, groundLayer))
        {
            float slopeAngle = Vector3.Angle(hit.normal, Vector3.up);

            _isSteepSlope = slopeAngle > maxSlopeAngle;
            _isOnSlope = slopeAngle > 0f && slopeAngle < maxSlopeAngle;
            _slopeNormal = _isOnSlope ? hit.normal : Vector3.up;
        }
        else
        {
            _isOnSlope = false;
            _slopeNormal = Vector3.up;
        }
        Debug.DrawRay(transform.position, Vector3.down * groundDetectionRange, Color.red, Time.fixedDeltaTime);
    }

    private void EnforceTerminalVelocity()
    {
        if (_isGrounded || _isOnSlope)
        {
            _rb.velocity = Vector3.ClampMagnitude(_rb.velocity, groundTerminalVelocity);
        }
        else
        {
            Vector2 horizontalVelocity = new Vector2(_rb.velocity.x, _rb.velocity.z);
            horizontalVelocity = Vector2.ClampMagnitude(horizontalVelocity, arialTerminalVelocity.x);
            float clampedY = Mathf.Clamp(_rb.velocity.y, -arialTerminalVelocity.y, arialTerminalVelocity.y);
            _rb.velocity = new Vector3(horizontalVelocity.x, clampedY, horizontalVelocity.y);
        }
    }

    public void Move()
    {
        if (_moveDirection == Vector2.zero) return;

        if (_isSteepSlope) return;
        
        Vector3 normalizedMoveDirection = new Vector3(_moveDirection.x, 0f, _moveDirection.y).normalized;
        float forceStrength = _isGrounded ? groundMoveForce : airMoveForce;

        _moveForce = transform.TransformDirection(normalizedMoveDirection * forceStrength * Time.fixedDeltaTime);

        SlopeCompensation();

        _rb.AddForce(_moveForce, ForceMode.VelocityChange);
    }

    private void SlopeCompensation()
    {
        if (_isOnSlope)
        {
            _moveForce = Vector3.ProjectOnPlane(_moveForce, _slopeNormal);

            Vector3 slopeParallelGravity = Vector3.Project(Physics.gravity, _slopeNormal);

            _rb.AddForce(-slopeParallelGravity, ForceMode.Acceleration);
        }
    }

    private void JumpCheck()
    {
        UpdateCoyoteTime();
        UpdateJumpBuffer();

        if (_coyoteTimeCounter > 0f && _jumpBufferCounter > 0f)
        {
            _readyToJump = true;
            _jumpBufferCounter = 0f;
        }

        if (!_inputManager.playerInputActions.Player.Jump.inProgress && _rb.velocity.y > 0f)
        {
            _coyoteTimeCounter = 0f;
        }
    }

    private void UpdateCoyoteTime()
    {
        if (_isGrounded || _isOnSlope)
        {
            _coyoteTimeCounter = coyoteTime;
        }
        else
        {
            _coyoteTimeCounter -= Time.deltaTime;
        }
        _coyoteTimeCounter = Mathf.Max(_coyoteTimeCounter, 0f);
    }

    private void UpdateJumpBuffer()
    {
        if (_inputManager.playerInputActions.Player.Jump.triggered)
        {
            _jumpBufferCounter = jumpBufferTime;
        }
        else
        {
            _jumpBufferCounter -= Time.deltaTime;
        }
        _jumpBufferCounter = Mathf.Max(_jumpBufferCounter, 0f);
    }

    public void Jump()
    {
        if (_isSteepSlope) return;
        if (!_readyToJump) return;

        Vector3 slopeJumpDirection = Vector3.Lerp(Vector3.up, _slopeNormal, slopeJumpNormalBias);
        Vector3 jumpDirection = _isOnSlope ? slopeJumpDirection : Vector3.up;
        _rb.velocity = new Vector3(_rb.velocity.x, 0f, _rb.velocity.z);
        _rb.AddForce(jumpDirection * initialJumpSpeed, ForceMode.VelocityChange);
        _readyToJump = false;
    }

    private void Look()
    {
        _lookDirection = _inputManager.playerInputActions.Player.Look.ReadValue<Vector2>();

        _mouseCameraRotation.x -= _lookDirection.y * mouseSensitivity.x * Time.deltaTime;
        _mouseCameraRotation.y += _lookDirection.x * mouseSensitivity.y * Time.deltaTime;

        _mouseCameraRotation.x = Mathf.Clamp(_mouseCameraRotation.x, -90f, 90f);

        transform.rotation = Quaternion.Euler(0.0f, _mouseCameraRotation.y, 0);
        _cameraTransform.rotation = Quaternion.Euler(_mouseCameraRotation.x, _mouseCameraRotation.y, 0.0f);
    }

    private void Fire(InputAction.CallbackContext context)
    {
        if (_fireRateCounter > 0.0f) return;

        RaycastHit hit;

        if (Physics.Raycast(transform.position + new Vector3(0.0f, 0.5f, 0.0f), _cameraTransform.forward, out hit, weaponRange))
        {
            if ((flattenLayerMask.value & (1 << hit.transform.gameObject.layer)) == 0) return;

            hit.transform.gameObject.layer = flattenLayer;

            flattenCamera.Render();

            GameObject decalObject = new GameObject(hit.transform.name + "_Flattened");
            decalObject.transform.position = hit.transform.position;
            decalObject.transform.rotation = _cameraTransform.rotation;
            DecalProjector decalProjector = decalObject.AddComponent<DecalProjector>();

            Destroy(hit.transform.gameObject);

            float projectionDistance = Vector3.Distance(_cameraTransform.position, hit.transform.position);

            if (Physics.Raycast(transform.position + new Vector3(0.0f, 0.5f, 0.0f), _cameraTransform.forward, out hit, weaponRange, _noFlattenMask))
            {
                decalObject.transform.position = hit.point;
                projectionDistance = hit.distance;
            }

            float frustumHeight = 2.0f * projectionDistance * Mathf.Tan(Camera.main.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float frustumWidth = frustumHeight * Camera.main.aspect;
            decalProjector.size = new Vector3(frustumWidth, frustumHeight, projectionDistance);

            decalProjector.material = RenderTextureSnapshot();

            _fireRateCounter = _fireRate;
        }
    }

    private Material RenderTextureSnapshot()
    {
        Material material = new Material(baseMaterial);

        Texture2D texture2D = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.ARGB32, false);
        var oldRT = RenderTexture.active;
        RenderTexture.active = renderTexture;
        texture2D.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        texture2D.Apply(true, false);
        RenderTexture.active = oldRT;

        material.SetTexture("Base_Map", texture2D);

        //add priority to ensure the new decals are rendered on top
        material.SetFloat("_DrawOrder", _decalPriority);
        _decalPriority += 1.0f;

        return material;
    }
}

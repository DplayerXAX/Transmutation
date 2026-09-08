using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Faithful Input System port of FullMovement's PlayerMovementAdvanced,
/// PlayerCam, MoveCamera and Sliding scripts, merged into one component.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class SmoothFirstPersonController : MonoBehaviour
{
    public enum MovementState
    {
        Freeze,
        Unlimited,
        Walking,
        Sprinting,
        WallRunning,
        Climbing,
        Crouching,
        Sliding,
        Air
    }

    [Header("Feature Toggles")]
    [SerializeField] private bool allowCrouch;
    [SerializeField] private bool allowDoubleJump;
    [Tooltip("When enabled, jump ignores the grounded check (can jump while airborne, still respects jump cooldown).")]
    [SerializeField] private bool allowUngroundedJump;
    [SerializeField] private bool allowSliding;
    [SerializeField] private bool enableCameraFovAndTilt;

    [Header("Movement — FullMovement Values")]
    [SerializeField] private float walkSpeed = 7f;
    [SerializeField] private float sprintSpeed = 10f;
    [SerializeField] private float slideSpeed = 30f;
    [SerializeField] private float wallRunSpeed = 8.5f;
    [SerializeField] private float climbSpeed = 3f;
    [SerializeField] private float airMinSpeed = 7f;
    [SerializeField] private float speedIncreaseMultiplier = 1.5f;
    [SerializeField] private float slopeIncreaseMultiplier = 2.5f;
    [SerializeField] private float groundDrag = 4f;

    [Header("Jumping — FullMovement Values")]
    [SerializeField] private float jumpForce = 12f;
    [SerializeField] private float jumpCooldown = 0.25f;
    [SerializeField] private float airMultiplier = 0.4f;

    [Header("Crouching — FullMovement Values")]
    [SerializeField] private float crouchSpeed = 3.5f;
    [SerializeField] private float crouchYScale = 0.5f;

    [Header("Sliding — FullMovement Values")]
    [SerializeField] private float maxSlideTime = 2f;
    [SerializeField] private float slideForce = 200f;
    [SerializeField] private float slideYScale = 0.5f;

    [Header("Keys")]
    [SerializeField] private Key jumpKey = Key.Space;
    [SerializeField] private Key sprintKey = Key.LeftShift;
    [SerializeField] private Key crouchAndSlideKey = Key.LeftCtrl;

    [Header("Ground Check — FullMovement Values")]
    [SerializeField] private float playerHeight = 2f;
    [SerializeField] private LayerMask whatIsGround;
    [Tooltip("How far the ground BoxCast travels downward from its origin.")]
    [SerializeField] private float groundCheckDistance = 0.2f;
    [Tooltip("Half-extents of the ground BoxCast. Keep Y thin; X/Z slightly smaller than the player collider.")]
    [SerializeField] private Vector3 groundCheckHalfExtents = new Vector3(0.35f, 0.05f, 0.35f);
    [Tooltip("Extra vertical offset added on top of the feet-centered origin (-playerHeight/2 + halfExtents.y).")]
    [SerializeField] private float groundCheckOriginYOffset = 0f;

    [Header("Slope Handling — FullMovement Values")]
    [SerializeField] private float maxSlopeAngle = 40f;

    [Header("Required References")]
    [Tooltip("The yaw-only Orientation child under Player.")]
    [SerializeField] private Transform orientation;
    [Tooltip("The CameraPos child on Player. CameraHolder follows this marker exactly like FullMovement's MoveCamera script.")]
    [SerializeField] private Transform cameraPosition;
    [Tooltip("The empty CameraHolder sibling of Player, containing PlayerCamera.")]
    [SerializeField] private Transform cameraHolder;
    [Tooltip("The Camera component on PlayerCamera.")]
    [SerializeField] private Camera playerCamera;

    [Header("Camera — Input System Equivalent")]
    [Tooltip("Degrees per mouse-delta unit. This is not comparable to the old Input Manager value of 400.")]
    [SerializeField] private float sensitivityX = 0.12f;
    [SerializeField] private float sensitivityY = 0.12f;

    [Header("Runtime State (Read Only)")]
    [SerializeField] private MovementState state;
    [SerializeField] private bool grounded;
    [SerializeField] private bool sliding;
    [SerializeField] private bool crouching;
    [SerializeField] private bool wallRunning;
    [SerializeField] private bool climbing;
    [SerializeField] private bool freeze;
    [SerializeField] private bool unlimited;
    [SerializeField] private bool restricted;
    [SerializeField] private float currentVelocity;

    public Transform Orientation => orientation;
    public Rigidbody Body => rb;
    public bool Grounded => grounded;
    public bool Sliding => sliding;
    public bool WallRunning => wallRunning;
    public bool Climbing => climbing;
    public bool Restricted => restricted;
    public MovementState State => state;
    public float CurrentVelocity => currentVelocity;

    private Rigidbody rb;
    private AdvancedFirstPersonTraversal advancedController;
    private float moveSpeed;
    private float desiredMoveSpeed;
    private float lastDesiredMoveSpeed;
    private float horizontalInput;
    private float verticalInput;
    private Vector3 moveDirection;
    private float startYScale;
    private bool readyToJump;
    private bool doubleJumpAvailable;
    private bool exitingSlope;
    private bool keepMomentum;
    private RaycastHit slopeHit;
    private float slideTimer;
    private float xRotation;
    private float yRotation;
    private Coroutine speedLerpCoroutine;

    private void Reset()
    {
        orientation = transform.Find("Orientation");
        cameraPosition = transform.Find("CameraPos");
        if (cameraPosition == null)
            cameraPosition = transform.Find("CameraPosition");

        if (transform.parent != null)
            cameraHolder = transform.parent.Find("CameraHolder");
        if (cameraHolder != null)
            playerCamera = cameraHolder.GetComponentInChildren<Camera>();
    }

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        advancedController = GetComponent<AdvancedFirstPersonTraversal>();
        rb.freezeRotation = true;

        readyToJump = true;
        doubleJumpAvailable = allowDoubleJump;
        startYScale = transform.localScale.y;

        yRotation = orientation != null ? orientation.eulerAngles.y : transform.eulerAngles.y;
        xRotation = cameraHolder != null ? NormalizeAngle(cameraHolder.eulerAngles.x) : 0f;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        GroundCheck();

        if (grounded)
            doubleJumpAvailable = allowDoubleJump;

        ReadMovementInput();
        SpeedControl();
        StateHandler();
        FollowCameraPosition();
        ReadCameraInput();

        rb.linearDamping = grounded ? groundDrag : 0f;
        currentVelocity = rb.linearVelocity.magnitude;

        if (!allowCrouch && crouching)
            StopCrouch();
        if (!allowSliding && sliding)
            StopSlide();
    }

    private void FixedUpdate()
    {
        MovePlayer();

        if (sliding)
            SlidingMovement();
    }

    private void GroundCheck()
    {
        grounded = CastGround(out _);
    }

    private Vector3 GetGroundCheckOrigin()
    {
        float feetY = -playerHeight * 0.5f + groundCheckHalfExtents.y + groundCheckOriginYOffset;
        return transform.position + Vector3.up * feetY;
    }

    private bool CastGround(out RaycastHit hit)
    {
        return Physics.BoxCast(
            GetGroundCheckOrigin(),
            groundCheckHalfExtents,
            Vector3.down,
            out hit,
            Quaternion.identity,
            groundCheckDistance,
            whatIsGround,
            QueryTriggerInteraction.Ignore);
    }

    private void ReadMovementInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            horizontalInput = 0f;
            verticalInput = 0f;
            return;
        }

        horizontalInput = ReadAxis(keyboard, Key.A, Key.D);
        verticalInput = ReadAxis(keyboard, Key.S, Key.W);

        if (keyboard[jumpKey].isPressed && readyToJump && (grounded || allowUngroundedJump))
        {
            readyToJump = false;
            Jump();
            Invoke(nameof(ResetJump), jumpCooldown);
        }
        else if (allowDoubleJump && keyboard[jumpKey].wasPressedThisFrame &&
                 doubleJumpAvailable && !wallRunning)
        {
            doubleJumpAvailable = false;
            Jump();
        }

        if (allowCrouch && keyboard[crouchAndSlideKey].wasPressedThisFrame &&
            horizontalInput == 0f && verticalInput == 0f)
        {
            StartCrouch();
        }

        if (allowSliding && keyboard[crouchAndSlideKey].wasPressedThisFrame &&
            (horizontalInput != 0f || verticalInput != 0f))
        {
            StartSlide();
        }

        if (keyboard[crouchAndSlideKey].wasReleasedThisFrame)
        {
            if (crouching)
                StopCrouch();
            if (sliding)
                StopSlide();
        }
    }

    private static float ReadAxis(Keyboard keyboard, Key negative, Key positive)
    {
        return (keyboard[positive].isPressed ? 1f : 0f) -
               (keyboard[negative].isPressed ? 1f : 0f);
    }

    private void ReadCameraInput()
    {
        if (Mouse.current == null || orientation == null || cameraHolder == null)
            return;

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();
        float mouseX = mouseDelta.x * sensitivityX;
        float mouseY = mouseDelta.y * sensitivityY;

        yRotation += mouseX;
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        cameraHolder.rotation = Quaternion.Euler(xRotation, yRotation, 0f);
        orientation.rotation = Quaternion.Euler(0f, yRotation, 0f);
    }

    private void FollowCameraPosition()
    {
        if (cameraHolder != null && cameraPosition != null)
            cameraHolder.position = cameraPosition.position;
    }

    private void StateHandler()
    {
        if (freeze)
        {
            state = MovementState.Freeze;
            rb.linearVelocity = Vector3.zero;
            desiredMoveSpeed = 0f;
        }
        else if (unlimited)
        {
            state = MovementState.Unlimited;
            desiredMoveSpeed = 999f;
        }
        else if (climbing)
        {
            state = MovementState.Climbing;
            desiredMoveSpeed = climbSpeed;
        }
        else if (wallRunning)
        {
            state = MovementState.WallRunning;
            desiredMoveSpeed = wallRunSpeed;
        }
        else if (sliding)
        {
            state = MovementState.Sliding;

            if (OnSlope() && rb.linearVelocity.y < 0.1f)
            {
                desiredMoveSpeed = slideSpeed;
                keepMomentum = true;
            }
            else
            {
                desiredMoveSpeed = sprintSpeed;
            }
        }
        else if (crouching)
        {
            state = MovementState.Crouching;
            desiredMoveSpeed = crouchSpeed;
        }
        else if (grounded && IsKeyPressed(sprintKey))
        {
            state = MovementState.Sprinting;
            desiredMoveSpeed = sprintSpeed;
        }
        else if (grounded)
        {
            state = MovementState.Walking;
            desiredMoveSpeed = walkSpeed;
        }
        else
        {
            state = MovementState.Air;

            if (moveSpeed < airMinSpeed)
                desiredMoveSpeed = airMinSpeed;
        }

        bool desiredMoveSpeedHasChanged = desiredMoveSpeed != lastDesiredMoveSpeed;
        if (desiredMoveSpeedHasChanged)
        {
            if (keepMomentum)
            {
                if (speedLerpCoroutine != null)
                    StopCoroutine(speedLerpCoroutine);
                speedLerpCoroutine = StartCoroutine(SmoothlyLerpMoveSpeed());
            }
            else
            {
                moveSpeed = desiredMoveSpeed;
            }
        }

        lastDesiredMoveSpeed = desiredMoveSpeed;

        if (Mathf.Abs(desiredMoveSpeed - moveSpeed) < 0.1f)
            keepMomentum = false;
    }

    private IEnumerator SmoothlyLerpMoveSpeed()
    {
        float time = 0f;
        float difference = Mathf.Abs(desiredMoveSpeed - moveSpeed);
        float startValue = moveSpeed;

        while (time < difference)
        {
            moveSpeed = Mathf.Lerp(startValue, desiredMoveSpeed, time / difference);

            if (OnSlope())
            {
                float slopeAngle = Vector3.Angle(Vector3.up, slopeHit.normal);
                float slopeAngleIncrease = 1f + slopeAngle / 90f;
                time += Time.deltaTime * speedIncreaseMultiplier * slopeIncreaseMultiplier * slopeAngleIncrease;
            }
            else
            {
                time += Time.deltaTime * speedIncreaseMultiplier;
            }

            yield return null;
        }

        moveSpeed = desiredMoveSpeed;
        speedLerpCoroutine = null;
    }

    private void MovePlayer()
    {
        if (advancedController != null && advancedController.ExitingClimbWall)
            return;
        if (restricted)
            return;
        if (orientation == null)
            return;

        moveDirection = orientation.forward * verticalInput + orientation.right * horizontalInput;

        if (OnSlope() && !exitingSlope)
        {
            rb.AddForce(GetSlopeMoveDirection(moveDirection) * moveSpeed * 20f, ForceMode.Force);

            if (rb.linearVelocity.y > 0f)
                rb.AddForce(Vector3.down * 80f, ForceMode.Force);
        }
        else if (grounded)
        {
            rb.AddForce(moveDirection.normalized * moveSpeed * 10f, ForceMode.Force);
        }
        else
        {
            rb.AddForce(moveDirection.normalized * moveSpeed * 10f * airMultiplier, ForceMode.Force);
        }

        rb.useGravity = !OnSlope();
    }

    private void SpeedControl()
    {
        if (OnSlope() && !exitingSlope)
        {
            if (rb.linearVelocity.magnitude > moveSpeed)
                rb.linearVelocity = rb.linearVelocity.normalized * moveSpeed;
        }
        else
        {
            Vector3 flatVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

            if (flatVelocity.magnitude > moveSpeed)
            {
                Vector3 limitedVelocity = flatVelocity.normalized * moveSpeed;
                rb.linearVelocity = new Vector3(limitedVelocity.x, rb.linearVelocity.y, limitedVelocity.z);
            }
        }
    }

    private void Jump()
    {
        exitingSlope = true;
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        rb.AddForce(transform.up * jumpForce, ForceMode.Impulse);
    }

    private void ResetJump()
    {
        readyToJump = true;
        doubleJumpAvailable = allowDoubleJump;
        exitingSlope = false;
    }

    private void StartCrouch()
    {
        transform.localScale = new Vector3(transform.localScale.x, crouchYScale, transform.localScale.z);
        rb.AddForce(Vector3.down * 5f, ForceMode.Impulse);
        crouching = true;
    }

    private void StopCrouch()
    {
        transform.localScale = new Vector3(transform.localScale.x, startYScale, transform.localScale.z);
        crouching = false;
    }

    private void StartSlide()
    {
        if (wallRunning)
            return;

        sliding = true;
        transform.localScale = new Vector3(transform.localScale.x, slideYScale, transform.localScale.z);
        rb.AddForce(Vector3.down * 5f, ForceMode.Impulse);
        slideTimer = maxSlideTime;
    }

    private void SlidingMovement()
    {
        Vector3 inputDirection = orientation.forward * verticalInput + orientation.right * horizontalInput;

        if (!OnSlope() || rb.linearVelocity.y > -0.1f)
        {
            rb.AddForce(inputDirection.normalized * slideForce, ForceMode.Force);
            slideTimer -= Time.deltaTime;
        }
        else
        {
            rb.AddForce(GetSlopeMoveDirection(inputDirection) * slideForce, ForceMode.Force);
        }

        if (slideTimer <= 0f)
            StopSlide();
    }

    private void StopSlide()
    {
        sliding = false;
        transform.localScale = new Vector3(transform.localScale.x, startYScale, transform.localScale.z);
    }

    public bool OnSlope()
    {
        if (CastGround(out slopeHit) && grounded)
        {
            float angle = Vector3.Angle(Vector3.up, slopeHit.normal);
            if (angle < maxSlopeAngle && angle != 0f)
                return true;
        }

        return false;
    }

    public Vector3 GetSlopeMoveDirection(Vector3 direction)
    {
        return Vector3.ProjectOnPlane(direction, slopeHit.normal).normalized;
    }

    public void SetWallRunning(bool value)
    {
        wallRunning = value;
    }

    public void SetClimbing(bool value)
    {
        climbing = value;
    }

    public void SetRestricted(bool value)
    {
        restricted = value;
    }

    public void SetFreeze(bool value)
    {
        freeze = value;
    }

    public void SetUnlimited(bool value)
    {
        unlimited = value;
    }

    public void DoFov(float endValue)
    {
        if (!enableCameraFovAndTilt || playerCamera == null)
            return;

        playerCamera.DOFieldOfView(endValue, 0.25f);
    }

    public void DoTilt(float zTilt)
    {
        if (!enableCameraFovAndTilt || playerCamera == null)
            return;

        playerCamera.transform.DOLocalRotate(new Vector3(0f, 0f, zTilt), 0.25f);
    }

    private static bool IsKeyPressed(Key key)
    {
        return Keyboard.current != null && Keyboard.current[key].isPressed;
    }

    private static float NormalizeAngle(float angle)
    {
        return angle > 180f ? angle - 360f : angle;
    }

    private void OnValidate()
    {
        playerHeight = Mathf.Max(0.1f, playerHeight);
        groundCheckDistance = Mathf.Max(0.01f, groundCheckDistance);
        groundCheckHalfExtents.x = Mathf.Max(0.01f, groundCheckHalfExtents.x);
        groundCheckHalfExtents.y = Mathf.Max(0.01f, groundCheckHalfExtents.y);
        groundCheckHalfExtents.z = Mathf.Max(0.01f, groundCheckHalfExtents.z);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 origin = Application.isPlaying ? GetGroundCheckOrigin()
            : transform.position + Vector3.up * (-playerHeight * 0.5f + groundCheckHalfExtents.y + groundCheckOriginYOffset);
        Vector3 end = origin + Vector3.down * groundCheckDistance;

        Gizmos.color = grounded ? new Color(0.2f, 1f, 0.35f, 0.9f) : new Color(1f, 0.55f, 0.15f, 0.9f);
        Gizmos.DrawWireCube(origin, groundCheckHalfExtents * 2f);
        Gizmos.DrawWireCube(end, groundCheckHalfExtents * 2f);
        Gizmos.DrawLine(origin, end);
    }
}

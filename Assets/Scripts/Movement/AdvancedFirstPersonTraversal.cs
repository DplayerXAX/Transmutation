using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Faithful Input System port of FullMovement's WallRunningAdvanced,
/// Climbing and LedgeGrabbing scripts, merged into one optional component.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SmoothFirstPersonController))]
public sealed class AdvancedFirstPersonTraversal : MonoBehaviour
{
    [Header("Feature Toggles")]
    [SerializeField] private bool allowWallRunning;
    [SerializeField] private bool allowWallClimbing;
    [SerializeField] private bool allowLedgeGrabbing;

    [Header("Required References")]
    [SerializeField] private Transform orientation;
    [Tooltip("The actual PlayerCamera Transform, used by the original ledge sphere cast.")]
    [SerializeField] private Transform cameraTransform;

    [Header("Wall Run — FullMovement Values")]
    [SerializeField] private LayerMask whatIsWall;
    [SerializeField] private LayerMask whatIsGround;
    [Tooltip("Surfaces flatter than this (degrees from horizontal) are floors, not walls. " +
             "Needed when walls and ground share a layer, as on generated terrain. 0 = any surface.")]
    [Range(0f, 90f)] [SerializeField] private float minWallSteepness;
    [SerializeField] private float wallRunForce = 200f;
    [SerializeField] private float wallJumpUpForce = 7f;
    [SerializeField] private float wallJumpSideForce = 14f;
    [SerializeField] private float wallClimbSpeed = 3f;
    [SerializeField] private float maxWallRunTime = 4f;
    [SerializeField] private float wallCheckDistance = 0.7f;
    [SerializeField] private float minJumpHeight = 2f;
    [SerializeField] private float wallRunExitTime = 0.2f;
    [SerializeField] private bool useGravityWhileWallRunning = true;
    [SerializeField] private float gravityCounterForce = 27f;

    [Header("Wall Run Keys")]
    [SerializeField] private Key jumpKey = Key.Space;
    [SerializeField] private Key upwardsRunKey = Key.LeftShift;
    [SerializeField] private Key downwardsRunKey = Key.LeftCtrl;

    [Header("Wall Climb — FullMovement Values")]
    [SerializeField] private float climbSpeed = 10f;
    [SerializeField] private float maxClimbTime = 0.01f;
    [SerializeField] private float climbJumpUpForce = 14f;
    [SerializeField] private float climbJumpBackForce = 12f;
    [SerializeField] private int climbJumps = 1;
    [SerializeField] private float climbDetectionLength = 0.7f;
    [SerializeField] private float climbSphereCastRadius = 0.25f;
    [SerializeField] private float maxWallLookAngle = 30f;
    [SerializeField] private float minWallNormalAngleChange = 5f;
    [SerializeField] private float climbExitWallTime = 0.2f;

    [Header("Ledge Grab — FullMovement Values")]
    [SerializeField] private LayerMask whatIsLedge;
    [SerializeField] private float moveToLedgeSpeed = 12f;
    [SerializeField] private float maxLedgeGrabDistance = 2f;
    [SerializeField] private float minTimeOnLedge = 0.5f;
    [SerializeField] private float ledgeJumpForwardForce = 14f;
    [SerializeField] private float ledgeJumpUpwardForce = 5f;
    [SerializeField] private float ledgeDetectionLength = 3f;
    [SerializeField] private float ledgeSphereCastRadius = 0.5f;
    [SerializeField] private float ledgeExitTime = 0.2f;

    [Header("Runtime State (Read Only)")]
    [SerializeField] private bool holdingLedge;
    [SerializeField] private bool exitingLedge;
    [SerializeField] private bool exitingClimbWall;

    public bool HoldingLedge => holdingLedge;
    public bool ExitingLedge => exitingLedge;
    public bool ExitingClimbWall => exitingClimbWall;

    // Read-only queries for UI prompts. They mirror the state machine conditions below.
    public bool IsClimbing => isClimbing;
    public bool IsWallRunning => controller != null && controller.WallRunning;

    /// <summary>A climbable wall is in front and holding W would start a climb.</summary>
    public bool CanStartClimb =>
        allowWallClimbing && wallFront && !isClimbing && !exitingClimbWall && !holdingLedge &&
        wallLookAngle < maxWallLookAngle && climbTimer > 0f;

    /// <summary>Pressing jump now would push off the wall in front.</summary>
    public bool CanClimbJump =>
        allowWallClimbing && wallFront && climbJumpsLeft > 0 && !holdingLedge &&
        controller != null && !controller.Grounded;

    /// <summary>A side wall is close while airborne, so moving forward would start a wall run.</summary>
    public bool CanStartWallRun =>
        allowWallRunning && (wallLeft || wallRight) && !wallRunExiting &&
        controller != null && !controller.WallRunning && AboveGround();

    private SmoothFirstPersonController controller;
    private Rigidbody rb;

    // Wall run state
    private float wallRunTimer;
    private float wallRunExitTimer;
    private bool wallRunExiting;
    private bool wallLeft;
    private bool wallRight;
    private RaycastHit leftWallHit;
    private RaycastHit rightWallHit;
    private float horizontalInput;
    private float verticalInput;
    private bool upwardsRunning;
    private bool downwardsRunning;

    // Wall climb state
    private float climbTimer;
    private float climbExitWallTimer;
    private bool isClimbing;
    private int climbJumpsLeft;
    private float wallLookAngle;
    private bool wallFront;
    private RaycastHit frontWallHit;
    private Transform lastWall;
    private Vector3 lastWallNormal;

    // Ledge state
    private float timeOnLedge;
    private float ledgeExitTimer;
    private Transform lastLedge;
    private Transform currentLedge;
    private RaycastHit ledgeHit;

    private void Reset()
    {
        controller = GetComponent<SmoothFirstPersonController>();
        orientation = controller != null ? controller.Orientation : transform.Find("Orientation");
        Camera foundCamera = GetComponentInChildren<Camera>();
        cameraTransform = foundCamera != null ? foundCamera.transform : null;
    }

    private void Start()
    {
        controller = GetComponent<SmoothFirstPersonController>();
        rb = GetComponent<Rigidbody>();

        if (orientation == null)
            orientation = controller.Orientation;
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || orientation == null)
            return;

        // This internal order follows the component order in FullMovement's Player prefab:
        // Climbing, WallRunningAdvanced, then LedgeGrabbing.
        if (allowWallClimbing)
        {
            ClimbWallCheck();
            ClimbStateMachine(keyboard);

            if (isClimbing && !exitingClimbWall)
                ClimbingMovement();
        }
        else
        {
            StopClimbing();
            exitingClimbWall = false;
        }

        if (allowWallRunning)
        {
            CheckForSideWalls();
            WallRunStateMachine(keyboard);
        }
        else
        {
            StopWallRun();
            wallRunExiting = false;
        }

        if (allowLedgeGrabbing)
        {
            LedgeDetection();
            LedgeStateMachine(keyboard);
        }
        else if (holdingLedge || exitingLedge)
        {
            ExitLedgeHold();
            exitingLedge = false;
        }
    }

    private void FixedUpdate()
    {
        if (controller.WallRunning)
            WallRunningMovement();
    }

    // ---------------- Wall running (ported directly) ----------------

    private void CheckForSideWalls()
    {
        wallRight = Physics.Raycast(
            transform.position,
            orientation.right,
            out rightWallHit,
            wallCheckDistance,
            whatIsWall) && IsSteepEnough(rightWallHit.normal);
        wallLeft = Physics.Raycast(
            transform.position,
            -orientation.right,
            out leftWallHit,
            wallCheckDistance,
            whatIsWall) && IsSteepEnough(leftWallHit.normal);
    }

    private bool IsSteepEnough(Vector3 normal)
    {
        return minWallSteepness <= 0f || Vector3.Angle(normal, Vector3.up) >= minWallSteepness;
    }

    private bool AboveGround()
    {
        return !Physics.Raycast(transform.position, Vector3.down, minJumpHeight, whatIsGround);
    }

    private void WallRunStateMachine(Keyboard keyboard)
    {
        horizontalInput = ReadAxis(keyboard, Key.A, Key.D);
        verticalInput = ReadAxis(keyboard, Key.S, Key.W);
        upwardsRunning = keyboard[upwardsRunKey].isPressed;
        downwardsRunning = keyboard[downwardsRunKey].isPressed;

        if ((wallLeft || wallRight) && verticalInput > 0f && AboveGround() && !wallRunExiting)
        {
            if (!controller.WallRunning)
                StartWallRun();

            if (wallRunTimer > 0f)
                wallRunTimer -= Time.deltaTime;

            if (wallRunTimer <= 0f && controller.WallRunning)
            {
                wallRunExiting = true;
                wallRunExitTimer = wallRunExitTime;
            }

            if (keyboard[jumpKey].wasPressedThisFrame)
                WallJump();
        }
        else if (wallRunExiting)
        {
            if (controller.WallRunning)
                StopWallRun();

            if (wallRunExitTimer > 0f)
                wallRunExitTimer -= Time.deltaTime;

            if (wallRunExitTimer <= 0f)
                wallRunExiting = false;
        }
        else if (controller.WallRunning)
        {
            StopWallRun();
        }
    }

    private void StartWallRun()
    {
        controller.SetWallRunning(true);
        wallRunTimer = maxWallRunTime;
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

        controller.DoFov(90f);
        if (wallLeft)
            controller.DoTilt(-5f);
        if (wallRight)
            controller.DoTilt(5f);
    }

    private void WallRunningMovement()
    {
        rb.useGravity = useGravityWhileWallRunning;

        Vector3 wallNormal = wallRight ? rightWallHit.normal : leftWallHit.normal;
        Vector3 wallForward = Vector3.Cross(wallNormal, transform.up);

        if ((orientation.forward - wallForward).magnitude >
            (orientation.forward - -wallForward).magnitude)
        {
            wallForward = -wallForward;
        }

        rb.AddForce(wallForward * wallRunForce, ForceMode.Force);

        if (upwardsRunning)
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, wallClimbSpeed, rb.linearVelocity.z);
        if (downwardsRunning)
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, -wallClimbSpeed, rb.linearVelocity.z);

        if (!(wallLeft && horizontalInput > 0f) && !(wallRight && horizontalInput < 0f))
            rb.AddForce(-wallNormal * 100f, ForceMode.Force);

        if (useGravityWhileWallRunning)
            rb.AddForce(transform.up * gravityCounterForce, ForceMode.Force);
    }

    private void StopWallRun()
    {
        if (controller == null || !controller.WallRunning)
            return;

        controller.SetWallRunning(false);
        controller.DoFov(80f);
        controller.DoTilt(0f);
    }

    private void WallJump()
    {
        if (holdingLedge || exitingLedge)
            return;

        wallRunExiting = true;
        wallRunExitTimer = wallRunExitTime;

        Vector3 wallNormal = wallRight ? rightWallHit.normal : leftWallHit.normal;
        Vector3 forceToApply = transform.up * wallJumpUpForce + wallNormal * wallJumpSideForce;

        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        rb.AddForce(forceToApply, ForceMode.Impulse);
    }

    // ---------------- Wall climbing (ported directly) ----------------

    private void ClimbWallCheck()
    {
        wallFront = Physics.SphereCast(
            transform.position,
            climbSphereCastRadius,
            orientation.forward,
            out frontWallHit,
            climbDetectionLength,
            whatIsWall) && IsSteepEnough(frontWallHit.normal);

        // Compare headings only, so leaning (non-vertical) rock faces still count as facing the wall.
        Vector3 flatWallNormal = Vector3.ProjectOnPlane(frontWallHit.normal, Vector3.up);
        wallLookAngle = flatWallNormal.sqrMagnitude > 1e-6f
            ? Vector3.Angle(orientation.forward, -flatWallNormal)
            : 180f;

        bool newWall = frontWallHit.transform != lastWall ||
                       Mathf.Abs(Vector3.Angle(lastWallNormal, frontWallHit.normal)) >
                       minWallNormalAngleChange;

        if ((wallFront && newWall) || controller.Grounded)
        {
            climbTimer = maxClimbTime;
            climbJumpsLeft = climbJumps;
        }
    }

    private void ClimbStateMachine(Keyboard keyboard)
    {
        if (holdingLedge)
        {
            if (isClimbing)
                StopClimbing();
        }
        else if (wallFront && keyboard.wKey.isPressed &&
                 wallLookAngle < maxWallLookAngle && !exitingClimbWall)
        {
            if (!isClimbing && climbTimer > 0f)
                StartClimbing();

            if (climbTimer > 0f)
                climbTimer -= Time.deltaTime;
            if (climbTimer < 0f)
                StopClimbing();
        }
        else if (exitingClimbWall)
        {
            if (isClimbing)
                StopClimbing();

            if (climbExitWallTimer > 0f)
                climbExitWallTimer -= Time.deltaTime;
            if (climbExitWallTimer < 0f)
                exitingClimbWall = false;
        }
        else if (isClimbing)
        {
            StopClimbing();
        }

        if (wallFront && keyboard[jumpKey].wasPressedThisFrame && climbJumpsLeft > 0)
            ClimbJump();
    }

    private void StartClimbing()
    {
        isClimbing = true;
        controller.SetClimbing(true);
        lastWall = frontWallHit.transform;
        lastWallNormal = frontWallHit.normal;
    }

    private void ClimbingMovement()
    {
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, climbSpeed, rb.linearVelocity.z);
    }

    private void StopClimbing()
    {
        // Only clear the flag this script set itself; HandClimber may be climbing.
        if (!isClimbing)
            return;
        isClimbing = false;
        if (controller != null)
            controller.SetClimbing(false);
    }

    private void ClimbJump()
    {
        if (controller.Grounded)
            return;
        if (holdingLedge || exitingLedge)
            return;

        exitingClimbWall = true;
        climbExitWallTimer = climbExitWallTime;

        Vector3 forceToApply = transform.up * climbJumpUpForce +
                               frontWallHit.normal * climbJumpBackForce;

        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        rb.AddForce(forceToApply, ForceMode.Impulse);
        climbJumpsLeft--;
    }

    // ---------------- Ledge grabbing (ported directly) ----------------

    private void LedgeStateMachine(Keyboard keyboard)
    {
        float ledgeHorizontalInput = ReadAxis(keyboard, Key.A, Key.D);
        float ledgeVerticalInput = ReadAxis(keyboard, Key.S, Key.W);
        bool anyInputKeyPressed = ledgeHorizontalInput != 0f || ledgeVerticalInput != 0f;

        if (holdingLedge)
        {
            FreezeRigidbodyOnLedge();
            timeOnLedge += Time.deltaTime;

            if (timeOnLedge > minTimeOnLedge && anyInputKeyPressed)
                ExitLedgeHold();

            if (keyboard[jumpKey].wasPressedThisFrame)
                LedgeJump();
        }
        else if (exitingLedge)
        {
            if (ledgeExitTimer > 0f)
                ledgeExitTimer -= Time.deltaTime;
            else
                exitingLedge = false;
        }
    }

    private void LedgeDetection()
    {
        if (cameraTransform == null)
            return;

        bool ledgeDetected = Physics.SphereCast(
            transform.position,
            ledgeSphereCastRadius,
            cameraTransform.forward,
            out ledgeHit,
            ledgeDetectionLength,
            whatIsLedge);

        if (!ledgeDetected)
            return;

        float distanceToLedge = Vector3.Distance(transform.position, ledgeHit.transform.position);

        if (ledgeHit.transform == lastLedge)
            return;

        if (distanceToLedge < maxLedgeGrabDistance && !holdingLedge)
            EnterLedgeHold();
    }

    private void LedgeJump()
    {
        ExitLedgeHold();
        Invoke(nameof(DelayedLedgeJumpForce), 0.05f);
    }

    private void DelayedLedgeJumpForce()
    {
        Vector3 forceToAdd = cameraTransform.forward * ledgeJumpForwardForce +
                             orientation.up * ledgeJumpUpwardForce;
        rb.linearVelocity = Vector3.zero;
        rb.AddForce(forceToAdd, ForceMode.Impulse);
    }

    private void EnterLedgeHold()
    {
        holdingLedge = true;
        controller.SetUnlimited(true);
        controller.SetRestricted(true);

        currentLedge = ledgeHit.transform;
        lastLedge = ledgeHit.transform;

        rb.useGravity = false;
        rb.linearVelocity = Vector3.zero;
    }

    private void FreezeRigidbodyOnLedge()
    {
        rb.useGravity = false;

        Vector3 directionToLedge = currentLedge.position - transform.position;
        float distanceToLedge = Vector3.Distance(transform.position, currentLedge.position);

        if (distanceToLedge > 1f)
        {
            if (rb.linearVelocity.magnitude < moveToLedgeSpeed)
            {
                rb.AddForce(
                    directionToLedge.normalized * moveToLedgeSpeed * 1000f * Time.deltaTime);
            }
        }
        else
        {
            controller.SetFreeze(true);
            controller.SetUnlimited(false);
        }

        if (distanceToLedge > maxLedgeGrabDistance)
            ExitLedgeHold();
    }

    private void ExitLedgeHold()
    {
        exitingLedge = true;
        ledgeExitTimer = ledgeExitTime;
        holdingLedge = false;
        timeOnLedge = 0f;

        if (controller != null)
        {
            controller.SetRestricted(false);
            controller.SetFreeze(false);
        }

        if (rb != null)
            rb.useGravity = true;

        Invoke(nameof(ResetLastLedge), 1f);
    }

    private void ResetLastLedge()
    {
        lastLedge = null;
    }

    private static float ReadAxis(Keyboard keyboard, Key negative, Key positive)
    {
        return (keyboard[positive].isPressed ? 1f : 0f) -
               (keyboard[negative].isPressed ? 1f : 0f);
    }
}

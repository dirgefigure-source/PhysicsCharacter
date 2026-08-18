using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Prototype physical pickup: E carries the nearest persistent object and E
/// again places it on the ground in front of the character.
/// </summary>
[DefaultExecutionOrder(500)]
[DisallowMultipleComponent]
public sealed class PlayerPickupController2D : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float pickupRange = 1.75f;
    [Tooltip("Offsets interaction distance from the torso toward ground-level objects.")]
    [SerializeField] private Vector2 pickupOriginOffset = new(0f, -1f);
    [SerializeField, Min(0f)] private float carryHorizontalOffset = 0.9f;
    [SerializeField] private float carryVerticalOffset = 0.15f;
    [SerializeField, Min(0f)] private float dropHorizontalOffset = 1.05f;
    [SerializeField] private LayerMask groundLayers = 128;
    [SerializeField, Min(0.1f)] private float dropGroundProbeHeight = 2f;
    [SerializeField, Min(0.1f)] private float dropGroundProbeDistance = 6f;
    [SerializeField, Min(0f)] private float dropGroundClearance = 0.02f;

    private readonly InputAction interactAction = new(
        "Pick Up / Put Down",
        InputActionType.Button,
        "<Keyboard>/e");

    private WalkCyclePdMotorDriver locomotion;
    private CylindricalWorld2D world;
    private Rigidbody2D carrierBody;
    private PersistentEntity2D heldEntity;
    private Rigidbody2D heldBody;
    private Collider2D[] heldColliders;
    private bool[] heldColliderStates;
    private float heldHalfHeight;
    private RigidbodyType2D savedBodyType;
    private float savedGravityScale;
    private RigidbodyConstraints2D savedConstraints;
    private CylindricalRigidbodyGroup2D heldWrap;
    private bool heldWrapWasEnabled;
    private ShipStorageZone2D storageZone;
    private PlanetPersistencePrototype persistence;

    public bool IsCarrying => heldEntity != null;
    public Vector2 InteractionPosition => carrierBody != null
        ? carrierBody.position
        : transform.position;

    private void Awake()
    {
        locomotion = GetComponent<WalkCyclePdMotorDriver>();
        world = FindFirstObjectByType<CylindricalWorld2D>();
        storageZone = FindFirstObjectByType<ShipStorageZone2D>();
        persistence = FindFirstObjectByType<PlanetPersistencePrototype>();
        carrierBody = FindBody(transform, "Body");

        if (locomotion == null)
            throw new InvalidOperationException("Player Pickup requires WalkCyclePdMotorDriver.");
        if (world == null)
            throw new InvalidOperationException("Player Pickup requires CylindricalWorld2D.");
        if (carrierBody == null)
            throw new InvalidOperationException("Player Pickup cannot find the Body Rigidbody2D.");
    }

    private void OnEnable()
    {
        interactAction.Enable();
    }

    private void OnDisable()
    {
        interactAction.Disable();
        if (heldEntity != null)
            PutDown();
    }

    private void Update()
    {
        if (!interactAction.WasPressedThisFrame()) return;

        bool insideStorage = storageZone != null &&
            storageZone.Contains(InteractionPosition);
        if (heldEntity != null)
        {
            if (insideStorage)
                StoreHeldEntity();
            else
                PutDown();
            return;
        }

        if (insideStorage && storageZone.TryGetStoredEntity(out PersistentEntity2D stored))
        {
            WithdrawStoredEntity(stored);
            return;
        }

        TryPickUpNearest();
    }

    private void FixedUpdate()
    {
        if (heldBody == null) return;

        heldBody.position = GetCarryPosition();
        heldBody.rotation = 0f;
        heldBody.linearVelocity = Vector2.zero;
        heldBody.angularVelocity = 0f;
    }

    private void TryPickUpNearest()
    {
        PersistentEntity2D nearest = null;
        float nearestDistance = pickupRange;
        Vector2 origin = carrierBody.position + pickupOriginOffset;

        foreach (PersistentEntity2D candidate in FindObjectsByType<PersistentEntity2D>(
                     FindObjectsInactive.Exclude,
                     FindObjectsSortMode.None))
        {
            if (!candidate.TryGetComponent(out Rigidbody2D candidateBody))
                continue;

            float distance = world.ShortestDistance(origin, candidateBody.position);
            if (distance > nearestDistance) continue;
            nearest = candidate;
            nearestDistance = distance;
        }

        if (nearest != null)
            PickUp(nearest);
    }

    private void PickUp(PersistentEntity2D entity)
    {
        if (!entity.TryGetComponent(out Rigidbody2D body)) return;

        heldEntity = entity;
        heldBody = body;
        savedBodyType = body.bodyType;
        savedGravityScale = body.gravityScale;
        savedConstraints = body.constraints;

        heldColliders = entity.GetComponentsInChildren<Collider2D>(true);
        heldColliderStates = new bool[heldColliders.Length];
        heldHalfHeight = 0f;
        for (int i = 0; i < heldColliders.Length; i++)
        {
            heldColliderStates[i] = heldColliders[i].enabled;
            if (heldColliders[i].enabled)
                heldHalfHeight = Mathf.Max(
                    heldHalfHeight,
                    heldColliders[i].bounds.extents.y);
            heldColliders[i].enabled = false;
        }

        heldWrap = entity.GetComponent<CylindricalRigidbodyGroup2D>();
        if (heldWrap != null)
        {
            heldWrapWasEnabled = heldWrap.enabled;
            heldWrap.enabled = false;
        }

        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;
        body.constraints = RigidbodyConstraints2D.FreezeRotation;
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
        body.position = GetCarryPosition();
        body.rotation = 0f;
        Physics2D.SyncTransforms();
    }

    private void PutDown()
    {
        if (heldBody == null)
        {
            ClearHeldState();
            return;
        }

        Vector2 dropPosition = GetDropPosition();
        heldBody.position = dropPosition;
        heldBody.rotation = 0f;
        heldBody.linearVelocity = Vector2.zero;
        heldBody.angularVelocity = 0f;

        for (int i = 0; i < heldColliders.Length; i++)
            if (heldColliders[i] != null)
                heldColliders[i].enabled = heldColliderStates[i];

        heldBody.constraints = savedConstraints;
        heldBody.gravityScale = savedGravityScale;
        heldBody.bodyType = savedBodyType;
        heldBody.linearVelocity = Vector2.zero;
        heldBody.angularVelocity = 0f;

        if (heldWrap != null)
            heldWrap.enabled = heldWrapWasEnabled;

        Physics2D.SyncTransforms();
        ClearHeldState();
        persistence?.NotifyWorldStateChanged();
    }

    private void StoreHeldEntity()
    {
        PersistentEntity2D entity = heldEntity;
        PutDown();
        if (entity == null) return;

        entity.SetStored(true);
        entity.gameObject.SetActive(false);
        persistence?.NotifyWorldStateChanged();
    }

    private void WithdrawStoredEntity(PersistentEntity2D entity)
    {
        if (entity == null) return;

        entity.SetStored(false);
        entity.gameObject.SetActive(true);
        if (entity.TryGetComponent(out Rigidbody2D body))
        {
            body.position = GetCarryPosition();
            body.rotation = 0f;
        }
        PickUp(entity);
        persistence?.NotifyWorldStateChanged();
    }

    private Vector2 GetCarryPosition()
    {
        Vector2 position = carrierBody.position + new Vector2(
            locomotion.FacingDirection * carryHorizontalOffset,
            carryVerticalOffset);
        return world.WrapPosition(position);
    }

    private Vector2 GetDropPosition()
    {
        float x = world.WrapX(
            carrierBody.position.x + locomotion.FacingDirection * dropHorizontalOffset);
        Vector2 probeOrigin = new(
            x,
            carrierBody.position.y + dropGroundProbeHeight);
        RaycastHit2D hit = Physics2D.Raycast(
            probeOrigin,
            Vector2.down,
            dropGroundProbeHeight + dropGroundProbeDistance,
            groundLayers);
        if (hit.collider == null)
            return GetCarryPosition();

        return new Vector2(
            x,
            hit.point.y + heldHalfHeight + dropGroundClearance);
    }

    private void ClearHeldState()
    {
        heldEntity = null;
        heldBody = null;
        heldColliders = null;
        heldColliderStates = null;
        heldHalfHeight = 0f;
        heldWrap = null;
        heldWrapWasEnabled = false;
    }

    private static Rigidbody2D FindBody(Transform root, string objectName)
    {
        foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(candidate.name.Trim(), objectName, StringComparison.Ordinal) &&
                candidate.TryGetComponent(out Rigidbody2D body))
                return body;
        return null;
    }
}

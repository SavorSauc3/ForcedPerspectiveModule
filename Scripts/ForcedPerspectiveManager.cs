using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using System.Collections.Generic;


#if UNITY_EDITOR
using UnityEditor;
#endif

public class RaycastDrawer : MonoBehaviour
{
    [SerializeField] private Texture2D grabTexture;  // Texture for the grab action
    [SerializeField] private Texture2D handTexture;  // Texture for the hand action
    [SerializeField] private float rayLength = 10f;  // Length of the ray
    [SerializeField] private LayerMask interactableLayer; // Layer mask to specify interactable layers
    [SerializeField] private LayerMask grabbedObjectLayer; // Layer mask for the grabbed object layer
    [SerializeField] private LayerMask playerLayer; // Layer mask for the player layer
    [SerializeField] private float lerpSpeed = 50f; // Adjust this value to control the speed of the lerping
    [SerializeField] private float sphereCheckRadiusMultiplier = 1f;
    [SerializeField] private float objectMovementCoefficient = 0.05f;
    [SerializeField] private float positionUpdateThreshold = 10f; // Must be above threshold to update position
    [SerializeField] private float maxPositionJump = 2.0f; // Maximum allowed distance for position jump
    [SerializeField] private float maxScaleFactor = 100f;
    [SerializeField] private float minScaleFactor = 0.1f;
    [SerializeField] private float maxDistanceToSizeRatio = 1.5f;
    [SerializeField] private float rotateSpeed = 100f; // Speed of rotation

    private Camera playerCamera;  // The camera component
    private Image cursorImage;    // UI Image component for the cursor
    private Sprite grabSprite;    // Sprite for the grab action
    private Sprite handSprite;    // Sprite for the hand action
    private GameObject grabbedObject; // Object currently being grabbed
    private Vector3 grabOffset; // Offset from the camera to the grabbed object
    private Rigidbody grabbedObjectRigidbody; // Rigidbody of the grabbed object
    private int originalLayer; // To store the original layer of the grabbed object
    private Collider grabbedObjectCollider; // Collider of the grabbed object

    private Vector3 previousPosition; // Stores the previous object position

    private float initialObjectDistance; // Initial distance from the camera
    private Vector3 initialObjectScale;   // Initial scale of the object

    private void Start()
    {
        // Find the camera in the scene
        playerCamera = Camera.main;

        if (playerCamera == null)
        {
            Debug.LogError("No camera found in the scene.");
        }

        // Find the UI Canvas and the Image component for the cursor
        Canvas uiCanvas = FindObjectOfType<Canvas>();
        if (uiCanvas != null)
        {
            cursorImage = uiCanvas.GetComponentInChildren<Image>();
        }

        if (cursorImage == null)
        {
            Debug.LogError("No Image component found in the UI Canvas.");
        }

        // Convert Texture2D to Sprite
        grabSprite = TextureToSprite(grabTexture);
        handSprite = TextureToSprite(handTexture);
    }

    private void Update()
    {
        if (playerCamera == null || cursorImage == null)
        {
            return;
        }

        // Create a ray from the center of the camera
        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        RaycastHit hit;

        // Draw the ray in the Scene view
        Debug.DrawRay(ray.origin, ray.direction * rayLength, Color.red);

        // Exclude the player layer from raycasting
        int layerMask = interactableLayer & ~(1 << LayerMask.NameToLayer("Player"));

        bool isObjectGetable = Physics.Raycast(ray, out hit, rayLength, layerMask) && hit.collider.CompareTag("Getable");

        if (grabbedObject != null)
        {
            // If an object is being grabbed, ensure the grab cursor is displayed
            cursorImage.sprite = grabSprite;

            // Rotate the object around the y-axis if the right mouse button is held
            if (Mouse.current.rightButton.isPressed)
            {
                // Set rotation direction based on mouse movement or input
                grabbedObject.transform.Rotate(Vector3.up, rotateSpeed * Time.deltaTime, Space.World);
            }
        }
        else
        {
            // Handle cursor for hover state
            if (isObjectGetable)
            {
                cursorImage.sprite = handSprite;

                if (Mouse.current.leftButton.wasPressedThisFrame) // Check for input using the new Input System
                {
                    GrabObject(hit.collider.gameObject);
                }
            }
            else
            {
                cursorImage.sprite = grabSprite;
            }
        }

        // Release the object if the button is released
        if (Mouse.current.leftButton.wasReleasedThisFrame)
        {
            ReleaseObject();
        }
    }

    private void FixedUpdate()
    {
        if (grabbedObject != null)
        {
            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
            HandleObjectMovement(ray);
            AdjustObjectSize();
        }
    }

    private void HandleObjectMovement(Ray ray)
    {
        if (grabbedObject != null)
        {
            Vector3 newPosition = CalculateTargetPosition(ray);

            // Ensure the new position is valid and adjusted if needed
            newPosition = AdjustPositionAwayFromSurfaces(newPosition, ray);

            // Clamp the position and ensure it is in front of the camera
            newPosition = ClampAndCorrectPosition(newPosition, ray);

            // Prevent the object from glitching through walls
            newPosition = PreventGlitchThroughWalls(newPosition, ray);

            // Move the object smoothly to the adjusted position
            MoveObject(newPosition);
        }
    }

    private Vector3 CalculateTargetPosition(Ray ray)
    {
        RaycastHit hitInfo;
        int ignoreLayerMask = LayerMask.GetMask("Player", "GrabbedObjectLayer");
        int layerMask = ~ignoreLayerMask;

        if (Physics.Raycast(ray, out hitInfo, rayLength, layerMask))
        {
            Vector3 targetPosition = hitInfo.point;
            Vector3 offset = ray.direction * grabbedObjectCollider.bounds.extents.magnitude;
            return targetPosition - offset;
        }
        else
        {
            return ray.GetPoint(rayLength);
        }
    }

    private Vector3 AdjustPositionAwayFromSurfaces(Vector3 position, Ray ray)
    {
        Vector3 directionAway = (position - ray.GetPoint(rayLength)).normalized;

        while (IsObjectTooCloseToSurfaces(position))
        {
            position += directionAway * objectMovementCoefficient;

            // Ensure we are not moving too far away
            if (Vector3.Distance(position, ray.GetPoint(rayLength)) > rayLength)
            {
                position = ray.GetPoint(rayLength);
                break;
            }
        }

        return position;
    }

    private Vector3 ClampAndCorrectPosition(Vector3 position, Ray ray)
    {
        position = Vector3.ClampMagnitude(position - ray.origin, rayLength) + ray.origin;

        if (Vector3.Dot(ray.direction, position - ray.origin) < 0)
        {
            position = ray.origin + ray.direction * 0.1f; // Move it slightly in front of the camera
        }

        return position;
    }

    private Vector3 PreventGlitchThroughWalls(Vector3 position, Ray ray)
    {
        Vector3 adjustedPosition = GetAdjustedPosition(position, previousPosition, ray);

        // Check if the adjusted position is valid
        if (IsObjectTooCloseToSurfaces(adjustedPosition))
        {
            // Move the position slightly back along the ray
            adjustedPosition = position - ray.direction * 0.1f;
        }

        return adjustedPosition;
    }

    private Vector3 GetAdjustedPosition(Vector3 currentPosition, Vector3 previousPosition, Ray ray)
    {
        Vector3 closestPoint = Vector3.Lerp(previousPosition, currentPosition, 0.5f);
        float adjustmentDistance = 0.5f; // Adjust this value as needed
        return closestPoint - ray.direction * adjustmentDistance;
    }

    private void MoveObject(Vector3 newPosition)
    {
        grabbedObject.transform.position = Vector3.Lerp(grabbedObject.transform.position, newPosition, lerpSpeed * Time.deltaTime);
        previousPosition = grabbedObject.transform.position; // Update previous position
    }

    private bool IsObjectTooCloseToSurfaces(Vector3 position)
    {
        if (grabbedObject == null)
            return false;

        Collider collider = grabbedObject.GetComponent<Collider>();

        if (collider == null)
            return false;

        // Calculate dynamic radius based on the current scale of the object
        float radius = Mathf.Max(collider.bounds.extents.x, collider.bounds.extents.y, collider.bounds.extents.z) * sphereCheckRadiusMultiplier;

        // Use the dynamic radius for the sphere check
        int ignoreLayerMask = LayerMask.GetMask("GrabbedObjectLayer", "Player");
        Collider[] hitColliders = Physics.OverlapSphere(position, radius, ~ignoreLayerMask);

        foreach (var hitCollider in hitColliders)
        {
            if (hitCollider != collider)
            {
                Debug.Log($"TOO CLOSE: {position} - Radius: {radius} - HitCollider: {hitCollider.name}");
                return true; // Object is too close to another surface
            }
        }
        Debug.Log($"NOT TOO CLOSE: {position} - Radius: {radius}");
        return false;
    }

    private void GrabObject(GameObject objectToGrab)
    {
        // Store the original layer of the object
        originalLayer = objectToGrab.layer;

        // Set the object's layer to the grabbed object layer
        objectToGrab.layer = LayerMask.NameToLayer("GrabbedObjectLayer");

        // Initialize the grab state
        grabbedObject = objectToGrab;
        grabbedObjectRigidbody = grabbedObject.GetComponent<Rigidbody>();
        grabbedObjectCollider = grabbedObject.GetComponent<Collider>();

        if (grabbedObjectRigidbody != null)
        {
            // Disable gravity and set kinematic to ensure script controls movement
            grabbedObjectRigidbody.useGravity = false;
            grabbedObjectRigidbody.isKinematic = true;
        }
        else
        {
            Debug.LogWarning("Grabbed object does not have a Rigidbody component.");
        }

        // Calculate the grab offset
        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        grabOffset = grabbedObject.transform.position - ray.origin;

        // Store the initial distance, scale of the object
        initialObjectDistance = Vector3.Distance(playerCamera.transform.position, grabbedObject.transform.position);
        initialObjectScale = grabbedObject.transform.localScale;

        // Ensure cursor image is updated
        if (cursorImage != null)
        {
            cursorImage.sprite = grabSprite;
        }
    }

    private void ReleaseObject()
    {
        if (grabbedObject != null)
        {
            // Reset the object's layer to its original state
            grabbedObject.layer = originalLayer;

            if (grabbedObjectRigidbody != null)
            {
                // Re-enable gravity and revert kinematic state
                grabbedObjectRigidbody.useGravity = true;
                grabbedObjectRigidbody.isKinematic = false;
            }

            // Clear the grab state
            grabbedObject = null;
            grabbedObjectRigidbody = null;
            grabbedObjectCollider = null;

            // Update the cursor image
            if (cursorImage != null)
            {
                cursorImage.sprite = grabSprite;
            }
        }
    }

    private void AdjustObjectSize()
    {
        if (grabbedObject != null)
        {
            // Calculate the closest point on the object to the camera
            Vector3 closestPoint = grabbedObjectCollider.ClosestPoint(playerCamera.transform.position);

            // Calculate the distance from the closest point to the camera
            float currentDistance = Vector3.Distance(closestPoint, playerCamera.transform.position);

            // Calculate the scale factor based on the closest distance
            float scaleFactor = Mathf.Clamp(currentDistance / initialObjectDistance, minScaleFactor, maxScaleFactor);

            // Apply the new scale to the object, preserving the aspect ratio
            Vector3 newScale = initialObjectScale * scaleFactor;
            grabbedObject.transform.localScale = newScale;
        }
    }

    private Sprite TextureToSprite(Texture2D texture)
    {
        Rect rect = new Rect(0, 0, texture.width, texture.height);
        return Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f));
    }


    // Optional method to draw gizmos in the Scene view
    private void OnDrawGizmos()
    {
        if (grabbedObject != null)
        {
            Gizmos.color = Color.red;

            // Get the collider of the grabbed object
            Collider collider = grabbedObject.GetComponent<Collider>();

            if (collider != null)
            {
                // Calculate the radius for the gizmo based on the collider's extents
                float radius = Mathf.Max(collider.bounds.extents.x, collider.bounds.extents.y, collider.bounds.extents.z);

                // Increase the radius by 1.2 times
                float adjustedRadius = radius * sphereCheckRadiusMultiplier;

                // Draw the wire sphere with the adjusted radius
                Gizmos.DrawWireSphere(grabbedObject.transform.position, adjustedRadius);
            }
            else
            {
                // If no collider, use a default radius increased by 1.2 times
                float defaultRadius = 0.5f * 1.2f;
                Gizmos.DrawWireSphere(grabbedObject.transform.position, defaultRadius);
            }
        }
    }
}

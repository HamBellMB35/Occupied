﻿/// ---------------------------------------------
/// Ultimate Character Controller
/// Copyright (c) Opsive. All Rights Reserved.
/// https://www.opsive.com
/// ---------------------------------------------

namespace Opsive.UltimateCharacterController.FirstPersonController.Camera.ViewTypes
{
    using Opsive.Shared.Game;
    using Opsive.Shared.Utility;
    using Opsive.UltimateCharacterController.Camera;
    using Opsive.UltimateCharacterController.Camera.ViewTypes;
    using Opsive.UltimateCharacterController.Utility;
    using UnityEngine;

    /// <summary>
    /// The TransformLook ViewType is a first person view type that will have the camera look in the forward direction relative to the target transform.
    /// </summary>
    public class TransformLook : ViewType
    {
        [Tooltip("The object that determines the position of the camera.")]
        [SerializeField] protected Transform m_MoveTarget;
        [Tooltip("The object that determines the rotation of the camera.")]
        [SerializeField] protected Transform m_RotationTarget;
        [Tooltip("The offset relative to the move target.")]
        [SerializeField] protected Vector3 m_Offset = new Vector3(0, 0.2f, 0.2f);
        [Tooltip("The radius of the camera's collision sphere to prevent it from clipping with other objects.")]
        [SerializeField] protected float m_CollisionRadius = 0.05f;
        [Tooltip("The amount of smoothing to apply to the position. Can be zero.")]
        [SerializeField] protected float m_PositionSmoothing = 0.08f;
        [Tooltip("The speed at which the view type should rotate towards the target rotation.")]
        [Range(0, 1)] [SerializeField] protected float m_RotationalLerpSpeed = 0.9f;
        [Tooltip("Should the pitch be restricted? If false the pitch can be changed by the player.")]
        [SerializeField] protected bool m_RestrictPitch = true;
        [Tooltip("The minimum and maximum pitch angle (in degrees), used if the pitch is not restricted.")]
        [MinMaxRange(-90, 90)] [SerializeField] protected MinMaxFloat m_PitchLimit = new MinMaxFloat(-72, 72);
        [Tooltip("Should the yaw be restricted? If false the yaw can be changed by the player.")]
        [SerializeField] protected bool m_RestrictYaw = true;

        public Transform MoveTarget { get { return m_MoveTarget; } set { m_MoveTarget = value; } }
        public Transform RotationTarget { get { return m_RotationTarget; } set { m_RotationTarget = value; } }
        public Vector3 Offset { get { return m_Offset; } set { m_Offset = value; } }
        public float CollisionRadius { get { return m_CollisionRadius; } set { m_CollisionRadius = value; } }
        public float PositionSmoothing { get { return m_PositionSmoothing; } set { m_PositionSmoothing = value; } }
        public float RotationalLerpSpeed { get { return m_RotationalLerpSpeed; } set { m_RotationalLerpSpeed = value; } }
        public bool RestrictPitch { get { return m_RestrictPitch; } set { m_RestrictPitch = value; } }
        public MinMaxFloat PitchLimit { get { return m_PitchLimit; } set { m_PitchLimit = value; } }
        public bool RestrictYaw { get { return m_RestrictYaw; } set { m_RestrictYaw = value; } }

        public override Quaternion BaseCharacterRotation { get { return CharacterRotation; } }
        public override bool FirstPersonPerspective { get { return true; } }
        public override float LookDirectionDistance { get { return m_Offset.magnitude; } }
        public override float Pitch { get { return m_Pitch; } }
        public override float Yaw { get { return m_Yaw; } }

        private RaycastHit m_RaycastHit;
        private float m_Pitch;
        private float m_Yaw;
        private Vector3 m_SmoothPositionVelocity;

        /// <summary>
        /// Initializes the view type to the specified camera controller.
        /// </summary>
        /// <param name="cameraController">The camera controller to initialize the view type to.</param>
        public override void Initialize(CameraController cameraController)
        {
            base.Initialize(cameraController);

            m_Camera = cameraController.gameObject.GetCachedComponent<UnityEngine.Camera>();
        }

        /// <summary>
        /// Attaches the view type to the specified character.
        /// </summary>
        /// <param name="character">The character to attach the camera to.</param>
        public override void AttachCharacter(GameObject character)
        {
            base.AttachCharacter(character);

            if (m_MoveTarget == null || m_RotationTarget == null) {
                if (m_Character == null) {
                    return;
                }
                Transform moveTarget = m_Character.transform, rotationTarget = m_Character.transform;
                Animator characterAnimator = null;
                var modelManager = m_Character.GetCachedComponent<Opsive.UltimateCharacterController.Character.ModelManager>();
                if (modelManager != null) {
                    characterAnimator = modelManager.ActiveModel.GetComponent<Animator>();
                } else {
                    var animatorMonitor = m_Character.GetComponentInChildren<Opsive.UltimateCharacterController.Character.AnimatorMonitor>();
                    if (animatorMonitor != null) {
                        characterAnimator = animatorMonitor.GetComponent<Animator>();
                    }
                }
                if (characterAnimator != null && characterAnimator.isHuman) {
                    moveTarget = characterAnimator.GetBoneTransform(HumanBodyBones.Head);
                    rotationTarget = characterAnimator.GetBoneTransform(HumanBodyBones.Hips);
                }

                m_MoveTarget = moveTarget;
                m_RotationTarget = rotationTarget;
            }
        }
        /// <summary>
        /// The view type has changed.
        /// </summary>
        /// <param name="activate">Should the current view type be activated?</param>
        /// <param name="pitch">The pitch of the camera (in degrees).</param>
        /// <param name="yaw">The yaw of the camera (in degrees).</param>
        /// <param name="baseCharacterRotation">The rotation of the character.</param>
        public override void ChangeViewType(bool activate, float pitch, float yaw, Quaternion baseCharacterRotation)
        {
            if (activate) {
                m_Pitch = m_RestrictPitch ? 0 : pitch;
                m_Yaw = m_RestrictYaw ? 0 : yaw;
            }
        }

        /// <summary>
        /// Reset the ViewType's variables.
        /// </summary>
        /// <param name="characterRotation">The rotation of the character.</param>
        public override void Reset(Quaternion characterRotation)
        {
            m_Pitch = 0;
            m_Yaw = 0;
        }

        /// <summary>
        /// Resets the View Type rotation parameters to the specified values.
        /// </summary>
        /// <param name="pitch">The pitch of the camera (in degrees).</param>
        /// <param name="yaw">The yaw of the camera (in degrees).</param>
        /// <param name="baseCharacterRotation">The rotation of the character.</param>
        public override void ResetRotation(float pitch, float yaw, Quaternion baseCharacterRotation)
        {
            m_Pitch = m_RestrictPitch ? 0 : pitch;
            m_Yaw = m_RestrictYaw ? 0 : yaw;
        }

        /// <summary>
        /// Rotates the camera to face in the same direction as the target.
        /// </summary>
        /// <param name="horizontalMovement">-1 to 1 value specifying the amount of horizontal movement.</param>
        /// <param name="verticalMovement">-1 to 1 value specifying the amount of vertical movement.</param>
        /// <param name="immediateUpdate">Should the camera be updated immediately?</param>
        /// <returns>The updated rotation.</returns>
        public override Quaternion Rotate(float horizontalMovement, float verticalMovement, bool immediateUpdate)
        {
            var rotation = m_RotationTarget.rotation;
            if (!immediateUpdate) {
                rotation = Quaternion.Slerp(m_Transform.rotation, rotation, m_RotationalLerpSpeed);
            }

            // Update the pitch and yaw if they are not restricted.
            if (!m_RestrictPitch) {
                if (Mathf.Abs(m_PitchLimit.MinValue - m_PitchLimit.MaxValue) < 180) {
                    m_Pitch = MathUtility.ClampAngle(m_Pitch, -verticalMovement, m_PitchLimit.MinValue, m_PitchLimit.MaxValue);
                } else {
                    m_Pitch -= verticalMovement;
                }
            }

            if (!m_RestrictYaw) {
                if (m_Pitch > 90 || m_Pitch < -90) {
                    horizontalMovement *= -1;
                }
                m_Yaw = MathUtility.ClampInnerAngle(m_Yaw + horizontalMovement);
            }

            return rotation * Quaternion.Euler(m_Pitch, m_Yaw, 0);
        }

        /// <summary>
        /// Moves the camera to be in the target position.
        /// </summary>
        /// <param name="immediateUpdate">Should the camera be updated immediately?</param>
        /// <returns>The updated position.</returns>
        public override Vector3 Move(bool immediateUpdate)
        {
            // Ensure there aren't any objects obstructing the distance between the anchor offset and the target position.
            var collisionEnabled = m_CharacterLocomotion.CollisionLayerEnabled;
            m_CharacterLocomotion.EnableColliderCollisionLayer(false);
            var targetPosition = m_MoveTarget.TransformPoint(m_Offset);
            var direction = targetPosition - m_MoveTarget.position;
            if (Physics.SphereCast(m_MoveTarget.position, m_CollisionRadius, direction.normalized, out m_RaycastHit, direction.magnitude + m_Camera.nearClipPlane,
                            m_CharacterLayerManager.IgnoreInvisibleCharacterWaterLayers, QueryTriggerInteraction.Ignore)) {
                // Move the camera in if an object obstructed the view.
                targetPosition = m_RaycastHit.point + m_RaycastHit.normal * (m_Camera.nearClipPlane + m_CharacterLocomotion.ColliderSpacing);
            }
            m_CharacterLocomotion.EnableColliderCollisionLayer(collisionEnabled);

            return Vector3.SmoothDamp(m_Transform.position, targetPosition, ref m_SmoothPositionVelocity, immediateUpdate ? 0 : m_PositionSmoothing);
        }

        /// <summary>
        /// Returns the direction that the character is looking.
        /// </summary>
        /// <param name="characterLookDirection">Is the character look direction being retrieved?</param>
        /// <returns>The direction that the character is looking.</returns>
        public override Vector3 LookDirection(bool characterLookDirection)
        {
            return m_CharacterLocomotion.Rotation * Vector3.forward;
        }

        /// <summary>
        /// Returns the direction that the character is looking.
        /// </summary>
        /// <param name="lookPosition">The position that the character is looking from.</param>
        /// <param name="characterLookDirection">Is the character look direction being retrieved?</param>
        /// <param name="layerMask">The LayerMask value of the objects that the look direction can hit.</param>
        /// <param name="includeRecoil">Should recoil be included in the look direction?</param>
        /// <param name="includeMovementSpread">Should the movement spread be included in the look direction?</param>
        /// <returns>The direction that the character is looking.</returns>
        public override Vector3 LookDirection(Vector3 lookPosition, bool characterLookDirection, int layerMask, bool includeRecoil, bool includeMovementSpread)
        {
            return m_CharacterLocomotion.Rotation * Vector3.forward;
        }
    }
}
using System.Collections.Generic;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>카메라 리그 회전, 1인칭 눈 위치, 3인칭 벽 충돌, 조준 확대, 1인칭 머리 숨김</summary>
    public partial class PlayerController
    {
        #region 카메라 리그
        /// <summary>카메라·리그의 원래 자리와 FOV 를 1회 저장한다. 빠진 참조가 있으면 카메라 기능을 끈다</summary>
        private void CacheRig()
        {
            rigReady = FirstParsonCam != null && ThirdParsonCam != null
                && fpRig != null && fpPitch != null && tpRig != null && tpPitch != null;
            if (rigReady == false)
            {
                Debug.LogError($"{name}: PlayerController 카메라 리그 참조가 비어 있습니다 (FP/TP Camera, Rig, Pitch)", this);
                return;
            }

            fpCamera = FirstParsonCam.GetComponent<Camera>();
            tpCamera = ThirdParsonCam.GetComponent<Camera>();
            if (fpCamera == null || tpCamera == null)
            {
                rigReady = false;
                Debug.LogError($"{name}: FirstParsonCam / ThirdParsonCam 에 Camera 컴포넌트가 없습니다", this);
                return;
            }

            fpFov = fpCamera.fieldOfView;
            tpFov = tpCamera.fieldOfView;
            FirstParsonCam.GetLocalPositionAndRotation(out fpCamHomePos, out fpCamHomeRot);
            ThirdParsonCam.GetLocalPositionAndRotation(out tpCamHomePos, out tpCamHomeRot);
            fpRigHomePos = fpRig.localPosition;
            if (tpCameraArm != null)
            {
                tpArmHomePos = tpCameraArm.localPosition;
                tpArmDistance = Mathf.Max(0f, -tpArmHomePos.z);
            }
            currentCameraTransform = viewMode == ViewMode.FirstPerson ? FirstParsonCam : ThirdParsonCam;
        }

        /// <summary>
        /// 리그 좌우 = 시선 yaw, 상하 = 시선 pitch.
        /// 리그는 Player 의 자식이지만 월드 yaw 를 직접 넣는다 → 몸(리지드바디 보간 회전)을 기다리지 않고 마우스에 바로 반응
        /// </summary>
        private void UpdateRigRotation()
        {
            Quaternion yaw = Quaternion.Euler(0f, lookYaw, 0f);
            Quaternion pitch = Quaternion.Euler(lookPitch, 0f, 0f);
            fpRig.rotation = yaw;
            fpPitch.localRotation = pitch;
            tpRig.rotation = yaw;
            tpPitch.localRotation = pitch;
        }

        /// <summary>
        /// 1인칭 리그 위치 = 원래 높이 + 정면으로 조금 내밀기.
        /// (선택) followHeadBone 이면 머리 뼈 위치를 부분적으로, 부드럽게 따라간다 (애니메이션 흔들림 포함)
        /// </summary>
        private void UpdateEyePosition()
        {
            Vector3 basePos = fpRigHomePos + new Vector3(0f, 0f, eyeForwardOffset);

            if (followHeadBone == false || headBone == null)
            {
                fpRig.localPosition = basePos;
                headFollowVelocity = Vector3.zero;
                return;
            }

            Vector3 headLocal = fpRig.parent.InverseTransformPoint(headBone.position) + headEyeOffset;
            Vector3 target = Vector3.Lerp(basePos, headLocal, headFollowWeight);
            fpRig.localPosition = Vector3.SmoothDamp(fpRig.localPosition, target, ref headFollowVelocity, headFollowSmoothTime);
        }

        /// <summary>
        /// 3인칭 벽 충돌: 어깨(팔의 x·y, z=0) 에서 뒤로 SphereCast → 막히면 팔의 로컬 z 만 당긴다.
        /// 막힐 때는 즉시, 풀릴 때는 cameraReturnSpeed 로 천천히 돌아간다. 1인칭(전환 중 아님)에서는 검사하지 않는다
        /// </summary>
        private void UpdateCameraArm(float dt)
        {
            if (tpCameraArm == null) return;
            if (viewMode == ViewMode.FirstPerson && CameraChanging == false) return;

            float maxDistance = Mathf.Max(0f, -tpArmHomePos.z);
            Vector3 origin = tpPitch.TransformPoint(new Vector3(tpArmHomePos.x, tpArmHomePos.y, 0f));
            Vector3 back = -tpPitch.forward;
            float target = CameraCollisionDistance(origin, back, maxDistance);

            tpArmDistance = target < tpArmDistance
                ? target
                : Mathf.MoveTowards(tpArmDistance, target, cameraReturnSpeed * dt);
            tpCameraArm.localPosition = new Vector3(tpArmHomePos.x, tpArmHomePos.y, -tpArmDistance);
        }

        /// <summary>총 조준 확대: 지금 카메라 FOV = 기본 FOV × 배율. 시점 전환 중에는 전환 연출이 FOV 를 쓰므로 건드리지 않는다</summary>
        private void UpdateAimZoom(float dt)
        {
            aimFovCurrent = Mathf.MoveTowards(aimFovCurrent, aimFovTarget, aimZoomSpeed * dt);
            if (CameraChanging) return;
            if (viewMode == ViewMode.FirstPerson) fpCamera.fieldOfView = fpFov * aimFovCurrent;
            else tpCamera.fieldOfView = tpFov * aimFovCurrent;
        }

        /// <summary>어깨 → 카메라 사이에 벽이 있으면 그 앞까지의 거리. 자기 콜라이더는 무시</summary>
        private float CameraCollisionDistance(Vector3 origin, Vector3 direction, float maxDistance)
        {
            if (maxDistance <= 0f) return 0f;
            int count = Physics.SphereCastNonAlloc(origin, cameraCollisionRadius, direction, cameraHits,
                maxDistance, cameraCollisionMask, QueryTriggerInteraction.Ignore);

            float distance = maxDistance;
            for (int i = 0; i < count; i++)
            {
                var hit = cameraHits[i];
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.distance <= 0f) continue; // 시작부터 겹친 경우는 무시
                distance = Mathf.Min(distance, hit.distance);
            }
            return distance;
        }
        #endregion

        #region 머리 숨김
        /// <summary>인스펙터 목록 + 머리 뼈 아래 렌더러(머리카락 등)를 합쳐 1회 저장</summary>
        private void CacheHeadRenderers()
        {
            var list = new List<Renderer>();
            if (headRenderers != null)
            {
                for (int i = 0; i < headRenderers.Length; i++)
                    if (headRenderers[i] != null && list.Contains(headRenderers[i]) == false) list.Add(headRenderers[i]);
            }
            if (headBone != null)
            {
                var children = headBone.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < children.Length; i++)
                    if (list.Contains(children[i]) == false) list.Add(children[i]);
            }
            headRendererCache = list.ToArray();
        }

        /// <summary>숨김 = 그림자만 그림(ShadowsOnly), 보임 = 원래대로(On). 상태가 바뀔 때만 렌더러를 돈다</summary>
        private void SetHeadHidden(bool hidden)
        {
            if (headStateApplied && headHidden == hidden) return;
            headHidden = hidden;
            headStateApplied = true;

            var mode = hidden ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.On;
            for (int i = 0; i < headRendererCache.Length; i++)
                headRendererCache[i].shadowCastingMode = mode;
        }
        #endregion
    }
}

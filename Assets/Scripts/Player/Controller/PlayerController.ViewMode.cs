using DG.Tweening;
using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>1·3인칭 시점 전환 연출</summary>
    public partial class PlayerController
    {
        /// <summary>시점을 바꾼다 (인스펙터 viewTransitionTime 동안)</summary>
        public void SwitchViewMode(ViewMode mode) => SwitchViewMode(mode, viewTransitionTime);

        /// <summary>
        /// 시점을 바꾼다. 출발 카메라가 도착 카메라 자리로 날아간 뒤 카메라를 교체한다.
        /// 시작 / 완료 때 CameraModeChangedEvent(IsComplete false / true) 를 보낸다 (보스는 완료를 받고 등장 공격).
        ///  - 이미 그 시점이면 시작·완료를 바로 보낸다 (기다리는 쪽이 멈추지 않게)
        ///  - 같은 쪽으로 전환 중이면 무시 (완료 때 알림), 반대쪽으로 전환 중이면 출발 시점으로 되돌리고 다시 시작
        ///  - duration 0 이하면 연출 없이 바로
        /// </summary>
        public void SwitchViewMode(ViewMode mode, float duration)
        {
            if (CameraChanging)
            {
                if (transitionTarget == mode) return;
                ApplyViewModeImmediate(viewMode); // viewMode 는 완료 때 바뀌므로 아직 출발 시점
            }
            if (mode == viewMode || rigReady == false || duration <= 0f)
            {
                bool changed = mode != viewMode;
                if (changed) ApplyViewModeImmediate(mode);
                PublishCameraMode(mode, false);
                PublishCameraMode(mode, true);
                if (changed) ViewModeChanged?.Invoke(viewMode);
                return;
            }
            if (ScopeView) SetScopeView(false);

            bool toFirst = mode == ViewMode.FirstPerson;
            transitionTarget = mode;
            transitionMover = toFirst ? ThirdParsonCam : FirstParsonCam;
            transitionGoal = toFirst ? FirstParsonCam : ThirdParsonCam;
            moverHomePos = toFirst ? tpCamHomePos : fpCamHomePos;
            moverHomeRot = toFirst ? tpCamHomeRot : fpCamHomeRot;
            goalHomePos = toFirst ? fpCamHomePos : tpCamHomePos;
            goalHomeRot = toFirst ? fpCamHomeRot : tpCamHomeRot;
            moverCamera = toFirst ? tpCamera : fpCamera;
            moverFovFrom = toFirst ? TpFov : fpFov;
            moverFovTo = toFirst ? fpFov : TpFov;

            LockFacing(lookYaw);
            CameraChanging = true;
            transitionT = 0f;
            PublishCameraMode(mode, false);

            viewTween?.Kill();
            viewTween = DOTween.To(transitionGetter, transitionSetter, 1f, duration)
                .SetEase(Ease.InOutSine)
                .SetUpdate(UpdateType.Late)
                .OnComplete(transitionComplete);
        }

        /// <summary>스코프 화면 중 (3인칭이어도 1인칭 눈 카메라로 본다)</summary>
        public bool ScopeView { get; private set; }

        /// <summary>
        /// 저격 스코프 화면: 3인칭에서도 1인칭 카메라(눈)로 보게 바꾼다 → 화면 중앙 = 사격 광선(GunSlot.AimRay 는 지금 카메라 기준).
        /// 1인칭은 이미 눈 카메라라 표시만 바꾼다. 시점 전환 중에는 켜지 않는다 (전환을 시작하면 꺼진다)
        /// </summary>
        public void SetScopeView(bool on)
        {
            if (rigReady == false || on == ScopeView) return;
            if (on && CameraChanging) return;
            ScopeView = on;
            if (viewMode == ViewMode.FirstPerson) return;

            FirstParsonCam.SetLocalPositionAndRotation(fpCamHomePos, fpCamHomeRot);
            fpCamera.fieldOfView = on ? fpFov * aimFovCurrent : fpFov;
            FirstParsonCam.gameObject.SetActive(on);
            ThirdParsonCam.gameObject.SetActive(on == false);
            currentCameraTransform = on ? FirstParsonCam : ThirdParsonCam;
            viewPoint = currentCameraTransform;
        }

        /// <summary>F1 전환. 전환 중(연타 방지)이거나 보스전 시점 고정 중이면 무시</summary>
        public void ToggleViewMode()
        {
            if (CameraChanging || viewLocked) return;
            SwitchViewMode(viewMode == ViewMode.FirstPerson ? ViewMode.ThirdPerson : ViewMode.FirstPerson);
        }

        /// <summary>
        /// 전환 중 매 프레임: 출발점·도착점을 둘 다 "각 카메라의 원래 자리(부모 기준 home 포즈)" 로 다시 계산해 보간한다.
        /// (자기 현재 위치를 출발점으로 쓰면 매 프레임 출발점이 따라와서 감속 곡선처럼 보인다)
        /// 트윈 콜백과 LateUpdate 양쪽에서 불리지만 같은 값을 다시 쓰는 것이라 순서에 상관없이 결과가 같다.
        /// </summary>
        private void ApplyTransitionPose()
        {
            if (CameraChanging == false) return;
            float t = transitionT;

            Transform moverParent = transitionMover.parent;
            Transform goalParent = transitionGoal.parent;
            Vector3 startPos = moverParent.TransformPoint(moverHomePos);
            Quaternion startRot = moverParent.rotation * moverHomeRot;
            Vector3 goalPos = goalParent.TransformPoint(goalHomePos);
            Quaternion goalRot = goalParent.rotation * goalHomeRot;

            Vector3 pos = Vector3.LerpUnclamped(startPos, goalPos, t);
            transitionMover.SetPositionAndRotation(pos, Quaternion.SlerpUnclamped(startRot, goalRot, t));
            moverCamera.fieldOfView = Mathf.LerpUnclamped(moverFovFrom, moverFovTo, t);

            // 카메라가 눈 근처면 머리를 숨기고, 밖이면 보여준다
            Vector3 eyePos = FirstParsonCam.parent.TransformPoint(fpCamHomePos);
            bool insideHead = (pos - eyePos).sqrMagnitude < headHideRadius * headHideRadius;
            SetHeadHidden(insideHead);
        }

        private void OnViewTransitionComplete()
        {
            ApplyViewModeImmediate(transitionTarget);
            PublishCameraMode(viewMode, true);
            ViewModeChanged?.Invoke(viewMode);
        }

        private static void PublishCameraMode(ViewMode mode, bool complete)
        {
            var cameraMode = mode == ViewMode.FirstPerson ? CameraMode.FirstPerson : CameraMode.ThirdPerson;
            EventBus<CameraModeChangedEvent>.Publish(new CameraModeChangedEvent(cameraMode, complete));
        }

        /// <summary>
        /// 트윈 없이 바로 그 시점으로 만든다 (시작 시, 전환 완료 시).
        /// 두 카메라의 로컬 포즈·FOV 원복, 카메라 활성 교체, 조준점·시야 기준점·머리 숨김 갱신
        /// </summary>
        private void ApplyViewModeImmediate(ViewMode mode)
        {
            viewTween?.Kill();
            viewTween = null;
            CameraChanging = false;
            ScopeView = false;
            viewMode = mode;
            if (rigReady == false) return;

            bool first = mode == ViewMode.FirstPerson;

            FirstParsonCam.SetLocalPositionAndRotation(fpCamHomePos, fpCamHomeRot);
            ThirdParsonCam.SetLocalPositionAndRotation(tpCamHomePos, tpCamHomeRot);
            fpCamera.fieldOfView = fpFov;
            tpCamera.fieldOfView = TpFov;

            FirstParsonCam.gameObject.SetActive(first);
            ThirdParsonCam.gameObject.SetActive(first == false);

            currentCameraTransform = first ? FirstParsonCam : ThirdParsonCam;
            viewPoint = currentCameraTransform;
            aimPoint = first ? FirstParsonAim : ThirdParsonAim;

            SetHeadHidden(first);
        }
    }
}

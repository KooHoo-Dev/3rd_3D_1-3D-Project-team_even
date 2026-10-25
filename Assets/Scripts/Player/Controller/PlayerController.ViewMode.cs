using DG.Tweening;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>1·3인칭 시점 전환 연출</summary>
    public partial class PlayerController
    {
        /// <summary>
        /// 시점을 바꾼다. 출발 카메라가 도착 카메라 자리로 날아간 뒤 카메라를 교체한다.
        /// 전환 중이거나 이미 그 시점이면 무시 (연타 방지)
        /// </summary>
        public void SwitchViewMode(ViewMode mode)
        {
            if (rigReady == false || CameraChanging || mode == viewMode) return;
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
            moverFovFrom = toFirst ? tpFov : fpFov;
            moverFovTo = toFirst ? fpFov : tpFov;

            LockFacing(lookYaw);
            CameraChanging = true;
            transitionT = 0f;

            viewTween?.Kill();
            viewTween = DOTween.To(transitionGetter, transitionSetter, 1f, viewTransitionTime)
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

        public void ToggleViewMode() =>
            SwitchViewMode(viewMode == ViewMode.FirstPerson ? ViewMode.ThirdPerson : ViewMode.FirstPerson);

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
            ViewModeChanged?.Invoke(viewMode);
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
            tpCamera.fieldOfView = tpFov;

            FirstParsonCam.gameObject.SetActive(first);
            ThirdParsonCam.gameObject.SetActive(first == false);

            currentCameraTransform = first ? FirstParsonCam : ThirdParsonCam;
            viewPoint = currentCameraTransform;
            aimPoint = first ? FirstParsonAim : ThirdParsonAim;

            SetHeadHidden(first);
        }
    }
}

using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HybridShooter.UI.Ending.Editor
{
    /// <summary>
    /// 엔딩 착륙장과 클리어 뒤 메인 메뉴가 함께 쓰는 '새벽' (같은 하늘·해·볼륨 → 엔딩이 끝나고 메뉴로 돌아와도 같은 아침).
    /// 하늘은 팀 밤하늘 셰이더(Alien Night Sky)를 그대로 쓰고 값만 바꾼다: 붉은 눈 달 → 작고 밝은 해, 별은 거의 지우고, 지평선은 따뜻하게.
    /// 재질·프로필은 다시 만들 때마다 값을 새로 넣는다 (직접 조정하려면 여기 값을 바꾼다)
    /// </summary>
    internal static class EndingDawnLook
    {
        private const string MaterialDir = "Assets/Materials/Ending";
        private const string SkyMaterialPath = MaterialDir + "/DawnSky.mat";
        /// <summary>볼륨 프로필은 씬마다 따로 (다시 만들 때 지우고 새로 만들어 GUID 가 바뀌므로, 같이 쓰면 다른 씬의 참조가 끊긴다)</summary>
        public const string EndingVolumeProfilePath = "Assets/Settings/EndingDawnVolumeProfile.asset";
        public const string MenuVolumeProfilePath = "Assets/Settings/MenuDawnVolumeProfile.asset";
        private const string SkyShaderName = "HybridShooter/Atmosphere/Alien Night Sky";

        /// <summary>우주선 팩 모델 (조종석 +Z, 엔진 -Z). 인트로와 같은 기체 — 이번엔 구조선</summary>
        public const string ShipPrefabPath = "Assets/ExternalAssets/Creepy_Cat/_3D Scifi Kit Vol 2/Prefabs/_Spaceship_01/Spaceship_01.prefab";

        public static readonly Color FogColor = new Color(0.86f, 0.64f, 0.5f);
        public const float FogDensity = 0.009f;
        public static readonly Color SunColor = new Color(1f, 0.8f, 0.58f);
        public const float SunIntensity = 2.1f;

        public static Material SkyMaterial()
        {
            var shader = Shader.Find(SkyShaderName);
            if (shader == null) throw new FileNotFoundException("[EndingDawnLook] 하늘 셰이더가 없습니다", SkyShaderName);
            EnsureFolder(MaterialDir);
            var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterialPath);
            if (sky == null)
            {
                sky = new Material(shader);
                AssetDatabase.CreateAsset(sky, SkyMaterialPath);
            }
            sky.shader = shader;
            sky.SetColor("_ZenithColor", new Color(0.24f, 0.38f, 0.6f));
            sky.SetColor("_HorizonColor", FogColor);
            sky.SetFloat("_HorizonBlend", 0.6f);
            sky.SetFloat("_StarBrightness", 0.25f);
            sky.SetFloat("_StarTwinkle", 0.2f);
            sky.SetColor("_NebulaColorA", new Color(0.9f, 0.5f, 0.6f));
            sky.SetColor("_NebulaColorB", new Color(0.4f, 0.7f, 0.9f));
            sky.SetFloat("_NebulaIntensity", 0.12f);
            // 달 자리에 해: 작고 아주 밝게, 따뜻한 빛무리, 눈동자는 지운다
            sky.SetFloat("_MoonRadius", 4.5f);
            sky.SetColor("_MoonColor", new Color(1f, 0.9f, 0.72f));
            sky.SetFloat("_MoonBrightness", 4f);
            sky.SetColor("_MoonGlowColor", new Color(1.6f, 0.85f, 0.45f));
            sky.SetFloat("_MoonGlowIntensity", 2.4f);
            sky.SetFloat("_EyeIntensity", 0f);
            EditorUtility.SetDirty(sky);
            return sky;
        }

        /// <summary>낮은 고도의 따뜻한 해 (그림자 부드럽게). rotation = 빛이 향하는 방향 (하늘의 해는 그 반대편에 그려진다)</summary>
        public static Light CreateSun(Transform parent, Quaternion rotation)
        {
            var go = new GameObject("Dawn Sun", typeof(Light));
            go.transform.SetParent(parent, false);
            go.transform.rotation = rotation;
            var light = go.GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = SunColor;
            light.intensity = SunIntensity;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.75f;
            return light;
        }

        public static SceneEnvironment Environment(Light sun) => new SceneEnvironment
        {
            Skybox = SkyMaterial(),
            Sun = sun,
            Fog = true,
            FogColor = FogColor,
            FogDensity = FogDensity,
            AmbientSky = new Color(0.56f, 0.62f, 0.76f),
            AmbientEquator = new Color(0.78f, 0.6f, 0.48f),
            AmbientGround = new Color(0.26f, 0.21f, 0.18f),
        };

        /// <summary>
        /// 새벽 볼륨: ACES + 살짝 밝게·따뜻하게(그림자는 푸르게, 밝은 곳은 금빛) + 부드러운 Bloom, 비네트·그레인은 메뉴의 밤보다 옅게.
        /// priority 0.5 → 메뉴의 기본 볼륨(0)은 덮고, 호버 볼륨(1)은 그대로 위에 섞인다
        /// </summary>
        public static Volume CreateVolume(Transform parent, string profilePath)
        {
            AssetDatabase.DeleteAsset(profilePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);

            AddOverride<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);
            var color = AddOverride<ColorAdjustments>(profile);
            color.postExposure.Override(0.35f);
            color.contrast.Override(8f);
            color.saturation.Override(8f);
            var split = AddOverride<SplitToning>(profile);
            split.shadows.Override(new Color(0.42f, 0.52f, 0.66f));
            split.highlights.Override(new Color(1f, 0.78f, 0.55f));
            split.balance.Override(15f);
            var bloom = AddOverride<Bloom>(profile);
            bloom.threshold.Override(0.95f);
            bloom.intensity.Override(1f);
            bloom.scatter.Override(0.75f);
            var vignette = AddOverride<Vignette>(profile);
            vignette.intensity.Override(0.2f);
            vignette.smoothness.Override(0.5f);
            var grain = AddOverride<FilmGrain>(profile);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.08f);
            AssetDatabase.SaveAssets();

            var go = new GameObject("Dawn Volume", typeof(Volume));
            go.transform.SetParent(parent, false);
            var volume = go.GetComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0.5f;
            volume.sharedProfile = profile;
            return volume;
        }

        /// <summary>
        /// 구조선 모델을 가장 긴 변이 length 가 되게 맞춰 parent 원점에 가운데를 둔다 (콜라이더 제거).
        /// 돌려주는 값 = 맞춘 뒤 parent 기준 크기 (바닥 높이·경사로 위치 계산용)
        /// </summary>
        public static Vector3 AddShipModel(Transform parent, float length)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShipPrefabPath);
            if (prefab == null) throw new FileNotFoundException("[EndingDawnLook] 우주선 프리팹이 없습니다", ShipPrefabPath);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            foreach (var collider in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
            ClearStatic(model);

            var bounds = ModelBounds(model);
            float scale = length / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            model.transform.localScale *= scale;
            bounds = ModelBounds(model);
            model.transform.position -= bounds.center - parent.position;
            return bounds.size;
        }

        /// <summary>
        /// 팩 우주선은 모든 오브젝트가 Static(Everything)이라, 플레이를 시작하면 정적 배칭으로 선체 메시가 그 자리에 굳고
        /// 부모를 움직여도 따라가지 않는다 (붙여 둔 다리·불꽃만 날아간다). 움직일 기체이므로 씬 인스턴스에서만 Static 을 끈다 (팩 원본은 그대로)
        /// </summary>
        public static void ClearStatic(GameObject root)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(child.gameObject, 0);
        }

        public static Bounds ModelBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        private static T AddOverride<T>(VolumeProfile profile) where T : VolumeComponent
        {
            var component = profile.Add<T>(true);
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        public static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent) == false) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}

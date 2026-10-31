using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 활성 씬의 맵(SciFiMap 또는 TutorialMap)을 정해진 시점에서 찍어 PNG로 저장한다. 밀도·배치를 사진으로 검토하는 용도.
/// 메뉴: Tools/Map/Capture Map Review Shots
/// - 저장 위치: 프로젝트 루트의 Temp/MapReview/ (Unity Temp 폴더라 git에 올라가지 않음, 찍을 때마다 해당 씬 사진을 갈아치움)
/// - 찍는 것: ① 위에서 내려다본 전체(안개 끔) ② 남쪽 위에서 비스듬히 본 전체(안개 끔)
///           ③ Props 아래 지점 그룹마다 비스듬히 본 모습 ④ Player·마커 위치의 눈높이 시점
/// </summary>
public static class MapReviewCapture
{
    private const string OutputDir = "Temp/MapReview";
    private const int Width = 1600, Height = 900, OverviewSize = 1400;
    private const float EyeHeight = 1.7f;
    private const float MaxSiteExtent = 120f; // 맵 전체에 걸친 그룹(외곽 경계 등)은 지점 사진에서 뺀다

    [MenuItem("Tools/Map/Capture Map Review Shots")]
    private static void Capture()
    {
        Scene scene = SceneManager.GetActiveScene();
        GameObject map = null;
        foreach (GameObject go in scene.GetRootGameObjects())
            if (go.name == "SciFiMap" || go.name == "TutorialMap") { map = go; break; }
        Terrain terrain = map != null ? map.GetComponentInChildren<Terrain>() : null;
        if (terrain == null)
        {
            Debug.LogError("[MapReviewCapture] 활성 씬에서 SciFiMap/TutorialMap과 그 지형을 찾지 못했습니다.");
            return;
        }

        Directory.CreateDirectory(OutputDir);
        foreach (string old in Directory.GetFiles(OutputDir, scene.name + "_*.png")) File.Delete(old);

        Camera cam = new GameObject("MapReviewCamera").AddComponent<Camera>();
        cam.gameObject.hideFlags = HideFlags.HideAndDontSave;
        cam.fieldOfView = 55f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 3000f;
        int index = 0;
        try
        {
            Vector3 tp = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            Vector3 center = tp + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);
            center.y = terrain.SampleHeight(center) + tp.y;

            // ① 위에서 내려다본 전체 (정사영)
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(size.x, size.z) * 0.5f;
            cam.transform.SetPositionAndRotation(center + Vector3.up * (size.y + 200f), Quaternion.Euler(90f, 0f, 0f));
            Save(cam, scene, ref index, "overview_top", OverviewSize, OverviewSize);

            // ② 남쪽 위에서 비스듬히 본 전체
            cam.orthographic = false;
            Vector3 from = center + new Vector3(0f, Mathf.Max(size.x, size.z) * 0.45f, -size.z * 0.75f);
            cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(center - from));
            Save(cam, scene, ref index, "overview_oblique", Width, Height);
            RenderSettings.fog = fog;

            // ③ 지점 그룹 (안개 켠 실제 분위기)
            Transform props = map.transform.Find("Props");
            if (props != null)
            {
                foreach (Transform site in props)
                {
                    if (!TryGetGroupBounds(site, out Bounds b)) continue;
                    if (Mathf.Max(b.extents.x, b.extents.z) > MaxSiteExtent) continue;
                    float dist = Mathf.Max(b.extents.x, b.extents.z) * 1.6f + 15f;
                    Vector3 target = b.center;
                    Vector3 eye = target + new Vector3(-0.5f, 0.75f, -0.85f).normalized * dist;
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                    Save(cam, scene, ref index, "site_" + site.name, Width, Height);
                }
            }

            // ④ 눈높이: Player(맵 밖 씬 오브젝트)와 Markers 아래 마커
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go == map) continue;
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != "Player") continue;
                    EyeShot(cam, scene, ref index, "eye_Player", t);
                    break;
                }
            }
            Transform markers = map.transform.Find("Markers");
            if (markers != null)
                foreach (Transform m in markers) EyeShot(cam, scene, ref index, "eye_" + m.name, m);
        }
        finally
        {
            Object.DestroyImmediate(cam.gameObject);
        }

        string full = Path.GetFullPath(OutputDir);
        Debug.Log($"[MapReviewCapture] {index}장 저장: {full}");
        EditorUtility.RevealInFinder(full);
    }

    private static void EyeShot(Camera cam, Scene scene, ref int index, string name, Transform at)
    {
        Vector3 forward = at.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
        cam.transform.SetPositionAndRotation(at.position + Vector3.up * EyeHeight, Quaternion.LookRotation(forward.normalized));
        Save(cam, scene, ref index, name, Width, Height);
    }

    private static bool TryGetGroupBounds(Transform group, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (Renderer r in group.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (found) bounds.Encapsulate(r.bounds);
            else { bounds = r.bounds; found = true; }
        }
        return found;
    }

    private static void Save(Camera cam, Scene scene, ref int index, string name, int width, int height)
    {
        RenderTexture rt = new RenderTexture(width, height, 24);
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        RenderTexture prevActive = RenderTexture.active;
        try
        {
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            File.WriteAllBytes(Path.Combine(OutputDir, $"{scene.name}_{index:00}_{name}.png"), tex.EncodeToPNG());
            index++;
        }
        finally
        {
            cam.targetTexture = null;
            RenderTexture.active = prevActive;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }
    }
}

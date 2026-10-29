namespace HybridShooter.Core.Data.Dtos
{
    /// <summary>
    /// SoundTable.tsv 의 한 행. 필드 이름 = TSV 헤더 이름 (컬럼 추가/변경 시 둘 다 수정).
    /// DTO 는 "파일 모양 그대로"만 담는다. 검증·변환·참조 연결은 SoundDef 쪽 책임.
    /// </summary>
    public sealed class SoundDto
    {
        public string Key = "";
        public string Category = "";  // SoundCategory enum
        public string Path = "";  // Resources 기준. ';' 로 여러 개 → 랜덤 재생
        public float Volume;
        public bool Loop;
        public bool Spatial;  // true = 위치가 있는 3D 소리
        public int MaxInstances;  // 같은 키 동시 재생 수 (0 = 제한 없음)
        public float Cooldown;  // 같은 키 최소 재생 간격(초)
        public float PitchJitter;  // 피치 랜덤 폭 (0.05 = ±5%)
        public int Priority;  // 0~100, 클수록 중요 (자리가 없을 때 낮은 것부터 끊김)
        public float MaxDistance;  // 3D 최대 들리는 거리(m), 0 = 제한 없음
    }
}

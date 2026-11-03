// 연료 셀(ExplosiveProp) 효과음 합성기 (외부 샘플 없이 렌더, 모노 16bit 44.1kHz WAV)
//   dotnet run --project tools/audio/FuelCellSfx -- Assets/Resources/Audio/Sfx/FuelCell
//   dotnet run --project tools/audio/FuelCellSfx -- Assets/Resources/Audio/Sfx/FuelCell LeakLoop   (이름을 주면 그것만)
// 재료(필터·쿵·깨짐·파편 등)는 TyranopodeSfx 와 같다. 소리 설계는 아래 각 함수 주석 참고
//   Hit_01~03  총알이 빈 금속 연료통을 때림        → sfx_fuelcell_hit
//   Puncture   통이 뚫려 고압 가스가 터져 나옴     → sfx_fuelcell_critical (위험 단계 진입)
//   LeakLoop   새는 가스 쉭 + 불티 (반복)          → sfx_fuelcell_leak_loop
//   Fuse       터지기 직전 압력이 비명처럼 치솟음   → sfx_fuelcell_fuse
//   Burn       폭발 뒤 연료 불길이 휘몰아치다 잦아듦 → sfx_fuelcell_burn (sfx_explosion_large 와 겹쳐 냄)
//   Crush      보스에 깔려 찌그러짐 + 가스 분출     → sfx_fuelcell_crush
using System.Text;

const int SR = 44100;
string outDir = args.Length > 0 ? args[0] : ".";
var only = args.Skip(1).ToHashSet();
Directory.CreateDirectory(outDir);

if (Wants("Hit"))
{
    Write("FuelCell_Hit_01", Hit(101, 1.00f), 1.4f);
    Write("FuelCell_Hit_02", Hit(202, 0.92f), 1.4f);
    Write("FuelCell_Hit_03", Hit(303, 1.08f), 1.4f);
}
if (Wants("Puncture")) Write("FuelCell_Puncture", Puncture(), 1.5f);
if (Wants("LeakLoop")) Write("FuelCell_LeakLoop", LeakLoop(), 1.2f, loop: true);
if (Wants("Fuse")) Write("FuelCell_Fuse", Fuse(), 1.6f);
if (Wants("Burn")) Write("FuelCell_Burn", Burn(), 1.7f);
if (Wants("Crush")) Write("FuelCell_Crush", Crush(), 1.6f);

bool Wants(string name) => only.Count == 0 || only.Contains(name);

// ───────────── 소리 설계 ─────────────

// 금속 연료통 피탄: 총알이 철판을 때리는 날카로운 '팅' + 속이 빈 통이 '웅' 하고 울림 (약 0.6초)
//   통 울림 = 원통 껍질의 비조화 배음(1 : 2.32 : 4.25 : 6.63)을 각기 다른 감쇠로. 높은 배음일수록 빨리 사라진다
//   pitch 로 세 벌을 조금씩 다르게 → 연사해도 같은 소리 반복처럼 들리지 않게
float[] Hit(int seed, float pitch)
{
    var b = Buf(0.7f);
    var r = new Random(seed);
    Crack(b, r, 0f, 0.012f, 4200f * pitch, 1.4f, 0.8f);                 // 탄이 박히는 날카로운 딱
    Ring(b, 0f, 210f * pitch, new[] { 1f, 2.32f, 4.25f, 6.63f, 9.4f },
         new[] { 0.55f, 0.45f, 0.35f, 0.22f, 0.12f }, new[] { 0.32f, 0.22f, 0.12f, 0.07f, 0.04f }, 0.9f);
    Ring(b, 0f, 1730f * pitch, new[] { 1f, 1.51f, 2.17f }, new[] { 0.5f, 0.3f, 0.2f },
         new[] { 0.09f, 0.06f, 0.035f }, 0.5f);                          // 철판 '팅' (밝은 금속성)
    Thud(b, r, 0f, 380f, 0.03f, 0.35f);                                  // 통에 실린 무게
    return b;
}

// 통이 뚫림: 금속이 찢어지는 소리 + '퍽' 하고 고압 가스가 뿜어져 나와 새는 소리로 잦아든다 (약 1.4초)
float[] Puncture()
{
    var b = Buf(1.5f);
    var r = new Random(404);
    Crack(b, r, 0f, 0.02f, 2600f, 1.2f, 0.9f);
    Tear(b, r, 0.005f, 0.18f, 0.5f);                                     // 찢어지는 금속 삐걱
    Ring(b, 0f, 190f, new[] { 1f, 2.32f, 4.25f }, new[] { 0.6f, 0.4f, 0.25f }, new[] { 0.35f, 0.2f, 0.1f }, 0.6f);
    // 가스 분출: 처음엔 굵고 세게(퍽) → 가늘고 높은 쉭으로
    var bp = new Svf(1800f, 0.9f);
    var hp = new Svf(3500f, 0.7f);
    int s0 = Idx(0.01f);
    for (int i = s0; i < b.Length; i++)
    {
        float t = (i - s0) / (float)SR;
        bp.SetFreq(1200f + 3800f * MathF.Min(1f, t / 0.5f));
        float burst = Attack(t, 0.006f) * (0.35f + 0.65f * MathF.Exp(-t / 0.12f)) * MathF.Min(1f, (1.45f - t) / 0.5f);
        float flutter = 0.85f + 0.15f * MathF.Sin(2 * MathF.PI * 37f * t);
        float n = Noise(r);
        b[i] += burst * flutter * (bp.Band(n) * 1.6f + hp.High(n) * 0.5f);
    }
    return b;
}

// 가스 누출 반복: 일정한 고압 쉭 + 느린 흔들림 + 이따금 타닥 불티 (2초, 끝이 처음으로 이어진다)
float[] LeakLoop()
{
    const float length = 2f;
    float overlap = 0.3f;
    int n = Idx(length + overlap), o = Idx(overlap);
    var x = new float[n];
    var r = new Random(505);
    var bp = new Svf(4200f, 0.8f);
    var lowHiss = new Svf(1300f, 1.2f);
    for (int i = 0; i < n; i++)
    {
        float t = i / (float)SR;
        // 주기가 length 로 나누어떨어지는 흔들림 → 교차 구간에서도 결이 맞는다
        float sway = 0.8f + 0.12f * MathF.Sin(2 * MathF.PI * 2f * t / length) + 0.08f * MathF.Sin(2 * MathF.PI * 11f * t / length);
        float noise = Noise(r);
        x[i] = sway * (bp.Band(noise) * 1.3f + lowHiss.Band(noise) * 0.6f);
    }
    // 불티 (위치·크기 무작위, 길이 안에서만)
    for (int k = 0; k < 14; k++)
    {
        float t0 = (float)r.NextDouble() * length;
        float level = 0.25f + 0.45f * (float)r.NextDouble();
        var pop = new Svf(1500f + 3000f * (float)r.NextDouble(), 2f);
        int p0 = Idx(t0);
        for (int i = 0; i < Idx(0.03f) && p0 + i < n; i++)
            x[p0 + i] += level * MathF.Exp(-i / (float)SR / 0.006f) * pop.Band(Noise(r)) * 2f;
    }
    // 뒤쪽 overlap 을 앞쪽에 등전력 교차로 섞는다
    var b = Buf(length);
    for (int i = 0; i < b.Length; i++)
    {
        float v = x[i];
        if (i < o)
        {
            float k = i / (float)o;
            v = x[i] * MathF.Sin(k * MathF.PI / 2) + x[b.Length + i] * MathF.Cos(k * MathF.PI / 2);
        }
        b[i] = v;
    }
    return b;
}

// 기폭 직전: 압력이 치솟는 휘파람 비명(900 → 3400Hz) + 점점 거세지는 쉭 + 금속 신음, 끝에서 뚝 끊긴다 (0.55초)
//   셀은 0.15~0.3초(보스 접촉 0.5초) 뒤 터지므로 폭발음이 끝을 덮는다
float[] Fuse()
{
    const float length = 0.55f;
    var b = Buf(length);
    var r = new Random(606);
    var bp = new Svf(2000f, 1f);
    double phase = 0, phase2 = 0;
    for (int i = 0; i < b.Length; i++)
    {
        float t = i / (float)SR;
        float k = t / length;
        float f = 900f * MathF.Pow(3400f / 900f, MathF.Pow(k, 0.7f)) * (1f + 0.012f * MathF.Sin(2 * MathF.PI * 31f * t));
        phase += 2 * Math.PI * f / SR;
        phase2 += 2 * Math.PI * f * 1.007f / SR;                          // 살짝 어긋난 두 번째 음 → 떨리는 비명
        float whistle = (float)(Math.Sin(phase) + 0.6 * Math.Sin(phase2) + 0.15 * Math.Sin(3 * phase));
        bp.SetFreq(1500f + 4000f * k);
        float hiss = bp.Band(Noise(r)) * 1.5f;
        float env = Attack(t, 0.02f) * (0.3f + 0.7f * k * k);
        b[i] += env * (whistle * 0.35f + hiss);
    }
    Tear(b, r, 0f, length, 0.25f);
    return b;
}

// 연료 불길: 불덩이가 '화르륵' 휘몰아쳐 솟고(0~0.6초), 낮은 불길이 일렁이며 타다가 타닥 소리와 함께 잦아든다 (약 4.5초)
//   폭발 본음(sfx_explosion_large)과 겹쳐 내므로 쿵은 약하게, 불길 질감에 집중한다
float[] Burn()
{
    var b = Buf(4.6f);
    var r = new Random(707);
    Whoosh(b, r, 0f, 0.9f, 250f, 1600f, 0.55f);
    Boom(b, 0f, 70f, 35f, 0.3f, 0.5f, 0.35f);
    // 불길 몸통: 갈색 잡음을 300~900Hz 대역으로, 빠르게 일렁인다
    var bp = new Svf(500f, 0.7f);
    var lp = new Svf(220f, 0.7f);
    float brown = 0f;
    for (int i = 0; i < b.Length; i++)
    {
        float t = i / (float)SR;
        brown = brown * 0.985f + Noise(r) * 0.15f;
        float lick = 0.65f + 0.35f * MathF.Sin(2 * MathF.PI * 5.3f * t + 2f * MathF.Sin(2 * MathF.PI * 1.7f * t));
        bp.SetFreq(380f + 300f * lick);
        float env = Attack(t, 0.15f) * MathF.Exp(-t / 1.6f);
        b[i] += env * lick * (bp.Band(brown) * 3.2f + lp.Low(brown) * 2.2f);
    }
    Crackle(b, r, 0.1f, 4.3f, 45f, 0.45f);
    Debris(b, r, 0.05f, 2.2f, 0.3f, 60);                                // 떨어지는 금속 조각
    return b;
}

// 찌그러짐: 거대한 몸에 깔려 철판이 우그러지는 '콰직' + 묵직한 눌림 + 짧은 가스 분출 (약 1초)
float[] Crush()
{
    var b = Buf(1.1f);
    var r = new Random(808);
    Thud(b, r, 0f, 300f, 0.12f, 0.8f);
    Crack(b, r, 0f, 0.03f, 1400f, 0.8f, 0.7f);
    Tear(b, r, 0.01f, 0.35f, 0.6f);
    Ring(b, 0.02f, 160f, new[] { 1f, 2.32f, 4.25f }, new[] { 0.5f, 0.3f, 0.2f }, new[] { 0.18f, 0.1f, 0.06f }, 0.5f);
    var hp = new Svf(2500f, 0.7f);
    int s0 = Idx(0.08f);
    for (int i = s0; i < b.Length; i++)
    {
        float t = (i - s0) / (float)SR;
        b[i] += Attack(t, 0.01f) * MathF.Exp(-t / 0.25f) * hp.High(Noise(r)) * 0.6f;
    }
    Debris(b, r, 0.02f, 0.6f, 0.2f, 30);
    return b;
}

// ───────────── 재료 ─────────────

// 금속 울림: 기본음 f0 의 배음 비율·세기·감쇠 시간 목록 (비조화 배음 = 종·통 같은 소리)
void Ring(float[] b, float start, float f0, float[] ratios, float[] levels, float[] taus, float amp)
{
    int s0 = Idx(start);
    for (int m = 0; m < ratios.Length; m++)
    {
        double w = 2 * Math.PI * f0 * ratios[m] / SR;
        for (int i = s0; i < b.Length; i++)
        {
            float t = (i - s0) / (float)SR;
            float env = Attack(t, 0.001f) * MathF.Exp(-t / taus[m]);
            if (env < 1e-4f && t > 0.02f) break;
            b[i] += amp * levels[m] * env * (float)Math.Sin(w * (i - s0));
        }
    }
}

// 금속 찢어짐·삐걱: 공명 대역이 흔들리며 미끄러지는 거친 잡음
void Tear(float[] b, Random r, float start, float length, float amp)
{
    var bp = new Svf(900f, 6f);
    int s0 = Idx(start), n = Idx(length);
    for (int i = 0; i < n && s0 + i < b.Length; i++)
    {
        float t = i / (float)SR;
        float k = t / length;
        bp.SetFreq(700f + 900f * k + 250f * MathF.Sin(2 * MathF.PI * 23f * t));
        float grit = (r.NextDouble() < 0.02 ? 3f : 1f) * Noise(r);           // 드문 걸림 → 거친 결
        float env = Attack(t, 0.004f) * MathF.Sin(MathF.PI * MathF.Min(1f, k));
        b[s0 + i] += amp * env * bp.Band(grit) * 2.5f;
    }
}

// 낮은 사인 쿵 (주파수가 f0 → f1 로 떨어짐)
void Boom(float[] b, float start, float f0, float f1, float sweep, float tau, float amp)
{
    double phase = 0;
    int s0 = Idx(start);
    for (int i = s0; i < b.Length; i++)
    {
        float t = (i - s0) / (float)SR;
        float f = f1 + (f0 - f1) * MathF.Exp(-t / (sweep * 0.35f));
        phase += 2 * Math.PI * f / SR;
        float env = Attack(t, 0.004f) * MathF.Exp(-t / tau);
        if (env < 1e-4f && t > 0.1f) break;
        b[i] += amp * env * (float)(Math.Sin(phase) + 0.25 * Math.Sin(2 * phase));
    }
}

// 묵직한 중저음 덩어리 (저역 통과 잡음)
void Thud(float[] b, Random r, float start, float cutoff, float tau, float amp)
{
    var lp = new Svf(cutoff, 0.8f);
    int s0 = Idx(start);
    for (int i = s0; i < b.Length; i++)
    {
        float t = (i - s0) / (float)SR;
        float env = Attack(t, 0.002f) * MathF.Exp(-t / tau);
        if (env < 1e-4f && t > 0.05f) break;
        b[i] += amp * env * lp.Low(Noise(r)) * 2.5f;
    }
}

// 날카로운 깨짐 (대역 통과 잡음 짧게)
void Crack(float[] b, Random r, float start, float length, float center, float q, float amp)
{
    var bp = new Svf(center, q);
    int s0 = Idx(start);
    for (int i = s0; i < Math.Min(b.Length, s0 + Idx(length * 4)); i++)
    {
        float t = (i - s0) / (float)SR;
        float env = Attack(t, 0.0005f) * MathF.Exp(-t / (length * 0.5f));
        b[i] += amp * env * bp.Band(Noise(r)) * 2f;
    }
}

// 흩어지는 파편 (짧은 알갱이, 시간이 갈수록 드물고 작게)
void Debris(float[] b, Random r, float start, float end, float amp, int count)
{
    for (int n = 0; n < count; n++)
    {
        float u = MathF.Pow((float)r.NextDouble(), 1.8f); // 앞쪽에 몰린다
        float t0 = start + (end - start) * u;
        float len = 0.004f + 0.025f * (float)r.NextDouble();
        float level = amp * (1f - u) * (0.3f + 0.7f * (float)r.NextDouble());
        var bp = new Svf(700f + 3500f * (float)r.NextDouble(), 1.5f + 3f * (float)r.NextDouble());
        int s0 = Idx(t0);
        for (int i = s0; i < Math.Min(b.Length, s0 + Idx(len * 3)); i++)
        {
            float t = (i - s0) / (float)SR;
            b[i] += level * MathF.Exp(-t / len) * bp.Band(Noise(r)) * 2f;
        }
    }
}

// 휙 (대역 통과 중심이 올라갔다 내려감, 점점 커졌다 사라짐)
void Whoosh(float[] b, Random r, float start, float end, float fLow, float fHigh, float amp)
{
    var bp = new Svf(fLow, 1.2f);
    int s0 = Idx(start), n = Idx(end - start);
    for (int i = 0; i < n && s0 + i < b.Length; i++)
    {
        float k = i / (float)n;
        bp.SetFreq(fLow + (fHigh - fLow) * Bell(k, 0.35f, 0.25f));
        float env = MathF.Pow(MathF.Sin(MathF.PI * MathF.Pow(k, 0.6f)), 2f);
        b[s0 + i] += amp * env * bp.Band(Noise(r)) * 2.5f;
    }
}

// 타닥 (드문 튐, 점점 드물고 작게)
void Crackle(float[] b, Random r, float start, float length, float rate, float amp)
{
    var hp = new Svf(1800f, 0.8f);
    int s0 = Idx(start), n = Idx(length);
    float spark = 0f;
    for (int i = 0; i < n && s0 + i < b.Length; i++)
    {
        float t = i / (float)SR;
        if (r.NextDouble() < rate / SR * (1f - t / length)) spark = 0.5f + 0.5f * (float)r.NextDouble();
        spark *= 0.993f;
        b[s0 + i] += amp * spark * hp.High(Noise(r)) * (1f - t / length);
    }
}

// ───────────── 도구 ─────────────

float[] Buf(float seconds) => new float[Idx(seconds)];
int Idx(float seconds) => (int)(seconds * SR);
float Noise(Random r) => (float)(r.NextDouble() * 2 - 1);
float Attack(float t, float time) => time <= 0f ? 1f : MathF.Min(1f, t / time);
float Bell(float x, float center, float width) => MathF.Exp(-((x - center) * (x - center)) / (2 * width * width));

// 마무리: 부드러운 포화 → 앞뒤 페이드 → 피크 -1dBFS 정규화 → 16bit WAV
// loop = true 면 페이드를 넣지 않고 DC 는 평균을 빼서 없앤다 (끝과 처음이 그대로 이어져야 하므로)
void Write(string name, float[] b, float drive, bool loop = false)
{
    float norm = MathF.Tanh(drive);
    for (int i = 0; i < b.Length; i++) b[i] = MathF.Tanh(b[i] * drive) / norm;
    if (loop)
    {
        float mean = b.Average();
        for (int i = 0; i < b.Length; i++) b[i] -= mean;
    }
    else
    {
        int fadeIn = Idx(0.002f), fadeOut = Idx(0.03f);
        for (int i = 0; i < fadeIn && i < b.Length; i++) b[i] *= i / (float)fadeIn;
        for (int i = 0; i < fadeOut && i < b.Length; i++) b[b.Length - 1 - i] *= i / (float)fadeOut;
        // DC 제거 (저역 쿵의 비대칭)
        float prevX = 0f, prevY = 0f;
        for (int i = 0; i < b.Length; i++) { float y = b[i] - prevX + 0.9995f * prevY; prevX = b[i]; prevY = y; b[i] = y; }
    }
    float peak = b.Max(MathF.Abs);
    float gain = peak > 0f ? 0.89f / peak : 1f;

    string path = Path.Combine(outDir, name + ".wav");
    using var w = new BinaryWriter(File.Create(path));
    int bytes = b.Length * 2;
    w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(Encoding.ASCII.GetBytes("WAVE"));
    w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
    w.Write(SR); w.Write(SR * 2); w.Write((short)2); w.Write((short)16);
    w.Write(Encoding.ASCII.GetBytes("data")); w.Write(bytes);
    foreach (float s in b) w.Write((short)Math.Clamp(s * gain * 32767f, -32768f, 32767f));
    Console.WriteLine($"{name}.wav  {b.Length / (float)SR:0.00}s  peak {20 * MathF.Log10(peak * gain):0.0}dB");
}

/// <summary>상태 변수 필터 (Simper TPT): 저역·대역·고역</summary>
sealed class Svf
{
    private float g, k, a1, a2, a3, ic1, ic2;
    private float v1, v2, v0;

    public Svf(float freq, float q) { k = 1f / q; SetFreq(freq); }

    public void SetFreq(float freq)
    {
        g = MathF.Tan(MathF.PI * Math.Clamp(freq, 10f, 20000f) / 44100f);
        a1 = 1f / (1f + g * (g + k)); a2 = g * a1; a3 = g * a2;
    }

    private void Tick(float x)
    {
        v0 = x;
        float v3 = x - ic2;
        v1 = a1 * ic1 + a2 * v3;
        v2 = ic2 + a2 * ic1 + a3 * v3;
        ic1 = 2 * v1 - ic1; ic2 = 2 * v2 - ic2;
    }

    public float Low(float x) { Tick(x); return v2; }
    public float Band(float x) { Tick(x); return v1; }
    public float High(float x) { Tick(x); return v0 - k * v1 - v2; }
}

// 티라노포드 효과음 합성기 (외부 샘플 없이 렌더, 모노 16bit 44.1kHz WAV)
//   dotnet run --project tools/audio/TyranopodeSfx -- Assets/Resources/Audio/Monster/Tyranopode
//   dotnet run --project tools/audio/TyranopodeSfx -- Assets/Resources/Audio/Monster/Tyranopode ChargeTentacleLoop   (이름을 주면 그것만)
// JumpSmash·SmashImpact·PartBreak·OrbitalBeam·ChargeTentacleLoop 를 만든다. 소리 설계는 아래 각 함수 주석 참고
using System.Text;

const int SR = 44100;
string outDir = args.Length > 0 ? args[0] : ".";
var only = args.Skip(1).ToHashSet();
Directory.CreateDirectory(outDir);

if (Wants("JumpSmash")) Write("Tyranopode_JumpSmash", JumpSmash(), 1.6f);
if (Wants("SmashImpact")) Write("Tyranopode_SmashImpact", SmashImpact(), 1.8f);
if (Wants("PartBreak")) Write("Tyranopode_PartBreak", PartBreak(), 1.6f);
if (Wants("OrbitalBeam")) Write("Tyranopode_OrbitalBeam", OrbitalBeam(), 1.8f);
if (Wants("ChargeTentacleLoop")) Write("Tyranopode_ChargeTentacleLoop", ChargeTentacleLoop(), 1.5f, loop: true);

bool Wants(string name) => only.Count == 0 || only.Contains(name);

// ───────────── 소리 설계 ─────────────

// 도약: 보스 자기 목소리(Tyranopode_Roar) 시작부 0.05~1.4초 = 낮게 시작해 커지는 부분을 0.8배 음높이로 낮춰 힘주는 소리로 (약 1.8초).
//   몸이 공기를 가르는 휙 소리를 아주 작게 깐다
float[] JumpSmash()
{
    var roar = Voice(0.05f, 1.4f, 0.8f, attack: 0.03f, release: 0.35f);
    var b = Buf(roar.Length / (float)SR + 0.1f);
    var r = new Random(11);
    for (int i = 0; i < roar.Length; i++) b[i] += roar[i];
    Whoosh(b, r, 0.2f, b.Length / (float)SR, 300f, 1400f, 0.12f);
    return b;
}

// 내려찍기 착지: 깨지는 소리 + 묵직한 쿵 + 땅 울림 + 파편
float[] SmashImpact()
{
    var b = Buf(2.2f);
    var r = new Random(22);
    Crack(b, r, 0f, 0.06f, 1800f, 0.9f, 0.7f);
    Thud(b, r, 0f, 420f, 0.12f, 0.8f);
    Boom(b, 0f, 85f, 32f, 0.4f, 0.45f, 1.0f);
    Rumble(b, r, 0f, 150f, 0.8f, 0.55f);
    Debris(b, r, 0.05f, 1.7f, 0.28f, 90);
    return b;
}

// 부위 파괴: 보스 자기 목소리(Tyranopode_Roar)의 가장 크고 날카로운 1.25~1.85초를 1.35배 음높이로 올려 아파하는 비명으로
float[] PartBreak()
{
    var cry = Voice(1.25f, 1.85f, 1.35f, attack: 0.01f, release: 0.18f);
    var b = Buf(cry.Length / (float)SR + 0.05f);
    for (int i = 0; i < cry.Length; i++) b[i] += cry[i];
    return b;
}

// Tyranopode_Roar.wav 의 from~to 초 구간을 잘라 pitch 배 음높이로 (재생 속도를 바꿔서) 가져온다. 앞뒤는 부드럽게, 피크 1로 맞춘다
float[] Voice(float from, float to, float pitch, float attack, float release)
{
    var (src, rate) = ReadWav(Path.Combine(outDir, "Tyranopode_Roar.wav"));
    double step = rate / (double)SR * pitch;
    double pos = from * rate, end = Math.Min(to * rate, src.Length - 2);
    var output = new List<float>();
    while (pos < end)
    {
        int i = (int)pos;
        float k = (float)(pos - i);
        output.Add(src[i] + (src[i + 1] - src[i]) * k);
        pos += step;
    }
    var v = output.ToArray();
    float len = v.Length / (float)SR;
    for (int i = 0; i < v.Length; i++)
    {
        float t = i / (float)SR;
        v[i] *= Attack(t, attack) * MathF.Min(1f, (len - t) / release);
    }
    float peak = v.Max(MathF.Abs);
    if (peak > 0f) for (int i = 0; i < v.Length; i++) v[i] /= peak;
    return v;
}

// 16bit PCM WAV 을 모노로 섞어 읽는다
(float[] samples, int rate) ReadWav(string path)
{
    var bytes = File.ReadAllBytes(path);
    int pos = 12, channels = 1, rate = SR;
    while (pos + 8 <= bytes.Length)
    {
        string id = Encoding.ASCII.GetString(bytes, pos, 4);
        int size = BitConverter.ToInt32(bytes, pos + 4);
        if (id == "fmt ")
        {
            channels = BitConverter.ToInt16(bytes, pos + 10);
            rate = BitConverter.ToInt32(bytes, pos + 12);
            if (BitConverter.ToInt16(bytes, pos + 22) != 16) throw new NotSupportedException($"{path}: 16bit 만 읽을 수 있습니다");
        }
        else if (id == "data")
        {
            int frames = size / (2 * channels);
            var mono = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++) sum += BitConverter.ToInt16(bytes, pos + 8 + (f * channels + c) * 2) / 32768f;
                mono[f] = sum / channels;
            }
            return (mono, rate);
        }
        pos += 8 + size + (size & 1);
    }
    throw new InvalidDataException($"{path}: data 청크가 없습니다");
}

// 돌진 루프: 여러 촉수가 땅을 빠르게 짚으며 달려간다 (끊김 없이 반복되도록 끝이 처음으로 이어진다)
//   촉수 한 번 = 묵직한 살덩이 탁 + 질척한 철벅 + 미끄러짐, 2초에 약 30번 (네 박 단위로 무리 지어 우르르)
//   밑에는 몸통이 땅을 끄는 낮은 마찰음이 깔린다
float[] ChargeTentacleLoop()
{
    const float length = 2f;
    var b = Buf(length);
    var r = new Random(66);
    int steps = 32;
    for (int n = 0; n < steps; n++)
    {
        // 4개씩 무리 (빠른 연타 → 짧은 틈) + 흔들림
        float group = n / 4 * (length / (steps / 4f));
        float t0 = group + (n % 4) * 0.045f + (float)(r.NextDouble() - 0.5) * 0.02f;
        float level = 0.55f + 0.45f * (float)r.NextDouble();
        TentacleStep(b, r, t0, level);
    }
    DragLoop(b, r, length, 0.35f);
    return b;
}

// 촉수가 땅을 한 번 짚음 (버퍼 끝을 넘는 꼬리는 처음으로 감는다)
void TentacleStep(float[] b, Random r, float start, float level)
{
    int s0 = Idx(start), n = Idx(0.22f);
    float thumpF = 85f + 45f * (float)r.NextDouble();
    float wetF = 700f + 700f * (float)r.NextDouble();
    var wet = new Svf(wetF, 5f);
    var slap = new Svf(2200f + 1500f * (float)r.NextDouble(), 1.2f);
    var slide = new Svf(400f, 1.5f);
    double phase = 0;
    for (int i = 0; i < n; i++)
    {
        float t = i / (float)SR;
        phase += 2 * Math.PI * thumpF * (1f - 0.4f * MathF.Min(1f, t / 0.05f)) / SR;
        float thump = (float)Math.Sin(phase) * Attack(t, 0.002f) * MathF.Exp(-t / 0.035f);
        float click = slap.Band(Noise(r)) * MathF.Exp(-t / 0.006f) * 1.5f;
        wet.SetFreq(wetF * (1f - 0.5f * MathF.Min(1f, t / 0.12f)));
        float squelch = wet.Band(Noise(r)) * Attack(t, 0.008f) * MathF.Exp(-t / 0.05f) * 3f;
        slide.SetFreq(300f + 500f * (1f - MathF.Min(1f, t / 0.2f)));
        float drag = slide.Band(Noise(r)) * MathF.Sin(MathF.PI * MathF.Min(1f, t / 0.2f)) * 0.8f;
        int j = (s0 + i) % b.Length;
        if (j < 0) j += b.Length;
        b[j] += level * (thump * 0.9f + click * 0.35f + squelch * 0.6f + drag * 0.3f);
    }
}

// 몸통이 땅을 끄는 낮은 마찰 (앞뒤를 겹쳐 끊김 없이 이어지게)
void DragLoop(float[] b, Random r, float length, float amp)
{
    float overlap = 0.4f;
    int n = Idx(length + overlap), o = Idx(overlap);
    var x = new float[n];
    var lp = new Svf(260f, 0.8f);
    float brown = 0f;
    for (int i = 0; i < n; i++)
    {
        float t = i / (float)SR;
        brown = brown * 0.996f + Noise(r) * 0.1f;
        float wobble = 0.7f + 0.3f * MathF.Sin(2 * MathF.PI * 2f * t / length * 4f); // 무리 박자와 같은 주기
        x[i] = lp.Low(brown) * wobble * 3f;
    }
    // 뒤쪽 overlap 을 앞쪽에 등전력 교차로 섞는다
    for (int i = 0; i < b.Length; i++)
    {
        float v = x[i];
        if (i < o)
        {
            float k = i / (float)o;
            v = x[i] * MathF.Sin(k * MathF.PI / 2) + x[b.Length + i] * MathF.Cos(k * MathF.PI / 2);
        }
        b[i] += amp * v;
    }
}

// 궤도 빔: 번쩍이는 전기 내리꽂힘 + 빔 웅웅 + 지지직 + 대폭발 + 긴 울림
float[] OrbitalBeam()
{
    var b = Buf(4.8f);
    var r = new Random(55);
    Zap(b, 0f, 3200f, 70f, 0.9f, 0.6f, 0.5f);
    Burst(b, r, 0f, 3000f, 0.15f, 0.6f);
    BeamHum(b, r, 0f, 1.65f, 0.45f);
    Crackle(b, r, 0f, 1.6f, 70f, 0.4f);
    Boom(b, 0.03f, 72f, 22f, 0.7f, 1.0f, 1.0f);
    Rumble(b, r, 0.03f, 100f, 1.9f, 0.8f);
    Debris(b, r, 0.25f, 3.8f, 0.2f, 120);
    return b;
}

// ───────────── 재료 ─────────────

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

// 땅 울림 (갈색 잡음 저역)
void Rumble(float[] b, Random r, float start, float cutoff, float tau, float amp)
{
    var lp = new Svf(cutoff, 0.7f);
    float brown = 0f;
    int s0 = Idx(start);
    for (int i = s0; i < b.Length; i++)
    {
        float t = (i - s0) / (float)SR;
        brown = brown * 0.995f + Noise(r) * 0.1f;
        float env = Attack(t, 0.03f) * MathF.Exp(-t / tau);
        // 천천히 출렁이는 울림
        float wobble = 0.75f + 0.25f * MathF.Sin(2 * MathF.PI * 3.3f * t + MathF.Sin(2 * MathF.PI * 0.7f * t) * 2f);
        b[i] += amp * env * wobble * lp.Low(brown) * 3f;
    }
}

// 흩어지는 파편·자갈 (짧은 알갱이, 시간이 갈수록 드물고 작게)
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
        bp.SetFreq(fLow + (fHigh - fLow) * Bell(k, 0.6f, 0.25f));
        float env = MathF.Pow(MathF.Sin(MathF.PI * MathF.Pow(k, 0.8f)), 2f);
        b[s0 + i] += amp * env * bp.Band(Noise(r)) * 2.5f;
    }
}

// 전기 내리꽂힘 (톱니파가 높은 음에서 떨어짐)
void Zap(float[] b, float start, float f0, float f1, float sweep, float tau, float amp)
{
    var lp = new Svf(8000f, 0.9f);
    double phase = 0;
    int s0 = Idx(start);
    for (int i = s0; i < b.Length; i++)
    {
        float t = (i - s0) / (float)SR;
        float f = f0 * MathF.Pow(f1 / f0, MathF.Min(1f, t / sweep));
        phase = (phase + f / SR) % 1.0;
        lp.SetFreq(MathF.Min(12000f, f * 5f));
        float env = Attack(t, 0.002f) * MathF.Exp(-t / tau);
        if (env < 1e-4f && t > 0.1f) break;
        b[i] += amp * env * lp.Low((float)(2 * phase - 1));
    }
}

// 밝은 잡음 섬광
void Burst(float[] b, Random r, float start, float highPass, float tau, float amp)
{
    var hp = new Svf(highPass, 0.7f);
    int s0 = Idx(start);
    for (int i = s0; i < b.Length; i++)
    {
        float t = (i - s0) / (float)SR;
        float env = Attack(t, 0.001f) * MathF.Exp(-t / tau);
        if (env < 1e-4f && t > 0.05f) break;
        b[i] += amp * env * hp.High(Noise(r));
    }
}

// 빔이 떠 있는 동안 웅웅 (낮은 톱니 두 개 + 떨림)
void BeamHum(float[] b, Random r, float start, float length, float amp)
{
    var bp = new Svf(500f, 1.5f);
    double p1 = 0, p2 = 0;
    int s0 = Idx(start), n = Idx(length);
    for (int i = 0; i < n && s0 + i < b.Length; i++)
    {
        float t = i / (float)SR;
        p1 = (p1 + 48.0 / SR) % 1.0;
        p2 = (p2 + 72.4 / SR) % 1.0;
        bp.SetFreq(300f + 600f * MathF.Exp(-t / 0.5f));
        float trem = 0.7f + 0.3f * MathF.Sin(2 * MathF.PI * 26f * t);
        float x = (float)(2 * p1 - 1) + 0.7f * (float)(2 * p2 - 1) + Noise(r) * 0.2f;
        float env = Attack(t, 0.05f) * MathF.Min(1f, (length - t) / 0.8f);
        b[s0 + i] += amp * env * trem * bp.Band(x) * 2f;
    }
}

// 지지직 (드문 전기 튐)
void Crackle(float[] b, Random r, float start, float length, float rate, float amp)
{
    var hp = new Svf(2200f, 0.8f);
    int s0 = Idx(start), n = Idx(length);
    float spark = 0f;
    for (int i = 0; i < n && s0 + i < b.Length; i++)
    {
        float t = i / (float)SR;
        if (r.NextDouble() < rate / SR * (1f - t / length)) spark = 1f;
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

// 마무리: 부드러운 포화(무게감) → 앞뒤 페이드 → 피크 -1dBFS 정규화 → 16bit WAV
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
        int fadeIn = Idx(0.002f), fadeOut = Idx(0.05f);
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

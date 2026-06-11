using System.Text.Json;
using Files_Tools.Services;

namespace Files.Tools.Tests;

/// <summary>
/// Parity tests for <see cref="DeepFilterNetDsp"/> against the validated Python reference
/// (tools/deepfilternet-rt/dump_dfn_fixture.py -> Fixtures/dfn_dsp.json). Confirms the C# ERB
/// bands, Vorbis STFT (incl. wnorm), normalized features (incl. libDF state init), and the
/// mask + deep-filter reconstruction reproduce the reference before the service relies on them.
/// </summary>
[TestClass]
public class DeepFilterNetDspTests
{
    private static JsonElement Fixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "dfn_dsp.json");
        Assert.IsTrue(File.Exists(path), $"fixture missing: {path}");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    private static float[] Floats(JsonElement a)
    {
        var r = new float[a.GetArrayLength()];
        int i = 0;
        foreach (var e in a.EnumerateArray())
        {
            r[i++] = e.GetSingle();
        }

        return r;
    }

    private static void Close(float[] expected, double[] actual, double tol, string what)
    {
        Assert.AreEqual(expected.Length, actual.Length, $"{what}: length");
        double max = 0;
        for (int i = 0; i < expected.Length; i++)
        {
            max = Math.Max(max, Math.Abs(expected[i] - actual[i]));
        }

        Assert.IsTrue(max <= tol, $"{what}: max|diff|={max:E3} > tol {tol:E3}");
    }

    private static void Close(float[] expected, float[] actual, double tol, string what)
        => Close(expected, Array.ConvertAll(actual, v => (double)v), tol, what);

    [TestMethod]
    public void ErbFb_MatchesReference()
    {
        var fx = Fixture();
        var widths = DeepFilterNetDsp.ErbFb();
        var expected = new int[fx.GetProperty("erb_widths").GetArrayLength()];
        int i = 0;
        foreach (var e in fx.GetProperty("erb_widths").EnumerateArray())
        {
            expected[i++] = e.GetInt32();
        }

        CollectionAssert.AreEqual(expected, widths, "ERB band widths");
    }

    [TestMethod]
    public void Stft_MatchesReference()
    {
        var fx = Fixture();
        var signal = Floats(fx.GetProperty("signal"));
        var (re, im, _) = DeepFilterNetDsp.Stft(signal, DeepFilterNetDsp.VorbisWindow());
        Close(Floats(fx.GetProperty("spec_re")), re, 1e-4, "stft.re");
        Close(Floats(fx.GetProperty("spec_im")), im, 1e-4, "stft.im");
    }

    [TestMethod]
    public void Features_MatchReference()
    {
        var fx = Fixture();
        var signal = Floats(fx.GetProperty("signal"));
        var (re, im, frames) = DeepFilterNetDsp.Stft(signal, DeepFilterNetDsp.VorbisWindow());
        DeepFilterNetDsp.Features(re, im, frames, DeepFilterNetDsp.ErbFb(), out var fe, out var fs);
        Close(Floats(fx.GetProperty("feat_erb")), fe, 2e-3, "feat_erb");
        Close(Floats(fx.GetProperty("feat_spec")), fs, 2e-3, "feat_spec");
    }

    [TestMethod]
    public void ApplyAndReconstruct_MatchesReference()
    {
        var fx = Fixture();
        var signal = Floats(fx.GetProperty("signal"));
        var window = DeepFilterNetDsp.VorbisWindow();
        var (re, im, frames) = DeepFilterNetDsp.Stft(signal, window);
        var widths = DeepFilterNetDsp.ErbFb();
        var m = Floats(fx.GetProperty("m"));
        var coefs = Floats(fx.GetProperty("coefs"));
        var enhanced = DeepFilterNetDsp.ApplyAndReconstruct(re, im, frames, widths, m, coefs, window);
        Close(Floats(fx.GetProperty("enhanced")), enhanced, 2e-3, "enhanced");
    }
}

using System.Numerics;

namespace KestrelBackend;

internal sealed class NumericsModule : ICapabilityModule
{
    public IEnumerable<CapabilityDescriptor> Describe() =>
    [
        new("numerics.bigint",      "Numerics", "BigInteger",      "Arbitrary-precision integer arithmetic",    Verdict.Works, "System.Numerics.BigInteger; AOT-safe"),
        new("numerics.genericmath", "Numerics", "Generic Math",    "INumber<T> generic sum over int/double",   Verdict.Works, "INumber<T>; static abstractions; AOT-safe"),
        new("numerics.simd",        "Numerics", "SIMD Vector<T>",  "System.Numerics.Vector<float> dot product", Verdict.Works, "Vector<T>; hardware SIMD on arm64/x64"),
    ];

    public Task<CapabilityResult> RunAsync(string id, CancellationToken ct) =>
        Task.FromResult(id switch
        {
            "numerics.bigint"      => RunBigInt(),
            "numerics.genericmath" => RunGenericMath(),
            "numerics.simd"        => RunSimd(),
            _ => Unknown(id)
        });

    public void MapRoutes(Router router) { }

    private static CapabilityResult RunBigInt()
    {
        BigInteger factorial = 1;
        for (int i = 1; i <= 50; i++) factorial *= i;
        string result = factorial.ToString();
        return Works("numerics.bigint", "Numerics", "BigInteger",
            $"50! = {result[..20]}… ({result.Length} digits)");
    }

    private static CapabilityResult RunGenericMath()
    {
        int intSum = GenericSum<int>([1, 2, 3, 4, 5]);
        double doubleSum = GenericSum<double>([1.1, 2.2, 3.3]);
        return Works("numerics.genericmath", "Numerics", "Generic Math",
            $"INumber<int> sum 1..5={intSum}; INumber<double> sum={doubleSum:F1}");
    }

    private static T GenericSum<T>(IEnumerable<T> items) where T : INumber<T>
    {
        T sum = T.Zero;
        foreach (var item in items) sum += item;
        return sum;
    }

    private static CapabilityResult RunSimd()
    {
        int count = Vector<float>.Count;
        var a = new float[count];
        var b = new float[count];
        for (int i = 0; i < count; i++) { a[i] = i + 1; b[i] = i + 1; }

        float dot = Vector.Dot(new Vector<float>(a), new Vector<float>(b));
        float expected = a.Zip(b, (x, y) => x * y).Sum();
        return Works("numerics.simd", "Numerics", "SIMD Vector<T>",
            $"Vector<float>.Count={count}; dot={dot:F0} (expected {expected:F0}); correct={Math.Abs(dot - expected) < 0.01f}");
    }

    private static CapabilityResult Works(string id, string cat, string title, string detail) =>
        new() { Id = id, Category = cat, Title = title, Verdict = Verdict.Works,
                Detail = detail, CorrelationId = CorrelationContext.Current };

    private static CapabilityResult Unknown(string id) =>
        new() { Id = id, Verdict = Verdict.Fails, Detail = $"Unknown: {id}" };
}

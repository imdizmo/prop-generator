using Xunit;

namespace PropGenerator.Nature.Tests.Support;

/// <summary>
/// One family of the kernel golden (kernel/&lt;name&gt;.bin): raw f64 records, each the inputs then three's outputs, named
/// as kernel/index.json names them.
/// </summary>
internal sealed class KernelFamily
{
    private readonly double[] _data;
    private readonly Dictionary<string, int> _columns = [];

    public KernelFamily(string name, IReadOnlyList<string> inputs, IReadOnlyList<string> outputs, double[] data)
    {
        Name = name;
        Inputs = inputs;
        Outputs = outputs;
        _data = data;
        Width = inputs.Count + outputs.Count;
        Records = data.Length / Width;
        if (Records * Width != data.Length)
        {
            throw new InvalidOperationException($"kernel/{name}.bin is not a whole number of {Width}-value records");
        }

        for (int i = 0; i < Width; i++)
        {
            _columns.Add(i < inputs.Count ? inputs[i] : outputs[i - inputs.Count], i);
        }
    }

    public string Name { get; }

    public IReadOnlyList<string> Inputs { get; }

    public IReadOnlyList<string> Outputs { get; }

    public int Width { get; }

    public int Records { get; }

    /// <summary>A value of one record, by its column name.</summary>
    public double this[int record, string column] => _data[(record * Width) + _columns[column]];

    /// <summary>Consecutive values of one record from a column on, e.g. the 16 elements m0..m15.</summary>
    public double[] Slice(int record, string first, int count) =>
        _data.AsSpan((record * Width) + _columns[first], count).ToArray();

    /// <summary>
    /// Runs <paramref name="compute"/> on every record and fails unless each output has the same bits as three's (NaN
    /// matches any NaN). <paramref name="compute"/> gets the record number and returns the outputs in the golden's order.
    /// </summary>
    public void AssertAll(Func<int, double[]> compute)
    {
        int differing = 0;
        var first = new List<string>();
        for (int r = 0; r < Records; r++)
        {
            double[] got = compute(r);
            if (got.Length != Outputs.Count)
            {
                throw new InvalidOperationException($"{Name}: computed {got.Length} outputs, the golden has {Outputs.Count}");
            }

            bool same = true;
            for (int o = 0; o < got.Length; o++)
            {
                double want = _data[(r * Width) + Inputs.Count + o];
                if (!Exact.Same(want, got[o]))
                {
                    same = false;
                    if (first.Count < 8)
                    {
                        first.Add($"record {r} {Outputs[o]}: three {want:R}, port {got[o]:R}");
                    }
                }
            }

            if (!same)
            {
                differing++;
            }
        }

        Assert.True(differing == 0, $"{Name}: {differing} of {Records} records differ:\n{string.Join('\n', first)}");
    }
}

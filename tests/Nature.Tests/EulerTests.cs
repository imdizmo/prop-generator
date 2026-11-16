using PropGenerator.Nature.Kernel;
using PropGenerator.Nature.Tests.Support;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class EulerTests
{
    private static readonly EulerOrder[] Orders = [EulerOrder.XYZ, EulerOrder.YXZ, EulerOrder.ZYX];

    [Fact]
    public void SetFromQuaternionMatchesThree()
    {
        var family = KernelGoldens.Family("setFromQuaternion");
        family.AssertAll(r =>
        {
            var q = new Quatd(family[r, "qx"], family[r, "qy"], family[r, "qz"], family[r, "qw"]);
            var e = new Euler().SetFromQuaternion(q, Orders[(int)family[r, "order"]]);
            return [e.X, e.Y, e.Z];
        });
    }

    [Fact]
    public void DefaultOrderIsXyz()
    {
        Assert.Equal(EulerOrder.XYZ, new Euler().Order);
        Assert.Equal(EulerOrder.XYZ, new Euler(1, 2, 3).Order);
    }

    [Fact]
    public void SetFromQuaternionKeepsItsOrderWhenGivenNone()
    {
        var q = new Quatd().SetFromEuler(new Euler(0.4, 0.5, 0.6, EulerOrder.ZYX));
        var e = new Euler(0, 0, 0, EulerOrder.ZYX).SetFromQuaternion(q);
        var expected = new Euler().SetFromQuaternion(q, EulerOrder.ZYX);
        Assert.Equal(EulerOrder.ZYX, e.Order);
        Assert.Equal((expected.X, expected.Y, expected.Z), (e.X, e.Y, e.Z));
    }

    [Fact]
    public void SetFromQuaternionTakesTheNewOrder()
    {
        var e = new Euler().SetFromQuaternion(new Quatd(), EulerOrder.YXZ);
        Assert.Equal(EulerOrder.YXZ, e.Order);
    }
}

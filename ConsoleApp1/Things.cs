using Overby.LazyProps;

namespace ConsoleApp1


{
    partial class Things(int a, int b)
    {
        [LazyProp(nameof(Sum), ThreadSafe = true)]
        int GetSum() => (a + b).Dump();

        [LazyProp(nameof(Diff), ThreadSafe = true)]
        int GetDiff() => a - b;

        [LazyProp(nameof(SumPlusDiff), ThreadSafe = true)]
        [LazyProp(nameof(SumPlusDiff2), ThreadSafe = true)]
        int GetSumPlusDiff() => Sum + Diff;

        [LazyProp(nameof(NonSense), ThreadSafe = true)]
        int GetNonsense() => SumPlusDiff + SumPlusDiff2;

        public override string ToString()
        {
            return NonSense.ToString();
        }
    }
}

static class x
{
    public static T Dump<T>(this T v)
    {
        Console.WriteLine(v);
        return v;
    }
}
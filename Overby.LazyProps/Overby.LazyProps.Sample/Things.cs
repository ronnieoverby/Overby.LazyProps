namespace Overby.LazyProps.Sample;

partial class Things(int a, int b)
{
    [LazyProp("Sum")]
    int GetSum() => a + b;

    [LazyProp("Diff", ThreadSafe = true, FieldPrefix = "_diff")]
    int GetDiff() => a - b;
}

class X
{
    public void Y()
    {
        var t = new Things(3, 4);
        
        // var sum = t.Sum;
        // var diff = t.Diff;
    }
}


public partial class Person
{
    public string FirstName { get; set; }
    public string LastName { get; set; }
        
    [LazyProp("FullName")]
    string GetFullName()
    {
        return $"{FirstName} {LastName}";
    }

    [LazyProp("XYZ")]
    void GetXYZ()
    {
    }
}
using System.ComponentModel;
using lychee;
using lychee.interfaces;

namespace lychee_dev;

[HelloTest]
partial class Test
{

}

public static class Program
{
    public static void Main(string[] args)
    {
        var test = new Test();
        test.Hello();

        Console.WriteLine(test is IComponentBundle);

        Foo<Test>();
        Foo<int>();
    }

    // [SealedRequired]
    static void Foo<T>()
    {

    }
}

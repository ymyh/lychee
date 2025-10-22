using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using lychee;
using lychee.attributes;
using lychee.collections;
using lychee.interfaces;

namespace lychee_dev;

public struct Position : IComponent
{
    public int X;
    public int Y;
}

public struct Velocity : IComponent
{
    public int X;
    public int Y;
}

public struct Tag : IComponent;

sealed class Time
{
    public float Elapsed;
}

[AutoImplSystem]
[SuppressMessage("MicrosoftCodeAnalysisCorrectness", "RS1035:不要使用禁用于分析器的 API")]
partial class InitSystem
{
    private static void Execute(EntityCommander entityCommander)
    {
        for (var i = 0; i < 4; i++)
        {
            var entity = entityCommander.CreateEntity();

            // entityCommander.AddComponents(entity, new Bundle()
            // {
            //     Position = new() { X = i, Y = i },
            //     Velocity = new() { X = i + 1, Y = i + 1 },
            // });

            entityCommander.AddComponent(entity, new Position { X = i, Y = i });
            entityCommander.AddComponent(entity, new Velocity { X = i + 1, Y = i + 1 });

            if (i % 2 == 0)
            {
                // entityCommander.RemoveEntity(entity);
                // entityCommander.RemoveComponent<Velocity>(entity);
                entityCommander.RemoveComponents<Bundle2>(entity);
            }
        }
    }
}

[AutoImplSystem]
[SuppressMessage("MicrosoftCodeAnalysisCorrectness", "RS1035:不要使用禁用于分析器的 API")]
partial class PrintSystem
{
    private static void Execute(Entity entity, ref Position position, ref Velocity velocity)
    {
        Console.WriteLine($"Entity: {entity.ID}");
        Console.WriteLine("Position: " + position.X + " " + position.Y);
        Console.WriteLine("Velocity: " + velocity.X + " " + velocity.Y);
    }
}

[AutoImplSystem]
[SuppressMessage("MicrosoftCodeAnalysisCorrectness", "RS1035:不要使用禁用于分析器的 API")]
partial class MoveSystem
{
    private static void Execute(ref Position position, ref Velocity velocity)
    {
        position.X += velocity.X;
        position.Y += velocity.Y;

        Console.WriteLine($"Position: {position.X} {position.Y}");
    }
}

struct Bundle : IComponentBundle
{
    public Position Position;

    public Velocity Velocity;

    public Tag tag;
}

struct Bundle2 : IComponentBundle
{
    public Velocity Velocity;

    public Tag tag;
}

sealed class C
{
    public int A;

    public float B;
}

[SuppressMessage("MicrosoftCodeAnalysisCorrectness", "RS1035:不要使用禁用于分析器的 API")]
public static class Program
{
    public static void Main(string[] args)
    {
        using var app = new App();

        var firstTime = true;
        var startUpSchedule = new SimpleSchedule(app, () =>
        {
            if (!firstTime) return false;
            firstTime = false;
            return true;
        });

        var firstTime2 = true;
        Stopwatch watch = null;
        var fixedUpdateSchedule = new SimpleSchedule(app, () =>
        {
            if (firstTime2)
            {
                firstTime2 = false;
                watch = Stopwatch.StartNew();
                return true;
            }

            if (watch!.ElapsedMilliseconds >= 1000)
            {
                watch.Restart();
                return true;
            }

            return false;
        });

        app.World.SystemSchedules.AddSchedule(startUpSchedule);
        // app.World.SystemSchedules.AddSchedule(fixedUpdateSchedule);

        var init = startUpSchedule.AddSystem<InitSystem>();
        startUpSchedule.AddSystem<PrintSystem>(new SystemDescriptor
        {
            AddAfter = init,
        });

        fixedUpdateSchedule.AddSystem<MoveSystem>();

        var t = new Thread(() => { app.Run(); });

        t.Start();

        Console.WriteLine("按回车键退出");
        Console.Read();
        app.ShouldExit = true;

        t.Join();

        // var fields = typeof(Bundle).GetFields().OrderBy(f => f.MetadataToken).ToArray();
        //
        // foreach (var field in fields)
        // {
        //     Console.WriteLine(field.Name);
        // }

        // var builder = new DirectedAcyclicGraph<int>();
        // var n1 = builder.AddNode(new (1));
        // var n2 = builder.AddNode(new (2));
        // var n3 = builder.AddNode(new (3));
        // var n4 = builder.AddNode(new (4));
        // var n5 = builder.AddNode(new (5));
        // var n6 = builder.AddNode(new (6));
        //
        // builder.AddEdge(n1, n3);
        // builder.AddEdge(n2, n3);
        // builder.AddEdge(n3, n4);
        // builder.AddEdge(n4, n5);
        // builder.AddEdge(n4, n6);
        //
        // var list = builder.AsList().Freeze();

        // var rng = new Random();
        // var list = new List<Bundle>();
        // var map = new SparseMap<Bundle>();
        //
        // for (var i = 0; i < 10000; i++)
        // {
        //     list.Add(new()
        //     {
        //         A = i, B = rng.NextSingle(),
        //     });
        //
        //     map.Add(list[^1]);
        // }
        //
        // for (var i = 0; i < 10000; i++)
        // {
        //     if (!map.TryGetValue(list[i].A, out var bundle))
        //     {
        //         Console.WriteLine("WTF");
        //     }
        //     else
        //     {
        //         if (bundle.B != list[i].B)
        //         {
        //             Console.WriteLine(i);
        //         }
        //     }
        // }

        // BenchmarkRunner.Run(typeof(Test).Assembly);
    }

    static void Foo([StringLiteral] string s)
    {
    }

    static void Foo<T>() where T : unmanaged
    {
    }

    static void Foo(ref readonly int num)
    {
    }
}

public class Test
{
    [Benchmark, ArgumentsSource(nameof(RandomList))]
    public void TestSparseMap(List<int> span)
    {
        var map = new SparseMap<C>();

        foreach (var i in span)
        {
            map.Add(i, new()
            {
                A = i, B = i,
            });
        }

        for (var n = 0; n < 10000; n++)
        {
            foreach (var i in span)
            {
                map.TryGetValue(i, out var bundle);
                // _ = map[i];
            }
        }
    }

    [Benchmark, ArgumentsSource(nameof(RandomList))]
    public void TestDict(List<int> span)
    {
        var map = new Dictionary<int, C>();

        foreach (var i in span)
        {
            map.Add(i, new()
            {
                A = i, B = i,
            });
        }

        for (var n = 0; n < 10000; n++)
        {
            foreach (var i in span)
            {
                map.TryGetValue(i, out var bundle);
                // _ = map[i];
            }
        }
    }

    [SuppressMessage("MicrosoftCodeAnalysisCorrectness", "RS1035:不要使用禁用于分析器的 API")]
    public static IEnumerable<List<int>> RandomList()
    {
        // var rng = new Random();
        // var list = new List<int>();
        //
        // for (var i = 0; i < 10000; i++)
        // {
        //     list.Add(i);
        // }
        //
        // rng.Shuffle(CollectionsMarshal.AsSpan(list));

        // yield return list;
        yield return [1, 2, 3, 4, 5, 6, 7, 8];
    }
}

static class ExtHelper<T>
{
    public delegate void ForEachRefAction(ref T item);
}

static class Ext
{
    extension<T>(List<T> self)
    {
        public void ForEachRef(ExtHelper<T>.ForEachRefAction act)
        {
            var span = CollectionsMarshal.AsSpan(self);

            for (var i = 0; i < self.Count; i++)
            {
                act(ref span[i]);
            }
        }
    }
}

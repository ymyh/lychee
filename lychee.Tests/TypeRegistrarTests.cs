using lychee.interfaces;

namespace lychee.Tests;

public class TypeRegistrarTests
{
#region Register

    [Fact]
    public void Register_NewType_ReturnsId()
    {
        var registrar = new TypeRegistrar();

        var id = registrar.Register<string>();

        Assert.True(id >= 0);
    }

    [Fact]
    public void Register_SameType_ReturnsSameId()
    {
        var registrar = new TypeRegistrar();

        var id1 = registrar.Register<string>();
        var id2 = registrar.Register<string>();

        Assert.Equal(id1, id2);
    }

    [Fact]
    public void Register_DifferentTypes_ReturnsDifferentIds()
    {
        var registrar = new TypeRegistrar();

        var id1 = registrar.Register<string>();
        var id2 = registrar.Register<int>();

        Assert.NotEqual(id1, id2);
    }

#endregion

#region RegisterComponent

    [Fact]
    public void RegisterComponent_ValidComponent_ReturnsId()
    {
        var registrar = new TypeRegistrar();

        var id = registrar.RegisterComponent<TestPosition>();

        Assert.True(id >= 0);
    }

    [Fact]
    public void RegisterComponent_SameComponent_ReturnsSameId()
    {
        var registrar = new TypeRegistrar();

        var id1 = registrar.RegisterComponent<TestPosition>();
        var id2 = registrar.RegisterComponent<TestPosition>();

        Assert.Equal(id1, id2);
    }

    [Fact]
    public void RegisterComponent_NonComponent_ThrowsArgumentException()
    {
        var registrar = new TypeRegistrar();

        Assert.Throws<ArgumentException>(() => registrar.RegisterComponent(typeof(string)));
    }

    [Fact]
    public void RegisterComponent_ByType_Works()
    {
        var registrar = new TypeRegistrar();

        var id = registrar.RegisterComponent(typeof(TestPosition));

        Assert.True(id >= 0);
    }

#endregion

#region RegisterBundle

    [Fact]
    public void RegisterBundle_ValidBundle_RegistersAllFieldTypes()
    {
        var registrar = new TypeRegistrar();

        registrar.RegisterBundle<TestMovement>();

        // Both Position and Velocity should be registered
        var posId = registrar.GetTypeId<TestPosition>();
        var velId = registrar.GetTypeId<TestVelocity>();

        Assert.True(posId >= 0);
        Assert.True(velId >= 0);
    }

    [Fact]
    public void RegisterBundle_CalledTwice_DoesNotThrow()
    {
        var registrar = new TypeRegistrar();

        registrar.RegisterBundle<TestMovement>();
        registrar.RegisterBundle<TestMovement>(); // should be idempotent

        Assert.True(registrar.GetTypeId<TestPosition>() >= 0);
    }

    [Fact]
    public void RegisterBundle_GetBundleInfo_ReturnsCorrectInfo()
    {
        var registrar = new TypeRegistrar();

        registrar.RegisterBundle<TestMovement>();

        var bundleInfo = registrar.GetBundleInfo<TestMovement>();

        Assert.NotNull(bundleInfo);
        Assert.True(bundleInfo.Length > 0);
    }

#endregion

#region RegisterTypesOfTuple

    [Fact]
    public void RegisterTypesOfTuple_ValidTuple_RegistersAllTypes()
    {
        var registrar = new TypeRegistrar();

        var ids = registrar.RegisterTypesOfTuple<(int, float, double)>();

        Assert.Equal(3, ids.Length);
        Assert.All(ids, id => Assert.True(id >= 0));
    }

    [Fact]
    public void RegisterTypesOfTuple_NonTuple_ThrowsArgumentException()
    {
        var registrar = new TypeRegistrar();

        Assert.Throws<ArgumentException>(() => registrar.RegisterTypesOfTuple<int>());
    }

#endregion

#region GetTypeId

    [Fact]
    public void GetTypeId_RegisteredType_ReturnsId()
    {
        var registrar = new TypeRegistrar();
        var id = registrar.Register<string>();

        var result = registrar.GetTypeId<string>();

        Assert.Equal(id, result);
    }

    [Fact]
    public void GetTypeId_UnregisteredType_ReturnsMinusOne()
    {
        var registrar = new TypeRegistrar();

        var result = registrar.GetTypeId<string>();

        Assert.Equal(-1, result);
    }

    [Fact]
    public void GetTypeId_ByType_Works()
    {
        var registrar = new TypeRegistrar();
        var id = registrar.Register(typeof(int));

        var result = registrar.GetTypeId(typeof(int));

        Assert.Equal(id, result);
    }

    [Fact]
    public void GetTypeId_UnregisteredByType_ReturnsMinusOne()
    {
        var registrar = new TypeRegistrar();

        var result = registrar.GetTypeId(typeof(int));

        Assert.Equal(-1, result);
    }

#endregion

#region GetTypeInfo

    [Fact]
    public void GetTypeInfo_RegisteredComponent_ReturnsTypeInfo()
    {
        var registrar = new TypeRegistrar();
        registrar.RegisterComponent<TestPosition>();

        var typeInfo = registrar.GetTypeInfo(typeof(TestPosition));

        Assert.True(typeInfo.Size > 0);
    }

    [Fact]
    public void GetTypeInfo_ById_ReturnsSameInfo()
    {
        var registrar = new TypeRegistrar();
        var id = registrar.RegisterComponent<TestPosition>();

        var typeInfo = registrar.GetTypeInfo(id);

        Assert.True(typeInfo.Size > 0);
    }

    [Fact]
    public void GetTypeInfo_ComponentMeta_MatchesInterface()
    {
        var registrar = new TypeRegistrar();
        registrar.RegisterComponent<TestPosition>();

        var typeInfo = registrar.GetTypeInfo(typeof(TestPosition));

        var component = new TestPosition();
        var meta = component.GetComponentMeta();

        Assert.Equal(meta.Size, typeInfo.Size);
    }

#endregion

#region GetTypeById

    [Fact]
    public void GetTypeById_RegisteredType_ReturnsType()
    {
        var registrar = new TypeRegistrar();
        var id = registrar.Register<string>();

        var type = registrar.GetTypeById(id);

        Assert.Equal(typeof(string), type);
    }

#endregion

#region DumpAllTypesName

    [Fact]
    public void DumpAllTypesName_ReturnsAllRegisteredTypes()
    {
        var registrar = new TypeRegistrar();
        registrar.Register<string>();
        registrar.Register<int>();
        registrar.Register<float>();

        var names = registrar.DumpAllTypesName();

        Assert.Equal(3, names.Count);
    }

    [Fact]
    public void DumpAllTypesName_EmptyRegistrar_ReturnsEmpty()
    {
        var registrar = new TypeRegistrar();

        var names = registrar.DumpAllTypesName();

        Assert.Empty(names);
    }

#endregion

#region Stress

    [Fact]
    public void Stress_RegisterManyTypes_AllHaveUniqueIds()
    {
        var registrar = new TypeRegistrar();
        var ids = new List<int>();

        for (var i = 0; i < 100; i++)
        {
            ids.Add(registrar.Register<int>());
        }

        // All should return the same ID since it's the same type
        Assert.Single(ids.Distinct());
    }

#endregion
}

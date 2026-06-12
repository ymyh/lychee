namespace lychee.Tests;

public class ResourcePoolTests : IDisposable
{
    private readonly TypeRegistrar typeRegistrar = new();

#region AddResource / GetResource (Reference Types)

    [Fact]
    public void AddResource_NewResource_CanBeRetrieved()
    {
        var pool = new ResourcePool(typeRegistrar);
        var resource = new TestResource { Value = 42 };

        pool.AddResource(resource);

        var retrieved = pool.GetResource<TestResource>();
        Assert.Equal(42, retrieved.Value);
    }

    [Fact]
    public void AddResource_DuplicateType_ThrowsArgumentException()
    {
        var pool = new ResourcePool(typeRegistrar);

        pool.AddResource(new TestResource());

        Assert.Throws<ArgumentException>(() => pool.AddResource(new TestResource()));
    }

    [Fact]
    public void AddResource_DefaultConstructor_CreatesNewInstance()
    {
        var pool = new ResourcePool(typeRegistrar);

        var resource = pool.AddResource<TestResource>();

        Assert.NotNull(resource);
    }

    [Fact]
    public void GetResource_NonExistent_ThrowsArgumentException()
    {
        var pool = new ResourcePool(typeRegistrar);

        Assert.Throws<ArgumentException>(() => pool.GetResource<TestResource>());
    }

    [Fact]
    public void GetResource_ByType_Works()
    {
        var pool = new ResourcePool(typeRegistrar);
        var resource = new TestResource { Value = 10 };
        pool.AddResource(resource);

        var retrieved = pool.GetResource(typeof(TestResource));

        Assert.NotNull(retrieved);
        Assert.IsType<TestResource>(retrieved);
    }

#endregion

#region GetResourceClassRef

    [Fact]
    public void GetResourceClassRef_ReturnsMutableReference()
    {
        var pool = new ResourcePool(typeRegistrar);
        pool.AddResource(new TestResource { Value = 10 });

        ref var resource = ref pool.GetResourceClassRef<TestResource>();
        resource.Value = 20;

        Assert.Equal(20, pool.GetResource<TestResource>().Value);
    }

    [Fact]
    public void GetResourceClassRef_NonExistent_ThrowsArgumentException()
    {
        var pool = new ResourcePool(typeRegistrar);

        Assert.Throws<ArgumentException>(() => pool.GetResourceClassRef<TestResource>());
    }

#endregion

#region AddResourceStruct / GetResourceStructRef (Unmanaged Types)

    [Fact]
    public void AddResourceStruct_NewResource_CanBeRetrieved()
    {
        var pool = new ResourcePool(typeRegistrar);

        pool.AddResourceStruct(42);

        ref var value = ref pool.GetResourceStructRef<int>();
        Assert.Equal(42, value);
    }

    [Fact]
    public void AddResourceStruct_DuplicateType_ThrowsArgumentException()
    {
        var pool = new ResourcePool(typeRegistrar);

        pool.AddResourceStruct(42);

        Assert.Throws<ArgumentException>(() => pool.AddResourceStruct(100));
    }

    [Fact]
    public void AddResourceStruct_DefaultValue_CreatesWithZero()
    {
        var pool = new ResourcePool(typeRegistrar);

        pool.AddResourceStruct<int>();

        ref var value = ref pool.GetResourceStructRef<int>();
        Assert.Equal(0, value);
    }

    [Fact]
    public void GetResourceStructRef_ReturnsMutableReference()
    {
        var pool = new ResourcePool(typeRegistrar);
        pool.AddResourceStruct(10);

        ref var value = ref pool.GetResourceStructRef<int>();
        value = 20;

        Assert.Equal(20, pool.GetResourceStructRef<int>());
    }

    [Fact]
    public void GetResourceStructRef_NonExistent_ThrowsArgumentException()
    {
        var pool = new ResourcePool(typeRegistrar);

        Assert.Throws<ArgumentException>(() => pool.GetResourceStructRef<int>());
    }

    [Fact]
    public void AddResourceStruct_Nint_ThrowsArgumentException()
    {
        var pool = new ResourcePool(typeRegistrar);

        Assert.Throws<ArgumentException>(() => pool.AddResourceStruct((nint)0));
    }

#endregion

#region GetResourcePtr (Unsafe)

    [Fact]
    public unsafe void GetResourcePtr_ReturnsValidPointer()
    {
        var pool = new ResourcePool(typeRegistrar);
        pool.AddResourceStruct(42);

        var ptr = pool.GetResourcePtr<int>();

        Assert.Equal(42, *ptr);
    }

    [Fact]
    public unsafe void GetResourcePtr_ModifyThroughPointer_AffectsResource()
    {
        var pool = new ResourcePool(typeRegistrar);
        pool.AddResourceStruct(10);

        var ptr = pool.GetResourcePtr<int>();
        *ptr = 20;

        Assert.Equal(20, pool.GetResourceStructRef<int>());
    }

#endregion

#region HasResource

    [Fact]
    public void HasResource_ExistingResource_ReturnsTrue()
    {
        var pool = new ResourcePool(typeRegistrar);
        pool.AddResource(new TestResource());

        Assert.True(pool.HasResource<TestResource>());
    }

    [Fact]
    public void HasResource_NonExistent_ReturnsFalse()
    {
        var pool = new ResourcePool(typeRegistrar);

        Assert.False(pool.HasResource<TestResource>());
    }

    [Fact]
    public void HasResource_ByType_Works()
    {
        var pool = new ResourcePool(typeRegistrar);
        pool.AddResource(new TestResource());

        Assert.True(pool.HasResource(typeof(TestResource)));
    }

    [Fact]
    public void HasResource_AfterRemove_ReturnsFalse()
    {
        var pool = new ResourcePool(typeRegistrar);
        pool.AddResource(new TestResource());

        pool.RemoveResource<TestResource>();

        Assert.False(pool.HasResource<TestResource>());
    }

#endregion

#region RemoveResource

    [Fact]
    public void RemoveResource_ExistingResource_RemovesIt()
    {
        var pool = new ResourcePool(typeRegistrar);
        pool.AddResource(new TestResource());

        pool.RemoveResource<TestResource>();

        Assert.False(pool.HasResource<TestResource>());
    }

    [Fact]
    public void RemoveResource_NonExistent_DoesNotThrow()
    {
        var pool = new ResourcePool(typeRegistrar);

        pool.RemoveResource<TestResource>(); // should not throw
    }

    [Fact]
    public void RemoveResource_ByType_Works()
    {
        var pool = new ResourcePool(typeRegistrar);
        pool.AddResource(new TestResource());

        pool.RemoveResource(typeof(TestResource));

        Assert.False(pool.HasResource<TestResource>());
    }

    [Fact]
    public void RemoveResource_StructResource_FreesMemory()
    {
        var pool = new ResourcePool(typeRegistrar);
        pool.AddResourceStruct(42);

        pool.RemoveResource<int>();

        Assert.False(pool.HasResource<int>());
    }

    [Fact]
    public void RemoveResource_DisposableResource_DisposesIt()
    {
        var pool = new ResourcePool(typeRegistrar);
        var disposable = new DisposableResource();
        pool.AddResource(disposable);

        pool.RemoveResource<DisposableResource>();

        Assert.True(disposable.Disposed);
    }

#endregion

#region Dispose

    [Fact]
    public void Dispose_DisposableResources_DisposesThem()
    {
        var pool = new ResourcePool(typeRegistrar);
        var disposable = new DisposableResource();
        pool.AddResource(disposable);

        pool.Dispose();

        Assert.True(disposable.Disposed);
    }

    [Fact]
    public void Dispose_MultipleCalls_DoesNotThrow()
    {
        var pool = new ResourcePool(typeRegistrar);

        pool.Dispose();
        pool.Dispose(); // should not throw
    }

#endregion

    public void Dispose()
    {
        // Each test creates its own pool
    }

    private class DisposableResource : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}

using lychee.exceptions;
using lychee.interfaces;

namespace lychee;

public sealed class App
{
#region Fields

    public delegate void RunnerDelegate();

    public TypeRegistry TypeRegistry { get; } = new();

    public World World { get; }

    public RunnerDelegate Runner { get; set; }

    private Dictionary<string, object> resourceMap = new();

    private bool shouldExit;

#endregion

    public App()
    {
        World = new World(TypeRegistry);
        Runner = () =>
        {
            while (!shouldExit)
            {
                World.Update();
            }
        };
    }

#region public methods

    /// <summary>
    /// Add a new resource with given type.
    /// This method uses Type.FullName to register
    /// DO NOT add the same resource twice.
    /// </summary>
    /// <param name="resource"></param>
    /// <exception cref="ArgumentNullException">if T.GetType().FullName is null</exception>
    /// <exception cref="ResourceExistsException">if resource already exists</exception>
    /// <typeparam name="T"></typeparam>
    public void AddResource<T>(T resource) where T : class
    {
        var type = resource.GetType();
        AddResource(type.FullName, resource);
    }

    /// <summary>
    /// Add a new resource with given name
    /// </summary>
    /// <param name="name"></param>
    /// <param name="resource"></param>
    /// <exception cref="ResourceExistsException">if resource already exists</exception>
    /// <typeparam name="T"></typeparam>
    public void AddResource<T>(string name, T resource) where T : class
    {
        if (!resourceMap.TryAdd(name, resource))
        {
            throw new ResourceExistsException(name);
        }
    }

    /// <summary>
    /// Replace an existing resource
    /// </summary>
    /// <param name="resource"></param>
    /// <exception cref="ArgumentNullException">if T.GetType().FullName is null</exception>
    /// <exception cref="ResourceExistsException">if resource is not exists</exception>
    /// <typeparam name="T"></typeparam>
    public void ReplaceResource<T>(T resource) where T : class
    {
        var type = resource.GetType();
        ReplaceResource(type.FullName, resource);
    }

    /// <summary>
    /// Replace an existing resource
    /// </summary>
    /// <param name="name"></param>
    /// <param name="resource"></param>
    /// <exception cref="ResourceExistsException">if resource does not exist</exception>
    /// <typeparam name="T"></typeparam>
    public void ReplaceResource<T>(string name, T resource) where T : class
    {
        if (!resourceMap.ContainsKey(name))
        {
            throw new ResourceNotExistsException(name);
        }

        resourceMap[name] = resource;
    }

    /// <summary>
    /// Gets resource by type
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public T GetResource<T>() where T : class
    {
        var type = typeof(T);
        return GetResource<T>(type.FullName);
    }

    public T GetResource<T>(string name) where T : class
    {
        return (T)resourceMap[name];
    }

    public void Exit()
    {
        shouldExit = true;
    }

    public void InstallPlugin(IPlugin plugin)
    {
        plugin.Install(this);
    }
    
    public void Run()
    {
        Runner();
    }

#endregion

#region private methods



#endregion
}

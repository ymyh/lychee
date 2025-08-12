namespace lychee.exceptions;

public class TypeAlreadyRegisteredException(string typename) : Exception($"Type {typename} is already registered");

public class UnsupportedTypeException(string typename)
    : Exception($"Type {typename} is unsupported because it is not a class or value type");

public class ResourceExistsException(string typename) : Exception($"Resource {typename} is already exists");

public class ResourceNotExistsException(string typename) : Exception($"Resource {typename} is not exists");
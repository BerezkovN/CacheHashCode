namespace CacheHashCode.Fody;

/// <summary>
/// Supports both classes and structs.
/// However, does not support structs without explicit constructors (using default construction).
/// Must contain an override of GetHashCode().
///
/// Takes the code from GetHashCode() and moves it into another method __ComputeHashCode().
/// Injects code that initializes __computedHash with __ComputeHashCode() result before
/// every return opcode in all supported constructors.
/// 
/// GetHashCode() will return the result of that computation instead. 
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class CacheHashCodeAttribute : Attribute;
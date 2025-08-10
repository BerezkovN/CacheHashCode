# Limitations
Supports both classes and structs.
However, does not support structs without explicit constructors (using default construction).
Must contain an override of `GetHashCode()`.

# How it works
Takes the code from `GetHashCode()` and moves it into another method `__ComputeHashCode()`.
Injects code that initializes `__computedHash` with `__ComputeHashCode()` result before
every return opcode in all supported constructors.
`GetHashCode()` will return the result of that computation instead. 

# Example and in-depth look

Add [https://www.nuget.org/packages/Fody](Fody NuGet package) to your project.

Create a *FodyWeavers.xml*:
```
<?xml version="1.0" encoding="utf-8"?>
<Weavers xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="FodyWeavers.xsd">
  <CacheHashCode />
</Weavers>
```

Use the provided attribute in your code:
```
[CacheHashCode]
internal record struct Example(float X, float Y);
```

After compiling, check the IL code to confirm that everything is working:
```
[CacheHashCode]
[StructLayout(LayoutKind.Sequential)]
internal struct Example : IEquatable<Example>
{
  // ...
  private readonly int __computedHash;

  public Example(float X, float Y)
  {
    // ...
    this.__computedHash = this.__ComputeHashCode();
  }

  // ...

  [IsReadOnly]
  [CompilerGenerated]
  public override readonly int GetHashCode()
  {
    return this.__computedHash;
  }

  // ...

  [IsReadOnly]
  [CompilerGenerated]
  public readonly virtual int __ComputeHashCode()
  {
    return EqualityComparer<float>.Default.GetHashCode(this.<X>k__BackingField) * -1521134295 + EqualityComparer<float>.Default.GetHashCode(this.<Y>k__BackingField);
  }

```


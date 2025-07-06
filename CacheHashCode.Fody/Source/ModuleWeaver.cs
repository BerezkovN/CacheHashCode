using System.Diagnostics;
using Fody;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace CacheHashCode.Fody;

/// <summary>
/// <see cref="CacheHashCodeAttribute"/>
/// </summary>
public class ModuleWeaver : BaseModuleWeaver
{
    private readonly string m_attributeName = typeof(CacheHashCodeAttribute).FullName!;
    private const string m_defaultFieldName = "__computedHash";
    private const string m_computeHashCodeMethodName = "__ComputeHashCode";

    /// <inheritdoc />
    public override void Execute()
    {
        IEnumerable<TypeDefinition> types = this.ModuleDefinition.GetTypes()
            .Where(t => (t.IsClass || t.IsValueType)
                        && t is { IsEnum: false, IsInterface: false, IsAbstract: false }
                        && this.HasCacheHashCodeAttribute(t));
        
        foreach (TypeDefinition type in types) {
            try {
                this.ProcessType(type);
            }
            catch (Exception ex) {
                this.WriteError($"Failed to process type {type.FullName}: {ex.Message}");
            }
        }
    }

    private bool HasCacheHashCodeAttribute(TypeDefinition type)
    {
        return type.CustomAttributes.Any(
            attr => attr.AttributeType.FullName == m_attributeName);
    }

    private void ProcessType(TypeDefinition type)
    {
        this.WriteInfo($"Processing type: {type.FullName} (IsValueType: {type.IsValueType})");
        
        MethodDefinition? getHashCodeMethod = this.FindGetHashCodeMethod(type);
        if (getHashCodeMethod == null) {
            this.WriteInfo($"Type {type.FullName} does not contain a GetHashCode(), skipping");
            return;
        }

        FieldDefinition cacheField = this.AddCacheField(type, m_defaultFieldName);

        MethodDefinition computeMethod = this.CreateComputeHashCodeMethod(type, getHashCodeMethod);

        // Modify constructors to call ComputeHashCode and store result
        this.ModifyConstructors(type, cacheField, computeMethod);

        // Modify GetHashCode to return cached value
        this.ModifyGetHashCodeMethod(getHashCodeMethod, cacheField);

        this.WriteInfo($"Successfully processed type: {type.FullName}");
    }

    private MethodDefinition? FindGetHashCodeMethod(TypeDefinition type)
    {
        return type.Methods.FirstOrDefault(m => m.Name == "GetHashCode" &&
                                                m.IsVirtual &&
                                                m.Parameters.Count == 0 &&
                                                m.ReturnType.FullName == "System.Int32");
    }

    private FieldDefinition AddCacheField(TypeDefinition type, string fieldName)
    {
        FieldDefinition? existingField = type.Fields.FirstOrDefault(f => f.Name == fieldName);
        if (existingField != null) {
            throw new InvalidOperationException($"Field {fieldName} already exists");
        }

        var cacheField = new FieldDefinition(
            fieldName,
            FieldAttributes.Private | FieldAttributes.InitOnly,
            this.ModuleDefinition.TypeSystem.Int32);

        type.Fields.Add(cacheField);
        return cacheField;
    }

    private MethodDefinition CreateComputeHashCodeMethod(
        TypeDefinition type,
        MethodDefinition originalGetHashCodeMethod)
    {
        MethodDefinition result = originalGetHashCodeMethod.Clone();
        result.Name = m_computeHashCodeMethodName;
        type.Methods.Add(result);
        return result;
    }

    private void ModifyConstructors(
        TypeDefinition type, FieldDefinition cacheField, MethodDefinition computeMethod)
    {
        List<MethodDefinition> constructors = type.Methods
            .Where(m => m.IsConstructor && !m.IsStatic)
            .ToList();

        if (constructors.Count == 0 && type.IsValueType) {
            // For structs without explicit constructors, we need to handle field initialization differently
            // This is a limitation - structs without constructors can't have their hash cached during construction
            throw new NotSupportedException(
                $"Struct {type.FullName} has no explicit constructors. Hash code caching may not work as expected.");
        }

        foreach (MethodDefinition constructor in constructors) {
            this.ModifyConstructor(constructor, cacheField, computeMethod);
        }
    }

    private void ModifyConstructor(
        MethodDefinition constructor, FieldDefinition cacheField,
        MethodDefinition computeMethod)
    {
        ILProcessor? processor = constructor.Body.GetILProcessor();

        List<Instruction> returnPoints = constructor.Body.Instructions
            .Where(el => el.OpCode == OpCodes.Ret)
            .ToList();

        if (returnPoints.Count == 0) {
            throw new InvalidOperationException(
                $"Method {constructor.FullName} has no return opcodes?");
        }

        var instructions = new List<Instruction>
        {
            Instruction.Create(OpCodes.Ldarg_0), // this pointer for stfld
            Instruction.Create(OpCodes.Ldarg_0), // this pointer for call
            Instruction.Create(OpCodes.Call, computeMethod),
            Instruction.Create(OpCodes.Stfld, cacheField)
        };

        foreach (Instruction returnPoint in returnPoints) {
            foreach (Instruction instruction in instructions) {
                processor.InsertBefore(returnPoint, instruction);
            }
        }
    }

    private void ModifyGetHashCodeMethod(
        MethodDefinition getHashCodeMethod, FieldDefinition cacheField)
    {
        getHashCodeMethod.Body.Instructions.Clear();
        getHashCodeMethod.Body.Variables.Clear();
        getHashCodeMethod.Body.ExceptionHandlers.Clear();

        ILProcessor? processor = getHashCodeMethod.Body.GetILProcessor();
        processor.Emit(OpCodes.Ldarg_0);
        processor.Emit(OpCodes.Ldfld, cacheField);
        processor.Emit(OpCodes.Ret);
    }

    // Might not be necessary
    /// <inheritdoc />
    public override IEnumerable<string> GetAssembliesForScanning()
    {
        yield return "mscorlib";
        yield return "System";
        yield return "System.Runtime";
        yield return "netstandard";
    }

    /// <inheritdoc />
    public override bool ShouldCleanReference => true;
}
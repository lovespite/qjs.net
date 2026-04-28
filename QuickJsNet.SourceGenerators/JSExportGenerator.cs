using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace QuickJsNet.SourceGenerators;

/// <summary>
/// Source generator that emits AOT-safe JS binders for every type marked with
/// <c>[QuickJsNet.Interop.JSExport]</c>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class JSExportGenerator : IIncrementalGenerator
{
    private const string ExportAttrFq  = "QuickJsNet.Interop.JSExportAttribute";
    private const string IgnoreAttrFq  = "QuickJsNet.Interop.JSIgnoreAttribute";
    private const string NameAttrFq    = "QuickJsNet.Interop.JSNameAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classes = context.SyntaxProvider.ForAttributeWithMetadataName(
            ExportAttrFq,
            predicate: (n, _) => n is ClassDeclarationSyntax,
            transform: (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol);

        context.RegisterSourceOutput(classes, Emit);
    }

    private static void Emit(SourceProductionContext spc, INamedTypeSymbol type)
    {
        // Validate
        if (type.IsGenericType)
        {
            spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.GenericNotSupported,
                type.Locations.FirstOrDefault(), type.Name));
            return;
        }
        if (type.IsAbstract && !type.IsStatic)
        {
            // abstract classes are OK to expose as base, no-op
        }

        var model = ModelBuilder.Build(type, spc);
        var src = Emitter.Emit(model);
        var hint = $"{model.SafeName}.JSBinder.g.cs";
        spc.AddSource(hint, SourceText.From(src, Encoding.UTF8));
    }
}

internal static class Diagnostics
{
    public static readonly DiagnosticDescriptor GenericNotSupported = new(
        id: "QJSGEN001",
        title: "Generic [JSExport] type not supported",
        messageFormat: "[JSExport] does not support generic type '{0}'",
        category: "QuickJsNet",
        DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor RefOutNotSupported = new(
        id: "QJSGEN002",
        title: "ref/out parameters not supported",
        messageFormat: "Member '{0}' uses ref/out parameter and was skipped",
        category: "QuickJsNet",
        DiagnosticSeverity.Warning, true);

    public static readonly DiagnosticDescriptor UnsupportedType = new(
        id: "QJSGEN003",
        title: "Unsupported parameter or return type",
        messageFormat: "Member '{0}' uses unsupported type '{1}' and was skipped",
        category: "QuickJsNet",
        DiagnosticSeverity.Warning, true);

    public static readonly DiagnosticDescriptor OverloadDropped = new(
        id: "QJSGEN004",
        title: "Method overload dropped",
        messageFormat: "Overload of '{0}' with arity {1} was dropped (only first occurrence is exported)",
        category: "QuickJsNet",
        DiagnosticSeverity.Warning, true);
}

// =============================================================================
//                                   Model
// =============================================================================

internal sealed class TypeModel
{
    public INamedTypeSymbol Symbol = null!;
    public string Namespace = "";
    public string TypeName = "";          // unqualified
    public string FullTypeName = "";      // global::Ns.TypeName
    public string SafeName = "";          // Ns_TypeName for filenames/identifiers
    public string JsTypeName = "";        // for static container
    public List<PropertyModel> Properties = new();
    public List<MethodModel> Methods = new();
    public List<MethodModel> StaticMethods = new();
    public List<PropertyModel> StaticProperties = new();
    public List<EventModel> Events = new();
    public IndexerModel? Indexer;
}

internal sealed class PropertyModel
{
    public string CSharpName = "";
    public string JsName = "";
    public string TypeFq = "";          // global:: qualified
    public ITypeSymbol TypeSymbol = null!;
    public bool HasGetter;
    public bool HasSetter;
    public bool IsStatic;
}

internal sealed class MethodModel
{
    public string CSharpName = "";
    public string JsName = "";
    public ITypeSymbol ReturnType = null!;
    public string ReturnTypeFq = "";
    public List<ParamModel> Parameters = new();
    public bool IsStatic;
    public bool IsAsync;        // returns Task / Task<T> / ValueTask / ValueTask<T>
    public ITypeSymbol? AsyncResultType; // null for Task / ValueTask
}

internal sealed class ParamModel
{
    public string Name = "";
    public ITypeSymbol Type = null!;
    public string TypeFq = "";
}

internal sealed class EventModel
{
    public string CSharpName = "";
    public string JsName = "";              // canonical event name (camelCase)
    public INamedTypeSymbol HandlerType = null!;
    public List<ITypeSymbol> EventArgTypes = new();
}

internal sealed class IndexerModel
{
    public ITypeSymbol IndexType = null!;
    public string IndexTypeFq = "";
    public ITypeSymbol ValueType = null!;
    public string ValueTypeFq = "";
    public bool HasSetter;
}

internal static class ModelBuilder
{
    public static TypeModel Build(INamedTypeSymbol type, SourceProductionContext spc)
    {
        var model = new TypeModel
        {
            Symbol = type,
            Namespace = type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString(),
            TypeName = type.Name,
            FullTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            SafeName = (type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString().Replace('.', '_') + "_") + type.Name,
            JsTypeName = type.Name,
        };

        // Override with [JSExport(name)]
        var exportAttr = type.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == "QuickJsNet.Interop.JSExportAttribute");
        if (exportAttr is { ConstructorArguments.Length: > 0 } && exportAttr.ConstructorArguments[0].Value is string name)
            model.JsTypeName = name;

        foreach (var member in type.GetMembers())
        {
            if (HasJsIgnore(member)) continue;
            if (member.DeclaredAccessibility != Accessibility.Public) continue;

            switch (member)
            {
                case IPropertySymbol prop when prop.IsIndexer:
                    if (model.Indexer is null)
                    {
                        var idxParams = prop.Parameters;
                        if (idxParams.Length == 1 && IsSupported(idxParams[0].Type) && IsSupported(prop.Type))
                        {
                            model.Indexer = new IndexerModel
                            {
                                IndexType = idxParams[0].Type,
                                IndexTypeFq = Fq(idxParams[0].Type),
                                ValueType = prop.Type,
                                ValueTypeFq = Fq(prop.Type),
                                HasSetter = prop.SetMethod is { DeclaredAccessibility: Accessibility.Public },
                            };
                        }
                    }
                    break;

                case IPropertySymbol prop:
                    {
                        if (!IsSupported(prop.Type))
                        {
                            spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.UnsupportedType,
                                prop.Locations.FirstOrDefault(), prop.Name, prop.Type.ToDisplayString()));
                            break;
                        }
                        var pm = new PropertyModel
                        {
                            CSharpName = prop.Name,
                            JsName = ResolveJsName(prop),
                            TypeFq = Fq(prop.Type),
                            TypeSymbol = prop.Type,
                            HasGetter = prop.GetMethod is { DeclaredAccessibility: Accessibility.Public },
                            HasSetter = prop.SetMethod is { DeclaredAccessibility: Accessibility.Public },
                            IsStatic = prop.IsStatic,
                        };
                        (pm.IsStatic ? model.StaticProperties : model.Properties).Add(pm);
                    }
                    break;

                case IMethodSymbol m when m.MethodKind == MethodKind.Ordinary:
                    {
                        if (m.IsGenericMethod)
                        {
                            spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.UnsupportedType,
                                m.Locations.FirstOrDefault(), m.Name, "generic methods"));
                            break;
                        }
                        if (m.Parameters.Any(p => p.RefKind != RefKind.None))
                        {
                            spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.RefOutNotSupported,
                                m.Locations.FirstOrDefault(), m.Name));
                            break;
                        }

                        bool ok = true;
                        foreach (var p in m.Parameters)
                            if (!IsSupported(p.Type)) { ok = false; break; }
                        if (!ok || (!m.ReturnsVoid && !IsSupported(m.ReturnType) && !IsAsyncReturn(m.ReturnType, out _)))
                        {
                            spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.UnsupportedType,
                                m.Locations.FirstOrDefault(), m.Name, m.ReturnType.ToDisplayString()));
                            break;
                        }

                        var mm = new MethodModel
                        {
                            CSharpName = m.Name,
                            JsName = ResolveJsName(m),
                            ReturnType = m.ReturnType,
                            ReturnTypeFq = Fq(m.ReturnType),
                            IsStatic = m.IsStatic,
                            IsAsync = IsAsyncReturn(m.ReturnType, out var inner),
                            AsyncResultType = inner,
                        };
                        foreach (var p in m.Parameters)
                            mm.Parameters.Add(new ParamModel { Name = p.Name, Type = p.Type, TypeFq = Fq(p.Type) });

                        var bucket = mm.IsStatic ? model.StaticMethods : model.Methods;
                        if (bucket.Any(x => x.JsName == mm.JsName))
                        {
                            spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.OverloadDropped,
                                m.Locations.FirstOrDefault(), m.Name, mm.Parameters.Count));
                            break;
                        }
                        bucket.Add(mm);
                    }
                    break;

                case IEventSymbol ev:
                    {
                        if (ev.Type is not INamedTypeSymbol delType) break;
                        var invoke = delType.DelegateInvokeMethod;
                        if (invoke is null) break;
                        if (!invoke.ReturnsVoid)
                        {
                            spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.UnsupportedType,
                                ev.Locations.FirstOrDefault(), ev.Name, "non-void event handler"));
                            break;
                        }
                        bool argsOk = true;
                        foreach (var p in invoke.Parameters)
                            if (!IsSupported(p.Type)) { argsOk = false; break; }
                        if (!argsOk)
                        {
                            spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.UnsupportedType,
                                ev.Locations.FirstOrDefault(), ev.Name, "event handler params"));
                            break;
                        }
                        var em = new EventModel
                        {
                            CSharpName = ev.Name,
                            JsName = ResolveJsName(ev),
                            HandlerType = delType,
                        };
                        foreach (var p in invoke.Parameters) em.EventArgTypes.Add(p.Type);
                        model.Events.Add(em);
                    }
                    break;
            }
        }

        return model;
    }

    private static bool HasJsIgnore(ISymbol sym)
        => sym.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "QuickJsNet.Interop.JSIgnoreAttribute");

    private static string ResolveJsName(ISymbol sym)
    {
        var jsAttr = sym.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "QuickJsNet.Interop.JSNameAttribute");
        if (jsAttr is { ConstructorArguments.Length: > 0 } && jsAttr.ConstructorArguments[0].Value is string n)
            return n;
        return ToCamelCase(sym.Name);
    }

    public static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        if (char.IsLower(name[0])) return name;
        // ABC -> abc, ABCFoo -> abcFoo, FooBar -> fooBar
        var sb = new StringBuilder(name.Length);
        int i = 0;
        while (i < name.Length && char.IsUpper(name[i]))
        {
            // Look ahead: if next char is lower-case and we've consumed > 1, keep last upper
            if (i > 0 && i + 1 < name.Length && char.IsLower(name[i + 1])) break;
            sb.Append(char.ToLowerInvariant(name[i]));
            i++;
        }
        sb.Append(name, i, name.Length - i);
        return sb.ToString();
    }

    public static bool IsSupported(ITypeSymbol t)
    {
        if (t is null) return false;
        if (t.SpecialType is
            SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte or
            SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or
            SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 or
            SpecialType.System_Single or SpecialType.System_Double or
            SpecialType.System_String or SpecialType.System_Char or SpecialType.System_Object or
            SpecialType.System_Void)
            return true;
        if (t is IArrayTypeSymbol arr && arr.ElementType.SpecialType == SpecialType.System_Byte) return true;
        if (t.TypeKind == TypeKind.Class || t.TypeKind == TypeKind.Interface) return true;
        if (t.TypeKind == TypeKind.Enum) return true;
        return false;
    }

    public static bool IsAsyncReturn(ITypeSymbol t, out ITypeSymbol? inner)
    {
        inner = null;
        if (t is INamedTypeSymbol nt)
        {
            var name = nt.OriginalDefinition.ToDisplayString();
            if (name == "System.Threading.Tasks.Task" || name == "System.Threading.Tasks.ValueTask")
                return true;
            if (name == "System.Threading.Tasks.Task<TResult>" || name == "System.Threading.Tasks.ValueTask<TResult>")
            {
                inner = nt.TypeArguments[0];
                return true;
            }
        }
        return false;
    }

    public static string Fq(ITypeSymbol t)
        => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}

// =============================================================================
//                                  Emitter
// =============================================================================

internal static class Emitter
{
    public static string Emit(TypeModel m)
    {
        var sb = new IndentedBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8625, CS0219");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine("using System.Runtime.InteropServices;");
        sb.AppendLine();

        bool hasNs = !string.IsNullOrEmpty(m.Namespace);
        if (hasNs)
        {
            sb.AppendLine($"namespace {m.Namespace}.Generated");
            sb.AppendLine("{");
            sb.Indent();
        }
        else
        {
            sb.AppendLine("namespace QuickJsNet.Generated");
            sb.AppendLine("{");
            sb.Indent();
        }

        EmitBinderClass(sb, m);

        sb.Outdent();
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void EmitBinderClass(IndentedBuilder sb, TypeModel m)
    {
        var binderName = $"{m.TypeName}_JSBinder";
        sb.AppendLine($"internal sealed class {binderName} : global::QuickJsNet.Interop.IJSBinder<{m.FullTypeName}>");
        sb.AppendLine("{");
        sb.Indent();

        sb.AppendLine($"public static readonly {binderName} Instance = new {binderName}();");
        sb.AppendLine();
        sb.AppendLine($"public global::System.Type TargetType => typeof({m.FullTypeName});");
        sb.AppendLine();

        // Module-init registration
        sb.AppendLine("[global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine($"internal static void __Register() => global::QuickJsNet.Interop.JSBinderRegistry.Register(typeof({m.FullTypeName}), Instance);");
        sb.AppendLine();

        // Per-runtime prototype storage
        sb.AppendLine("private readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<global::QuickJsNet.Core.QuickJSRuntime, ProtoBox> _protos = new();");
        sb.AppendLine("private sealed class ProtoBox { public global::QuickJSNet.Bindings.JSValue Value; }");
        sb.AppendLine();

        // Wrap entry points
        sb.AppendLine($"public global::QuickJSNet.Bindings.JSValue Wrap(global::QuickJsNet.Core.QuickJSRuntime rt, {m.FullTypeName} target)");
        sb.AppendLine("{");
        sb.Indent();
        sb.AppendLine("var ctx = rt.Context;");
        sb.AppendLine("var proto = GetProto(rt);");
        sb.AppendLine("long id = global::QuickJsNet.Interop.JSObjectTable.Register(target);");
        sb.AppendLine("rt.TrackWrappedObjectId(id);");
        sb.AppendLine($"return global::QuickJsNet.Interop.JSInteropRuntime.NewWrapper(ctx, proto, id, {(m.Indexer != null ? "true" : "false")});");
        sb.Outdent();
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("global::QuickJSNet.Bindings.JSValue global::QuickJsNet.Interop.IJSBinder.Wrap(global::QuickJsNet.Core.QuickJSRuntime rt, object target)");
        sb.AppendLine($"    => Wrap(rt, ({m.FullTypeName})target);");
        sb.AppendLine();

        // Static container
        sb.AppendLine("public global::QuickJSNet.Bindings.JSValue BuildStaticContainer(global::QuickJsNet.Core.QuickJSRuntime rt)");
        sb.AppendLine("{");
        sb.Indent();
        sb.AppendLine("var ctx = rt.Context;");
        sb.AppendLine("var obj = global::QuickJSNet.Bindings.QuickJSNative.QJS_NewObject(ctx);");
        foreach (var sm in m.StaticMethods)
        {
            sb.AppendLine("unsafe {");
            sb.AppendLine($"    var fp = (global::System.IntPtr)(delegate* unmanaged[Cdecl]<global::System.IntPtr, global::QuickJSNet.Bindings.JSValue, int, global::System.IntPtr, global::QuickJSNet.Bindings.JSValue>)&__SM_{Sanitize(sm.JsName)};");
            sb.AppendLine($"    global::QuickJsNet.Interop.JSInteropRuntime.InstallProtoFunction(ctx, obj, \"{sm.JsName}\", fp, {sm.Parameters.Count});");
            sb.AppendLine("}");
        }
        foreach (var sp in m.StaticProperties)
        {
            sb.AppendLine("unsafe {");
            string g = sp.HasGetter
                ? $"(global::System.IntPtr)(delegate* unmanaged[Cdecl]<global::System.IntPtr, global::QuickJSNet.Bindings.JSValue, int, global::System.IntPtr, global::QuickJSNet.Bindings.JSValue>)&__SG_{Sanitize(sp.JsName)}"
                : "global::System.IntPtr.Zero";
            string s = sp.HasSetter
                ? $"(global::System.IntPtr)(delegate* unmanaged[Cdecl]<global::System.IntPtr, global::QuickJSNet.Bindings.JSValue, int, global::System.IntPtr, global::QuickJSNet.Bindings.JSValue>)&__SS_{Sanitize(sp.JsName)}"
                : "global::System.IntPtr.Zero";
            sb.AppendLine($"    global::QuickJsNet.Interop.JSInteropRuntime.InstallProtoAccessor(ctx, obj, \"{sp.JsName}\", {g}, {s});");
            sb.AppendLine("}");
        }
        sb.AppendLine("return obj;");
        sb.Outdent();
        sb.AppendLine("}");
        sb.AppendLine();

        // GetProto
        sb.AppendLine("private global::QuickJSNet.Bindings.JSValue GetProto(global::QuickJsNet.Core.QuickJSRuntime rt)");
        sb.AppendLine("{");
        sb.Indent();
        sb.AppendLine("if (!_protos.TryGetValue(rt, out var box))");
        sb.AppendLine("{");
        sb.AppendLine("    box = new ProtoBox { Value = BuildProto(rt) };");
        sb.AppendLine("    _protos.Add(rt, box);");
        sb.AppendLine("}");
        sb.AppendLine("return box.Value;");
        sb.Outdent();
        sb.AppendLine("}");
        sb.AppendLine();

        // BuildProto
        sb.AppendLine("private static global::QuickJSNet.Bindings.JSValue BuildProto(global::QuickJsNet.Core.QuickJSRuntime rt)");
        sb.AppendLine("{");
        sb.Indent();
        sb.AppendLine("var ctx = rt.Context;");
        sb.AppendLine("var proto = global::QuickJSNet.Bindings.QuickJSNative.QJS_NewObject(ctx);");
        foreach (var meth in m.Methods)
        {
            sb.AppendLine("unsafe {");
            sb.AppendLine($"    var fp = (global::System.IntPtr)(delegate* unmanaged[Cdecl]<global::System.IntPtr, global::QuickJSNet.Bindings.JSValue, int, global::System.IntPtr, global::QuickJSNet.Bindings.JSValue>)&__M_{Sanitize(meth.JsName)};");
            sb.AppendLine($"    global::QuickJsNet.Interop.JSInteropRuntime.InstallProtoFunction(ctx, proto, \"{meth.JsName}\", fp, {meth.Parameters.Count});");
            sb.AppendLine("}");
        }
        foreach (var p in m.Properties)
        {
            sb.AppendLine("unsafe {");
            string g = p.HasGetter
                ? $"(global::System.IntPtr)(delegate* unmanaged[Cdecl]<global::System.IntPtr, global::QuickJSNet.Bindings.JSValue, int, global::System.IntPtr, global::QuickJSNet.Bindings.JSValue>)&__G_{Sanitize(p.JsName)}"
                : "global::System.IntPtr.Zero";
            string s = p.HasSetter
                ? $"(global::System.IntPtr)(delegate* unmanaged[Cdecl]<global::System.IntPtr, global::QuickJSNet.Bindings.JSValue, int, global::System.IntPtr, global::QuickJSNet.Bindings.JSValue>)&__S_{Sanitize(p.JsName)}"
                : "global::System.IntPtr.Zero";
            sb.AppendLine($"    global::QuickJsNet.Interop.JSInteropRuntime.InstallProtoAccessor(ctx, proto, \"{p.JsName}\", {g}, {s});");
            sb.AppendLine("}");
        }
        // Events: install on/off methods
        if (m.Events.Count > 0)
        {
            sb.AppendLine("unsafe {");
            sb.AppendLine("    var fpOn = (global::System.IntPtr)(delegate* unmanaged[Cdecl]<global::System.IntPtr, global::QuickJSNet.Bindings.JSValue, int, global::System.IntPtr, global::QuickJSNet.Bindings.JSValue>)&__EV_On;");
            sb.AppendLine("    var fpOff = (global::System.IntPtr)(delegate* unmanaged[Cdecl]<global::System.IntPtr, global::QuickJSNet.Bindings.JSValue, int, global::System.IntPtr, global::QuickJSNet.Bindings.JSValue>)&__EV_Off;");
            sb.AppendLine("    global::QuickJsNet.Interop.JSInteropRuntime.InstallProtoFunction(ctx, proto, \"on\", fpOn, 2);");
            sb.AppendLine("    global::QuickJsNet.Interop.JSInteropRuntime.InstallProtoFunction(ctx, proto, \"off\", fpOff, 2);");
            sb.AppendLine("}");
        }
        // Indexer: wrap with Proxy via a one-off helper
        if (m.Indexer != null)
        {
            // Install a JS Proxy wrapper-creator via a special synthetic accessor
            // Implementation: emit indexer get/set as static methods, and override
            // proto.__indexerGet__ / __indexerSet__ properties pointing at them.
            // Then engineExec a small helper that wraps any object in a Proxy when
            // these properties exist. For simplicity, install __idxGet/__idxSet as
            // hidden methods invoked via JS proxy in NewWrapper bootstrap.
            sb.AppendLine("unsafe {");
            sb.AppendLine("    var fpGet = (global::System.IntPtr)(delegate* unmanaged[Cdecl]<global::System.IntPtr, global::QuickJSNet.Bindings.JSValue, int, global::System.IntPtr, global::QuickJSNet.Bindings.JSValue>)&__IDX_Get;");
            sb.AppendLine("    global::QuickJsNet.Interop.JSInteropRuntime.InstallProtoFunction(ctx, proto, \"__idxGet\", fpGet, 1);");
            if (m.Indexer.HasSetter)
            {
                sb.AppendLine("    var fpSet = (global::System.IntPtr)(delegate* unmanaged[Cdecl]<global::System.IntPtr, global::QuickJSNet.Bindings.JSValue, int, global::System.IntPtr, global::QuickJSNet.Bindings.JSValue>)&__IDX_Set;");
                sb.AppendLine("    global::QuickJsNet.Interop.JSInteropRuntime.InstallProtoFunction(ctx, proto, \"__idxSet\", fpSet, 2);");
            }
            sb.AppendLine("}");
        }
        sb.AppendLine("return proto;");
        sb.Outdent();
        sb.AppendLine("}");
        sb.AppendLine();

        // Emit per-member callbacks
        EmitInstanceCallbacks(sb, m);
        EmitStaticCallbacks(sb, m);
        EmitEventCallbacks(sb, m);
        EmitIndexerCallbacks(sb, m);

        sb.Outdent();
        sb.AppendLine("}");
    }

    private static void EmitInstanceCallbacks(IndentedBuilder sb, TypeModel m)
    {
        foreach (var p in m.Properties)
        {
            if (p.HasGetter) EmitGetter(sb, m, p, isStatic: false);
            if (p.HasSetter) EmitSetter(sb, m, p, isStatic: false);
        }
        foreach (var meth in m.Methods)
            EmitMethod(sb, m, meth, isStatic: false);
    }

    private static void EmitStaticCallbacks(IndentedBuilder sb, TypeModel m)
    {
        foreach (var p in m.StaticProperties)
        {
            if (p.HasGetter) EmitGetter(sb, m, p, isStatic: true);
            if (p.HasSetter) EmitSetter(sb, m, p, isStatic: true);
        }
        foreach (var meth in m.StaticMethods)
            EmitMethod(sb, m, meth, isStatic: true);
    }

    private static void EmitGetter(IndentedBuilder sb, TypeModel m, PropertyModel p, bool isStatic)
    {
        string fnName = isStatic ? $"__SG_{Sanitize(p.JsName)}" : $"__G_{Sanitize(p.JsName)}";
        sb.AppendLine("[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]");
        sb.AppendLine($"private static global::QuickJSNet.Bindings.JSValue {fnName}(global::System.IntPtr ctx, global::QuickJSNet.Bindings.JSValue thisVal, int argc, global::System.IntPtr argv)");
        sb.AppendLine("{");
        sb.Indent();
        sb.AppendLine("try");
        sb.AppendLine("{");
        sb.Indent();
        if (isStatic)
        {
            sb.AppendLine($"var __v = {m.FullTypeName}.{p.CSharpName};");
        }
        else
        {
            sb.AppendLine($"var t = global::QuickJsNet.Interop.JSInteropRuntime.Unwrap<{m.FullTypeName}>(ctx, thisVal);");
            sb.AppendLine("if (t is null) return global::QuickJsNet.Interop.JSInteropRuntime.ThrowMissingTarget(ctx);");
            sb.AppendLine($"var __v = t.{p.CSharpName};");
        }
        sb.AppendLine($"return {ToJsExpr(p.TypeSymbol, "__v")};");
        sb.Outdent();
        sb.AppendLine("}");
        sb.AppendLine("catch (global::System.Exception ex) { return global::QuickJsNet.Interop.JSInteropRuntime.ThrowInternal(ctx, ex); }");
        sb.Outdent();
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void EmitSetter(IndentedBuilder sb, TypeModel m, PropertyModel p, bool isStatic)
    {
        string fnName = isStatic ? $"__SS_{Sanitize(p.JsName)}" : $"__S_{Sanitize(p.JsName)}";
        sb.AppendLine("[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]");
        sb.AppendLine($"private static global::QuickJSNet.Bindings.JSValue {fnName}(global::System.IntPtr ctx, global::QuickJSNet.Bindings.JSValue thisVal, int argc, global::System.IntPtr argv)");
        sb.AppendLine("{");
        sb.Indent();
        sb.AppendLine("try");
        sb.AppendLine("{");
        sb.Indent();
        sb.AppendLine($"var __v = {FromJsExpr(p.TypeSymbol, "0")};");
        if (isStatic)
        {
            sb.AppendLine($"{m.FullTypeName}.{p.CSharpName} = __v;");
        }
        else
        {
            sb.AppendLine($"var t = global::QuickJsNet.Interop.JSInteropRuntime.Unwrap<{m.FullTypeName}>(ctx, thisVal);");
            sb.AppendLine("if (t is null) return global::QuickJsNet.Interop.JSInteropRuntime.ThrowMissingTarget(ctx);");
            sb.AppendLine($"t.{p.CSharpName} = __v;");
        }
        sb.AppendLine("return global::QuickJsNet.Interop.JSInteropRuntime.Undefined();");
        sb.Outdent();
        sb.AppendLine("}");
        sb.AppendLine("catch (global::System.Exception ex) { return global::QuickJsNet.Interop.JSInteropRuntime.ThrowInternal(ctx, ex); }");
        sb.Outdent();
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void EmitMethod(IndentedBuilder sb, TypeModel m, MethodModel meth, bool isStatic)
    {
        string fnName = isStatic ? $"__SM_{Sanitize(meth.JsName)}" : $"__M_{Sanitize(meth.JsName)}";
        sb.AppendLine("[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]");
        sb.AppendLine($"private static global::QuickJSNet.Bindings.JSValue {fnName}(global::System.IntPtr ctx, global::QuickJSNet.Bindings.JSValue thisVal, int argc, global::System.IntPtr argv)");
        sb.AppendLine("{");
        sb.Indent();
        sb.AppendLine("try");
        sb.AppendLine("{");
        sb.Indent();
        if (!isStatic)
        {
            sb.AppendLine($"var t = global::QuickJsNet.Interop.JSInteropRuntime.Unwrap<{m.FullTypeName}>(ctx, thisVal);");
            sb.AppendLine("if (t is null) return global::QuickJsNet.Interop.JSInteropRuntime.ThrowMissingTarget(ctx);");
        }
        // Convert each parameter
        for (int i = 0; i < meth.Parameters.Count; i++)
        {
            var p = meth.Parameters[i];
            sb.AppendLine($"var __p{i} = {FromJsExpr(p.Type, i.ToString())};");
        }
        // Build call
        var argList = string.Join(", ", Enumerable.Range(0, meth.Parameters.Count).Select(i => $"__p{i}"));
        string callExpr;
        if (isStatic)
            callExpr = $"{m.FullTypeName}.{meth.CSharpName}({argList})";
        else
            callExpr = $"t.{meth.CSharpName}({argList})";

        if (meth.IsAsync)
        {
            // Wrap in Promise. Use Task continuation.
            sb.AppendLine($"var __task = {callExpr};");
            sb.AppendLine("var __rt = global::QuickJsNet.Core.QuickJSRuntime.FromContext(ctx);");
            sb.AppendLine("if (__rt is null) return global::QuickJsNet.Interop.JSInteropRuntime.ThrowInternal(ctx, new global::System.InvalidOperationException(\"Runtime not found for context\"));");
            if (meth.AsyncResultType is null)
            {
                // Task / ValueTask -> Promise<undefined>
                sb.AppendLine("return global::QuickJsNet.Interop.JSPromiseBridge.FromTask(__rt, __task);");
            }
            else
            {
                sb.AppendLine($"return global::QuickJsNet.Interop.JSPromiseBridge.FromTask<{Fq(meth.AsyncResultType)}>(__rt, __task, (rt, v) => {ToJsExpr(meth.AsyncResultType, "v")});");
            }
        }
        else if (meth.ReturnType.SpecialType == SpecialType.System_Void)
        {
            sb.AppendLine($"{callExpr};");
            sb.AppendLine("return global::QuickJsNet.Interop.JSInteropRuntime.Undefined();");
        }
        else
        {
            sb.AppendLine($"var __r = {callExpr};");
            sb.AppendLine($"return {ToJsExpr(meth.ReturnType, "__r")};");
        }
        sb.Outdent();
        sb.AppendLine("}");
        sb.AppendLine("catch (global::System.Exception ex) { return global::QuickJsNet.Interop.JSInteropRuntime.ThrowInternal(ctx, ex); }");
        sb.Outdent();
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void EmitEventCallbacks(IndentedBuilder sb, TypeModel m)
    {
        if (m.Events.Count == 0) return;
        // on(name, fn)
        sb.AppendLine("[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]");
        sb.AppendLine("private static global::QuickJSNet.Bindings.JSValue __EV_On(global::System.IntPtr ctx, global::QuickJSNet.Bindings.JSValue thisVal, int argc, global::System.IntPtr argv)");
        sb.AppendLine("{");
        sb.Indent();
        sb.AppendLine("try {");
        sb.Indent();
        sb.AppendLine($"var t = global::QuickJsNet.Interop.JSInteropRuntime.Unwrap<{m.FullTypeName}>(ctx, thisVal);");
        sb.AppendLine("if (t is null) return global::QuickJsNet.Interop.JSInteropRuntime.ThrowMissingTarget(ctx);");
        sb.AppendLine("var name = global::QuickJsNet.Interop.JSInteropRuntime.ArgString(ctx, argv, argc, 0) ?? string.Empty;");
        sb.AppendLine("var fnVal = global::QuickJsNet.Interop.JSInteropRuntime.ArgAt(argv, 1);");
        sb.AppendLine("var rt = global::QuickJsNet.Core.QuickJSRuntime.FromContext(ctx);");
        sb.AppendLine("if (rt is null) return global::QuickJsNet.Interop.JSInteropRuntime.Undefined();");
        sb.AppendLine("switch (name) {");
        foreach (var ev in m.Events)
        {
            sb.AppendLine($"    case \"{ev.JsName}\": {{");
            var typedParams = string.Join(", ", ev.EventArgTypes.Select((tp, i) => $"{Fq(tp)} a{i}"));
            var argsCtor = string.Join(", ", ev.EventArgTypes.Select((tp, i) => ToJsExprFromVar(tp, $"a{i}")));
            // Pre-create the subscription so its Invoke is reachable from inside the lambda
            sb.AppendLine($"        global::QuickJsNet.Interop.JSEventBridge.Subscription? __subRef = null;");
            sb.AppendLine($"        var __ctxCap = ctx;");
            sb.AppendLine($"        {Fq(ev.HandlerType)} __handler = ({typedParams}) =>");
            sb.AppendLine("        {");
            sb.AppendLine("            var ctx = __ctxCap;"); // shadow for converters
            if (ev.EventArgTypes.Count == 0)
                sb.AppendLine("            var __args = global::System.Array.Empty<global::QuickJSNet.Bindings.JSValue>();");
            else
                sb.AppendLine($"            var __args = new global::QuickJSNet.Bindings.JSValue[] {{ {argsCtor} }};");
            sb.AppendLine("            if (__subRef is not null) global::QuickJsNet.Interop.JSEventBridge.Invoke(__subRef, __args);");
            sb.AppendLine("        };");
            sb.AppendLine($"        var __sub = global::QuickJsNet.Interop.JSEventBridge.CreateSubscription(rt, fnVal, __handler);");
            sb.AppendLine($"        __subRef = __sub;");
            sb.AppendLine($"        t.{ev.CSharpName} += __handler;");
            sb.AppendLine($"        global::QuickJsNet.Interop.JSEventBridge.Track(t, \"{ev.JsName}\", __sub);");
            sb.AppendLine("        break;");
            sb.AppendLine("    }");
        }
        sb.AppendLine("}");
        sb.AppendLine("return global::QuickJsNet.Interop.JSInteropRuntime.Undefined();");
        sb.Outdent();
        sb.AppendLine("} catch (global::System.Exception ex) { return global::QuickJsNet.Interop.JSInteropRuntime.ThrowInternal(ctx, ex); }");
        sb.Outdent();
        sb.AppendLine("}");
        sb.AppendLine();

        // off(name, fn)
        sb.AppendLine("[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]");
        sb.AppendLine("private static global::QuickJSNet.Bindings.JSValue __EV_Off(global::System.IntPtr ctx, global::QuickJSNet.Bindings.JSValue thisVal, int argc, global::System.IntPtr argv)");
        sb.AppendLine("{");
        sb.Indent();
        sb.AppendLine("try {");
        sb.Indent();
        sb.AppendLine($"var t = global::QuickJsNet.Interop.JSInteropRuntime.Unwrap<{m.FullTypeName}>(ctx, thisVal);");
        sb.AppendLine("if (t is null) return global::QuickJsNet.Interop.JSInteropRuntime.ThrowMissingTarget(ctx);");
        sb.AppendLine("var name = global::QuickJsNet.Interop.JSInteropRuntime.ArgString(ctx, argv, argc, 0) ?? string.Empty;");
        sb.AppendLine("var fnVal = global::QuickJsNet.Interop.JSInteropRuntime.ArgAt(argv, 1);");
        sb.AppendLine("var sub = global::QuickJsNet.Interop.JSEventBridge.UntrackByJSValue(t, name, fnVal);");
        sb.AppendLine("if (sub is not null) {");
        sb.AppendLine("switch (name) {");
        foreach (var ev in m.Events)
        {
            sb.AppendLine($"    case \"{ev.JsName}\": t.{ev.CSharpName} -= ({Fq(ev.HandlerType)})sub.Handler; break;");
        }
        sb.AppendLine("}");
        sb.AppendLine("}");
        sb.AppendLine("return global::QuickJsNet.Interop.JSInteropRuntime.Undefined();");
        sb.Outdent();
        sb.AppendLine("} catch (global::System.Exception ex) { return global::QuickJsNet.Interop.JSInteropRuntime.ThrowInternal(ctx, ex); }");
        sb.Outdent();
        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void EmitIndexerCallbacks(IndentedBuilder sb, TypeModel m)
    {
        if (m.Indexer is null) return;
        var idx = m.Indexer;

        sb.AppendLine("[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]");
        sb.AppendLine("private static global::QuickJSNet.Bindings.JSValue __IDX_Get(global::System.IntPtr ctx, global::QuickJSNet.Bindings.JSValue thisVal, int argc, global::System.IntPtr argv)");
        sb.AppendLine("{");
        sb.Indent();
        sb.AppendLine("try {");
        sb.Indent();
        sb.AppendLine($"var t = global::QuickJsNet.Interop.JSInteropRuntime.Unwrap<{m.FullTypeName}>(ctx, thisVal);");
        sb.AppendLine("if (t is null) return global::QuickJsNet.Interop.JSInteropRuntime.ThrowMissingTarget(ctx);");
        sb.AppendLine($"var key = {FromJsExpr(idx.IndexType, "0")};");
        sb.AppendLine($"var v = t[key];");
        sb.AppendLine($"return {ToJsExpr(idx.ValueType, "v")};");
        sb.Outdent();
        sb.AppendLine("} catch (global::System.Exception ex) { return global::QuickJsNet.Interop.JSInteropRuntime.ThrowInternal(ctx, ex); }");
        sb.Outdent();
        sb.AppendLine("}");
        sb.AppendLine();

        if (idx.HasSetter)
        {
            sb.AppendLine("[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]");
            sb.AppendLine("private static global::QuickJSNet.Bindings.JSValue __IDX_Set(global::System.IntPtr ctx, global::QuickJSNet.Bindings.JSValue thisVal, int argc, global::System.IntPtr argv)");
            sb.AppendLine("{");
            sb.Indent();
            sb.AppendLine("try {");
            sb.Indent();
            sb.AppendLine($"var t = global::QuickJsNet.Interop.JSInteropRuntime.Unwrap<{m.FullTypeName}>(ctx, thisVal);");
            sb.AppendLine("if (t is null) return global::QuickJsNet.Interop.JSInteropRuntime.ThrowMissingTarget(ctx);");
            sb.AppendLine($"var key = {FromJsExpr(idx.IndexType, "0")};");
            sb.AppendLine($"var val = {FromJsExpr(idx.ValueType, "1")};");
            sb.AppendLine($"t[key] = val;");
            sb.AppendLine("return global::QuickJsNet.Interop.JSInteropRuntime.Undefined();");
            sb.Outdent();
            sb.AppendLine("} catch (global::System.Exception ex) { return global::QuickJsNet.Interop.JSInteropRuntime.ThrowInternal(ctx, ex); }");
            sb.Outdent();
            sb.AppendLine("}");
            sb.AppendLine();
        }
    }

    // ──────────── Conversion helpers ────────────

    private static string Fq(ITypeSymbol t) => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string ToJsExpr(ITypeSymbol t, string varExpr) => ToJsExprFromVar(t, varExpr);

    private static string ToJsExprFromVar(ITypeSymbol t, string varExpr)
    {
        switch (t.SpecialType)
        {
            case SpecialType.System_Boolean: return $"global::QuickJsNet.Interop.JSInteropRuntime.Bool(ctx, {varExpr})";
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32: return $"global::QuickJsNet.Interop.JSInteropRuntime.Int32(ctx, (int)({varExpr}))";
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64: return $"global::QuickJsNet.Interop.JSInteropRuntime.Int64(ctx, (long)({varExpr}))";
            case SpecialType.System_UInt64: return $"global::QuickJsNet.Interop.JSInteropRuntime.Int64(ctx, unchecked((long)({varExpr})))";
            case SpecialType.System_Single:
            case SpecialType.System_Double: return $"global::QuickJsNet.Interop.JSInteropRuntime.Float64(ctx, (double)({varExpr}))";
            case SpecialType.System_String: return $"global::QuickJsNet.Interop.JSInteropRuntime.String(ctx, {varExpr})";
            case SpecialType.System_Char: return $"global::QuickJsNet.Interop.JSInteropRuntime.String(ctx, ({varExpr}).ToString())";
            case SpecialType.System_Object: return $"global::QuickJsNet.Interop.JSInteropRuntime.ManagedObject(ctx, {varExpr})";
            case SpecialType.System_Void: return "global::QuickJsNet.Interop.JSInteropRuntime.Undefined()";
        }
        if (t is IArrayTypeSymbol arr && arr.ElementType.SpecialType == SpecialType.System_Byte)
            return $"global::QuickJsNet.Interop.JSInteropRuntime.Bytes(ctx, {varExpr})";
        if (t.TypeKind == TypeKind.Enum)
            return $"global::QuickJsNet.Interop.JSInteropRuntime.Int32(ctx, (int)({varExpr}))";
        // Object: defer to ManagedObject (uses registry)
        return $"global::QuickJsNet.Interop.JSInteropRuntime.ManagedObject(ctx, (object?)({varExpr}))";
    }

    private static string FromJsExpr(ITypeSymbol t, string idxExpr)
    {
        switch (t.SpecialType)
        {
            case SpecialType.System_Boolean: return $"global::QuickJsNet.Interop.JSInteropRuntime.ArgBool(ctx, argv, argc, {idxExpr})";
            case SpecialType.System_Byte: return $"(byte)global::QuickJsNet.Interop.JSInteropRuntime.ArgInt32(ctx, argv, argc, {idxExpr})";
            case SpecialType.System_SByte: return $"(sbyte)global::QuickJsNet.Interop.JSInteropRuntime.ArgInt32(ctx, argv, argc, {idxExpr})";
            case SpecialType.System_Int16: return $"(short)global::QuickJsNet.Interop.JSInteropRuntime.ArgInt32(ctx, argv, argc, {idxExpr})";
            case SpecialType.System_UInt16: return $"(ushort)global::QuickJsNet.Interop.JSInteropRuntime.ArgInt32(ctx, argv, argc, {idxExpr})";
            case SpecialType.System_Int32: return $"global::QuickJsNet.Interop.JSInteropRuntime.ArgInt32(ctx, argv, argc, {idxExpr})";
            case SpecialType.System_UInt32: return $"(uint)global::QuickJsNet.Interop.JSInteropRuntime.ArgInt64(ctx, argv, argc, {idxExpr})";
            case SpecialType.System_Int64: return $"global::QuickJsNet.Interop.JSInteropRuntime.ArgInt64(ctx, argv, argc, {idxExpr})";
            case SpecialType.System_UInt64: return $"unchecked((ulong)global::QuickJsNet.Interop.JSInteropRuntime.ArgInt64(ctx, argv, argc, {idxExpr}))";
            case SpecialType.System_Single: return $"global::QuickJsNet.Interop.JSInteropRuntime.ArgFloat32(ctx, argv, argc, {idxExpr})";
            case SpecialType.System_Double: return $"global::QuickJsNet.Interop.JSInteropRuntime.ArgFloat64(ctx, argv, argc, {idxExpr})";
            case SpecialType.System_String: return $"global::QuickJsNet.Interop.JSInteropRuntime.ArgString(ctx, argv, argc, {idxExpr})";
            case SpecialType.System_Char: return $"((global::QuickJsNet.Interop.JSInteropRuntime.ArgString(ctx, argv, argc, {idxExpr}) ?? \"\\0\")[0])";
        }
        if (t.TypeKind == TypeKind.Enum)
            return $"({Fq(t)})global::QuickJsNet.Interop.JSInteropRuntime.ArgInt32(ctx, argv, argc, {idxExpr})";
        if (t is IArrayTypeSymbol arr && arr.ElementType.SpecialType == SpecialType.System_Byte)
            return $"global::QuickJsNet.Interop.JSInteropRuntime.ArgBytes(ctx, argv, argc, {idxExpr})";
        // Class: try unwrap a managed object
        return $"global::QuickJsNet.Interop.JSInteropRuntime.ArgObject<{Fq(t)}>(ctx, argv, argc, {idxExpr})";
    }

    private static string Sanitize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.ToString();
    }
}

internal sealed class IndentedBuilder
{
    private readonly StringBuilder _sb = new();
    private int _level;
    public void Indent() => _level++;
    public void Outdent() { if (_level > 0) _level--; }
    public void AppendLine(string text = "")
    {
        if (text.Length == 0) { _sb.AppendLine(); return; }
        _sb.Append(' ', _level * 4);
        _sb.AppendLine(text);
    }
    public override string ToString() => _sb.ToString();
}

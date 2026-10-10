using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace CostLedger.Domain;

/// <summary>
/// 编译期反射委托缓存：把 <c>PropertyInfo.GetValue/SetValue</c> 换成一次编译、永久缓存的强类型委托。
/// 解决 GenericRecordView / ReportService 等热路径反复反射的开销（尤其 RebuildPaths 树遍历、报表投影）。
/// 值类型（int/decimal/DateOnly/bool）自动装箱/拆箱，调用方照常强转即可。
/// </summary>
public static class PropertyAccessor
{
    private static readonly ConcurrentDictionary<(Type Type, string Property), Func<object, object?>> Getters = new();
    private static readonly ConcurrentDictionary<(Type Type, string Property), Action<object, object?>> Setters = new();

    public static object? Get(object target, string property)
        => Getter(target.GetType(), property)(target);

    public static void Set(object target, string property, object? value)
        => Setter(target.GetType(), property)(target, value);

    public static Func<object, object?> Getter(Type type, string property)
        => Getters.GetOrAdd((type, property), k => CompileGetter(k.Type, k.Property));

    public static Action<object, object?> Setter(Type type, string property)
        => Setters.GetOrAdd((type, property), k => CompileSetter(k.Type, k.Property));

    private static Func<object, object?> CompileGetter(Type type, string property)
    {
        var prop = Resolve(type, property);
        var obj = Expression.Parameter(typeof(object), "obj");
        var cast = Expression.Convert(obj, type);
        var access = Expression.Property(cast, prop);
        var box = Expression.Convert(access, typeof(object));
        return Expression.Lambda<Func<object, object?>>(box, obj).Compile();
    }

    private static Action<object, object?> CompileSetter(Type type, string property)
    {
        var prop = Resolve(type, property);
        var obj = Expression.Parameter(typeof(object), "obj");
        var value = Expression.Parameter(typeof(object), "value");
        var castObj = Expression.Convert(obj, type);
        var castVal = Expression.Convert(value, prop.PropertyType);
        var assign = Expression.Call(castObj, prop.SetMethod!, castVal);
        return Expression.Lambda<Action<object, object?>>(assign, obj, value).Compile();
    }

    private static PropertyInfo Resolve(Type type, string property)
        => type.GetProperty(property, BindingFlags.Public | BindingFlags.Instance)
           ?? throw new InvalidOperationException($"属性 {type.Name}.{property} 不存在，无法编译访问委托。");
}

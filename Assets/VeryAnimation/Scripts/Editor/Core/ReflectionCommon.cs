using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using UnityEditor;
using UnityEngine;

namespace VeryAnimation
{
    internal static class ReflectionCommon
    {
        private static Assembly[] s_unityEditorAssemblies;
        private static Assembly[] UnityEditorAssemblies
        {
            get
            {
                if (s_unityEditorAssemblies == null)
                {
                    var set = new HashSet<Assembly>();
                    foreach (var t in TypeCache.GetTypesDerivedFrom<Editor>())
                    {
                        if (IsUnityNamespace(t.Namespace))
                            set.Add(t.Assembly);
                    }
                    foreach (var t in TypeCache.GetTypesDerivedFrom<EditorWindow>())
                    {
                        if (IsUnityNamespace(t.Namespace))
                            set.Add(t.Assembly);
                    }
                    foreach (var t in TypeCache.GetTypesDerivedFrom<ScriptableObject>())
                    {
                        if (IsUnityNamespace(t.Namespace))
                            set.Add(t.Assembly);
                    }
                    var arr = new Assembly[set.Count];
                    set.CopyTo(arr);
                    s_unityEditorAssemblies = arr;
                }
                return s_unityEditorAssemblies;
            }
        }

        private static bool IsUnityNamespace(string ns)
        {
            if (ns == null) return false;
            return ns.StartsWith("UnityEditor") || ns.StartsWith("UnityEngine.");
        }

        public static Type GetUnityEditorType(string fullName)
        {
            foreach (var asm in UnityEditorAssemblies)
            {
                var t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }

        public static T EnsureInstanceDelegate<T>(ref T cache, object instance, MethodInfo method) where T : Delegate
        {
            if (instance == null || method == null)
                return null;
            if (cache == null || cache.Target != instance)
                cache = (T)Delegate.CreateDelegate(typeof(T), instance, method);
            return cache;
        }
        public static TResult InvokeInstanceDelegate<TResult>(ref Func<TResult> cache, object instance, MethodInfo method)
        {
            var dg = EnsureInstanceDelegate(ref cache, instance, method);
            return dg != null ? dg() : default;
        }
        public static TResult InvokeInstanceDelegate<T1, TResult>(ref Func<T1, TResult> cache, object instance, MethodInfo method, T1 arg1)
        {
            var dg = EnsureInstanceDelegate(ref cache, instance, method);
            return dg != null ? dg(arg1) : default;
        }
        public static void InvokeInstanceDelegate(ref Action cache, object instance, MethodInfo method)
        {
            EnsureInstanceDelegate(ref cache, instance, method)?.Invoke();
        }
        public static void InvokeInstanceDelegate<T1>(ref Action<T1> cache, object instance, MethodInfo method, T1 arg1)
        {
            EnsureInstanceDelegate(ref cache, instance, method)?.Invoke(arg1);
        }

        public static TDelegate CreateConvertingDelegate<TDelegate>(MethodInfo method) where TDelegate : Delegate
        {
            if (method == null)
                return null;

            var invoke = typeof(TDelegate).GetMethod("Invoke");
            var invokeParameters = invoke.GetParameters();
            var methodParameters = method.GetParameters();
            var instanceOffset = method.IsStatic ? 0 : 1;
            if (invokeParameters.Length != methodParameters.Length + instanceOffset)
                return null;

            var args = new ParameterExpression[invokeParameters.Length];
            for (int i = 0; i < args.Length; i++)
                args[i] = Expression.Parameter(invokeParameters[i].ParameterType);

            var callArgs = new Expression[methodParameters.Length];
            for (int i = 0; i < callArgs.Length; i++)
                callArgs[i] = ConvertExpressionIfNeeded(args[i + instanceOffset], methodParameters[i].ParameterType);

            var body = method.IsStatic ?
                Expression.Call(method, callArgs) :
                Expression.Call(ConvertExpressionIfNeeded(args[0], method.DeclaringType), method, callArgs);
            return Expression.Lambda<TDelegate>(ConvertExpressionIfNeeded(body, invoke.ReturnType), args).Compile();
        }
        private static Expression ConvertExpressionIfNeeded(Expression expression, Type type) =>
            expression.Type != type ? Expression.Convert(expression, type) : expression;

        public static Func<object, T> CreateGetFieldDelegate<T>(FieldInfo fi)
        {
            if (fi == null)
                return null;

            string methodName = $"{fi.ReflectedType.FullName}.get_{fi.Name}";
            var dynamicMethod = new DynamicMethod(methodName, typeof(T), new[] { typeof(object) }, true);
            ILGenerator gen = dynamicMethod.GetILGenerator();
            if (fi.IsStatic)
            {
                gen.Emit(OpCodes.Ldsfld, fi);
            }
            else
            {
                gen.Emit(OpCodes.Ldarg_0);
                gen.Emit(OpCodes.Ldfld, fi);
            }
            gen.Emit(OpCodes.Ret);
            return (Func<object, T>)dynamicMethod.CreateDelegate(typeof(Func<object, T>));
        }
        public static Action<object, T> CreateSetFieldDelegate<T>(FieldInfo fi)
        {
            if (fi == null)
                return null;

            string methodName = $"{fi.ReflectedType.FullName}.set_{fi.Name}";
            var dynamicMethod = new DynamicMethod(methodName, null, new[] { typeof(object), typeof(T) }, true);
            ILGenerator gen = dynamicMethod.GetILGenerator();
            if (fi.IsStatic)
            {
                gen.Emit(OpCodes.Ldarg_1);
                gen.Emit(OpCodes.Stsfld, fi);
            }
            else
            {
                gen.Emit(OpCodes.Ldarg_0);
                gen.Emit(OpCodes.Ldarg_1);
                gen.Emit(OpCodes.Stfld, fi);
            }
            gen.Emit(OpCodes.Ret);
            return (Action<object, T>)dynamicMethod.CreateDelegate(typeof(Action<object, T>));
        }
    }
}

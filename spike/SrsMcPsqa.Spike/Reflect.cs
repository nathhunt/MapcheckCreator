using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SrsMcPsqa.Spike
{
    /// <summary>
    /// Reflection helpers used by the spike for ESAPI members whose v18 existence/signature is not yet confirmed
    /// (VERIFY items). Using reflection means the spike compiles even if a member is missing, and the report
    /// says exactly what exists.
    /// </summary>
    internal static class Reflect
    {
        private const BindingFlags Pub = BindingFlags.Public | BindingFlags.Instance;

        /// <summary>Try to set a public instance property. Logs and returns false if it is missing, read-only or throws.</summary>
        public static bool TrySetProperty(object target, string name, object? value, Action<string> log)
        {
            var type = target.GetType();
            var p = type.GetProperty(name, Pub);
            if (p == null) { log($"[INFO] {type.Name}.{name}: property not found."); return false; }
            if (p.GetSetMethod() == null) { log($"[INFO] {type.Name}.{name}: property is read-only."); return false; }
            try
            {
                p.SetValue(target, value);
                return true;
            }
            catch (TargetInvocationException ex)
            {
                log($"[FAIL] {type.Name}.{name} setter threw: {ex.InnerException}");
                return false;
            }
            catch (Exception ex)
            {
                log($"[FAIL] {type.Name}.{name} setter threw: {ex}");
                return false;
            }
        }

        /// <summary>Read a public instance property by name; returns null if missing or if the getter throws.</summary>
        public static object? TryGet(object target, string name)
        {
            try { return target.GetType().GetProperty(name, Pub)?.GetValue(target); }
            catch { return null; }
        }

        /// <summary>"Name=Value; ..." for every readable public instance property (getter exceptions shown inline).</summary>
        public static string DescribeProperties(object o)
        {
            var sb = new StringBuilder();
            foreach (var p in o.GetType().GetProperties(Pub).Where(p => p.GetIndexParameters().Length == 0))
            {
                string v;
                try { v = p.GetValue(o)?.ToString() ?? "null"; }
                catch (Exception ex) { v = "<" + (ex.InnerException ?? ex).GetType().Name + ">"; }
                sb.Append(p.Name).Append('=').Append(v).Append("; ");
            }
            return sb.ToString();
        }

        /// <summary>Signatures of all public members called <paramref name="memberName"/> on <paramref name="t"/> (incl. inherited).</summary>
        public static List<string> Signatures(Type t, string memberName)
        {
            var result = new List<string>();
            if (memberName == ".ctor")
            {
                result.AddRange(t.GetConstructors().Select(c => $"{t.Name}({Params(c)})"));
                return result;
            }
            foreach (var m in t.GetMember(memberName, Pub | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            {
                switch (m)
                {
                    case MethodInfo mi:
                        result.Add($"{TypeName(mi.ReturnType)} {t.Name}.{mi.Name}({Params(mi)})");
                        break;
                    case PropertyInfo pi:
                        var get = pi.GetGetMethod() != null ? "get; " : "";
                        var set = pi.GetSetMethod() != null ? "set; " : "";
                        result.Add($"{TypeName(pi.PropertyType)} {t.Name}.{pi.Name} {{ {get}{set}}}");
                        break;
                    default:
                        result.Add($"{m.MemberType} {t.Name}.{m.Name}");
                        break;
                }
            }
            return result;
        }

        /// <summary>Full public surface of a type (properties, methods, constructors), one line each.</summary>
        public static string FullDump(Type t)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== {t.FullName}  (base: {t.BaseType?.FullName})");
            foreach (var c in t.GetConstructors())
                sb.AppendLine($"  ctor {t.Name}({Params(c)})");
            foreach (var p in t.GetProperties(Pub | BindingFlags.Static).OrderBy(p => p.Name))
            {
                var get = p.GetGetMethod() != null ? "get; " : "";
                var set = p.GetSetMethod() != null ? "set; " : "";
                sb.AppendLine($"  prop {TypeName(p.PropertyType)} {p.Name} {{ {get}{set}}}");
            }
            foreach (var m in t.GetMethods(Pub | BindingFlags.Static).Where(m => !m.IsSpecialName && m.DeclaringType != typeof(object)).OrderBy(m => m.Name))
                sb.AppendLine($"  meth {(m.IsStatic ? "static " : "")}{TypeName(m.ReturnType)} {m.Name}({Params(m)})");
            return sb.ToString();
        }

        private static string Params(MethodBase m) =>
            string.Join(", ", m.GetParameters().Select(p => (p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : "") + TypeName(p.ParameterType) + " " + p.Name));

        private static string TypeName(Type t)
        {
            if (t.IsByRef) t = t.GetElementType()!;
            if (!t.IsGenericType) return t.Name;
            var name = t.Name.Substring(0, t.Name.IndexOf('`'));
            return name + "<" + string.Join(", ", t.GetGenericArguments().Select(TypeName)) + ">";
        }
    }
}

// Reflection discovery of IAppModule (and provider) implementations across every loaded MusicRoom.* assembly except
// the test assemblies: a probe written in one module's test folder must never join the boots of other tests (tests
// pass probes explicitly through BootOptions.ExtraModules). Deterministic order: Order ascending, then full type name.
// Works under IL2CPP because Core/link.xml preserves the MusicRoom assemblies whole.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace MusicRoom.Core
{
    public static class ModuleDiscovery
    {
        public const string AssemblyPrefix = "MusicRoom.";
        public const string TestAssemblyPrefix = "MusicRoom.Tests.";

        public static bool IsTestAssembly(string assemblyName) => assemblyName.StartsWith(TestAssemblyPrefix, StringComparison.Ordinal);

        /// <summary>The runtime and editor MusicRoom.* assemblies (never MusicRoom.Tests.*).</summary>
        public static IEnumerable<Assembly> MusicRoomAssemblies() =>
            AppDomain.CurrentDomain.GetAssemblies().Where(a =>
            {
                string n = a.GetName().Name;
                return n.StartsWith(AssemblyPrefix, StringComparison.Ordinal) && !IsTestAssembly(n);
            });

        /// <summary>Concrete, parameterless types implementing <typeparamref name="T"/> (not instantiated).</summary>
        public static List<Type> FindTypes<T>(Func<Type, bool> filter = null)
        {
            var list = new List<Type>();
            foreach (var asm in MusicRoomAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                foreach (var t in types)
                {
                    if (t.IsAbstract || t.IsInterface || t.IsGenericTypeDefinition || !typeof(T).IsAssignableFrom(t)) continue;
                    if (typeof(UnityEngine.Object).IsAssignableFrom(t)) continue;          // MonoBehaviours are built by modules, not discovered
                    if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                    if (filter != null && !filter(t)) continue;
                    list.Add(t);
                }
            }
            return list.OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();
        }

        /// <summary>All discovered modules plus <paramref name="extra"/> (test probes, already instantiated), sorted by
        /// Order then full type name. The filter applies to discovered types only.</summary>
        public static List<IAppModule> CreateModules(Func<Type, bool> filter = null, IEnumerable<IAppModule> extra = null)
        {
            var modules = new List<IAppModule>();
            foreach (var t in FindTypes<IAppModule>(filter))
            {
                try { modules.Add((IAppModule)Activator.CreateInstance(t)); }
                catch (Exception e) { Debug.LogError($"[App] module {t.FullName} could not be created: {e}"); }
            }
            if (extra != null) foreach (var m in extra) if (m != null && !modules.Contains(m)) modules.Add(m);
            return modules.OrderBy(m => m.Order).ThenBy(m => m.GetType().FullName, StringComparer.Ordinal).ToList();
        }

        public static List<T> CreateAll<T>(Func<Type, bool> filter = null) where T : class
        {
            var list = new List<T>();
            foreach (var t in FindTypes<T>(filter))
            {
                try { list.Add((T)Activator.CreateInstance(t)); }
                catch (Exception e) { Debug.LogError($"[App] {typeof(T).Name} {t.FullName} could not be created: {e}"); }
            }
            return list;
        }
    }
}

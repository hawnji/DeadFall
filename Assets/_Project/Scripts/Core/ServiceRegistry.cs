using System;
using System.Collections.Generic;
using UnityEngine;

namespace Deadfall.Core
{
    /// <summary>
    /// Minimal service locator for cross-cutting services (settings, hit resolver, audio, session).
    /// Register by interface so local and networked implementations can be swapped.
    /// Reset automatically when play mode starts.
    /// </summary>
    public static class ServiceRegistry
    {
        private static readonly Dictionary<Type, object> Services = new Dictionary<Type, object>();

        public static void Register<T>(T service) where T : class
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            Services[typeof(T)] = service;
        }

        public static void Unregister<T>(T service) where T : class
        {
            if (Services.TryGetValue(typeof(T), out object current) && ReferenceEquals(current, service))
                Services.Remove(typeof(T));
        }

        public static bool TryGet<T>(out T service) where T : class
        {
            if (Services.TryGetValue(typeof(T), out object obj))
            {
                service = (T)obj;
                return true;
            }
            service = null;
            return false;
        }

        public static T Get<T>() where T : class
        {
            if (TryGet(out T service)) return service;
            throw new InvalidOperationException($"Service {typeof(T).Name} is not registered.");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset() => Services.Clear();
    }
}
